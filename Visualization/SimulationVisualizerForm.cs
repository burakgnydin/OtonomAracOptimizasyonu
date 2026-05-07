using System.Drawing.Drawing2D;
using OtonomAracOptimizasyonu.Models;
using OtonomAracOptimizasyonu.Services;

namespace OtonomAracOptimizasyonu.Visualization;

public sealed class SimulationVisualizerForm : Form
{
    private const int RoadMarginLeft = 50;
    private const int RoadMarginRight = 50;
    private const int RoadY = 150;
    private const int SensorSize = 10;
    private const int VehicleDotSize = 12;
    private const int DefaultRoadLengthMeters = 240;
    private const int DefaultMaxTicks = 220;
    private const double DefaultTickDurationSeconds = 1.0;
    private const double SensorVisibilityToleranceMeters = 20d;

    private Road? _road;
    private SimulationEngine? _engine;
    private int _maxTicks = DefaultMaxTicks;
    private double _tickDurationSeconds = DefaultTickDurationSeconds;
    private readonly System.Windows.Forms.Timer _timer;

    private IReadOnlyCollection<VehicleTickState> _currentStates = Array.Empty<VehicleTickState>();
    private int _currentTick;

    private readonly Label _statusLabel;
    private readonly ListBox _logListBox;
    private readonly Panel _controlPanel;

    private readonly NumericUpDown _vehicleCountInput;
    private readonly NumericUpDown _vehicleSpeedInput;
    private readonly NumericUpDown _speedMultiplierInput;
    private readonly TextBox _pocketLocationsInput;
    private readonly TextBox _depotLocationsInput;
    private readonly Button _startButton;
    private readonly Button _stopButton;

    public SimulationVisualizerForm()
    {
        Text = "Otonom Arac Simulasyon Gorsellestirme";
        Width = 1120;
        Height = 520;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        BackColor = Color.White;

        _controlPanel = new Panel
        {
            Dock = DockStyle.Right,
            Width = 320,
            Padding = new Padding(12),
            BackColor = Color.FromArgb(245, 246, 248)
        };
        Controls.Add(_controlPanel);

        _statusLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 30,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0)
        };
        Controls.Add(_statusLabel);

        _logListBox = new ListBox
        {
            Dock = DockStyle.Bottom,
            Height = 130
        };
        Controls.Add(_logListBox);

        var title = new Label
        {
            Text = "Kontrol Paneli",
            Dock = DockStyle.Top,
            Height = 26,
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _controlPanel.Controls.Add(title);

        var help = new Label
        {
            Text = "Konumlari virgul ile girin (orn: 80, 200, 160).",
            Dock = DockStyle.Top,
            Height = 44,
            ForeColor = Color.DimGray
        };
        _controlPanel.Controls.Add(help);

        _startButton = new Button
        {
            Text = "Baslat",
            Dock = DockStyle.Top,
            Height = 36
        };
        _startButton.Click += (_, _) => StartSimulationFromUi();
        _controlPanel.Controls.Add(_startButton);

        _stopButton = new Button
        {
            Text = "Sonlandir",
            Dock = DockStyle.Top,
            Height = 36
        };
        _stopButton.Click += (_, _) => ResetSimulation();
        _controlPanel.Controls.Add(_stopButton);

        _controlPanel.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 10 });

        _vehicleCountInput = CreateNumericInput("Arac Sayisi", min: 1, max: 50, value: 4);
        _vehicleSpeedInput = CreateNumericInput("Arac Hizi (km/h)", min: 0, max: (decimal)Vehicle.MaxSpeedKmh, value: 10);
        _speedMultiplierInput = CreateNumericInput("Simulasyon Hizi (x tick/frame)", min: 1, max: 50, value: 5);
        _pocketLocationsInput = CreateTextInput("Cep (Pocket) Konumlari", "80, 200");
        _depotLocationsInput = CreateTextInput("Depo (Depot) Konumlari", "70, 120, 240");

        _timer = new System.Windows.Forms.Timer
        {
            Interval = Math.Max(20, (int)Math.Round(_tickDurationSeconds * 1000d, MidpointRounding.AwayFromZero))
        };
        _timer.Tick += OnTimerTick;

        UpdateStatusText("Hazir");
        SimulationLogger.LogReceived += HandleLogReceived;
        FormClosed += (_, _) =>
        {
            SimulationLogger.LogReceived -= HandleLogReceived;
            _timer.Dispose();
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        DrawRoad(g);
        DrawSensors(g);
        DrawStorageAreas(g);
        DrawVehicles(g);
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_engine is null || _road is null)
        {
            _timer.Stop();
            return;
        }

        if (_currentTick >= _maxTicks)
        {
            _timer.Stop();
            UpdateStatusText("Maksimum adim sayisina ulasildi");
            return;
        }

        var ticksThisFrame = Math.Max(1, (int)_speedMultiplierInput.Value);
        SimulationTickReport? report = null;
        for (var i = 0; i < ticksThisFrame; i++)
        {
            if (_currentTick >= _maxTicks)
            {
                break;
            }

            report = _engine.Tick();
            _currentTick = report.TickIndex;
            _currentStates = report.VehicleStates;

            if (_currentStates.Count > 0 && _currentStates.All(state => state.CurrentTask == VehicleTask.Completed))
            {
                break;
            }
        }

        if (report is null)
        {
            return;
        }

        UpdateStatusText($"Adim: {_currentTick}/{_maxTicks}  |  Simulasyon Saati: {report.SimulationTime:hh\\:mm\\:ss}");

        Invalidate();

        if (_currentStates.Count > 0 && _currentStates.All(state => state.CurrentTask == VehicleTask.Completed))
        {
            _timer.Stop();
            UpdateStatusText($"Tum araclar gorevini tamamlayip baslangica dondu (adim {_currentTick})");
        }
    }

    private void DrawRoad(Graphics graphics)
    {
        if (_road is null)
        {
            return;
        }

        var x1 = RoadMarginLeft;
        var x2 = (ClientSize.Width - RoadMarginRight) - _controlPanel.Width;
        using var roadPen = new Pen(Color.Black, 3);
        graphics.DrawLine(roadPen, x1, RoadY, x2, RoadY);

        using var font = new Font("Segoe UI", 9);
        graphics.DrawString("0m", font, Brushes.DimGray, x1 - 10, RoadY + 14);
        graphics.DrawString($"{_road.LengthMeters}m", font, Brushes.DimGray, x2 - 26, RoadY + 14);
    }

    private void DrawSensors(Graphics graphics)
    {
        if (_road is null)
        {
            return;
        }

        using var font = new Font("Segoe UI", 8);

        foreach (var sensor in _road.Sensors.OrderBy(s => s.PositionMeters))
        {
            var isRed = IsSensorRed(sensor.PositionMeters);
            using var sensorBrush = new SolidBrush(isRed ? Color.IndianRed : Color.SeaGreen);
            var x = MeterToPixel(sensor.PositionMeters);
            graphics.FillRectangle(
                sensorBrush,
                x - (SensorSize / 2),
                RoadY - (SensorSize / 2),
                SensorSize,
                SensorSize);

            graphics.DrawString($"{sensor.PositionMeters}", font, Brushes.Gray, x - 8, RoadY + 18);
        }
    }

    private bool IsSensorRed(int sensorPositionMeters)
    {
        // Sensor, ±20m icinde ana yolda (veya manevrada) aktif bir arac varsa kirmizi kabul edilir.
        // WaitingInPocket araclarin ana yolu bosalttigi varsayimi ile hesaba katilmaz.
        return _currentStates.Any(state =>
            (state.CurrentTask == VehicleTask.GoingToDepot ||
             state.CurrentTask == VehicleTask.ReturningHome ||
             state.CurrentTask == VehicleTask.GoingToPocketForYielding) &&
            Math.Abs(state.PositionMeters - sensorPositionMeters) <= SensorVisibilityToleranceMeters);
    }

    private void DrawStorageAreas(Graphics graphics)
    {
        if (_road is null)
        {
            return;
        }

        using var pocketBrush = new SolidBrush(Color.Khaki);
        using var depotBrush = new SolidBrush(Color.LightSteelBlue);
        using var outlinePen = new Pen(Color.DimGray, 1);
        using var font = new Font("Segoe UI", 8);

        foreach (var pocket in _road.Pockets)
        {
            var x = MeterToPixel(pocket.PositionMeters);
            graphics.FillRectangle(pocketBrush, x - 8, RoadY - 34, 16, 16);
            graphics.DrawRectangle(outlinePen, x - 8, RoadY - 34, 16, 16);
            graphics.DrawString($"C{pocket.PositionMeters} ({pocket.Occupancy}/{pocket.Capacity})", font, Brushes.Goldenrod, x - 26, RoadY - 54);
        }

        foreach (var depot in _road.Depots)
        {
            var x = MeterToPixel(depot.PositionMeters);
            graphics.FillRectangle(depotBrush, x - 10, RoadY + 28, 20, 14);
            graphics.DrawRectangle(outlinePen, x - 10, RoadY + 28, 20, 14);
            graphics.DrawString($"D{depot.PositionMeters} ({depot.Occupancy}/{depot.Capacity})", font, Brushes.SteelBlue, x - 28, RoadY + 44);
        }
    }

    private void DrawVehicles(Graphics graphics)
    {
        if (_road is null)
        {
            return;
        }

        var orderedVehicles = _currentStates
            .OrderBy(state => state.VehicleId)
            .ToList();

        using var infoFont = new Font("Segoe UI", 7);

        for (var i = 0; i < orderedVehicles.Count; i++)
        {
            var vehicle = orderedVehicles[i];
            var x = MeterToPixel(vehicle.PositionMeters);
            var y = RoadY - 22 - ((i % 3) * 16);
            using var brush = new SolidBrush(GetColorForVehicle(vehicle.VehicleId));
            graphics.FillEllipse(
                brush,
                x - (VehicleDotSize / 2),
                y - (VehicleDotSize / 2),
                VehicleDotSize,
                VehicleDotSize);

            var loadText = vehicle.HasLoad ? "Yuklu" : "Yuksuz";
            var info = $"{vehicle.VehicleId} | {vehicle.CurrentTask} | {loadText} | Gorev:{vehicle.CompletedMissionCount}";
            graphics.DrawString(info, infoFont, Brushes.Black, x + 6, y - 8);
        }
    }

    private int MeterToPixel(double meters)
    {
        if (_road is null)
        {
            return RoadMarginLeft;
        }

        var availableWidth = (ClientSize.Width - RoadMarginLeft - RoadMarginRight) - _controlPanel.Width;
        var roadWidthPixels = Math.Max(1, availableWidth);
        var ratio = meters / _road.LengthMeters;
        return RoadMarginLeft + (int)Math.Round(ratio * roadWidthPixels, MidpointRounding.AwayFromZero);
    }

    private void UpdateStatusText(string message)
    {
        _statusLabel.Text = message;
    }

    private void HandleLogReceived(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => HandleLogReceived(message));
            return;
        }

        _logListBox.Items.Insert(0, message);
        if (_logListBox.Items.Count > 120)
        {
            _logListBox.Items.RemoveAt(_logListBox.Items.Count - 1);
        }
    }

    private NumericUpDown CreateNumericInput(string label, decimal min, decimal max, decimal value)
    {
        var lbl = new Label
        {
            Text = label,
            Dock = DockStyle.Top,
            Height = 20
        };
        _controlPanel.Controls.Add(lbl);

        var input = new NumericUpDown
        {
            Dock = DockStyle.Top,
            Height = 28,
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max)
        };
        _controlPanel.Controls.Add(input);
        _controlPanel.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 10 });
        return input;
    }

    private TextBox CreateTextInput(string label, string placeholder)
    {
        var lbl = new Label
        {
            Text = label,
            Dock = DockStyle.Top,
            Height = 20
        };
        _controlPanel.Controls.Add(lbl);

        var input = new TextBox
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = placeholder
        };
        _controlPanel.Controls.Add(input);
        _controlPanel.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 10 });
        return input;
    }

    private void StartSimulationFromUi()
    {
        try
        {
            ResetSimulation();

            var vehicleCount = (int)_vehicleCountInput.Value;
            var speedKmh = (double)_vehicleSpeedInput.Value;
            var pocketPositions = ParsePositions(_pocketLocationsInput.Text);
            var depotPositions = ParsePositions(_depotLocationsInput.Text);

            if (depotPositions.Count == 0)
            {
                depotPositions.Add(DefaultRoadLengthMeters);
            }

            var inferredRoadLength = Math.Max(
                DefaultRoadLengthMeters,
                Math.Max(
                    depotPositions.DefaultIfEmpty(DefaultRoadLengthMeters).Max(),
                    pocketPositions.DefaultIfEmpty(0).Max()));

            _road = BuildRoad(inferredRoadLength, pocketPositions, depotPositions);
            _engine = BuildEngine(_road, vehicleCount, speedKmh, depotPositions);
            _maxTicks = DefaultMaxTicks;
            _tickDurationSeconds = DefaultTickDurationSeconds;
            _timer.Interval = Math.Max(20, (int)Math.Round(_tickDurationSeconds * 1000d, MidpointRounding.AwayFromZero));

            _timer.Start();
            UpdateStatusText("Simulasyon basladi");
            Invalidate();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Baslatma hatasi:\n{ex.Message}",
                "Hata",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ResetSimulation()
    {
        _timer.Stop();
        _currentTick = 0;
        _currentStates = Array.Empty<VehicleTickState>();
        _engine = null;
        _road = null;
        _logListBox.Items.Clear();
        UpdateStatusText("Sifirlandi");
        Invalidate();
    }

    private static List<int> ParsePositions(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var items = raw.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<int>(items.Length);
        foreach (var item in items)
        {
            if (!int.TryParse(item, out var value))
            {
                throw new InvalidOperationException($"Gecersiz konum: '{item}'");
            }

            if (value < 0)
            {
                throw new InvalidOperationException($"Konum negatif olamaz: {value}");
            }

            result.Add(value);
        }

        var duplicate = result.GroupBy(x => x).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Konumlar tekrar ediyor: {duplicate.Key}");
        }

        result.Sort();
        return result;
    }

    private static Road BuildRoad(int lengthMeters, IReadOnlyCollection<int> pockets, IReadOnlyCollection<int> depots)
    {
        var sensors = Enumerable.Range(0, (lengthMeters / 40) + 1)
            .Select(i => new Sensor(i * 40))
            .ToList();

        return new Road(
            lengthMeters,
            sensors,
            pockets.Select(p => new Pocket(p)),
            depots.Select(d => new Depot(d)));
    }

    private static SimulationEngine BuildEngine(Road road, int vehicleCount, double speedKmh, IReadOnlyList<int> depots)
    {
        var vehicles = new List<Vehicle>(vehicleCount);
        for (var i = 0; i < vehicleCount; i++)
        {
            var fromLeft = (i % 2) == 0;
            var startPosition = fromLeft ? 0d : road.LengthMeters;
            var direction = fromLeft ? VehicleDirection.LeftToRight : VehicleDirection.RightToLeft;
            var targetDepot = depots[i % depots.Count];

            vehicles.Add(new Vehicle(
                id: $"V{i + 1}",
                direction: direction,
                positionMeters: startPosition,
                speedKmh: speedKmh,
                targetDepotPositionMeters: targetDepot,
                isPriority: i == 0));
        }

        var minSafeDistance = 20d;
        var strategy = new SafetyFirstTrafficStrategy(minSafeDistance);
        var controller = new TrafficController(strategy, DefaultTickDurationSeconds);
        return new SimulationEngine(
            road,
            vehicles,
            controller,
            enableReturnTrip: true,
            minimumSafeDistanceMeters: minSafeDistance,
            conflictDetectionRangeMeters: minSafeDistance);
    }

    private static Color GetColorForVehicle(string vehicleId)
    {
        var hash = Math.Abs(vehicleId.GetHashCode(StringComparison.Ordinal));
        var palette = new[]
        {
            Color.Crimson,
            Color.DodgerBlue,
            Color.MediumSeaGreen,
            Color.DarkOrange,
            Color.MediumVioletRed,
            Color.SlateBlue
        };

        return palette[hash % palette.Length];
    }
}
