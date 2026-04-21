namespace OtonomAracOptimizasyonu.Models;

public sealed record Sensor
{
    public Sensor(int positionMeters)
    {
        if (positionMeters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(positionMeters), "Sensor position cannot be negative.");
        }

        PositionMeters = positionMeters;
    }

    public int PositionMeters { get; }
}
