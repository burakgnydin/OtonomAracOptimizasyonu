using OtonomAracOptimizasyonu.Models;

namespace OtonomAracOptimizasyonu.Services;

public sealed class TrafficController
{
    private const double SensorVisibilityToleranceMeters = 20d;
    private const double SensorTimeoutGraceSeconds = 2d;
    private IReadOnlyCollection<Vehicle> _trafficState = [];
    private readonly Dictionary<string, VehicleObservationTracker> _trackers = new();
    private readonly Dictionary<string, VehicleRestrictedState> _restrictedStateByVehicleId = new();
    private int _tickIndex;

    public TrafficController(ITrafficStrategy trafficStrategy, double tickDurationSeconds = 1d)
    {
        ArgumentNullException.ThrowIfNull(trafficStrategy);

        if (tickDurationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tickDurationSeconds), "Tick suresi sifirdan buyuk olmalidir.");
        }

        TrafficStrategy = trafficStrategy;
        TickDurationSeconds = tickDurationSeconds;
    }

    public ITrafficStrategy TrafficStrategy { get; }

    public double TickDurationSeconds { get; }

    public void UpdateTrafficState(IReadOnlyCollection<Vehicle> trafficState, Road road)
    {
        ArgumentNullException.ThrowIfNull(road);
        _trafficState = trafficState ?? throw new ArgumentNullException(nameof(trafficState));
        _tickIndex++;

        var orderedSensors = road.Sensors.Select(sensor => sensor.PositionMeters).OrderBy(position => position).ToArray();
        var currentTime = TimeSpan.FromSeconds(_tickIndex * TickDurationSeconds);

        foreach (var vehicle in _trafficState)
        {
            if (!IsTrackableOnMainLane(vehicle))
            {
                _restrictedStateByVehicleId.Remove(vehicle.Id);
                continue;
            }

            if (!_trackers.TryGetValue(vehicle.Id, out var tracker))
            {
                tracker = new VehicleObservationTracker(vehicle.PositionMeters, vehicle.Direction, vehicle.SpeedKmh);
                _trackers[vehicle.Id] = tracker;
            }

            tracker.Update(vehicle, orderedSensors, currentTime, road.LengthMeters);
            _restrictedStateByVehicleId[vehicle.Id] = tracker.BuildRestrictedState(vehicle, orderedSensors, currentTime, road.LengthMeters);
        }
    }

    public bool CanMove(Vehicle vehicle, Road road)
    {
        return Evaluate(vehicle, road).CanMove;
    }

    public TrafficDecision Evaluate(Vehicle vehicle, Road road)
    {
        if (!_restrictedStateByVehicleId.TryGetValue(vehicle.Id, out var currentVehicleState))
        {
            throw new InvalidOperationException($"Arac {vehicle.Id} icin kisitli durum bulunamadi.");
        }

        var offMainLaneVehicleIds = _trafficState
            .Where(v => v.CurrentTask == VehicleTask.WaitingInPocket)
            .Select(v => v.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return TrafficStrategy.Evaluate(
            vehicle,
            road,
            currentVehicleState,
            _restrictedStateByVehicleId.Values
                .Where(state => state.VehicleId == vehicle.Id || !offMainLaneVehicleIds.Contains(state.VehicleId))
                .ToList(),
            TickDurationSeconds);
    }

    private sealed class VehicleObservationTracker
    {
        public VehicleObservationTracker(double initialPositionMeters, VehicleDirection initialDirection, double initialSpeedKmh)
        {
            PreviousObservedPositionMeters = initialPositionMeters;
            InferredDirection = initialDirection;
            LastKnownSpeedKmh = initialSpeedKmh;
        }

        public double PreviousObservedPositionMeters { get; private set; }

        public int? PreviousSensorId { get; private set; }

        public int? LastSensorId { get; private set; }

        public TimeSpan? PreviousSensorTriggeredAt { get; private set; }

        public TimeSpan? LastSensorTriggeredAt { get; private set; }

        public VehicleDirection InferredDirection { get; private set; }

        public double LastKnownSpeedKmh { get; private set; }

        public int? ActiveSensorId { get; private set; }

        public bool IsSensorSignalRed { get; private set; }

        public bool NextSensorTimedOut { get; private set; }

        public void Update(Vehicle vehicle, IReadOnlyList<int> orderedSensors, TimeSpan currentTime, int roadLengthMeters)
        {
            var crossedSensors = orderedSensors
                .Where(sensor => IsCrossed(PreviousObservedPositionMeters, vehicle.PositionMeters, sensor))
                .ToList();

            foreach (var sensorId in crossedSensors)
            {
                if (RegisterSensorTrigger(sensorId, currentTime))
                {
                    SimulationLogger.Log($"Arac {vehicle.Id}, {sensorId}m sensorunde algilandi; trajektori hesaplaniyor...");
                }
            }

            if (PreviousSensorId.HasValue && LastSensorId.HasValue && PreviousSensorId != LastSensorId)
            {
                InferredDirection = LastSensorId.Value > PreviousSensorId.Value
                    ? VehicleDirection.LeftToRight
                    : VehicleDirection.RightToLeft;
            }

            if (LastSensorId is null)
            {
                LastSensorId = FindNearestSensor(orderedSensors, vehicle.PositionMeters, SensorVisibilityToleranceMeters);
                if (LastSensorId.HasValue)
                {
                    LastSensorTriggeredAt = currentTime;
                }
            }

            LastKnownSpeedKmh = vehicle.SpeedKmh;
            PreviousObservedPositionMeters = Math.Clamp(vehicle.PositionMeters, 0d, roadLengthMeters);
            RefreshSensorSignal(orderedSensors, currentTime);
        }

        public VehicleRestrictedState BuildRestrictedState(Vehicle vehicle, IReadOnlyList<int> orderedSensors, TimeSpan currentTime, int roadLengthMeters)
        {
            ArgumentNullException.ThrowIfNull(vehicle);
            var estimatedPosition = EstimatePosition(currentTime);
            estimatedPosition = Math.Clamp(estimatedPosition, 0d, roadLengthMeters);
            var insideSensorZone = orderedSensors.Any(sensor => Math.Abs(sensor - estimatedPosition) <= SensorVisibilityToleranceMeters);

            return new VehicleRestrictedState(
                VehicleId: vehicle.Id,
                IsPriority: vehicle.IsPriority,
                HasLoad: vehicle.HasLoad,
                PreviousSensorId: PreviousSensorId,
                LastSensorId: LastSensorId,
                PreviousSensorTriggeredAt: PreviousSensorTriggeredAt,
                LastSensorTriggeredAt: LastSensorTriggeredAt,
                ActiveSensorId: ActiveSensorId,
                IsSensorSignalRed: IsSensorSignalRed,
                NextSensorTimedOut: NextSensorTimedOut,
                InferredDirection: InferredDirection,
                LastKnownSpeedKmh: LastKnownSpeedKmh,
                EstimatedPositionMeters: estimatedPosition,
                PositionUncertaintyMeters: EstimateUncertainty(currentTime),
                IsInsideSensorZone: insideSensorZone);
        }

        private double EstimatePosition(TimeSpan currentTime)
        {
            if (LastSensorId is null || LastSensorTriggeredAt is null)
            {
                return PreviousObservedPositionMeters;
            }

            var elapsedSeconds = Math.Max(0d, (currentTime - LastSensorTriggeredAt.Value).TotalSeconds);
            var speedMetersPerSecond = LastKnownSpeedKmh * (1000d / 3600d);
            var directionSign = InferredDirection == VehicleDirection.LeftToRight ? 1d : -1d;
            return LastSensorId.Value + (speedMetersPerSecond * elapsedSeconds * directionSign);
        }

        private double EstimateUncertainty(TimeSpan currentTime)
        {
            if (LastSensorTriggeredAt is null)
            {
                return 40d;
            }

            var elapsedSeconds = Math.Max(0d, (currentTime - LastSensorTriggeredAt.Value).TotalSeconds);
            var driftMeters = LastKnownSpeedKmh * (1000d / 3600d) * elapsedSeconds;
            return Math.Max(2d, Math.Min(40d, 2d + (driftMeters * 0.35d)));
        }

        private bool RegisterSensorTrigger(int sensorId, TimeSpan currentTime)
        {
            if (LastSensorId == sensorId && LastSensorTriggeredAt.HasValue)
            {
                return false;
            }

            PreviousSensorId = LastSensorId;
            PreviousSensorTriggeredAt = LastSensorTriggeredAt;
            LastSensorId = sensorId;
            LastSensorTriggeredAt = currentTime;
            return true;
        }

        private void RefreshSensorSignal(IReadOnlyList<int> orderedSensors, TimeSpan currentTime)
        {
            ActiveSensorId = LastSensorId;
            IsSensorSignalRed = false;
            NextSensorTimedOut = false;

            if (LastSensorId is null || LastSensorTriggeredAt is null)
            {
                return;
            }

            var estimatedPosition = EstimatePosition(currentTime);
            var distanceToActiveSensor = Math.Abs(estimatedPosition - LastSensorId.Value);
            IsSensorSignalRed = distanceToActiveSensor <= SensorVisibilityToleranceMeters;
            if (!IsSensorSignalRed)
            {
                ActiveSensorId = null;
                return;
            }

            var nextSensor = FindNextSensor(orderedSensors, LastSensorId.Value, InferredDirection);
            if (nextSensor is null)
            {
                return;
            }

            var speedMetersPerSecond = LastKnownSpeedKmh * (1000d / 3600d);
            if (speedMetersPerSecond < 0.05d)
            {
                return;
            }

            var distanceToNextSensor = Math.Abs(nextSensor.Value - LastSensorId.Value);
            var expectedTravelSeconds = (distanceToNextSensor / speedMetersPerSecond) + SensorTimeoutGraceSeconds;
            var elapsedSeconds = Math.Max(0d, (currentTime - LastSensorTriggeredAt.Value).TotalSeconds);
            NextSensorTimedOut = elapsedSeconds > expectedTravelSeconds;
        }

        private static bool IsCrossed(double previousPosition, double currentPosition, int sensorPosition)
        {
            if (Math.Abs(currentPosition - previousPosition) < 0.0001d)
            {
                return false;
            }

            var min = Math.Min(previousPosition, currentPosition);
            var max = Math.Max(previousPosition, currentPosition);
            return sensorPosition >= min && sensorPosition <= max;
        }

        private static int? FindNearestSensor(IReadOnlyList<int> orderedSensors, double positionMeters, double toleranceMeters)
        {
            var nearest = orderedSensors
                .Select(sensor => new { Sensor = sensor, Distance = Math.Abs(sensor - positionMeters) })
                .OrderBy(entry => entry.Distance)
                .FirstOrDefault();

            return nearest is null || nearest.Distance > toleranceMeters
                ? null
                : nearest.Sensor;
        }

        private static int? FindNextSensor(IReadOnlyList<int> orderedSensors, int lastSensor, VehicleDirection direction)
        {
            if (direction == VehicleDirection.LeftToRight)
            {
                return orderedSensors
                    .Where(sensor => sensor > lastSensor)
                    .Cast<int?>()
                    .FirstOrDefault();
            }

            var candidates = orderedSensors.Where(sensor => sensor < lastSensor).ToList();
            return candidates.Count == 0 ? null : candidates.Max();
        }
    }

    private static bool IsTrackableOnMainLane(Vehicle vehicle)
    {
        return vehicle.CurrentTask == VehicleTask.GoingToDepot ||
               vehicle.CurrentTask == VehicleTask.ReturningHome ||
               vehicle.CurrentTask == VehicleTask.GoingToPocketForYielding ||
               vehicle.CurrentTask == VehicleTask.WaitingInPocket;
    }
}
