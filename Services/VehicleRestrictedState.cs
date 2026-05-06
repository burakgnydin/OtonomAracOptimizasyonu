using OtonomAracOptimizasyonu.Models;

namespace OtonomAracOptimizasyonu.Services;

public sealed record VehicleRestrictedState(
    string VehicleId,
    int? PreviousSensorId,
    int? LastSensorId,
    TimeSpan? PreviousSensorTriggeredAt,
    TimeSpan? LastSensorTriggeredAt,
    int? ActiveSensorId,
    bool IsSensorSignalRed,
    bool NextSensorTimedOut,
    VehicleDirection InferredDirection,
    double LastKnownSpeedKmh,
    double EstimatedPositionMeters,
    double PositionUncertaintyMeters,
    bool IsInsideSensorZone);
