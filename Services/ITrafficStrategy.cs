using OtonomAracOptimizasyonu.Models;

namespace OtonomAracOptimizasyonu.Services;

public interface ITrafficStrategy
{
    TrafficDecision Evaluate(
        Vehicle vehicle,
        Road road,
        IReadOnlyCollection<Vehicle> trafficState,
        double tickDurationSeconds);
}
