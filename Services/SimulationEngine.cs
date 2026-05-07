using OtonomAracOptimizasyonu.Models;

namespace OtonomAracOptimizasyonu.Services;

public sealed class SimulationEngine
{
    private const double DepotReachToleranceMeters = 0.001d;
    private const double MinimumDistanceMeters = 20d;
    private const int DepotLoadingTicks = 3;
    private const int HomeUnloadingTicks = 2;
    private const double ConflictDetectionRangeMeters = 20d;
    private readonly Dictionary<string, int> _homePositionByVehicleId;
    private readonly Dictionary<string, int> _missionDepotByVehicleId;
    private readonly Dictionary<string, double> _cruiseSpeedByVehicleId;
    private readonly Dictionary<string, int> _loadingTicksRemainingByVehicleId = new();
    private readonly Dictionary<string, int> _unloadingTicksRemainingByVehicleId = new();
    private readonly List<Vehicle> _vehicles;
    private readonly Queue<Vehicle> _leftGarageQueue = new();
    private readonly Queue<Vehicle> _rightGarageQueue = new();
    private readonly HashSet<string> _enqueuedVehicleIds = new();
    private bool _startupLogged;

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

        _homePositionByVehicleId = _vehicles.ToDictionary(
            v => v.Id,
            v => (int)Math.Round(v.HomePositionMeters, MidpointRounding.AwayFromZero));
        _missionDepotByVehicleId = _vehicles.ToDictionary(v => v.Id, v => v.TargetDepotPositionMeters);
        _cruiseSpeedByVehicleId = _vehicles.ToDictionary(v => v.Id, v => v.SpeedKmh);

        InitializeGarages();
    }

    public Road Road { get; }

    public TrafficController TrafficController { get; }

    public bool EnableReturnTrip { get; }

    public IReadOnlyCollection<Vehicle> Vehicles => _vehicles.AsReadOnly();

    public int TickCount { get; private set; }

    public SimulationTickReport Tick()
    {
        try
        {
            TickCount++;
            if (!_startupLogged)
            {
                _startupLogged = true;
                SimulationLogger.Log($"Simulasyon basladi. Sol kuyruk={_leftGarageQueue.Count}, sag kuyruk={_rightGarageQueue.Count}");
            }

            DispatchVehiclesFromGarages();
            RefreshStorageOccupancy();
            TrafficController.UpdateTrafficState(_vehicles, Road);
            var vehicleStates = new List<VehicleTickState>(_vehicles.Count);

            foreach (var vehicle in _vehicles)
            {
                if (vehicle.CurrentTask == VehicleTask.InGarage)
                {
                    vehicleStates.Add(CreateTickState(vehicle, TrafficStopReason.None, "Garajda spawn sirasi bekleniyor"));
                    continue;
                }

                if (vehicle.CurrentTask == VehicleTask.Completed)
                {
                    vehicleStates.Add(CreateTickState(vehicle, TrafficStopReason.None, "Gorevi tamamladi"));
                    continue;
                }

                if (vehicle.CurrentTask == VehicleTask.LoadingAtDepot)
                {
                    var loadingStatus = ProcessDepotLoading(vehicle);
                    vehicleStates.Add(CreateTickState(vehicle, TrafficStopReason.None, loadingStatus));
                    continue;
                }

                if (vehicle.CurrentTask == VehicleTask.UnloadingAtHome)
                {
                    var unloadingStatus = ProcessHomeUnloading(vehicle);
                    vehicleStates.Add(CreateTickState(vehicle, TrafficStopReason.None, unloadingStatus));
                    continue;
                }

                if ((vehicle.CurrentTask == VehicleTask.GoingToDepot || vehicle.CurrentTask == VehicleTask.ReturningHome) &&
                    HasReachedTarget(vehicle))
                {
                    HandleVehicleAtTarget(vehicle);
                    vehicleStates.Add(CreateTickState(vehicle, TrafficStopReason.None, "Hedef noktaya ulasti"));
                    continue;
                }

                var trafficDecision = TrafficController.Evaluate(vehicle, Road);
                if (trafficDecision.ManeuverDirective is not null)
                {
                    if (!TryApplyManeuverDirective(vehicle, trafficDecision.ManeuverDirective))
                    {
                        vehicleStates.Add(CreateTickState(
                            vehicle,
                            TrafficStopReason.DeadlockAvoidance,
                            $"Guvenli alan rezerve edilemedi ({trafficDecision.ManeuverDirective.SafeAreaPositionMeters}m), bekleniyor."));
                        continue;
                    }

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

                if (vehicle.CurrentTask == VehicleTask.WaitingInPocket)
                {
                    ReleaseSafeAreaReservation(vehicle);
                    vehicle.ResumeOriginalMission();
                }

                if (vehicle.SpeedKmh <= 0 && _cruiseSpeedByVehicleId.TryGetValue(vehicle.Id, out var cruiseSpeedForResume))
                {
                    vehicle.UpdateSpeed(cruiseSpeedForResume);
                }

                var nextPosition = CalculateNextPosition(vehicle);
                nextPosition = EnforceHardDistanceConstraint(vehicle, nextPosition);
                vehicle.UpdatePosition(nextPosition);

                if (HasReachedTarget(vehicle))
                {
                    if (vehicle.CurrentTask == VehicleTask.GoingToPocketForYielding)
                    {
                        vehicle.MarkWaitingInSafeArea();
                        SimulationLogger.Log($"Arac {vehicle.Id}, {vehicle.PositionMeters:0.0}m guvenli alana girdi ve bekliyor.");
                        vehicleStates.Add(CreateTickState(vehicle, TrafficStopReason.DeadlockAvoidance, "Cepte/depoda yol veriyor"));
                        continue;
                    }

                    HandleVehicleAtTarget(vehicle);
                    vehicleStates.Add(CreateTickState(vehicle, TrafficStopReason.None, "Hedef noktaya ulasti"));
                    continue;
                }

                vehicleStates.Add(CreateTickState(vehicle, TrafficStopReason.None, "Hareket ediyor"));
            }

            RefreshStorageOccupancy();
            var simulationTime = TimeSpan.FromSeconds(TickCount * TrafficController.TickDurationSeconds);
            return new SimulationTickReport(TickCount, simulationTime, vehicleStates);
        }
        catch (Exception ex)
        {
            SimulationLogger.Log($"Simulasyon dongusunda hata: {ex.GetType().Name} - {ex.Message}");
            throw;
        }
    }

    private void HandleVehicleAtTarget(Vehicle vehicle)
    {
        if (vehicle.CurrentTask == VehicleTask.GoingToPocketForYielding)
        {
            return;
        }

        var missionDepot = _missionDepotByVehicleId[vehicle.Id];
        var homePosition = _homePositionByVehicleId[vehicle.Id];
        var atHomeDepot = Math.Abs(vehicle.PositionMeters - homePosition) < DepotReachToleranceMeters;
        var atMissionDepot = Math.Abs(vehicle.PositionMeters - missionDepot) < DepotReachToleranceMeters;

        if (!vehicle.HasLoad && atMissionDepot)
        {
            vehicle.RegisterDepotVisit((int)Math.Round(vehicle.PositionMeters, MidpointRounding.AwayFromZero));
            vehicle.StartDepotLoading();
            _loadingTicksRemainingByVehicleId[vehicle.Id] = DepotLoadingTicks;
            SimulationLogger.Log($"Arac {vehicle.Id}, {vehicle.PositionMeters:0.0}m depoya girerek yukleme islemini baslatti.");
            return;
        }

        if (vehicle.HasLoad && atHomeDepot)
        {
            vehicle.StartUnloadingAtHome();
            _unloadingTicksRemainingByVehicleId[vehicle.Id] = HomeUnloadingTicks;
            SimulationLogger.Log($"Arac {vehicle.Id}, {homePosition}m baslangic noktasina dondu. Bosaltma basladi.");
            return;
        }

        if (!EnableReturnTrip || vehicle.SingleMissionOnly)
        {
            ReleaseSafeAreaReservation(vehicle);
            vehicle.UpdateSpeed(0);
            SimulationLogger.Log($"Arac {vehicle.Id}, tek yon gorevini {vehicle.PositionMeters:0.0}m depoda tamamladi.");
            return;
        }

        vehicle.StartReturningHome(_cruiseSpeedByVehicleId[vehicle.Id]);
        SimulationLogger.Log($"Arac {vehicle.Id}, yuklu donus gorevi icin {homePosition}m baslangic noktasina yoneldi.");
    }

    private bool HasReachedTarget(Vehicle vehicle)
    {
        if (vehicle.CurrentTask == VehicleTask.ReturningHome)
        {
            return Math.Abs(vehicle.PositionMeters - _homePositionByVehicleId[vehicle.Id]) < DepotReachToleranceMeters;
        }

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

    private bool TryApplyManeuverDirective(Vehicle vehicle, ManeuverDirective directive)
    {
        var area = FindSafeAreaByPosition(directive.SafeAreaPositionMeters);
        if (area is null)
        {
            return false;
        }

        if (!area.TryReserveSlot(vehicle.Id))
        {
            return false;
        }

        vehicle.StartRetreatManeuver(directive.SafeAreaPositionMeters, directive.YieldToVehicleId);
        return true;
    }

    private void InitializeGarages()
    {
        foreach (var vehicle in _vehicles)
        {
            vehicle.EnterGarage();
            if (Math.Abs(vehicle.HomePositionMeters - Road.LengthMeters) < DepotReachToleranceMeters)
            {
                _rightGarageQueue.Enqueue(vehicle);
                _enqueuedVehicleIds.Add(vehicle.Id);
                SimulationLogger.Log($"{vehicle.Id}, 240m kuyruguna eklendi.");
            }
            else
            {
                _leftGarageQueue.Enqueue(vehicle);
                _enqueuedVehicleIds.Add(vehicle.Id);
                SimulationLogger.Log($"{vehicle.Id}, 0m kuyruguna eklendi.");
            }
        }
    }

    private void DispatchVehiclesFromGarages()
    {
        TryDispatchFromLeftGarage();
        TryDispatchFromRightGarage();
    }

    private void TryDispatchFromLeftGarage()
    {
        if (_leftGarageQueue.Count == 0 || !IsLeftEntrySegmentClear())
        {
            return;
        }

        var vehicle = _leftGarageQueue.Dequeue();
        _enqueuedVehicleIds.Remove(vehicle.Id);
        SpawnVehicleFromGarage(vehicle, 0);
    }

    private void TryDispatchFromRightGarage()
    {
        if (_rightGarageQueue.Count == 0 || !IsRightEntrySegmentClear())
        {
            return;
        }

        var vehicle = _rightGarageQueue.Dequeue();
        _enqueuedVehicleIds.Remove(vehicle.Id);
        SpawnVehicleFromGarage(vehicle, Road.LengthMeters);
    }

    private bool IsLeftEntrySegmentClear()
    {
        return _vehicles
            .Where(IsOnMainLane)
            .All(v => v.PositionMeters >= MinimumDistanceMeters);
    }

    private bool IsRightEntrySegmentClear()
    {
        return _vehicles
            .Where(IsOnMainLane)
            .All(v => v.PositionMeters <= (Road.LengthMeters - MinimumDistanceMeters));
    }

    private void SpawnVehicleFromGarage(Vehicle vehicle, double entryPosition)
    {
        var missionDepot = _missionDepotByVehicleId[vehicle.Id];
        var cruiseSpeed = _cruiseSpeedByVehicleId[vehicle.Id];
        vehicle.UpdatePosition(entryPosition);
        vehicle.DispatchToDepot(missionDepot, cruiseSpeed);
        SimulationLogger.Log($"{vehicle.Id} yola cikti ({entryPosition:0}m), yol bos.");
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

        vehicle.StartReturningHome(_cruiseSpeedByVehicleId[vehicle.Id]);
        SimulationLogger.Log($"Arac {vehicle.Id}, yuklemeyi tamamlayip {_homePositionByVehicleId[vehicle.Id]}m konumundaki baslangic noktasina donus icin cikti.");
        return "Yukleme tamamlandi, donuse geciliyor";
    }

    private double EnforceHardDistanceConstraint(Vehicle vehicle, double candidatePosition)
    {
        var boundedCandidate = Math.Clamp(candidatePosition, 0d, Road.LengthMeters);
        if (!IsOnMainLane(vehicle))
        {
            return boundedCandidate;
        }

        foreach (var other in _vehicles)
        {
            if (other.Id == vehicle.Id || !IsOnMainLane(other))
            {
                continue;
            }

            var otherCandidate = EstimateOtherCandidate(other);
            var finalGap = Math.Abs(boundedCandidate - otherCandidate);

            if (vehicle.Direction != other.Direction)
            {
                var currentGap = Math.Abs(vehicle.PositionMeters - other.PositionMeters);
                var closing = currentGap > finalGap;
                if (closing && currentGap <= ConflictDetectionRangeMeters && finalGap < MinimumDistanceMeters)
                {
                    if (!HasRightOfWay(vehicle, other))
                    {
                        vehicle.UpdateSpeed(0d);
                        return vehicle.PositionMeters;
                    }

                    continue;
                }
            }

            if (vehicle.Direction == other.Direction && IsFollower(vehicle, other))
            {
                var directionSign = vehicle.Direction == VehicleDirection.LeftToRight ? 1d : -1d;
                var maxAllowed = otherCandidate - (directionSign * MinimumDistanceMeters);
                if (vehicle.Direction == VehicleDirection.LeftToRight)
                {
                    boundedCandidate = Math.Min(boundedCandidate, maxAllowed);
                }
                else
                {
                    boundedCandidate = Math.Max(boundedCandidate, maxAllowed);
                }
            }
        }

        if (Math.Abs(boundedCandidate - vehicle.PositionMeters) < 0.0001d)
        {
            vehicle.UpdateSpeed(0d);
            return vehicle.PositionMeters;
        }

        if (_cruiseSpeedByVehicleId.TryGetValue(vehicle.Id, out var cruiseSpeed) && cruiseSpeed > 0)
        {
            vehicle.UpdateSpeed(cruiseSpeed);
        }

        return Math.Clamp(boundedCandidate, 0d, Road.LengthMeters);
    }

    private double EstimateOtherCandidate(Vehicle vehicle)
    {
        if (!IsOnMainLane(vehicle))
        {
            return vehicle.PositionMeters;
        }

        var candidate = CalculateNextPosition(vehicle);
        return Math.Clamp(candidate, 0d, Road.LengthMeters);
    }

    private static bool IsOnMainLane(Vehicle vehicle)
    {
        return vehicle.CurrentTask == VehicleTask.GoingToDepot ||
               vehicle.CurrentTask == VehicleTask.ReturningHome ||
               vehicle.CurrentTask == VehicleTask.GoingToPocketForYielding;
    }

    private static bool IsFollower(Vehicle vehicle, Vehicle other)
    {
        if (vehicle.Direction == VehicleDirection.LeftToRight)
        {
            return vehicle.PositionMeters <= other.PositionMeters;
        }

        return vehicle.PositionMeters >= other.PositionMeters;
    }

    private static bool HasRightOfWay(Vehicle candidate, Vehicle other)
    {
        if (candidate.IsPriority != other.IsPriority)
        {
            return candidate.IsPriority;
        }

        if (candidate.HasLoad != other.HasLoad)
        {
            return candidate.HasLoad;
        }

        return string.CompareOrdinal(candidate.Id, other.Id) < 0;
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

    private VehicleStorageArea? FindSafeAreaByPosition(int positionMeters)
    {
        return Road.Pockets.Cast<VehicleStorageArea>()
            .Concat(Road.Depots)
            .FirstOrDefault(area => area.PositionMeters == positionMeters);
    }

    private void ReleaseSafeAreaReservation(Vehicle vehicle)
    {
        if (vehicle.ManeuverSafeAreaPositionMeters is null)
        {
            return;
        }

        var area = FindSafeAreaByPosition(vehicle.ManeuverSafeAreaPositionMeters.Value);
        area?.ReleaseReservation(vehicle.Id);
    }

    private string ProcessHomeUnloading(Vehicle vehicle)
    {
        if (!_unloadingTicksRemainingByVehicleId.TryGetValue(vehicle.Id, out var remaining))
        {
            remaining = HomeUnloadingTicks;
        }

        remaining--;
        if (remaining > 0)
        {
            _unloadingTicksRemainingByVehicleId[vehicle.Id] = remaining;
            return $"Baslangicta bosaltiyor ({remaining} adim kaldi)";
        }

        _unloadingTicksRemainingByVehicleId.Remove(vehicle.Id);
        vehicle.CompleteDeliveryCycle();
        SimulationLogger.Log($"Arac {vehicle.Id}, {_homePositionByVehicleId[vehicle.Id]}m noktasinda bosaltmayi tamamlayip gorevini bitirdi.");

        if (!EnableReturnTrip || vehicle.SingleMissionOnly)
        {
            vehicle.CompleteDeliveryCycle();
            vehicle.EnterGarage();
            return "Gorev tamamlandi";
        }

        vehicle.CompleteDeliveryCycle();
        EnqueueForGarage(vehicle);
        return "Gorev tamamlandi, garaja alindi";
    }

    private void EnqueueForGarage(Vehicle vehicle)
    {
        vehicle.EnterGarage();
        if (_enqueuedVehicleIds.Contains(vehicle.Id))
        {
            return;
        }

        if (Math.Abs(vehicle.HomePositionMeters - Road.LengthMeters) < DepotReachToleranceMeters)
        {
            _rightGarageQueue.Enqueue(vehicle);
            SimulationLogger.Log($"{vehicle.Id}, gorev sonrasi 240m kuyruguna girdi.");
        }
        else
        {
            _leftGarageQueue.Enqueue(vehicle);
            SimulationLogger.Log($"{vehicle.Id}, gorev sonrasi 0m kuyruguna girdi.");
        }

        _enqueuedVehicleIds.Add(vehicle.Id);
    }
}
