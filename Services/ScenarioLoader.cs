using System.Text.Json;
using System.Text.Json.Serialization;
using OtonomAracOptimizasyonu.Models;

namespace OtonomAracOptimizasyonu.Services;

public sealed class ScenarioLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ScenarioDefinition LoadFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ScenarioLoadException("Senaryo dosya yolu bos olamaz.");
        }

        if (!File.Exists(filePath))
        {
            throw new ScenarioLoadException($"Senaryo dosyasi bulunamadi: {filePath}");
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var config = JsonSerializer.Deserialize<ScenarioJsonConfig>(json, JsonOptions)
                ?? throw new ScenarioLoadException("Senaryo JSON icerigi bos veya okunamiyor.");

            return BuildScenario(config);
        }
        catch (JsonException ex)
        {
            throw new ScenarioLoadException($"Senaryo JSON ayristirma hatasi: {ex.Message}");
        }
        catch (IOException ex)
        {
            throw new ScenarioLoadException($"Senaryo dosyasi okunamadi: {ex.Message}");
        }
    }

    private static ScenarioDefinition BuildScenario(ScenarioJsonConfig config)
    {
        ValidateTopLevel(config);
        ValidateInfrastructure(config.Road);
        ValidateSensorLayout(config.Road);
        ValidateSimulation(config.Simulation);
        ValidateVehicles(config.Vehicles, config.Road);

        var road = new Road(
            config.Road.LengthMeters,
            config.Road.SensorPositionsMeters.Select(position => new Sensor(position)),
            config.Road.PocketPositionsMeters.Select(position => new Pocket(position)),
            config.Road.DepotPositionsMeters.Select(position => new Depot(position)));

        var vehicles = config.Vehicles
            .Select(vehicle => new Vehicle(
                id: vehicle.Id,
                speedKmh: vehicle.SpeedKmh,
                targetDepots: vehicle.TargetDepots.Count > 0 ? vehicle.TargetDepots : [vehicle.TargetDepotPositionMeters],
                spawnDelaySeconds: vehicle.SpawnDelaySeconds,
                isPriority: vehicle.IsPriority,
                singleMissionOnly: vehicle.SingleMissionOnly))
            .ToList();

        ValidateInitialStorageCapacity(vehicles, road);

        return new ScenarioDefinition(
            config.Scenario.Name,
            config.Scenario.Description,
            road,
            vehicles,
            config.Simulation.TickDurationSeconds,
            config.Simulation.MaxTicks,
            config.Simulation.EnableReturnTrip);
    }

    private static void ValidateTopLevel(ScenarioJsonConfig config)
    {
        if (config.Scenario is null || config.Road is null || config.Simulation is null || config.Vehicles is null)
        {
            throw new ScenarioLoadException("Senaryo JSON zorunlu alanlari icermiyor (scenario, road, simulation, vehicles).");
        }

        if (string.IsNullOrWhiteSpace(config.Scenario.Name))
        {
            throw new ScenarioLoadException("Senaryo adi alani bos olamaz.");
        }
    }

    private static void ValidateInfrastructure(RoadJsonConfig road)
    {
        if (road.LengthMeters <= 0)
        {
            throw new ScenarioLoadException("Yol uzunlugu 0'dan buyuk olmali.");
        }

        if (road.SensorPositionsMeters.Count == 0)
        {
            throw new ScenarioLoadException("En az bir sensor tanimli olmali.");
        }

        ValidatePositions("Sensor", road.SensorPositionsMeters, road.LengthMeters);
        ValidatePositions("Cep", road.PocketPositionsMeters, road.LengthMeters);
        ValidatePositions("Depo", road.DepotPositionsMeters, road.LengthMeters);
    }

    private static void ValidateSimulation(SimulationJsonConfig simulation)
    {
        if (simulation.TickDurationSeconds <= 0)
        {
            throw new ScenarioLoadException("TickDurationSeconds 0'dan buyuk olmali.");
        }

        if (simulation.MaxTicks <= 0)
        {
            throw new ScenarioLoadException("MaxTicks 0'dan buyuk olmali.");
        }
    }

    private static void ValidateVehicles(IReadOnlyCollection<VehicleJsonConfig> vehicles, RoadJsonConfig road)
    {
        if (vehicles.Count < 3 || vehicles.Count > 20)
        {
            throw new ScenarioLoadException("Arac sayisi 3 ile 20 arasinda olmalidir.");
        }

        var duplicateId = vehicles
            .GroupBy(v => v.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateId is not null)
        {
            throw new ScenarioLoadException($"Ayni ID ile birden fazla arac tanimli: {duplicateId.Key}");
        }

        var depotSet = road.DepotPositionsMeters.ToHashSet();
        foreach (var vehicle in vehicles)
        {
            if (string.IsNullOrWhiteSpace(vehicle.Id))
            {
                throw new ScenarioLoadException("Arac ID alani bos olamaz.");
            }

            if (vehicle.SpeedKmh < 0 || vehicle.SpeedKmh > Vehicle.MaxSpeedKmh)
            {
                throw new ScenarioLoadException(
                    $"Arac {vehicle.Id} icin gecersiz hiz: {vehicle.SpeedKmh} km/h (Maks: {Vehicle.MaxSpeedKmh}).");
            }

            var targets = vehicle.TargetDepots.Count > 0 ? vehicle.TargetDepots : [vehicle.TargetDepotPositionMeters];
            if (targets.Any(target => !depotSet.Contains(target)))
            {
                throw new ScenarioLoadException(
                    $"Arac {vehicle.Id} icin hedef depo gecersiz.");
            }
        }
    }

    private static void ValidateInitialStorageCapacity(IReadOnlyCollection<Vehicle> vehicles, Road road)
    {
        foreach (var pocket in road.Pockets)
        {
            var count = vehicles.Count(vehicle => Math.Abs(vehicle.PositionMeters - pocket.PositionMeters) < 0.001d);
            if (count > pocket.Capacity)
            {
                throw new ScenarioLoadException(
                    $"Cep kapasitesi asildi: {pocket.PositionMeters}m konumunda {count} arac var (Kapasite: {pocket.Capacity}).");
            }
        }

        foreach (var depot in road.Depots)
        {
            var count = vehicles.Count(vehicle => Math.Abs(vehicle.PositionMeters - depot.PositionMeters) < 0.001d);
            if (count > depot.Capacity)
            {
                throw new ScenarioLoadException(
                    $"Depo kapasitesi asildi: {depot.PositionMeters}m konumunda {count} arac var (Kapasite: {depot.Capacity}).");
            }
        }
    }

    private static void ValidatePositions(string label, IReadOnlyCollection<int> positions, int roadLength)
    {
        ArgumentNullException.ThrowIfNull(positions);

        var duplicatePosition = positions
            .GroupBy(x => x)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicatePosition is not null)
        {
            throw new ScenarioLoadException($"{label} konumlari tekrar ediyor: {duplicatePosition.Key}m");
        }

        var outOfBounds = positions.FirstOrDefault(position => position < 0 || position > roadLength);
        if (positions.Any(position => position < 0 || position > roadLength))
        {
            throw new ScenarioLoadException($"{label} konumu yol siniri disinda: {outOfBounds}m");
        }
    }

    private static void ValidateSensorLayout(RoadJsonConfig road)
    {
        var expected = Enumerable.Range(0, (road.LengthMeters / 40) + 1)
            .Select(index => index * 40)
            .ToArray();

        var actual = road.SensorPositionsMeters.OrderBy(x => x).ToArray();
        if (!expected.SequenceEqual(actual))
        {
            throw new ScenarioLoadException(
                $"Sensor dizilimi odev kisitini saglamiyor. Beklenen: [{string.Join(", ", expected)}], Gelen: [{string.Join(", ", actual)}]");
        }
    }
}

public sealed class ScenarioLoadException : Exception
{
    public ScenarioLoadException(string message) : base(message)
    {
    }
}

public sealed record ScenarioJsonConfig(
    ScenarioMetadataJsonConfig Scenario,
    RoadJsonConfig Road,
    SimulationJsonConfig Simulation,
    IReadOnlyCollection<VehicleJsonConfig> Vehicles);

public sealed record ScenarioMetadataJsonConfig(
    string Name,
    string Description);

public sealed record RoadJsonConfig(
    int LengthMeters,
    IReadOnlyCollection<int> SensorPositionsMeters,
    IReadOnlyCollection<int> PocketPositionsMeters,
    IReadOnlyCollection<int> DepotPositionsMeters);

public sealed record SimulationJsonConfig(
    double TickDurationSeconds,
    int MaxTicks,
    bool EnableReturnTrip);

public sealed record VehicleJsonConfig(
    string Id,
    VehicleDirection Direction,
    double PositionMeters,
    double SpeedKmh,
    int TargetDepotPositionMeters,
    IReadOnlyCollection<int>? TargetDepots = null,
    double SpawnDelaySeconds = 0d,
    bool IsPriority = false,
    bool SingleMissionOnly = false)
{
    public IReadOnlyCollection<int> TargetDepots { get; init; } = TargetDepots ?? [];
}
