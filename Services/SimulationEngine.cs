using OtonomAracOptimizasyonu.Models;

namespace OtonomAracOptimizasyonu.Services;

public sealed class SimulationEngine
{
    private const double DepotReachToleranceMeters = 0.001d;
    private const double SensorRangeMeters = 20d;
    private const double OppositeConflictRangeMeters = 30d;
    private const int MaxYieldWaitTicks = 12;
    private const int DepotLoadingTicks = 3;
    private const int HomeUnloadingTicks = 2;
    private readonly double _minimumDistanceMeters;
    private readonly double _conflictDetectionRangeMeters;
    private readonly Dictionary<string, double> _cruiseSpeedByVehicleId;
    private readonly Dictionary<string, int> _loadingTicksRemainingByVehicleId = new();
    private readonly Dictionary<string, int> _unloadingTicksRemainingByVehicleId = new();
    private readonly Dictionary<string, int> _yieldWaitStartedTickByVehicleId = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _conflictPairLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Vehicle> _vehicles;
    private bool _startupLogged;

    public SimulationEngine(
        Road road,
        IEnumerable<Vehicle> vehicles,
        TrafficController trafficController,
        bool enableReturnTrip = true,
        double minimumSafeDistanceMeters = 20d,
        double conflictDetectionRangeMeters = 20d)
    {
        ArgumentNullException.ThrowIfNull(road);
        ArgumentNullException.ThrowIfNull(vehicles);
        ArgumentNullException.ThrowIfNull(trafficController);

        if (minimumSafeDistanceMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumSafeDistanceMeters), "Minimum guvenli mesafe sifirdan buyuk olmalidir.");
        }

        if (conflictDetectionRangeMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(conflictDetectionRangeMeters), "Catisma tespit araligi sifirdan buyuk olmalidir.");
        }

        Road = road;
        TrafficController = trafficController;
        EnableReturnTrip = enableReturnTrip;
        _minimumDistanceMeters = minimumSafeDistanceMeters;
        _conflictDetectionRangeMeters = conflictDetectionRangeMeters;
        _vehicles = vehicles.ToList();
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
        TickCount++;
        if (!_startupLogged)
        {
            _startupLogged = true;
            SimulationLogger.Log($"Simulasyon basladi. Toplam arac={_vehicles.Count}");
        }

        DispatchVehiclesFromGarageWithStagger();
        RefreshStorageOccupancy();
        _conflictPairLocks.Clear();
        ResolveProactiveOncomingConflicts();
        ResolveImmediateMainLaneConflicts();
        var vehicleStates = new List<VehicleTickState>(_vehicles.Count);

        foreach (var vehicle in _vehicles)
        {
            if (vehicle.CurrentTask == VehicleTask.InGarage)
            {
                vehicleStates.Add(CreateTickState(vehicle, TrafficStopReason.None, $"Spawn gecikmesi bekleniyor ({vehicle.SpawnDelayRemainingSeconds:0.0}s)"));
                continue;
            }

            if (vehicle.CurrentTask == VehicleTask.Completed)
            {
                vehicleStates.Add(CreateTickState(vehicle, TrafficStopReason.None, "Gorevi tamamladi"));
                continue;
            }

            if (vehicle.CurrentTask == VehicleTask.LoadingAtDepot)
            {
                vehicleStates.Add(CreateTickState(vehicle, TrafficStopReason.None, ProcessDepotLoading(vehicle)));
                continue;
            }

            if (vehicle.CurrentTask == VehicleTask.UnloadingAtHome)
            {
                vehicleStates.Add(CreateTickState(vehicle, TrafficStopReason.None, ProcessHomeUnloading(vehicle)));
                continue;
            }

            if (vehicle.CurrentTask == VehicleTask.WaitingForClearance)
            {
                vehicle.UpdateSpeed(0d);
                vehicleStates.Add(CreateTickState(vehicle, TrafficStopReason.DeadlockAvoidance, "Yol acilmasini bekliyor"));
                continue;
            }

            if (vehicle.CurrentTask is VehicleTask.WaitingInPocket or VehicleTask.WaitingInDepot)
            {
                if (!HasClearanceToResume(vehicle) && !HasWaitTimedOut(vehicle))
                {
                    vehicleStates.Add(CreateTickState(vehicle, TrafficStopReason.DeadlockAvoidance, "Cepte bekliyor, gecis acilmadi"));
                    continue;
                }

                ReleaseSafeAreaReservation(vehicle);
                vehicle.ResumeOriginalMission(_cruiseSpeedByVehicleId[vehicle.Id]);
                _yieldWaitStartedTickByVehicleId.Remove(vehicle.Id);
                SimulationLogger.Log($"Arac {vehicle.Id}, gecis acildigi icin cepten cikarak gorevine dondu.");
            }

            if (HasReachedTarget(vehicle))
            {
                if (vehicle.CurrentTask == VehicleTask.ReversingToPocket)
                {
                    vehicle.MarkWaitingInSafeArea();
                    _yieldWaitStartedTickByVehicleId[vehicle.Id] = TickCount;
                    NotifyPasserIfWaiting(vehicle);
                    vehicleStates.Add(CreateTickState(vehicle, TrafficStopReason.DeadlockAvoidance, "Cepte/depoda yol veriyor"));
                    continue;
                }

                HandleVehicleAtTarget(vehicle);
                vehicleStates.Add(CreateTickState(vehicle, TrafficStopReason.None, "Hedefe ulasti"));
                continue;
            }

            // CRITICAL: ReversingToPocket araci cebe kadar KESINTISIZ hareket etmeli (sensor stop yok).
            if (vehicle.CurrentTask == VehicleTask.ReversingToPocket)
            {
                var reverseNext = CalculateNextPosition(vehicle);
                vehicle.UpdatePosition(reverseNext);
                vehicleStates.Add(CreateTickState(vehicle, TrafficStopReason.None, "Geri vites ile cebe/depoya gidiyor"));
                continue;
            }

            if (ShouldStopForLeadingVehicle(vehicle))
            {
                vehicle.UpdateSpeed(0d);
                vehicleStates.Add(CreateTickState(vehicle, TrafficStopReason.CollisionRisk, "Onunde duran arac var, takip mesafesini koruyor"));
                continue;
            }

            var nextPosition = CalculateNextPosition(vehicle);
            nextPosition = EnforceHardDistanceConstraint(vehicle, nextPosition);
            vehicle.UpdatePosition(nextPosition);
            vehicleStates.Add(CreateTickState(vehicle, TrafficStopReason.None, "Hareket ediyor"));
        }

        RefreshStorageOccupancy();
        var simulationTime = TimeSpan.FromSeconds(TickCount * TrafficController.TickDurationSeconds);
        return new SimulationTickReport(TickCount, simulationTime, vehicleStates);
    }

    private void DispatchVehiclesFromGarageWithStagger()
    {
        var waiting = _vehicles
            .Where(v => v.CurrentTask == VehicleTask.InGarage)
            .OrderBy(v => v.SpawnDelayRemainingSeconds)
            .ThenBy(v => v.Id)
            .ToList();

        foreach (var vehicle in waiting)
        {
            vehicle.DecreaseSpawnDelay(TrafficController.TickDurationSeconds);
            if (!vehicle.IsSpawnDelayElapsed || !IsZeroEntrySegmentClear())
            {
                continue;
            }

            vehicle.DispatchToDepot(_cruiseSpeedByVehicleId[vehicle.Id]);
            SimulationLogger.Log($"{vehicle.Id} yola cikti (0m), hedef D{vehicle.TargetDepotPositionMeters}");
        }
    }

    private void ResolveImmediateMainLaneConflicts()
    {
        var mainLaneVehicles = _vehicles.Where(IsOnMainLane).ToList();
        for (var i = 0; i < mainLaneVehicles.Count; i++)
        {
            for (var j = i + 1; j < mainLaneVehicles.Count; j++)
            {
                var a = mainLaneVehicles[i];
                var b = mainLaneVehicles[j];
                if (a.Direction == b.Direction || !IsFacingEachOther(a, b))
                {
                    continue;
                }

                var gap = Math.Abs(a.PositionMeters - b.PositionMeters);
                if (gap > _minimumDistanceMeters)
                {
                    continue;
                }

                // Hard physical wall: birbirinin icinden gecis yasak.
                if (a.CurrentTask != VehicleTask.ReversingToPocket) a.UpdateSpeed(0d);
                if (b.CurrentTask != VehicleTask.ReversingToPocket) b.UpdateSpeed(0d);
                ResolveConflict(a, b);
            }
        }
    }

    private void ResolveProactiveOncomingConflicts()
    {
        var mainLaneVehicles = _vehicles.Where(IsOnMainLane).ToList();
        for (var i = 0; i < mainLaneVehicles.Count; i++)
        {
            for (var j = i + 1; j < mainLaneVehicles.Count; j++)
            {
                var a = mainLaneVehicles[i];
                var b = mainLaneVehicles[j];
                if (a.Direction == b.Direction || !IsFacingEachOther(a, b))
                {
                    continue;
                }

                var gap = Math.Abs(a.PositionMeters - b.PositionMeters);
                if (gap > OppositeConflictRangeMeters || gap <= _minimumDistanceMeters)
                {
                    continue;
                }

                ResolveConflict(a, b);
            }
        }
    }

    private void ResolveConflict(Vehicle a, Vehicle b)
    {
        if (IsConflictLocked(a) || IsConflictLocked(b))
        {
            return;
        }

        // One-shot lock per pair per tick (prevents A/B swapped double decision)
        var pairKey = CreatePairKey(a.Id, b.Id);
        if (!_conflictPairLocks.Add(pairKey))
        {
            return;
        }

        var yielder = DetermineYielder(a, b);
        if (yielder is null)
        {
            return;
        }

        var passer = yielder.Id == a.Id ? b : a;
        if (yielder.CurrentTask is VehicleTask.WaitingInPocket or VehicleTask.WaitingInDepot)
        {
            return;
        }

        // Yielder zaten geri cekiliyorsa tekrar karar verme.
        if (yielder.CurrentTask == VehicleTask.ReversingToPocket)
        {
            return;
        }

        // One-shot decision lock: karar verildiyse passer bekler, tekrar resolve tetiklenmez.
        if (passer.CurrentTask != VehicleTask.WaitingForClearance)
        {
            passer.StartWaitingForClearance(yielder.Id);
        }

        if (!TryAutoPullOver(yielder, passer.Id))
        {
            // Alan yoksa hard-stop: fiziksel duvar gibi beklesinler.
            yielder.UpdateSpeed(0d);
            passer.UpdateSpeed(0d);
            return;
        }

        TriggerChainYieldForFollowers(yielder, passer.Id);
    }

    private static bool IsConflictLocked(Vehicle vehicle)
    {
        return vehicle.CurrentTask is VehicleTask.WaitingForClearance
            or VehicleTask.ReversingToPocket
            or VehicleTask.WaitingInPocket
            or VehicleTask.WaitingInDepot
            or VehicleTask.LoadingAtDepot
            or VehicleTask.UnloadingAtHome;
    }

    private static string CreatePairKey(string aId, string bId)
    {
        return string.CompareOrdinal(aId, bId) <= 0 ? $"{aId}|{bId}" : $"{bId}|{aId}";
    }

    private bool IsZeroEntrySegmentClear()
    {
        return _vehicles
            .Where(IsOnMainLane)
            .All(v => v.PositionMeters >= _minimumDistanceMeters);
    }

    private void HandleVehicleAtTarget(Vehicle vehicle)
    {
        var atHome = Math.Abs(vehicle.PositionMeters) < DepotReachToleranceMeters;
        var atDepot = vehicle.CurrentTask == VehicleTask.GoingToDepot &&
                      Math.Abs(vehicle.PositionMeters - vehicle.TargetDepotPositionMeters) < DepotReachToleranceMeters;

        if (atDepot)
        {
            vehicle.StartDepotLoading();
            _loadingTicksRemainingByVehicleId[vehicle.Id] = DepotLoadingTicks;
            return;
        }

        if (vehicle.CurrentTask == VehicleTask.ReturningHome && atHome && vehicle.HasLoad)
        {
            vehicle.StartUnloadingAtHome();
            _unloadingTicksRemainingByVehicleId[vehicle.Id] = HomeUnloadingTicks;
        }
    }

    private bool HasReachedTarget(Vehicle vehicle)
    {
        return Math.Abs(vehicle.PositionMeters - vehicle.TargetDepotPositionMeters) < DepotReachToleranceMeters;
    }

    private double CalculateNextPosition(Vehicle vehicle)
    {
        var distanceMeters = vehicle.SpeedKmh * (1000d / 3600d) * TrafficController.TickDurationSeconds;
        var directionSign = vehicle.Direction == VehicleDirection.LeftToRight ? 1d : -1d;
        var bounded = Math.Clamp(vehicle.PositionMeters + (distanceMeters * directionSign), 0d, Road.LengthMeters);
        var passedTarget = vehicle.Direction == VehicleDirection.LeftToRight
            ? bounded >= vehicle.TargetDepotPositionMeters
            : bounded <= vehicle.TargetDepotPositionMeters;
        return passedTarget ? vehicle.TargetDepotPositionMeters : bounded;
    }

    private double EnforceHardDistanceConstraint(Vehicle vehicle, double candidatePosition)
    {
        var boundedCandidate = Math.Clamp(candidatePosition, 0d, Road.LengthMeters);
        if (!IsOnMainLane(vehicle))
        {
            return boundedCandidate;
        }

        // Hard stop ISTISNA: geri cekilmek icin hareket eden arac fren yapmaz.
        if (vehicle.CurrentTask == VehicleTask.ReversingToPocket)
        {
            return boundedCandidate;
        }

        foreach (var other in _vehicles.Where(v => v.Id != vehicle.Id && IsOnMainLane(v)))
        {
            // CRITICAL FIX: ReversingToPocket araci, yol verdigi Passer aracini engel olarak gormemeli.
            if (vehicle.CurrentTask == VehicleTask.ReversingToPocket &&
                !string.IsNullOrWhiteSpace(vehicle.YieldToVehicleId) &&
                string.Equals(other.Id, vehicle.YieldToVehicleId, StringComparison.Ordinal))
            {
                continue;
            }

            // CRITICAL FIX (reverse direction bug): Passer araci da, kendisine yol veren ReversingToPocket araci engel olarak gormemeli.
            // Aksi halde car-following / hard-gap kurali Passer'i dondurup deadlock olusturuyor.
            if (other.CurrentTask == VehicleTask.ReversingToPocket &&
                !string.IsNullOrWhiteSpace(other.YieldToVehicleId) &&
                string.Equals(other.YieldToVehicleId, vehicle.Id, StringComparison.Ordinal))
            {
                continue;
            }

            var otherCandidate = EstimateOtherCandidate(other);
            var finalGap = Math.Abs(boundedCandidate - otherCandidate);

            if (vehicle.Direction == other.Direction && IsFollower(vehicle, other))
            {
                var gapNow = Math.Abs(vehicle.PositionMeters - other.PositionMeters);
                if (gapNow > SensorRangeMeters)
                {
                    continue;
                }

                var sign = vehicle.Direction == VehicleDirection.LeftToRight ? 1d : -1d;
                var maxAllowed = otherCandidate - (sign * _minimumDistanceMeters);
                boundedCandidate = vehicle.Direction == VehicleDirection.LeftToRight
                    ? Math.Min(boundedCandidate, maxAllowed)
                    : Math.Max(boundedCandidate, maxAllowed);
            }
        }

        if (_cruiseSpeedByVehicleId.TryGetValue(vehicle.Id, out var cruiseSpeed) && cruiseSpeed > 0)
        {
            vehicle.UpdateSpeed(cruiseSpeed);
        }

        return Math.Clamp(boundedCandidate, 0d, Road.LengthMeters);
    }

    private bool TryAutoPullOver(Vehicle vehicle, string yieldToVehicleId)
    {
        if (vehicle.HasLoad ||
            !IsOnMainLane(vehicle) ||
            vehicle.CurrentTask == VehicleTask.ReversingToPocket ||
            vehicle.CurrentTask == VehicleTask.WaitingInPocket)
        {
            return false;
        }

        // CRITICAL: En yakin alan doluysa/rezerveliyse bir sonrakini dene (yeni eklenen ceplerin kullanilmasi icin).
        var safeArea = GetSafeAreaCandidatesPreferPocket(vehicle.Id, vehicle.PositionMeters)
            .FirstOrDefault(area => area.TryReserveSlot(vehicle.Id));
        if (safeArea is null)
        {
            return false;
        }

        vehicle.StartRetreatManeuver(safeArea.PositionMeters, yieldToVehicleId, safeArea is Depot);
        if (_cruiseSpeedByVehicleId.TryGetValue(vehicle.Id, out var cruiseSpeed) && cruiseSpeed > 0)
        {
            // Reversing state'inde arac fiziksel olarak hedefe dogru hareket etmeli.
            vehicle.UpdateSpeed(cruiseSpeed);
        }
        return true;
    }

    private IEnumerable<VehicleStorageArea> GetSafeAreaCandidatesPreferPocket(string vehicleId, double positionMeters)
    {
        // Cep oncelikli: Depo sadece cep bulunamazsa secilsin.
        var pockets = Road.Pockets
            .Cast<VehicleStorageArea>()
            .Where(area => !area.IsFullyAllocated || area.HasReservation(vehicleId))
            .OrderBy(area => Math.Abs(area.PositionMeters - positionMeters));

        var depots = Road.Depots
            .Cast<VehicleStorageArea>()
            .Where(area => !area.IsFullyAllocated || area.HasReservation(vehicleId))
            .OrderBy(area => Math.Abs(area.PositionMeters - positionMeters));

        return pockets.Concat(depots);
    }
    private void TriggerChainYieldForFollowers(Vehicle retreatingFrontVehicle, string yieldToVehicleId)
    {
        var followers = _vehicles
            .Where(other =>
                other.Id != retreatingFrontVehicle.Id &&
                !other.HasLoad &&
                IsOnMainLane(other) &&
                other.CurrentTask != VehicleTask.ReversingToPocket &&
                other.CurrentTask != VehicleTask.WaitingInPocket &&
                other.Direction == retreatingFrontVehicle.Direction &&
                IsFollower(other, retreatingFrontVehicle) &&
                Math.Abs(other.PositionMeters - retreatingFrontVehicle.PositionMeters) <= (_conflictDetectionRangeMeters * 2d))
            .OrderBy(other => Math.Abs(other.PositionMeters - retreatingFrontVehicle.PositionMeters))
            .ToList();

        foreach (var follower in followers)
        {
            if (TryAutoPullOver(follower, yieldToVehicleId))
            {
                SimulationLogger.Log($"Zincirleme geri cekilme: {follower.Id}, {yieldToVehicleId} icin yoldan cikiyor.");
            }
        }
    }

    private VehicleStorageArea? FindNearestAvailableSafeArea(string vehicleId, double positionMeters)
    {
        return Road.Pockets
            .Cast<VehicleStorageArea>()
            .Concat(Road.Depots)
            .Where(area => !area.IsFullyAllocated || area.HasReservation(vehicleId))
            .OrderBy(area => Math.Abs(area.PositionMeters - positionMeters))
            .FirstOrDefault();
    }

    private bool HasClearanceToResume(Vehicle waitingVehicle)
    {
        if (string.IsNullOrWhiteSpace(waitingVehicle.YieldToVehicleId))
        {
            return true;
        }

        var yieldedVehicle = _vehicles.FirstOrDefault(v => string.Equals(v.Id, waitingVehicle.YieldToVehicleId, StringComparison.OrdinalIgnoreCase));
        if (yieldedVehicle is null)
        {
            return true;
        }

        if (yieldedVehicle.CurrentTask == VehicleTask.Completed || yieldedVehicle.CurrentTask == VehicleTask.InGarage)
        {
            return true;
        }

        // Passer artik ana yolda degilse (yukleme/bosaltma/cep/depo bekleme) karsilasma fiilen bitmistir.
        if (!IsOnMainLane(yieldedVehicle))
        {
            return true;
        }

        var clearanceDistance = _minimumDistanceMeters;
        if (yieldedVehicle.Direction == VehicleDirection.LeftToRight)
        {
            return yieldedVehicle.PositionMeters >= waitingVehicle.PositionMeters + clearanceDistance;
        }

        if (yieldedVehicle.Direction == VehicleDirection.RightToLeft)
        {
            return yieldedVehicle.PositionMeters <= waitingVehicle.PositionMeters - clearanceDistance;
        }

        return Math.Abs(yieldedVehicle.PositionMeters - waitingVehicle.PositionMeters) >= clearanceDistance;
    }

    private bool HasWaitTimedOut(Vehicle waitingVehicle)
    {
        if (!_yieldWaitStartedTickByVehicleId.TryGetValue(waitingVehicle.Id, out var startedAt))
        {
            _yieldWaitStartedTickByVehicleId[waitingVehicle.Id] = TickCount;
            return false;
        }

        return (TickCount - startedAt) >= MaxYieldWaitTicks;
    }

    private double EstimateOtherCandidate(Vehicle vehicle)
    {
        if (!IsOnMainLane(vehicle))
        {
            return vehicle.PositionMeters;
        }

        return Math.Clamp(CalculateNextPosition(vehicle), 0d, Road.LengthMeters);
    }

    private static bool IsOnMainLane(Vehicle vehicle)
    {
        return vehicle.CurrentTask == VehicleTask.GoingToDepot ||
               vehicle.CurrentTask == VehicleTask.ReturningHome ||
               vehicle.CurrentTask == VehicleTask.ReversingToPocket ||
               vehicle.CurrentTask == VehicleTask.WaitingForClearance;
    }

    private void NotifyPasserIfWaiting(Vehicle yielder)
    {
        if (string.IsNullOrWhiteSpace(yielder.YieldToVehicleId))
        {
            return;
        }

        var passer = _vehicles.FirstOrDefault(v => string.Equals(v.Id, yielder.YieldToVehicleId, StringComparison.Ordinal));
        if (passer is null)
        {
            return;
        }

        if (passer.CurrentTask == VehicleTask.WaitingForClearance)
        {
            passer.ResumeAfterClearance(_cruiseSpeedByVehicleId[passer.Id]);
            SimulationLogger.Log($"Arac {passer.Id}, {yielder.Id} cebe girdigi icin beklemeyi bitirip devam ediyor.");
        }
    }

    private bool ShouldStopForLeadingVehicle(Vehicle vehicle)
    {
        if (!IsOnMainLane(vehicle))
        {
            return false;
        }

        // Directional sensor: sadece gidilen yonde "ondeki" araci dikkate al.
        var leading = _vehicles
            .Where(other =>
                other.Id != vehicle.Id &&
                IsOnMainLane(other) &&
                other.Direction == vehicle.Direction &&
                IsAhead(vehicle, other))
            .OrderBy(other => Math.Abs(other.PositionMeters - vehicle.PositionMeters))
            .FirstOrDefault();

        if (leading is null)
        {
            return false;
        }

        var gap = Math.Abs(leading.PositionMeters - vehicle.PositionMeters);
        if (gap > SensorRangeMeters)
        {
            return false;
        }

        // CRITICAL FIX: ReversingToPocket yapan yielder, Passer (YieldToVehicleId) aracini "durmus on arac" gibi gormemeli.
        if (vehicle.CurrentTask == VehicleTask.ReversingToPocket &&
            !string.IsNullOrWhiteSpace(vehicle.YieldToVehicleId) &&
            string.Equals(leading.Id, vehicle.YieldToVehicleId, StringComparison.Ordinal))
        {
            return false;
        }

        // CRITICAL FIX: Passer araci da, kendisine yol veren ReversingToPocket araci onunde engel saymamalidir (ghosting).
        if (leading.CurrentTask == VehicleTask.ReversingToPocket &&
            !string.IsNullOrWhiteSpace(leading.YieldToVehicleId) &&
            string.Equals(leading.YieldToVehicleId, vehicle.Id, StringComparison.Ordinal))
        {
            return false;
        }

        return leading.SpeedKmh <= 0.01d;
    }

    private bool ShouldYieldForOncoming(Vehicle vehicle, out string oncomingVehicleId)
    {
        oncomingVehicleId = string.Empty;
        if (!IsOnMainLane(vehicle))
        {
            return false;
        }

        var oncoming = _vehicles
            .Where(other =>
                other.Id != vehicle.Id &&
                IsOnMainLane(other) &&
                other.Direction != vehicle.Direction &&
                IsFacingEachOther(vehicle, other) &&
                Math.Abs(other.PositionMeters - vehicle.PositionMeters) <= OppositeConflictRangeMeters)
            .OrderBy(other => Math.Abs(other.PositionMeters - vehicle.PositionMeters))
            .FirstOrDefault();

        if (oncoming is null)
        {
            return false;
        }

        var yielder = DetermineYielder(vehicle, oncoming);
        if (yielder is null || yielder.Id != vehicle.Id)
        {
            return false;
        }

        oncomingVehicleId = oncoming.Id;
        return true;
    }

    private Vehicle? DetermineYielder(Vehicle a, Vehicle b)
    {
        // Asymmetric hierarchy (yielder = lower priority):
        // 1) ReturningHome (highest)
        // 2) GoingToDepot + HasLoad=true
        // 3) GoingToDepot + HasLoad=false
        var aRank = GetPriorityRank(a);
        var bRank = GetPriorityRank(b);
        if (aRank != bRank)
        {
            return aRank > bRank ? a : b;
        }

        // Tie-breaker: ID'si KUCUK olan yielder olsun (kural).
        return string.CompareOrdinal(a.Id, b.Id) < 0 ? a : b;
    }

    private static int GetPriorityRank(Vehicle v)
    {
        if (v.CurrentTask == VehicleTask.ReturningHome)
        {
            return 0;
        }

        if (v.CurrentTask == VehicleTask.GoingToDepot && v.HasLoad)
        {
            return 1;
        }

        if (v.CurrentTask == VehicleTask.GoingToDepot && !v.HasLoad)
        {
            return 2;
        }

        // default: lowest priority (yields)
        return 3;
    }

    private static bool IsFacingEachOther(Vehicle a, Vehicle b)
    {
        if (a.Direction == VehicleDirection.LeftToRight && b.Direction == VehicleDirection.RightToLeft)
        {
            return a.PositionMeters < b.PositionMeters;
        }

        if (a.Direction == VehicleDirection.RightToLeft && b.Direction == VehicleDirection.LeftToRight)
        {
            return a.PositionMeters > b.PositionMeters;
        }

        return false;
    }

    private static bool IsAhead(Vehicle source, Vehicle other)
    {
        return source.Direction == VehicleDirection.LeftToRight
            ? other.PositionMeters > source.PositionMeters
            : other.PositionMeters < source.PositionMeters;
    }

    private static bool IsFollower(Vehicle vehicle, Vehicle other)
    {
        return vehicle.Direction == VehicleDirection.LeftToRight
            ? vehicle.PositionMeters <= other.PositionMeters
            : vehicle.PositionMeters >= other.PositionMeters;
    }

    private static bool HasRightOfWay(Vehicle candidate, Vehicle other)
    {
        if (candidate.HasLoad != other.HasLoad)
        {
            return candidate.HasLoad;
        }

        var candidateDistance = Math.Abs(candidate.TargetDepotPositionMeters - candidate.PositionMeters);
        var otherDistance = Math.Abs(other.TargetDepotPositionMeters - other.PositionMeters);
        if (Math.Abs(candidateDistance - otherDistance) > 0.001d)
        {
            return candidateDistance < otherDistance;
        }

        return string.CompareOrdinal(candidate.Id, other.Id) < 0;
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
        if (vehicle.HasLoad)
        {
            return false;
        }

        var area = FindSafeAreaByPosition(directive.SafeAreaPositionMeters);
        if (area is null || !area.TryReserveSlot(vehicle.Id))
        {
            return false;
        }

        vehicle.StartRetreatManeuver(
            directive.SafeAreaPositionMeters,
            directive.YieldToVehicleId,
            safeAreaIsDepot: area is Depot);
        return true;
    }

    private void InitializeGarages()
    {
        foreach (var vehicle in _vehicles)
        {
            vehicle.EnterGarage();
        }
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
            foreach (var pocket in Road.Pockets.Where(p => Math.Abs(vehicle.PositionMeters - p.PositionMeters) < DepotReachToleranceMeters))
            {
                pocket.TryAddVehicle(vehicle);
            }

            foreach (var depot in Road.Depots.Where(d => Math.Abs(vehicle.PositionMeters - d.PositionMeters) < DepotReachToleranceMeters))
            {
                depot.TryAddVehicle(vehicle);
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
        vehicle.UpdateSpeed(_cruiseSpeedByVehicleId[vehicle.Id]);

        var nextTarget = vehicle.CurrentTask == VehicleTask.GoingToDepot
            ? $"D{vehicle.TargetDepotPositionMeters}"
            : "0m";
        return $"Yukleme tamamlandi, yeni hedef: {nextTarget}";
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
        ReleaseSafeAreaReservation(vehicle);
        vehicle.CompleteDeliveryCycle();
        vehicle.UpdateSpeed(0d);
        return "Gorev tamamlandi (0m)";
    }
}
