namespace OtonomAracOptimizasyonu.Models;

public sealed class Road
{
    public const int DefaultLengthMeters = 240;

    public Road(
        int lengthMeters,
        IEnumerable<Sensor> sensors,
        IEnumerable<Pocket> pockets,
        IEnumerable<Depot> depots)
    {
        if (lengthMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lengthMeters), "Yol uzunlugu sifirdan buyuk olmalidir.");
        }

        ArgumentNullException.ThrowIfNull(sensors);
        ArgumentNullException.ThrowIfNull(pockets);
        ArgumentNullException.ThrowIfNull(depots);

        LengthMeters = lengthMeters;
        Sensors = sensors.ToList().AsReadOnly();
        Pockets = pockets.ToList().AsReadOnly();
        Depots = depots.ToList().AsReadOnly();
    }

    public int LengthMeters { get; }

    public IReadOnlyCollection<Sensor> Sensors { get; }

    public IReadOnlyCollection<Pocket> Pockets { get; }

    public IReadOnlyCollection<Depot> Depots { get; }

    public static Road CreateDefault()
    {
        var sensors = new[]
        {
            new Sensor(0),
            new Sensor(40),
            new Sensor(80),
            new Sensor(120),
            new Sensor(160),
            new Sensor(200),
            new Sensor(240)
        };

        var pockets = new[]
        {
            new Pocket(80),
            new Pocket(200)
        };

        var depots = new[]
        {
            new Depot(70),
            new Depot(120),
            new Depot(240)
        };

        return new Road(DefaultLengthMeters, sensors, pockets, depots);
    }
}
