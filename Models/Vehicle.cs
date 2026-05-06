namespace OtonomAracOptimizasyonu.Models;

public sealed class Vehicle
{
    public const double MaxSpeedKmh = 20d;
    private readonly HashSet<int> _visitedDepotPositions = [];

    public Vehicle(
        string id,
        VehicleDirection direction,
        double positionMeters,
        double speedKmh,
        int targetDepotPositionMeters,
        bool isPriority = false,
        bool singleMissionOnly = false)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Arac kimligi bos olamaz.", nameof(id));
        }

        if (positionMeters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(positionMeters), "Arac konumu negatif olamaz.");
        }

        if (speedKmh < 0 || speedKmh > MaxSpeedKmh)
        {
            throw new ArgumentOutOfRangeException(nameof(speedKmh), $"Arac hizi [0, {MaxSpeedKmh}] km/h araliginda olmalidir.");
        }

        if (targetDepotPositionMeters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetDepotPositionMeters), "Hedef depo konumu negatif olamaz.");
        }

        Id = id;
        Direction = direction;
        PositionMeters = positionMeters;
        SpeedKmh = speedKmh;
        TargetDepotPositionMeters = targetDepotPositionMeters;
        OriginalTargetDepotPositionMeters = targetDepotPositionMeters;
        IsPriority = isPriority;
        SingleMissionOnly = singleMissionOnly;
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

    public bool IsPriority { get; }

    public bool SingleMissionOnly { get; }

    public bool HasLoad { get; private set; }

    public int CompletedMissionCount { get; private set; }

    public IReadOnlyCollection<int> VisitedDepotPositions => _visitedDepotPositions;

    public void UpdatePosition(double newPositionMeters)
    {
        if (newPositionMeters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(newPositionMeters), "Arac konumu negatif olamaz.");
        }

        PositionMeters = newPositionMeters;
    }

    public void UpdateSpeed(double newSpeedKmh)
    {
        if (newSpeedKmh < 0 || newSpeedKmh > MaxSpeedKmh)
        {
            throw new ArgumentOutOfRangeException(nameof(newSpeedKmh), $"Arac hizi [0, {MaxSpeedKmh}] km/h araliginda olmalidir.");
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
            throw new ArgumentOutOfRangeException(nameof(targetDepotPositionMeters), "Hedef depo konumu negatif olamaz.");
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
            throw new ArgumentException("Yol verilecek arac kimligi bos olamaz.", nameof(yieldToVehicleId));
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

    public void StartDepotLoading()
    {
        CurrentTask = VehicleTask.LoadingAtDepot;
        UpdateSpeed(0d);
        RegisterDepotVisit((int)Math.Round(PositionMeters, MidpointRounding.AwayFromZero));
    }

    public void FinishLoadingAndCarryLoad()
    {
        HasLoad = true;
        CurrentTask = VehicleTask.NormalDrive;
    }

    public void CompleteDeliveryCycle()
    {
        HasLoad = false;
        CompletedMissionCount++;
        CurrentTask = VehicleTask.NormalDrive;
    }

    public void RegisterDepotVisit(int depotPositionMeters)
    {
        if (depotPositionMeters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(depotPositionMeters));
        }

        _visitedDepotPositions.Add(depotPositionMeters);
    }
}
