namespace OtonomAracOptimizasyonu.Models;

public sealed class Vehicle
{
    public const double MaxSpeedKmh = 20d;

    public Vehicle(string id, VehicleDirection direction, double positionMeters, double speedKmh, int targetDepotPositionMeters)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Vehicle id cannot be empty.", nameof(id));
        }

        if (positionMeters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(positionMeters), "Vehicle position cannot be negative.");
        }

        if (speedKmh < 0 || speedKmh > MaxSpeedKmh)
        {
            throw new ArgumentOutOfRangeException(nameof(speedKmh), $"Vehicle speed must be in range [0, {MaxSpeedKmh}] km/h.");
        }

        if (targetDepotPositionMeters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetDepotPositionMeters), "Target depot position cannot be negative.");
        }

        Id = id;
        Direction = direction;
        PositionMeters = positionMeters;
        SpeedKmh = speedKmh;
        TargetDepotPositionMeters = targetDepotPositionMeters;
        OriginalTargetDepotPositionMeters = targetDepotPositionMeters;
        CurrentTask = VehicleTask.NormalDrive;
    }

    public string Id { get; }

    public VehicleDirection Direction { get; private set; }

    public double PositionMeters { get; private set; }

    public double SpeedKmh { get; private set; }

    public int TargetDepotPositionMeters { get; private set; }

    public VehicleTask CurrentTask { get; private set; }

    public int OriginalTargetDepotPositionMeters { get; private set; }

    public int? ManeuverSafeAreaPositionMeters { get; private set; }

    public string? YieldToVehicleId { get; private set; }

    public void UpdatePosition(double newPositionMeters)
    {
        if (newPositionMeters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(newPositionMeters), "Vehicle position cannot be negative.");
        }

        PositionMeters = newPositionMeters;
    }

    public void UpdateSpeed(double newSpeedKmh)
    {
        if (newSpeedKmh < 0 || newSpeedKmh > MaxSpeedKmh)
        {
            throw new ArgumentOutOfRangeException(nameof(newSpeedKmh), $"Vehicle speed must be in range [0, {MaxSpeedKmh}] km/h.");
        }

        SpeedKmh = newSpeedKmh;
    }

    public void UpdateDirection(VehicleDirection newDirection)
    {
        Direction = newDirection;
    }

    public void UpdateTargetDepot(int targetDepotPositionMeters)
    {
        if (targetDepotPositionMeters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetDepotPositionMeters), "Target depot position cannot be negative.");
        }

        TargetDepotPositionMeters = targetDepotPositionMeters;
    }

    public void StartRetreatManeuver(int safeAreaPositionMeters, string yieldToVehicleId)
    {
        if (safeAreaPositionMeters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(safeAreaPositionMeters));
        }

        if (string.IsNullOrWhiteSpace(yieldToVehicleId))
        {
            throw new ArgumentException("Yield vehicle id cannot be empty.", nameof(yieldToVehicleId));
        }

        if (CurrentTask == VehicleTask.NormalDrive)
        {
            OriginalTargetDepotPositionMeters = TargetDepotPositionMeters;
        }

        CurrentTask = VehicleTask.RetreatingToSafeArea;
        ManeuverSafeAreaPositionMeters = safeAreaPositionMeters;
        YieldToVehicleId = yieldToVehicleId;
        TargetDepotPositionMeters = safeAreaPositionMeters;
        Direction = safeAreaPositionMeters >= PositionMeters
            ? VehicleDirection.LeftToRight
            : VehicleDirection.RightToLeft;
    }

    public void MarkWaitingInSafeArea()
    {
        if (CurrentTask == VehicleTask.RetreatingToSafeArea || CurrentTask == VehicleTask.WaitingInSafeArea)
        {
            CurrentTask = VehicleTask.WaitingInSafeArea;
        }
    }

    public void ResumeOriginalMission()
    {
        CurrentTask = VehicleTask.NormalDrive;
        TargetDepotPositionMeters = OriginalTargetDepotPositionMeters;
        ManeuverSafeAreaPositionMeters = null;
        YieldToVehicleId = null;
        Direction = TargetDepotPositionMeters >= PositionMeters
            ? VehicleDirection.LeftToRight
            : VehicleDirection.RightToLeft;
    }
}
