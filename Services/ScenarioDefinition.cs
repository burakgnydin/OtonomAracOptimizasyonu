using OtonomAracOptimizasyonu.Models;

namespace OtonomAracOptimizasyonu.Services;

public sealed record ScenarioDefinition(
    string Name,
    string Description,
    Road Road,
    IReadOnlyCollection<Vehicle> Vehicles,
    double TickDurationSeconds,
    int MaxTicks,
    bool EnableReturnTrip);
