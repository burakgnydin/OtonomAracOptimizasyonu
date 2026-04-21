using OtonomAracOptimizasyonu.Models;

namespace OtonomAracOptimizasyonu.Services;

public sealed class SafetyFirstTrafficStrategy : ITrafficStrategy
{
    public const double MinimumDistanceMeters = 20d;

    public TrafficDecision Evaluate(
        Vehicle vehicle,
        Road road,
        IReadOnlyCollection<Vehicle> trafficState,
        double tickDurationSeconds)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(road);
        ArgumentNullException.ThrowIfNull(trafficState);

        if (tickDurationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tickDurationSeconds), "Tick duration must be greater than zero.");
        }

        if (vehicle.CurrentTask == VehicleTask.WaitingInSafeArea)
        {
            return CanExitSafeArea(vehicle, trafficState)
                ? TrafficDecision.MoveAllowed()
                : TrafficDecision.Stop(TrafficStopReason.DeadlockAvoidance, "Waiting In Safe Area");
        }

        var predictedPosition = PredictNextPosition(vehicle, road, tickDurationSeconds);
        var sameDirectionVehicles = trafficState
            .Where(x => x.Id != vehicle.Id && x.Direction == vehicle.Direction)
            .ToList();

        if (ViolatesMinimumGapWithSameDirectionVehicles(vehicle, predictedPosition, sameDirectionVehicles))
        {
            return TrafficDecision.Stop(TrafficStopReason.CollisionRisk, "Collision Risk (Same Direction)");
        }

        var oppositeDirectionVehicles = trafficState
            .Where(x => x.Id != vehicle.Id && x.Direction != vehicle.Direction)
            .ToList();

        var oncomingVehicle = oppositeDirectionVehicles
            .Where(other => IsFacingEachOther(vehicle, other))
            .OrderBy(other => Math.Abs(other.PositionMeters - vehicle.PositionMeters))
            .FirstOrDefault();

        if (ViolatesMinimumGapWithOppositeDirectionVehicles(predictedPosition, oppositeDirectionVehicles))
        {
            var maneuverDecision = TryCreateManeuverDecision(vehicle, oncomingVehicle, road);
            if (maneuverDecision is not null)
            {
                return maneuverDecision;
            }

            return TrafficDecision.Stop(TrafficStopReason.CollisionRisk, "Collision Risk (Opposite Direction)");
        }

        if (TriggersDeadlockWait(vehicle, oppositeDirectionVehicles, road))
        {
            return TrafficDecision.Stop(TrafficStopReason.DeadlockAvoidance, "Deadlock Avoidance");
        }

        return TrafficDecision.MoveAllowed();
    }

    private static TrafficDecision? TryCreateManeuverDecision(Vehicle vehicle, Vehicle? oncomingVehicle, Road road)
    {
        if (oncomingVehicle is null || !IsLowerPriority(vehicle, oncomingVehicle))
        {
            return null;
        }

        if (vehicle.CurrentTask == VehicleTask.RetreatingToSafeArea || vehicle.CurrentTask == VehicleTask.WaitingInSafeArea)
        {
            return null;
        }

        var safeArea = FindNearestAvailableSafeArea(vehicle, road);
        if (safeArea is null)
        {
            return null;
        }

        var safeAreaType = safeArea is Pocket ? "Pocket" : "Depot";
        var directive = new ManeuverDirective(safeArea.PositionMeters, oncomingVehicle.Id, safeAreaType);
        return TrafficDecision.StartManeuver(
            directive,
            $"Retreat to {safeAreaType} at {safeArea.PositionMeters}m to resolve deadlock");
    }

    private static VehicleStorageArea? FindNearestAvailableSafeArea(Vehicle vehicle, Road road)
    {
        return road.Pockets
            .Cast<VehicleStorageArea>()
            .Concat(road.Depots)
            .Where(area => !area.IsFull)
            .OrderBy(area => Math.Abs(area.PositionMeters - vehicle.PositionMeters))
            .FirstOrDefault();
    }

    private static bool IsLowerPriority(Vehicle candidate, Vehicle other)
    {
        return string.CompareOrdinal(candidate.Id, other.Id) > 0;
    }

    private static bool CanExitSafeArea(Vehicle vehicle, IReadOnlyCollection<Vehicle> trafficState)
    {
        if (string.IsNullOrWhiteSpace(vehicle.YieldToVehicleId))
        {
            return true;
        }

        var yieldToVehicle = trafficState.FirstOrDefault(other => other.Id == vehicle.YieldToVehicleId);
        if (yieldToVehicle is null)
        {
            return true;
        }

        if (vehicle.ManeuverSafeAreaPositionMeters is null)
        {
            return true;
        }

        return Math.Abs(yieldToVehicle.PositionMeters - vehicle.ManeuverSafeAreaPositionMeters.Value) > (MinimumDistanceMeters * 2d);
    }

    private static bool ViolatesMinimumGapWithSameDirectionVehicles(
        Vehicle vehicle,
        double predictedPosition,
        IReadOnlyCollection<Vehicle> sameDirectionVehicles)
    {
        var leadingVehicleDistance = sameDirectionVehicles
            .Select(other => vehicle.Direction == VehicleDirection.LeftToRight
                ? other.PositionMeters - predictedPosition
                : predictedPosition - other.PositionMeters)
            .Where(distance => distance >= 0)
            .DefaultIfEmpty(double.MaxValue)
            .Min();

        return leadingVehicleDistance < MinimumDistanceMeters;
    }

    private static bool ViolatesMinimumGapWithOppositeDirectionVehicles(
        double predictedPosition,
        IReadOnlyCollection<Vehicle> oppositeDirectionVehicles)
    {
        var closestDistance = oppositeDirectionVehicles
            .Select(other => Math.Abs(other.PositionMeters - predictedPosition))
            .DefaultIfEmpty(double.MaxValue)
            .Min();

        return closestDistance < MinimumDistanceMeters;
    }

    private static bool TriggersDeadlockWait(
        Vehicle vehicle,
        IReadOnlyCollection<Vehicle> oppositeDirectionVehicles,
        Road road)
    {
        var oncomingVehicle = oppositeDirectionVehicles
            .Where(other => IsFacingEachOther(vehicle, other))
            .OrderBy(other => Math.Abs(other.PositionMeters - vehicle.PositionMeters))
            .FirstOrDefault();

        if (oncomingVehicle is null)
        {
            return false;
        }

        var segmentStart = Math.Min(vehicle.PositionMeters, oncomingVehicle.PositionMeters);
        var segmentEnd = Math.Max(vehicle.PositionMeters, oncomingVehicle.PositionMeters);

        var safeAreas = road.Pockets.Cast<VehicleStorageArea>()
            .Concat(road.Depots)
            .Where(area => area.PositionMeters > segmentStart && area.PositionMeters < segmentEnd)
            .ToList();

        if (safeAreas.Count > 0)
        {
            var vehicleHasReachableSafeArea = HasReachableSafeArea(vehicle, safeAreas);
            var oncomingHasReachableSafeArea = HasReachableSafeArea(oncomingVehicle, safeAreas);

            if (vehicleHasReachableSafeArea || oncomingHasReachableSafeArea)
            {
                return false;
            }
        }

        return string.CompareOrdinal(vehicle.Id, oncomingVehicle.Id) > 0;
    }

    private static bool HasReachableSafeArea(Vehicle vehicle, IReadOnlyCollection<VehicleStorageArea> safeAreas)
    {
        return safeAreas.Any(area =>
            !area.IsFull &&
            (vehicle.Direction == VehicleDirection.LeftToRight
                ? area.PositionMeters >= vehicle.PositionMeters
                : area.PositionMeters <= vehicle.PositionMeters));
    }

    private static bool IsFacingEachOther(Vehicle left, Vehicle right)
    {
        return left.Direction == VehicleDirection.LeftToRight &&
               right.Direction == VehicleDirection.RightToLeft &&
               left.PositionMeters < right.PositionMeters
               ||
               left.Direction == VehicleDirection.RightToLeft &&
               right.Direction == VehicleDirection.LeftToRight &&
               left.PositionMeters > right.PositionMeters;
    }

    private static double PredictNextPosition(Vehicle vehicle, Road road, double tickDurationSeconds)
    {
        var speedMetersPerSecond = vehicle.SpeedKmh * (1000d / 3600d);
        var directionSign = vehicle.Direction == VehicleDirection.LeftToRight ? 1d : -1d;
        var candidate = vehicle.PositionMeters + (speedMetersPerSecond * tickDurationSeconds * directionSign);

        return Math.Clamp(candidate, 0, road.LengthMeters);
    }
}
