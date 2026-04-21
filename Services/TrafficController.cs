using OtonomAracOptimizasyonu.Models;

namespace OtonomAracOptimizasyonu.Services;

public sealed class TrafficController
{
    private IReadOnlyCollection<Vehicle> _trafficState = [];

    public TrafficController(ITrafficStrategy trafficStrategy, double tickDurationSeconds = 1d)
    {
        ArgumentNullException.ThrowIfNull(trafficStrategy);

        if (tickDurationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tickDurationSeconds), "Tick duration must be greater than zero.");
        }

        TrafficStrategy = trafficStrategy;
        TickDurationSeconds = tickDurationSeconds;
    }

    public ITrafficStrategy TrafficStrategy { get; }

    public double TickDurationSeconds { get; }

    public void UpdateTrafficState(IReadOnlyCollection<Vehicle> trafficState)
    {
        _trafficState = trafficState ?? throw new ArgumentNullException(nameof(trafficState));
    }

    public bool CanMove(Vehicle vehicle, Road road)
    {
        return Evaluate(vehicle, road).CanMove;
    }

    public TrafficDecision Evaluate(Vehicle vehicle, Road road)
    {
        return TrafficStrategy.Evaluate(vehicle, road, _trafficState, TickDurationSeconds);
    }
}
