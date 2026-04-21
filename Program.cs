using OtonomAracOptimizasyonu.Models;
using OtonomAracOptimizasyonu.Services;

var loader = new ScenarioLoader();
var scenario = LoadScenario(loader);
var road = scenario.Road;
var strategy = new SafetyFirstTrafficStrategy();
var controller = new TrafficController(strategy, scenario.TickDurationSeconds);
var engine = new SimulationEngine(road, scenario.Vehicles, controller, scenario.EnableReturnTrip);

Console.WriteLine($"Scenario: {scenario.Name}");
Console.WriteLine($"Description: {scenario.Description}");
Console.WriteLine($"Road Length: {road.LengthMeters}m | Tick: {scenario.TickDurationSeconds:0.##}s | MaxTicks: {scenario.MaxTicks}");
Console.WriteLine(new string('-', 90));

var deadlockStops = 0;
var collisionStops = 0;

for (var tick = 0; tick < scenario.MaxTicks; tick++)
{
    var report = engine.Tick();
    deadlockStops += report.VehicleStates.Count(v => v.StopReason == TrafficStopReason.DeadlockAvoidance);
    collisionStops += report.VehicleStates.Count(v => v.StopReason == TrafficStopReason.CollisionRisk);

    WriteTickReport(report, road);

    if (AllVehiclesReachedTargets(report.VehicleStates))
    {
        Console.WriteLine($"Simulation completed early at tick {report.TickIndex}.");
        break;
    }
}

WriteFinalSummary(engine, deadlockStops, collisionStops);

static ScenarioDefinition LoadScenario(ScenarioLoader loader)
{
    Console.WriteLine("Scenario source:");
    Console.WriteLine("  [1] Varsayilan scenario (ScenarioConfig.json)");
    Console.WriteLine("  [2] Custom JSON dosyasi");
    Console.Write("Seciminiz (Enter = 1): ");
    var sourceInput = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(sourceInput) || sourceInput == "1")
    {
        return TryLoadOrExit(loader, "ScenarioConfig.json");
    }

    if (sourceInput == "2")
    {
        Console.Write("JSON dosya yolu (Enter = ScenarioConfig.json): ");
        var customPath = Console.ReadLine();
        var path = string.IsNullOrWhiteSpace(customPath) ? "ScenarioConfig.json" : customPath.Trim();
        return TryLoadOrExit(loader, path);
    }

    Console.WriteLine("Gecersiz secim. Varsayilan scenario yukleniyor.");
    return TryLoadOrExit(loader, "ScenarioConfig.json");
}

static ScenarioDefinition TryLoadOrExit(ScenarioLoader loader, string filePath)
{
    try
    {
        return loader.LoadFromFile(filePath);
    }
    catch (ScenarioLoadException ex)
    {
        Console.WriteLine($"Scenario yuklenemedi: {ex.Message}");
        Environment.Exit(1);
        throw;
    }
}

static void WriteTickReport(SimulationTickReport report, Road road)
{
    Console.WriteLine($"Tick {report.TickIndex:000} | Time {report.SimulationTime:hh\\:mm\\:ss}");
    Console.WriteLine($"Road: {RenderRoad(report.VehicleStates, road)}");

    foreach (var state in report.VehicleStates.OrderBy(v => v.VehicleId))
    {
        var direction = state.Direction == VehicleDirection.LeftToRight ? ">" : "<";
        Console.WriteLine(
            $"  {state.VehicleId,-4} pos={state.PositionMeters,6:0.0}m speed={state.SpeedKmh,5:0.0}km/h dir={direction} target={state.TargetDepotPositionMeters,3}m status={state.StatusMessage}");
    }

    Console.WriteLine(new string('-', 90));
}

static void WriteFinalSummary(SimulationEngine engine, int deadlockStops, int collisionStops)
{
    var totalSeconds = engine.TickCount * engine.TrafficController.TickDurationSeconds;
    var completedVehicles = engine.Vehicles.Count(v => Math.Abs(v.PositionMeters - v.TargetDepotPositionMeters) < 0.001d);

    Console.WriteLine("Simulation Summary");
    Console.WriteLine($"  Total ticks: {engine.TickCount}");
    Console.WriteLine($"  Elapsed time: {totalSeconds:0.##}s");
    Console.WriteLine($"  Vehicles reached target: {completedVehicles}/{engine.Vehicles.Count}");
    Console.WriteLine($"  Deadlock avoidance stops: {deadlockStops}");
    Console.WriteLine($"  Collision risk stops: {collisionStops}");
}

static bool AllVehiclesReachedTargets(IReadOnlyCollection<VehicleTickState> states)
{
    return states.All(s => s.ReachedTargetDepot);
}

static string RenderRoad(IReadOnlyCollection<VehicleTickState> states, Road road)
{
    const int segmentSizeMeters = 40;
    var segmentCount = road.LengthMeters / segmentSizeMeters;
    var cells = Enumerable.Repeat(".", segmentCount + 1).ToArray();

    foreach (var pocket in road.Pockets)
    {
        var index = pocket.PositionMeters / segmentSizeMeters;
        cells[index] = "P";
    }

    foreach (var depot in road.Depots)
    {
        var index = depot.PositionMeters / segmentSizeMeters;
        cells[index] = cells[index] == "." ? "D" : $"{cells[index]}/D";
    }

    foreach (var vehicle in states)
    {
        var index = (int)Math.Round(vehicle.PositionMeters / segmentSizeMeters, MidpointRounding.AwayFromZero);
        index = Math.Clamp(index, 0, cells.Length - 1);
        var marker = vehicle.Direction == VehicleDirection.LeftToRight ? ">" : "<";
        cells[index] = $"{vehicle.VehicleId}{marker}";
    }

    return "[" + string.Join("  ", cells) + "]";
}
