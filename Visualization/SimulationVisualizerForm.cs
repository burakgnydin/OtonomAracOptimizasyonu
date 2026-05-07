using System.Drawing.Drawing2D;
using OtonomAracOptimizasyonu.Models;
using OtonomAracOptimizasyonu.Services;

namespace OtonomAracOptimizasyonu.Visualization;

public sealed class SimulationVisualizerForm : Form
{
    private const int RoadMarginLeft = 50;
    private const int RoadMarginRight = 50;
    private const int RoadY = 170;
    private const int SensorSize = 10;
    private const int VehicleDotSize = 12;
    private const int DefaultRoadLengthMeters = 240;
    private const int DefaultMaxTicks = 320;
    private const double DefaultTickDurationSeconds = 1.0;
    private const double SensorVisibilityToleranceMeters = 20d;

    private readonly System.Windows.Forms.Timer _timer;
    private readonly Panel _controlPanel;
    private readonly Panel _pnlTopControls;
    private readonly Label _statusLabel;
    private readonly ListBox _logListBox;
    private readonly NumericUpDown _vehicleSpeedInput;
    private readonly NumericUpDown _speedMultiplierInput;
    private readonly TextBox _pocketLocationsInput;
    private readonly TextBox _depotLocationsInput;
    private readonly FlowLayoutPanel _pnlDynamicVehicles;
    private readonly Button _addVehicleButton;
    private readonly Button _startButton;
    private readonly Button _stopButton;

    private readonly List<VehicleConfigInput> _vehicleInputList = [];
    private readonly Dictionary<VehicleConfigInput, VehicleRowView> _vehicleRowMap = new();

    private Road? _road;
    private SimulationEngine? _engine;
    private IReadOnlyCollection<VehicleTickState> _currentStates = Array.Empty<VehicleTickState>();
    private int _currentTick;
    private int _vehicleSequence = 1;

    public SimulationVisualizerForm()
    {
        Text = "Otonom Arac Simulasyon Gorsellestirme";
        Width = 1240;
        Height = 640;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        BackColor = Color.White;

        _controlPanel = new Panel
        {
            Dock = DockStyle.Right,
            Width = 430,
            Padding = new Padding(10),
            BackColor = Color.FromArgb(245, 246, 248)
        };
        Controls.Add(_controlPanel);

        _pnlTopControls = new Panel
        {
            Name = "PnlTopControls",
            Dock = DockStyle.Top,
            Height = 320
        };

        _pnlDynamicVehicles = new FlowLayoutPanel
        {
            Name = "PnlDynamicVehicles",
            Dock = DockStyle.Fill,
            AutoScroll = true,
            WrapContents = false,
            FlowDirection = FlowDirection.TopDown,
            BackColor = Color.FromArgb(235, 238, 242),
            AutoSize = false,
            Margin = new Padding(0),
            Padding = new Padding(4)
        };
        _pnlDynamicVehicles.SizeChanged += (_, _) => ResizeVehicleRowsToPanelWidth();
        _controlPanel.Controls.Add(_pnlDynamicVehicles);
        _controlPanel.Controls.Add(_pnlTopControls);

        _statusLabel = new Label { Dock = DockStyle.Top, Height = 32, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0) };
        Controls.Add(_statusLabel);

        _logListBox = new ListBox { Dock = DockStyle.Bottom, Height = 160 };
        Controls.Add(_logListBox);

        AddTopLabel("Kontrol Paneli", 12, true);
        AddTopHelp("Arac satirlari dinamik eklenir. Depo secimleri kod listesine aninda yazilir.");

        _startButton = new Button { Text = "Simulasyonu Baslat", Dock = DockStyle.Top, Height = 36 };
        _startButton.Click += (_, _) => StartSimulationFromUi();
        _pnlTopControls.Controls.Add(_startButton);

        _stopButton = new Button { Text = "Sonlandir", Dock = DockStyle.Top, Height = 36 };
        _stopButton.Click += (_, _) => ResetSimulation();
        _pnlTopControls.Controls.Add(_stopButton);
        _pnlTopControls.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8 });

        // Kural 1: Varsayilan hiz kesin olarak 20.
        _vehicleSpeedInput = CreateNumericInput("Arac Hizi (km/h)", 1, (decimal)Vehicle.MaxSpeedKmh, 20);
        _speedMultiplierInput = CreateNumericInput("Simulasyon Hizi (x tick/frame)", 1, 50, 4);
        _pocketLocationsInput = CreateTextInput("Cep (Pocket) Konumlari", "80, 200");
        _depotLocationsInput = CreateTextInput("Depo (Depot) Konumlari", "70, 120, 240");
        _depotLocationsInput.TextChanged += (_, _) => RefreshDepotOptionsForAllRows();

        _addVehicleButton = new Button { Text = "Arac Ekle", Dock = DockStyle.Top, Height = 32 };
        _addVehicleButton.Click += BtnAddVehicle_Click;
        _pnlTopControls.Controls.Add(_addVehicleButton);
        _pnlTopControls.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8 });

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += OnTimerTick;

        BtnAddVehicle_Click(this, EventArgs.Empty);
        BtnAddVehicle_Click(this, EventArgs.Empty);
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

    private void BtnAddVehicle_Click(object? sender, EventArgs e)
    {
        var config = new VehicleConfigInput
        {
            VehicleID = $"Arac {_vehicleSequence++}",
            TargetDepots = []
        };

        _vehicleInputList.Add(config);
        var view = CreateVehicleInputRow(config);
        _vehicleRowMap[config] = view;
        _pnlDynamicVehicles.Controls.Add(view.RowPanel);
        ResizeVehicleRowsToPanelWidth();
        _pnlDynamicVehicles.PerformLayout();
        _controlPanel.PerformLayout();
    }

    private VehicleRowView CreateVehicleInputRow(VehicleConfigInput config)
    {
        var rowPanel = new Panel
        {
            Width = GetVehicleRowWidth(),
            Height = 92,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(2),
            Visible = true
        };

        var idLabel = new Label
        {
            Text = "Arac ID",
            Left = 6,
            Top = 8,
            Width = 58
        };

        var idTextBox = new TextBox
        {
            Left = 70,
            Top = 6,
            Width = 130,
            Text = config.VehicleID
        };

        var depotLabel = new Label
        {
            Text = "Depolar",
            Left = 6,
            Top = 34,
            Width = 58
        };

        var depotCheckedList = new CheckedListBox
        {
            Left = 70,
            Top = 30,
            Width = rowPanel.Width - 78,
            Height = 54,
            CheckOnClick = true
        };

        var removeButton = new Button
        {
            Text = "Sil",
            Width = 44,
            Height = 22,
            Left = rowPanel.Width - 50,
            Top = 4
        };

        idTextBox.TextChanged += (_, _) =>
        {
            config.VehicleID = idTextBox.Text.Trim();
        };

        depotCheckedList.ItemCheck += (_, args) =>
        {
            BeginInvoke(() =>
            {
                config.TargetDepots = depotCheckedList.CheckedItems
                    .Cast<object>()
                    .Select(item => Convert.ToInt32(item, System.Globalization.CultureInfo.InvariantCulture))
                    .OrderBy(x => x)
                    .ToList();
            });
        };

        removeButton.Click += (_, _) =>
        {
            _vehicleInputList.Remove(config);
            _vehicleRowMap.Remove(config);
            _pnlDynamicVehicles.Controls.Remove(rowPanel);
            rowPanel.Dispose();
            _pnlDynamicVehicles.PerformLayout();
        };

        rowPanel.Controls.Add(idLabel);
        rowPanel.Controls.Add(idTextBox);
        rowPanel.Controls.Add(depotLabel);
        rowPanel.Controls.Add(depotCheckedList);
        rowPanel.Controls.Add(removeButton);

        PopulateDepotOptions(depotCheckedList, config);
        return new VehicleRowView(rowPanel, idTextBox, depotCheckedList);
    }

    private void RefreshDepotOptionsForAllRows()
    {
        foreach (var kvp in _vehicleRowMap)
        {
            PopulateDepotOptions(kvp.Value.DepotSelector, kvp.Key);
        }
    }

    private void PopulateDepotOptions(CheckedListBox depotSelector, VehicleConfigInput config)
    {
        var depots = ParsePositionsAllowEmpty(_depotLocationsInput.Text);
        var previous = config.TargetDepots.ToHashSet();
        depotSelector.Items.Clear();

        foreach (var depot in depots)
        {
            var index = depotSelector.Items.Add(depot);
            if (previous.Contains(depot))
            {
                depotSelector.SetItemChecked(index, true);
            }
        }

        config.TargetDepots = depotSelector.CheckedItems
            .Cast<object>()
            .Select(item => Convert.ToInt32(item, System.Globalization.CultureInfo.InvariantCulture))
            .OrderBy(x => x)
            .ToList();
    }

    private void StartSimulationFromUi()
    {
        try
        {
            ResetSimulationRuntimeOnly();
            var speedKmh = (double)_vehicleSpeedInput.Value;
            var pocketPositions = ParsePositionsAllowEmpty(_pocketLocationsInput.Text);
            var depotPositions = ParsePositions(_depotLocationsInput.Text);

            HarvestVehicleInputsFromDynamicPanel();
            ValidateVehicleInputList(depotPositions);
            var assignments = _vehicleInputList
                .Select(vehicle => new VehicleAssignment(vehicle.VehicleID, vehicle.TargetDepots))
                .ToList();

            var inferredRoadLength = Math.Max(DefaultRoadLengthMeters, Math.Max(
                depotPositions.DefaultIfEmpty(DefaultRoadLengthMeters).Max(),
                pocketPositions.DefaultIfEmpty(0).Max()));

            _road = BuildRoad(inferredRoadLength, pocketPositions, depotPositions);
            _engine = BuildEngine(_road, assignments, speedKmh);
            _timer.Start();
            UpdateStatusText("Simulasyon basladi");
            Invalidate();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Baslatma hatasi:\n{ex.Message}", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void HarvestVehicleInputsFromDynamicPanel()
    {
        _vehicleInputList.Clear();

        foreach (var row in _pnlDynamicVehicles.Controls.OfType<Panel>())
        {
            var idInput = row.Controls.OfType<TextBox>().FirstOrDefault();
            var depotSelector = row.Controls.OfType<CheckedListBox>().FirstOrDefault();
            if (idInput is null || depotSelector is null)
            {
                continue;
            }

            var config = new VehicleConfigInput
            {
                VehicleID = idInput.Text.Trim(),
                TargetDepots = depotSelector.CheckedItems
                    .Cast<object>()
                    .Select(item => Convert.ToInt32(item, System.Globalization.CultureInfo.InvariantCulture))
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList()
            };

            _vehicleInputList.Add(config);
        }
    }

    private void ValidateVehicleInputList(IReadOnlyCollection<int> depotPositions)
    {
        if (_vehicleInputList.Count == 0)
        {
            throw new InvalidOperationException("Arac listesi bos. En az bir arac ekleyin.");
        }

        var depots = depotPositions.ToHashSet();
        foreach (var vehicle in _vehicleInputList)
        {
            if (string.IsNullOrWhiteSpace(vehicle.VehicleID))
            {
                throw new InvalidOperationException("Arac ID bos olamaz.");
            }

            if (vehicle.TargetDepots.Count == 0)
            {
                throw new InvalidOperationException($"{vehicle.VehicleID} icin en az bir depo secilmeli.");
            }

            if (vehicle.TargetDepots.Any(depot => !depots.Contains(depot)))
            {
                throw new InvalidOperationException($"{vehicle.VehicleID} icin secilen depo, depo listesinde yok.");
            }
        }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_engine is null)
        {
            _timer.Stop();
            return;
        }

        if (_currentTick >= DefaultMaxTicks)
        {
            _timer.Stop();
            UpdateStatusText("Maksimum adim sayisina ulasildi");
            return;
        }

        var ticksThisFrame = Math.Max(1, (int)_speedMultiplierInput.Value);
        SimulationTickReport? report = null;
        for (var i = 0; i < ticksThisFrame; i++)
        {
            report = _engine.Tick();
            _currentTick = report.TickIndex;
            _currentStates = report.VehicleStates;
            if (_currentStates.All(state => state.CurrentTask == VehicleTask.Completed))
            {
                break;
            }
        }

        if (report is null)
        {
            return;
        }

        UpdateStatusText($"Adim: {_currentTick}/{DefaultMaxTicks} | Saat: {report.SimulationTime:hh\\:mm\\:ss}");
        Invalidate();
    }

    private void ResetSimulation()
    {
        ResetSimulationRuntimeOnly();
        _logListBox.Items.Clear();
        _pnlDynamicVehicles.Controls.Clear();
        _vehicleInputList.Clear();
        _vehicleRowMap.Clear();
        _vehicleSequence = 1;
        _pnlDynamicVehicles.PerformLayout();
        _controlPanel.PerformLayout();
        UpdateStatusText("Sifirlandi");
        Invalidate();
    }

    private void ResetSimulationRuntimeOnly()
    {
        _timer.Stop();
        _currentTick = 0;
        _currentStates = Array.Empty<VehicleTickState>();
        _engine = null;
        _road = null;
    }

    private void ResizeVehicleRowsToPanelWidth()
    {
        var width = GetVehicleRowWidth();
        foreach (var row in _vehicleRowMap.Values)
        {
            row.RowPanel.Width = width;
            row.DepotSelector.Width = row.RowPanel.Width - 78;
        }
    }

    private int GetVehicleRowWidth()
    {
        var baseWidth = _pnlDynamicVehicles.ClientSize.Width - _pnlDynamicVehicles.Padding.Horizontal - 6;
        return Math.Max(280, baseWidth);
    }

    private void DrawRoad(Graphics graphics)
    {
        if (_road is null) return;
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
        if (_road is null) return;
        using var font = new Font("Segoe UI", 8);
        foreach (var sensor in _road.Sensors.OrderBy(s => s.PositionMeters))
        {
            var isRed = IsSensorRed(sensor.PositionMeters);
            using var brush = new SolidBrush(isRed ? Color.IndianRed : Color.SeaGreen);
            var x = MeterToPixel(sensor.PositionMeters);
            graphics.FillRectangle(brush, x - (SensorSize / 2), RoadY - (SensorSize / 2), SensorSize, SensorSize);
            graphics.DrawString($"{sensor.PositionMeters}", font, Brushes.Gray, x - 8, RoadY + 18);
        }
    }

    private bool IsSensorRed(int sensorPositionMeters)
    {
        return _currentStates.Any(state =>
            (state.CurrentTask == VehicleTask.GoingToDepot ||
             state.CurrentTask == VehicleTask.ReturningHome ||
             state.CurrentTask == VehicleTask.ReversingToPocket) &&
            Math.Abs(state.PositionMeters - sensorPositionMeters) <= SensorVisibilityToleranceMeters);
    }

    private void DrawStorageAreas(Graphics graphics)
    {
        if (_road is null) return;
        using var pocketBrush = new SolidBrush(Color.Khaki);
        using var depotBrush = new SolidBrush(Color.LightSteelBlue);
        using var outlinePen = new Pen(Color.DimGray, 1);
        using var font = new Font("Segoe UI", 8);
        foreach (var pocket in _road.Pockets)
        {
            var x = MeterToPixel(pocket.PositionMeters);
            graphics.FillRectangle(pocketBrush, x - 8, RoadY - 34, 16, 16);
            graphics.DrawRectangle(outlinePen, x - 8, RoadY - 34, 16, 16);
            graphics.DrawString($"C{pocket.PositionMeters} ({pocket.Occupancy}/{pocket.Capacity})", font, Brushes.Goldenrod, x - 34, RoadY - 54);
        }

        foreach (var depot in _road.Depots)
        {
            var x = MeterToPixel(depot.PositionMeters);
            graphics.FillRectangle(depotBrush, x - 10, RoadY + 28, 20, 14);
            graphics.DrawRectangle(outlinePen, x - 10, RoadY + 28, 20, 14);
            graphics.DrawString($"D{depot.PositionMeters} ({depot.Occupancy}/{depot.Capacity})", font, Brushes.SteelBlue, x - 38, RoadY + 44);
        }
    }

    private void DrawVehicles(Graphics graphics)
    {
        var ordered = _currentStates.OrderBy(state => state.VehicleId).ToList();
        using var infoFont = new Font("Segoe UI", 7);
        for (var i = 0; i < ordered.Count; i++)
        {
            var vehicle = ordered[i];
            var x = MeterToPixel(vehicle.PositionMeters);
            var y = RoadY - 18 - ((i % 3) * 16);
            using var brush = new SolidBrush(GetColorForVehicle(vehicle.VehicleId));
            graphics.FillEllipse(brush, x - (VehicleDotSize / 2), y - (VehicleDotSize / 2), VehicleDotSize, VehicleDotSize);
            var loadText = vehicle.HasLoad ? "Yuklu" : "Yuksuz";
            graphics.DrawString($"{vehicle.VehicleId} | {vehicle.CurrentTask} | {loadText}", infoFont, Brushes.Black, x + 6, y - 7);
        }
    }

    private int MeterToPixel(double meters)
    {
        if (_road is null) return RoadMarginLeft;
        var availableWidth = (ClientSize.Width - RoadMarginLeft - RoadMarginRight) - _controlPanel.Width;
        var roadWidthPixels = Math.Max(1, availableWidth);
        var ratio = meters / _road.LengthMeters;
        return RoadMarginLeft + (int)Math.Round(ratio * roadWidthPixels, MidpointRounding.AwayFromZero);
    }

    private static List<int> ParsePositions(string? raw)
    {
        var result = ParsePositionsAllowEmpty(raw);
        if (result.Count == 0)
        {
            throw new InvalidOperationException("Depo listesi bos olamaz.");
        }

        return result;
    }

    private static List<int> ParsePositionsAllowEmpty(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
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

        return result.Distinct().OrderBy(x => x).ToList();
    }

    private static Road BuildRoad(int lengthMeters, IReadOnlyCollection<int> pockets, IReadOnlyCollection<int> depots)
    {
        var sensors = Enumerable.Range(0, (lengthMeters / 40) + 1).Select(i => new Sensor(i * 40)).ToList();
        return new Road(lengthMeters, sensors, pockets.Select(p => new Pocket(p)), depots.Select(d => new Depot(d)));
    }

    private static SimulationEngine BuildEngine(Road road, IReadOnlyList<VehicleAssignment> assignments, double speedKmh)
    {
        var vehicles = new List<Vehicle>(assignments.Count);
        for (var i = 0; i < assignments.Count; i++)
        {
            var assignment = assignments[i];
            var delay = i * 0.5d;
            vehicles.Add(new Vehicle(
                id: assignment.VehicleId.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase).ToUpperInvariant(),
                speedKmh: speedKmh,
                targetDepots: assignment.Depots,
                spawnDelaySeconds: delay,
                isPriority: false));
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

    private void HandleLogReceived(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => HandleLogReceived(message));
            return;
        }

        _logListBox.Items.Insert(0, message);
        if (_logListBox.Items.Count > 140)
        {
            _logListBox.Items.RemoveAt(_logListBox.Items.Count - 1);
        }
    }

    private NumericUpDown CreateNumericInput(string label, decimal min, decimal max, decimal value)
    {
        AddTopLabel(label, 20, false);
        var input = new NumericUpDown
        {
            Dock = DockStyle.Top,
            Height = 28,
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max)
        };
        _pnlTopControls.Controls.Add(input);
        _pnlTopControls.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8 });
        return input;
    }

    private TextBox CreateTextInput(string label, string defaultText)
    {
        AddTopLabel(label, 20, false);
        var input = new TextBox { Dock = DockStyle.Top, Height = 28, Text = defaultText };
        _pnlTopControls.Controls.Add(input);
        _pnlTopControls.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8 });
        return input;
    }

    private void AddTopLabel(string text, int height, bool bold)
    {
        _pnlTopControls.Controls.Add(new Label
        {
            Text = text,
            Dock = DockStyle.Top,
            Height = height,
            Font = bold ? new Font("Segoe UI", 11, FontStyle.Bold) : new Font("Segoe UI", 9)
        });
    }

    private void AddTopHelp(string text)
    {
        _pnlTopControls.Controls.Add(new Label { Text = text, Dock = DockStyle.Top, Height = 38, ForeColor = Color.DimGray });
    }

    private void UpdateStatusText(string message) => _statusLabel.Text = message;

    private static Color GetColorForVehicle(string vehicleId)
    {
        var hash = Math.Abs(vehicleId.GetHashCode(StringComparison.Ordinal));
        var palette = new[] { Color.Crimson, Color.DodgerBlue, Color.MediumSeaGreen, Color.DarkOrange, Color.MediumVioletRed, Color.SlateBlue };
        return palette[hash % palette.Length];
    }

    private sealed class VehicleConfigInput
    {
        public string VehicleID { get; set; } = string.Empty;
        public List<int> TargetDepots { get; set; } = [];
    }

    private sealed record VehicleAssignment(string VehicleId, IReadOnlyList<int> Depots);

    private sealed record VehicleRowView(Panel RowPanel, TextBox VehicleIdInput, CheckedListBox DepotSelector);
}
