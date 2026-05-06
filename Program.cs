using OtonomAracOptimizasyonu.Services;
using OtonomAracOptimizasyonu.Visualization;

Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);

var loader = new ScenarioLoader();
var scenario = LoadScenario(loader, args);
SimulationLogger.Log($"Scenario loaded: {scenario.Name}");
SimulationLogger.Log(
    $"Road={scenario.Road.LengthMeters}m | Sensors={scenario.Road.Sensors.Count} | Pockets={scenario.Road.Pockets.Count} | Depots={scenario.Road.Depots.Count} | Vehicles={scenario.Vehicles.Count}");
var strategy = new SafetyFirstTrafficStrategy();
var controller = new TrafficController(strategy, scenario.TickDurationSeconds);
var engine = new SimulationEngine(scenario.Road, scenario.Vehicles, controller, scenario.EnableReturnTrip);

var form = new SimulationVisualizerForm(
    scenario.Road,
    engine,
    scenario.MaxTicks,
    scenario.TickDurationSeconds);

Application.Run(form);

static ScenarioDefinition LoadScenario(ScenarioLoader loader, string[] args)
{
    var path = args.FirstOrDefault();
    var scenarioPath = string.IsNullOrWhiteSpace(path) ? "ScenarioConfig.json" : path.Trim();

    try
    {
        return loader.LoadFromFile(scenarioPath);
    }
    catch (ScenarioLoadException ex)
    {
        MessageBox.Show(
            $"Scenario yuklenemedi:\n{ex.Message}",
            "Yukleme Hatasi",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
        Environment.Exit(1);
        throw;
    }
}
