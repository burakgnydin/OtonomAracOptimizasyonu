using OtonomAracOptimizasyonu.Models;

namespace OtonomAracOptimizasyonu.Services;

public interface ITrafficStrategy
{
    TrafficDecision Evaluate(
        Vehicle vehicle,
        Road road,
        VehicleRestrictedState currentVehicleState,
        IReadOnlyCollection<VehicleRestrictedState> trafficState,
        double tickDurationSeconds);
}
