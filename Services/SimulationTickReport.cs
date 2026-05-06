using OtonomAracOptimizasyonu.Models;

namespace OtonomAracOptimizasyonu.Services;

public sealed record VehicleTickState(
    string VehicleId,
    double PositionMeters,
    double SpeedKmh,
    VehicleDirection Direction,
    int TargetDepotPositionMeters,
    bool ReachedTargetDepot,
    VehicleTask CurrentTask,
    bool HasLoad,
    int CompletedMissionCount,
    TrafficStopReason StopReason,
    string StatusMessage);

public sealed record SimulationTickReport(
    int TickIndex,
    TimeSpan SimulationTime,
    IReadOnlyCollection<VehicleTickState> VehicleStates);
