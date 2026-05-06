using OtonomAracOptimizasyonu.Models;

namespace OtonomAracOptimizasyonu.Services;

public sealed class SimulationEngine
{
    private const double DepotReachToleranceMeters = 0.001d;
    private readonly Dictionary<string, int> _homeDepotByVehicleId;
    private readonly Dictionary<string, double> _cruiseSpeedByVehicleId;
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
        _cruiseSpeedByVehicleId = _vehicles.ToDictionary(v => v.Id, v => v.SpeedKmh);
    }

    public Road Road { get; }

    public TrafficController TrafficController { get; }

    public bool EnableReturnTrip { get; }

    public IReadOnlyCollection<Vehicle> Vehicles => _vehicles.AsReadOnly();

    public int TickCount { get; private set; }

    public SimulationTickReport Tick()
    {
        TrafficController.UpdateTrafficState(_vehicles, Road);
        TickCount++;
        var vehicleStates = new List<VehicleTickState>(_vehicles.Count);

        foreach (var vehicle in _vehicles)
        {
            if (vehicle.CurrentTask == VehicleTask.NormalDrive && HasReachedTargetDepot(vehicle))
            {
                HandleVehicleAtTargetDepot(vehicle);
                vehicleStates.Add(CreateTickState(vehicle, TrafficStopReason.None, "At Target Depot"));
                continue;
            }

            var trafficDecision = TrafficController.Evaluate(vehicle, Road);
            if (trafficDecision.ManeuverDirective is not null)
            {
                ApplyManeuverDirective(vehicle, trafficDecision.ManeuverDirective);
                SimulationLogger.Log(
                    $"Vehicle {vehicle.Id} retreating to {trafficDecision.ManeuverDirective.SafeAreaType} " +
                    $"at {trafficDecision.ManeuverDirective.SafeAreaPositionMeters}m.");
                vehicleStates.Add(CreateTickState(
                    vehicle,
                    TrafficStopReason.DeadlockAvoidance,
                    $"Vehicle {vehicle.Id} is retreating to {trafficDecision.ManeuverDirective.SafeAreaType} at {trafficDecision.ManeuverDirective.SafeAreaPositionMeters}m to resolve deadlock."));
                continue;
            }

            if (!trafficDecision.CanMove)
            {
                vehicleStates.Add(CreateTickState(vehicle, trafficDecision.StopReason, $"Waiting: {trafficDecision.Message}"));
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
                    SimulationLogger.Log($"Vehicle {vehicle.Id} entered safe area at {vehicle.PositionMeters:0.0}m and is waiting.");
                    vehicleStates.Add(CreateTickState(vehicle, TrafficStopReason.DeadlockAvoidance, "Waiting in Safe Area"));
                    continue;
                }

                HandleVehicleAtTargetDepot(vehicle);
                vehicleStates.Add(CreateTickState(vehicle, TrafficStopReason.None, "Reached Target Depot"));
                continue;
            }

            vehicleStates.Add(CreateTickState(vehicle, TrafficStopReason.None, "Moving"));
        }

        var simulationTime = TimeSpan.FromSeconds(TickCount * TrafficController.TickDurationSeconds);
        return new SimulationTickReport(TickCount, simulationTime, vehicleStates);
    }

    private void HandleVehicleAtTargetDepot(Vehicle vehicle)
    {
        vehicle.UpdateSpeed(0);

        if (!EnableReturnTrip || vehicle.SingleMissionOnly)
        {
            SimulationLogger.Log($"Vehicle {vehicle.Id} completed single mission at depot {vehicle.PositionMeters:0.0}m.");
            return;
        }

        var returnTarget = _homeDepotByVehicleId[vehicle.Id];
        if (vehicle.TargetDepotPositionMeters == returnTarget)
        {
            return;
        }

        var currentDirection = vehicle.Direction;
        vehicle.UpdateDirection(currentDirection == VehicleDirection.LeftToRight
            ? VehicleDirection.RightToLeft
            : VehicleDirection.LeftToRight);

        vehicle.UpdateTargetDepot(returnTarget);
        vehicle.UpdateSpeed(_cruiseSpeedByVehicleId[vehicle.Id]);
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
            reason,
            statusMessage);
    }

    private static void ApplyManeuverDirective(Vehicle vehicle, ManeuverDirective directive)
    {
        vehicle.StartRetreatManeuver(directive.SafeAreaPositionMeters, directive.YieldToVehicleId);
    }
}
