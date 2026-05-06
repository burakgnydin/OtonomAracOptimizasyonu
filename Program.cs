using OtonomAracOptimizasyonu.Services;
using OtonomAracOptimizasyonu.Visualization;

Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);

var loader = new ScenarioLoader();
var scenario = LoadScenario(loader, args);
SimulationLogger.Log($"Senaryo yuklendi: {scenario.Name}");
SimulationLogger.Log(
    $"Yol={scenario.Road.LengthMeters}m | Sensorler={scenario.Road.Sensors.Count} | Cepler={scenario.Road.Pockets.Count} | Depolar={scenario.Road.Depots.Count} | Araclar={scenario.Vehicles.Count}");
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
            $"Senaryo yuklenemedi:\n{ex.Message}",
            "Yukleme Hatasi",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
        Environment.Exit(1);
        throw;
    }
}
