namespace OtonomAracOptimizasyonu.Models;

public sealed class Vehicle
{
    public const double MaxSpeedKmh = 20d;
    private readonly HashSet<int> _visitedDepotPositions = [];
    private readonly Queue<int> _targetDepots;
    private int? _resumeTargetAfterYield;

    public Vehicle(
        string id,
        double speedKmh,
        IEnumerable<int> targetDepots,
        double spawnDelaySeconds = 0d,
        bool isPriority = false,
        bool singleMissionOnly = false)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Arac kimligi bos olamaz.", nameof(id));
        }

        if (speedKmh < 0 || speedKmh > MaxSpeedKmh)
        {
            throw new ArgumentOutOfRangeException(nameof(speedKmh), $"Arac hizi [0, {MaxSpeedKmh}] km/h araliginda olmalidir.");
        }

        ArgumentNullException.ThrowIfNull(targetDepots);
        var depots = targetDepots.ToList();
        if (depots.Count == 0)
        {
            throw new ArgumentException("En az bir hedef depo atanmalidir.", nameof(targetDepots));
        }

        if (depots.Any(position => position < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(targetDepots), "Hedef depo konumu negatif olamaz.");
        }

        if (spawnDelaySeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spawnDelaySeconds), "Spawn gecikmesi negatif olamaz.");
        }

        _targetDepots = new Queue<int>(depots);
        Id = id;
        Direction = VehicleDirection.LeftToRight;
        PositionMeters = 0d;
        HomePositionMeters = 0d;
        SpeedKmh = speedKmh;
        SpawnDelaySeconds = spawnDelaySeconds;
        SpawnDelayRemainingSeconds = spawnDelaySeconds;
        TargetDepotPositionMeters = _targetDepots.Peek();
        OriginalTargetDepotPositionMeters = TargetDepotPositionMeters;
        IsPriority = isPriority;
        SingleMissionOnly = singleMissionOnly;
        CurrentTask = VehicleTask.InGarage;
    }

    public string Id { get; }

    public VehicleDirection Direction { get; private set; }

    public double PositionMeters { get; private set; }

    public double HomePositionMeters { get; }

    public double SpeedKmh { get; private set; }

    public double SpawnDelaySeconds { get; }

    public double SpawnDelayRemainingSeconds { get; private set; }

    public IReadOnlyCollection<int> TargetDepots => _targetDepots.ToArray();

    public int RemainingTargetCount => _targetDepots.Count;

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

    public void DecreaseSpawnDelay(double seconds)
    {
        if (seconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        SpawnDelayRemainingSeconds = Math.Max(0d, SpawnDelayRemainingSeconds - seconds);
    }

    public bool IsSpawnDelayElapsed => SpawnDelayRemainingSeconds <= 0d;

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

        _resumeTargetAfterYield = TargetDepotPositionMeters;
        CurrentTask = VehicleTask.ReversingToPocket;
        ManeuverSafeAreaPositionMeters = safeAreaPositionMeters;
        YieldToVehicleId = yieldToVehicleId;
        TargetDepotPositionMeters = safeAreaPositionMeters;
        Direction = safeAreaPositionMeters >= PositionMeters
            ? VehicleDirection.LeftToRight
            : VehicleDirection.RightToLeft;
    }

    public void MarkWaitingInSafeArea()
    {
        if (CurrentTask == VehicleTask.ReversingToPocket || CurrentTask == VehicleTask.WaitingInPocket)
        {
            CurrentTask = VehicleTask.WaitingInPocket;
        }
    }

    public void ResumeOriginalMission(double cruiseSpeedKmh)
    {
        var resumeTarget = _resumeTargetAfterYield ?? (_targetDepots.Count > 0 ? _targetDepots.Peek() : 0);
        TargetDepotPositionMeters = resumeTarget;
        CurrentTask = resumeTarget == 0 ? VehicleTask.ReturningHome : VehicleTask.GoingToDepot;
        UpdateSpeed(cruiseSpeedKmh);
        ManeuverSafeAreaPositionMeters = null;
        YieldToVehicleId = null;
        _resumeTargetAfterYield = null;
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
        if (_targetDepots.Count > 0)
        {
            _targetDepots.Dequeue();
        }

        TargetDepotPositionMeters = _targetDepots.Count > 0 ? _targetDepots.Peek() : 0;
        CurrentTask = _targetDepots.Count > 0
            ? VehicleTask.GoingToDepot
            : VehicleTask.ReturningHome;
        Direction = TargetDepotPositionMeters >= PositionMeters
            ? VehicleDirection.LeftToRight
            : VehicleDirection.RightToLeft;
    }

    public void CompleteDeliveryCycle()
    {
        HasLoad = false;
        CompletedMissionCount++;
        CurrentTask = VehicleTask.Completed;
    }

    public void RegisterDepotVisit(int depotPositionMeters)
    {
        if (depotPositionMeters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(depotPositionMeters));
        }

        _visitedDepotPositions.Add(depotPositionMeters);
    }

    public void EnterGarage()
    {
        CurrentTask = VehicleTask.InGarage;
        UpdateSpeed(0d);
        UpdatePosition(0d);
        ManeuverSafeAreaPositionMeters = null;
        YieldToVehicleId = null;
    }

    public void DispatchToDepot(double cruiseSpeedKmh)
    {
        if (_targetDepots.Count == 0)
        {
            throw new InvalidOperationException("Araca depo atanmamis.");
        }

        UpdatePosition(0d);
        UpdateTargetDepot(_targetDepots.Peek());
        OriginalTargetDepotPositionMeters = TargetDepotPositionMeters;
        UpdateDirection(TargetDepotPositionMeters >= PositionMeters
            ? VehicleDirection.LeftToRight
            : VehicleDirection.RightToLeft);
        UpdateSpeed(cruiseSpeedKmh);
        CurrentTask = VehicleTask.GoingToDepot;
    }

    public void StartUnloadingAtHome()
    {
        CurrentTask = VehicleTask.UnloadingAtHome;
        UpdateSpeed(0d);
        UpdatePosition(0d);
    }

    public void StartReturningHome(double cruiseSpeedKmh)
    {
        UpdateTargetDepot(0);
        UpdateDirection(0 >= PositionMeters
            ? VehicleDirection.LeftToRight
            : VehicleDirection.RightToLeft);
        UpdateSpeed(cruiseSpeedKmh);
        CurrentTask = VehicleTask.ReturningHome;
    }

    public void StartReturningHome(int homePositionMeters, double cruiseSpeedKmh)
    {
        if (homePositionMeters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(homePositionMeters), "Baslangic konumu negatif olamaz.");
        }

        UpdateTargetDepot(homePositionMeters);
        UpdateDirection(homePositionMeters >= PositionMeters
            ? VehicleDirection.LeftToRight
            : VehicleDirection.RightToLeft);
        UpdateSpeed(cruiseSpeedKmh);
        CurrentTask = VehicleTask.ReturningHome;
    }
}
