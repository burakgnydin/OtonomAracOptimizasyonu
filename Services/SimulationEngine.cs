using OtonomAracOptimizasyonu.Models;

namespace OtonomAracOptimizasyonu.Services;

public sealed class SimulationEngine
{
    private const double DepotReachToleranceMeters = 0.001d;
    private const int DepotLoadingTicks = 3;
    private readonly Dictionary<string, int> _homeDepotByVehicleId;
    private readonly Dictionary<string, int> _missionDepotByVehicleId;
    private readonly Dictionary<string, double> _cruiseSpeedByVehicleId;
    private readonly Dictionary<string, int> _loadingTicksRemainingByVehicleId = new();
    private readonly List<Vehicle> _vehicles;

    public SimulationEngine(
        Road road,
        IEnumerable<Vehicle> vehicles,
        TrafficController trafficController,
        bool enableReturnTrip = true)
    {
        ArgumentNullException.ThrowIfNull(road);
        ArgumentNullException.ThrowIfNull(vehicles);
        ArgumentNullException.ThrowIfNull(trafficController);

        Road = road;
        TrafficController = trafficController;
        EnableReturnTrip = enableReturnTrip;
        _vehicles = vehicles.ToList();

        _homeDepotByVehicleId = _vehicles.ToDictionary(
            v => v.Id,
            v => FindNearestDepotPosition(v.PositionMeters, road));
        _missionDepotByVehicleId = _vehicles.ToDictionary(v => v.Id, v => v.TargetDepotPositionMeters);
        _cruiseSpeedByVehicleId = _vehicles.ToDictionary(v => v.Id, v => v.SpeedKmh);
    }

    public Road Road { get; }

    public TrafficController TrafficController { get; }

    public bool EnableReturnTrip { get; }

    public IReadOnlyCollection<Vehicle> Vehicles => _vehicles.AsReadOnly();

    public int TickCount { get; private set; }

    public SimulationTickReport Tick()
    {
        RefreshStorageOccupancy();
        TrafficController.UpdateTrafficState(_vehicles, Road);
        TickCount++;
        var vehicleStates = new List<VehicleTickState>(_vehicles.Count);

        foreach (var vehicle in _vehicles)
        {
            if (vehicle.CurrentTask == VehicleTask.LoadingAtDepot)
            {
                var loadingStatus = ProcessDepotLoading(vehicle);
                vehicleStates.Add(CreateTickState(vehicle, TrafficStopReason.None, loadingStatus));
                continue;
            }

            if (vehicle.CurrentTask == VehicleTask.NormalDrive && HasReachedTargetDepot(vehicle))
            {
                HandleVehicleAtTargetDepot(vehicle);
                vehicleStates.Add(CreateTickState(vehicle, TrafficStopReason.None, "Hedef depoda"));
                continue;
            }

            var trafficDecision = TrafficController.Evaluate(vehicle, Road);
            if (trafficDecision.ManeuverDirective is not null)
            {
                ApplyManeuverDirective(vehicle, trafficDecision.ManeuverDirective);
                SimulationLogger.Log(
                    $"Arac {vehicle.Id}, {trafficDecision.ManeuverDirective.SafeAreaPositionMeters}m konumundaki " +
                    $"{trafficDecision.ManeuverDirective.SafeAreaType} bolgesine geri cekiliyor.");
                vehicleStates.Add(CreateTickState(
                    vehicle,
                    TrafficStopReason.DeadlockAvoidance,
                    $"Arac {vehicle.Id}, kilitlenmeyi cozmek icin {trafficDecision.ManeuverDirective.SafeAreaPositionMeters}m konumundaki {trafficDecision.ManeuverDirective.SafeAreaType} bolgesine geri cekiliyor."));
                continue;
            }

            if (!trafficDecision.CanMove)
            {
                vehicleStates.Add(CreateTickState(vehicle, trafficDecision.StopReason, $"Bekliyor: {trafficDecision.Message}"));
                continue;
            }

            if (vehicle.CurrentTask == VehicleTask.WaitingInSafeArea)
            {
                vehicle.ResumeOriginalMission();
            }

            var nextPosition = CalculateNextPosition(vehicle);
            vehicle.UpdatePosition(nextPosition);

            if (HasReachedTargetDepot(vehicle))
            {
                if (vehicle.CurrentTask == VehicleTask.RetreatingToSafeArea)
                {
                    vehicle.MarkWaitingInSafeArea();
                    SimulationLogger.Log($"Arac {vehicle.Id}, {vehicle.PositionMeters:0.0}m guvenli alana girdi ve bekliyor.");
                    vehicleStates.Add(CreateTickState(vehicle, TrafficStopReason.DeadlockAvoidance, "Guvenli alanda bekliyor"));
                    continue;
                }

                HandleVehicleAtTargetDepot(vehicle);
                vehicleStates.Add(CreateTickState(vehicle, TrafficStopReason.None, "Hedef depoya ulasti"));
                continue;
            }

            vehicleStates.Add(CreateTickState(vehicle, TrafficStopReason.None, "Hareket ediyor"));
        }

        RefreshStorageOccupancy();
        var simulationTime = TimeSpan.FromSeconds(TickCount * TrafficController.TickDurationSeconds);
        return new SimulationTickReport(TickCount, simulationTime, vehicleStates);
    }

    private void HandleVehicleAtTargetDepot(Vehicle vehicle)
    {
        var homeDepot = _homeDepotByVehicleId[vehicle.Id];
        var missionDepot = _missionDepotByVehicleId[vehicle.Id];
        var atHomeDepot = Math.Abs(vehicle.PositionMeters - homeDepot) < DepotReachToleranceMeters;
        var atMissionDepot = Math.Abs(vehicle.PositionMeters - missionDepot) < DepotReachToleranceMeters;

        vehicle.RegisterDepotVisit((int)Math.Round(vehicle.PositionMeters, MidpointRounding.AwayFromZero));

        if (!vehicle.HasLoad && atMissionDepot)
        {
            vehicle.StartDepotLoading();
            _loadingTicksRemainingByVehicleId[vehicle.Id] = DepotLoadingTicks;
            SimulationLogger.Log($"Arac {vehicle.Id}, {vehicle.PositionMeters:0.0}m depoya girerek yukleme islemini baslatti.");
            return;
        }

        if (vehicle.HasLoad && atHomeDepot)
        {
            vehicle.CompleteDeliveryCycle();
            SimulationLogger.Log($"Arac {vehicle.Id}, {vehicle.PositionMeters:0.0}m depoda yuku teslim etti (Tamamlanan gorev: {vehicle.CompletedMissionCount}).");

            if (!EnableReturnTrip || vehicle.SingleMissionOnly)
            {
                vehicle.UpdateSpeed(0);
                return;
            }

            var outboundTarget = _missionDepotByVehicleId[vehicle.Id];
            vehicle.UpdateTargetDepot(outboundTarget);
            vehicle.UpdateDirection(outboundTarget >= vehicle.PositionMeters
                ? VehicleDirection.LeftToRight
                : VehicleDirection.RightToLeft);
            vehicle.UpdateSpeed(_cruiseSpeedByVehicleId[vehicle.Id]);
            SimulationLogger.Log($"Arac {vehicle.Id}, yeni gorev icin {outboundTarget}m hedef depoya yoneldi.");
            return;
        }

        if (!EnableReturnTrip || vehicle.SingleMissionOnly)
        {
            vehicle.UpdateSpeed(0);
            SimulationLogger.Log($"Arac {vehicle.Id}, tek yon gorevini {vehicle.PositionMeters:0.0}m depoda tamamladi.");
            return;
        }

        var returnTarget = _homeDepotByVehicleId[vehicle.Id];
        vehicle.UpdateDirection(returnTarget >= vehicle.PositionMeters
            ? VehicleDirection.LeftToRight
            : VehicleDirection.RightToLeft);
        vehicle.UpdateTargetDepot(returnTarget);
        vehicle.UpdateSpeed(_cruiseSpeedByVehicleId[vehicle.Id]);
        SimulationLogger.Log($"Arac {vehicle.Id}, yuklu donus gorevi icin {returnTarget}m baslangic deposuna yoneldi.");
    }

    private bool HasReachedTargetDepot(Vehicle vehicle)
    {
        return Math.Abs(vehicle.PositionMeters - vehicle.TargetDepotPositionMeters) < DepotReachToleranceMeters;
    }

    private double CalculateNextPosition(Vehicle vehicle)
    {
        var distanceMeters = vehicle.SpeedKmh * (1000d / 3600d) * TrafficController.TickDurationSeconds;
        var directionSign = vehicle.Direction == VehicleDirection.LeftToRight ? 1d : -1d;
        var unclamped = vehicle.PositionMeters + (distanceMeters * directionSign);

        var bounded = Math.Clamp(unclamped, 0d, Road.LengthMeters);
        var target = vehicle.TargetDepotPositionMeters;
        var passedTarget = vehicle.Direction == VehicleDirection.LeftToRight
            ? bounded >= target
            : bounded <= target;

        return passedTarget ? target : bounded;
    }

    private static int FindNearestDepotPosition(double vehiclePosition, Road road)
    {
        return road.Depots
            .OrderBy(depot => Math.Abs(depot.PositionMeters - vehiclePosition))
            .Select(depot => depot.PositionMeters)
            .First();
    }

    private static VehicleTickState CreateTickState(Vehicle vehicle, TrafficStopReason reason, string statusMessage)
    {
        return new VehicleTickState(
            vehicle.Id,
            vehicle.PositionMeters,
            vehicle.SpeedKmh,
            vehicle.Direction,
            vehicle.TargetDepotPositionMeters,
            Math.Abs(vehicle.PositionMeters - vehicle.TargetDepotPositionMeters) < DepotReachToleranceMeters,
            vehicle.CurrentTask,
            vehicle.HasLoad,
            vehicle.CompletedMissionCount,
            reason,
            statusMessage);
    }

    private static void ApplyManeuverDirective(Vehicle vehicle, ManeuverDirective directive)
    {
        vehicle.StartRetreatManeuver(directive.SafeAreaPositionMeters, directive.YieldToVehicleId);
    }

    private string ProcessDepotLoading(Vehicle vehicle)
    {
        if (!_loadingTicksRemainingByVehicleId.TryGetValue(vehicle.Id, out var remaining))
        {
            remaining = DepotLoadingTicks;
        }

        remaining--;
        if (remaining > 0)
        {
            _loadingTicksRemainingByVehicleId[vehicle.Id] = remaining;
            return $"Depoda yukleniyor ({remaining} adim kaldi)";
        }

        _loadingTicksRemainingByVehicleId.Remove(vehicle.Id);
        vehicle.FinishLoadingAndCarryLoad();

        var returnTarget = _homeDepotByVehicleId[vehicle.Id];
        vehicle.UpdateTargetDepot(returnTarget);
        vehicle.UpdateDirection(returnTarget >= vehicle.PositionMeters
            ? VehicleDirection.LeftToRight
            : VehicleDirection.RightToLeft);
        vehicle.UpdateSpeed(_cruiseSpeedByVehicleId[vehicle.Id]);
        SimulationLogger.Log($"Arac {vehicle.Id}, yuklemeyi tamamlayip {returnTarget}m konumundaki baslangic deposuna donus icin cikti.");
        return "Yukleme tamamlandi, donuse geciliyor";
    }

    private void RefreshStorageOccupancy()
    {
        foreach (var pocket in Road.Pockets)
        {
            pocket.ResetOccupancy();
        }

        foreach (var depot in Road.Depots)
        {
            depot.ResetOccupancy();
        }

        foreach (var vehicle in _vehicles)
        {
            foreach (var pocket in Road.Pockets)
            {
                if (Math.Abs(vehicle.PositionMeters - pocket.PositionMeters) < DepotReachToleranceMeters)
                {
                    pocket.TryAddVehicle(vehicle);
                }
            }

            foreach (var depot in Road.Depots)
            {
                if (Math.Abs(vehicle.PositionMeters - depot.PositionMeters) < DepotReachToleranceMeters)
                {
                    depot.TryAddVehicle(vehicle);
                }
            }
        }
    }
}
