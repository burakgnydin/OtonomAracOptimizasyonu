using OtonomAracOptimizasyonu.Models;

namespace OtonomAracOptimizasyonu.Services;

public sealed class SafetyFirstTrafficStrategy : ITrafficStrategy
{
    public const double MinimumDistanceMeters = 20d;
    private const double ProactiveManeuverWindowMeters = 60d;
    private const double UncertaintyBufferMultiplier = 0.5d;
    private const string AssumedOncomingVehicleId = "ASSUMED_ONCOMING";

    public TrafficDecision Evaluate(
        Vehicle vehicle,
        Road road,
        VehicleRestrictedState currentVehicleState,
        IReadOnlyCollection<VehicleRestrictedState> trafficState,
        double tickDurationSeconds)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(road);
        ArgumentNullException.ThrowIfNull(currentVehicleState);
        ArgumentNullException.ThrowIfNull(trafficState);

        if (tickDurationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tickDurationSeconds), "Tick suresi sifirdan buyuk olmalidir.");
        }

        if (vehicle.CurrentTask == VehicleTask.WaitingInSafeArea)
        {
            return CanExitSafeArea(vehicle, trafficState)
                ? TrafficDecision.MoveAllowed()
                : TrafficDecision.Stop(TrafficStopReason.DeadlockAvoidance, "Guvenli alanda bekliyor");
        }

        if (vehicle.CurrentTask == VehicleTask.RetreatingToSafeArea)
        {
            return TrafficDecision.MoveAllowed();
        }

        if (vehicle.CurrentTask == VehicleTask.LoadingAtDepot)
        {
            return TrafficDecision.Stop(TrafficStopReason.None, "Yukleme islemi devam ediyor");
        }

        if (ShouldStartSensorBasedManeuver(vehicle, currentVehicleState))
        {
            var timeoutManeuverDecision = CreateSensorTimeoutManeuverDecision(currentVehicleState, road);
            if (timeoutManeuverDecision is not null)
            {
                return timeoutManeuverDecision;
            }
        }

        var predictedPosition = PredictNextPosition(currentVehicleState, road, tickDurationSeconds);
        var requiredGap = DynamicMinimumGap(currentVehicleState.PositionUncertaintyMeters);
        var sameDirectionVehicles = trafficState
            .Where(x => x.VehicleId != vehicle.Id && x.InferredDirection == currentVehicleState.InferredDirection)
            .ToList();

        if (ViolatesMinimumGapWithSameDirectionVehicles(currentVehicleState, predictedPosition, requiredGap, sameDirectionVehicles))
        {
            return TrafficDecision.Stop(TrafficStopReason.CollisionRisk, "Carpisma riski (ayni yon)");
        }

        var oppositeDirectionVehicles = trafficState
            .Where(x => x.VehicleId != vehicle.Id && x.InferredDirection != currentVehicleState.InferredDirection)
            .ToList();

        var oncomingVehicle = oppositeDirectionVehicles
            .Where(other => IsFacingEachOther(currentVehicleState, other))
            .OrderBy(other => Math.Abs(other.EstimatedPositionMeters - currentVehicleState.EstimatedPositionMeters))
            .FirstOrDefault();

        var proactiveManeuverDecision = TryCreateProactiveManeuverDecision(vehicle, currentVehicleState, oncomingVehicle, road);
        if (proactiveManeuverDecision is not null)
        {
            return proactiveManeuverDecision;
        }

        if (ViolatesMinimumGapWithOppositeDirectionVehicles(predictedPosition, requiredGap, oppositeDirectionVehicles))
        {
            var maneuverDecision = TryCreateManeuverDecision(vehicle, currentVehicleState, oncomingVehicle, road);
            if (maneuverDecision is not null)
            {
                return maneuverDecision;
            }

            return TrafficDecision.Stop(TrafficStopReason.CollisionRisk, "Carpisma riski (zit yon)");
        }

        if (TriggersDeadlockWait(vehicle, currentVehicleState, oppositeDirectionVehicles, road))
        {
            return TrafficDecision.Stop(TrafficStopReason.DeadlockAvoidance, "Kilitlenme onleme");
        }

        return TrafficDecision.MoveAllowed();
    }

    private static TrafficDecision? TryCreateManeuverDecision(
        Vehicle vehicle,
        VehicleRestrictedState currentVehicleState,
        VehicleRestrictedState? oncomingVehicle,
        Road road)
    {
        if (oncomingVehicle is null || !IsLowerPriority(vehicle, oncomingVehicle))
        {
            return null;
        }

        if (vehicle.CurrentTask != VehicleTask.NormalDrive)
        {
            return null;
        }

        var safeArea = FindNearestAvailableSafeArea(vehicle.Id, currentVehicleState, road);
        if (safeArea is null)
        {
            return null;
        }

        var safeAreaType = safeArea is Pocket ? "Cep" : "Depo";
        var directive = new ManeuverDirective(safeArea.PositionMeters, oncomingVehicle.VehicleId, safeAreaType);
        return TrafficDecision.StartManeuver(
            directive,
            $"Kilitlenmeyi cozmek icin {safeArea.PositionMeters}m konumundaki {safeAreaType} alanina geri cekil");
    }

    private static VehicleStorageArea? FindNearestAvailableSafeArea(string vehicleId, VehicleRestrictedState currentVehicleState, Road road)
    {
        return road.Pockets
            .Cast<VehicleStorageArea>()
            .Concat(road.Depots)
            .Where(area => !area.IsFullyAllocated || area.HasReservation(vehicleId))
            .OrderBy(area => Math.Abs(area.PositionMeters - currentVehicleState.EstimatedPositionMeters))
            .FirstOrDefault();
    }

    private static bool IsLowerPriority(Vehicle candidate, VehicleRestrictedState other)
    {
        if (candidate.IsPriority)
        {
            return false;
        }

        return string.CompareOrdinal(candidate.Id, other.VehicleId) > 0;
    }

    private static bool CanExitSafeArea(Vehicle vehicle, IReadOnlyCollection<VehicleRestrictedState> trafficState)
    {
        if (string.IsNullOrWhiteSpace(vehicle.YieldToVehicleId))
        {
            return true;
        }

        var yieldToVehicle = trafficState.FirstOrDefault(other => other.VehicleId == vehicle.YieldToVehicleId);
        if (yieldToVehicle is null)
        {
            return true;
        }

        if (vehicle.ManeuverSafeAreaPositionMeters is null)
        {
            return true;
        }

        return Math.Abs(yieldToVehicle.EstimatedPositionMeters - vehicle.ManeuverSafeAreaPositionMeters.Value) > (MinimumDistanceMeters * 2d);
    }

    private static TrafficDecision? TryCreateProactiveManeuverDecision(
        Vehicle vehicle,
        VehicleRestrictedState currentVehicleState,
        VehicleRestrictedState? oncomingVehicle,
        Road road)
    {
        if (oncomingVehicle is null || !IsLowerPriority(vehicle, oncomingVehicle))
        {
            return null;
        }

        var distance = Math.Abs(oncomingVehicle.EstimatedPositionMeters - currentVehicleState.EstimatedPositionMeters);
        if (distance > ProactiveManeuverWindowMeters)
        {
            return null;
        }

        if (HasSafeAreaBetween(currentVehicleState, oncomingVehicle, road))
        {
            return null;
        }

        var maneuverDecision = TryCreateManeuverDecision(vehicle, currentVehicleState, oncomingVehicle, road);
        if (maneuverDecision is null)
        {
            return null;
        }

        return maneuverDecision with
        {
            Message = $"Proaktif kilitlenme onleme: {maneuverDecision.Message}"
        };
    }

    private static bool HasSafeAreaBetween(
        VehicleRestrictedState currentVehicleState,
        VehicleRestrictedState oncomingVehicle,
        Road road)
    {
        var segmentStart = Math.Min(currentVehicleState.EstimatedPositionMeters, oncomingVehicle.EstimatedPositionMeters);
        var segmentEnd = Math.Max(currentVehicleState.EstimatedPositionMeters, oncomingVehicle.EstimatedPositionMeters);

        return road.Pockets.Cast<VehicleStorageArea>()
            .Concat(road.Depots)
            .Any(area => area.PositionMeters > segmentStart && area.PositionMeters < segmentEnd);
    }

    private static bool ShouldStartSensorBasedManeuver(Vehicle vehicle, VehicleRestrictedState state)
    {
        if (!state.IsSensorSignalRed || !state.NextSensorTimedOut)
        {
            return false;
        }

        return vehicle.CurrentTask == VehicleTask.NormalDrive;
    }

    private static TrafficDecision? CreateSensorTimeoutManeuverDecision(VehicleRestrictedState currentVehicleState, Road road)
    {
        var safeArea = FindNearestAvailableSafeArea(currentVehicleState.VehicleId, currentVehicleState, road);
        if (safeArea is null)
        {
            return null;
        }

        var safeAreaType = safeArea is Pocket ? "Cep" : "Depo";
        var directive = new ManeuverDirective(safeArea.PositionMeters, AssumedOncomingVehicleId, safeAreaType);
        SimulationLogger.Log(
            $"Arac {currentVehicleState.VehicleId}, {currentVehicleState.ActiveSensorId}m sensorunde zaman asimina ugradi. " +
            $"Karsi trafik varsayiliyor; {safeArea.PositionMeters}m konumundaki {safeAreaType} alanina manevra yapiliyor.");
        return TrafficDecision.StartManeuver(
            directive,
            $"Sensor zaman asimi ({currentVehicleState.ActiveSensorId}m), {safeArea.PositionMeters}m konumundaki {safeAreaType} alanina geri cekiliyor");
    }

    private static bool ViolatesMinimumGapWithSameDirectionVehicles(
        VehicleRestrictedState currentVehicleState,
        double predictedPosition,
        double requiredGapMeters,
        IReadOnlyCollection<VehicleRestrictedState> sameDirectionVehicles)
    {
        var leadingVehicleDistance = sameDirectionVehicles
            .Select(other => currentVehicleState.InferredDirection == VehicleDirection.LeftToRight
                ? other.EstimatedPositionMeters - predictedPosition
                : predictedPosition - other.EstimatedPositionMeters)
            .Where(distance => distance >= 0)
            .DefaultIfEmpty(double.MaxValue)
            .Min();

        return leadingVehicleDistance < requiredGapMeters;
    }

    private static bool ViolatesMinimumGapWithOppositeDirectionVehicles(
        double predictedPosition,
        double requiredGapMeters,
        IReadOnlyCollection<VehicleRestrictedState> oppositeDirectionVehicles)
    {
        var closestDistance = oppositeDirectionVehicles
            .Select(other => Math.Abs(other.EstimatedPositionMeters - predictedPosition))
            .DefaultIfEmpty(double.MaxValue)
            .Min();

        return closestDistance < requiredGapMeters;
    }

    private static bool TriggersDeadlockWait(
        Vehicle vehicle,
        VehicleRestrictedState currentVehicleState,
        IReadOnlyCollection<VehicleRestrictedState> oppositeDirectionVehicles,
        Road road)
    {
        var oncomingVehicle = oppositeDirectionVehicles
            .Where(other => IsFacingEachOther(currentVehicleState, other))
            .OrderBy(other => Math.Abs(other.EstimatedPositionMeters - currentVehicleState.EstimatedPositionMeters))
            .FirstOrDefault();

        if (oncomingVehicle is null)
        {
            return false;
        }

        var segmentStart = Math.Min(currentVehicleState.EstimatedPositionMeters, oncomingVehicle.EstimatedPositionMeters);
        var segmentEnd = Math.Max(currentVehicleState.EstimatedPositionMeters, oncomingVehicle.EstimatedPositionMeters);

        var safeAreas = road.Pockets.Cast<VehicleStorageArea>()
            .Concat(road.Depots)
            .Where(area => area.PositionMeters > segmentStart && area.PositionMeters < segmentEnd)
            .ToList();

        if (safeAreas.Count > 0)
        {
            var vehicleHasReachableSafeArea = HasReachableSafeArea(currentVehicleState, safeAreas);
            var oncomingHasReachableSafeArea = HasReachableSafeArea(oncomingVehicle, safeAreas);

            if (vehicleHasReachableSafeArea || oncomingHasReachableSafeArea)
            {
                return false;
            }
        }

        return string.CompareOrdinal(vehicle.Id, oncomingVehicle.VehicleId) > 0;
    }

    private static bool HasReachableSafeArea(VehicleRestrictedState vehicle, IReadOnlyCollection<VehicleStorageArea> safeAreas)
    {
        return safeAreas.Any(area =>
            !area.IsFull &&
            (vehicle.InferredDirection == VehicleDirection.LeftToRight
                ? area.PositionMeters >= vehicle.EstimatedPositionMeters
                : area.PositionMeters <= vehicle.EstimatedPositionMeters));
    }

    private static bool IsFacingEachOther(VehicleRestrictedState left, VehicleRestrictedState right)
    {
        return left.InferredDirection == VehicleDirection.LeftToRight &&
               right.InferredDirection == VehicleDirection.RightToLeft &&
               left.EstimatedPositionMeters < right.EstimatedPositionMeters
               ||
               left.InferredDirection == VehicleDirection.RightToLeft &&
               right.InferredDirection == VehicleDirection.LeftToRight &&
               left.EstimatedPositionMeters > right.EstimatedPositionMeters;
    }

    private static double PredictNextPosition(VehicleRestrictedState vehicle, Road road, double tickDurationSeconds)
    {
        var speedMetersPerSecond = vehicle.LastKnownSpeedKmh * (1000d / 3600d);
        var directionSign = vehicle.InferredDirection == VehicleDirection.LeftToRight ? 1d : -1d;
        var candidate = vehicle.EstimatedPositionMeters + (speedMetersPerSecond * tickDurationSeconds * directionSign);

        return Math.Clamp(candidate, 0, road.LengthMeters);
    }

    private static double DynamicMinimumGap(double uncertaintyMeters)
    {
        return MinimumDistanceMeters + (uncertaintyMeters * UncertaintyBufferMultiplier);
    }
}
