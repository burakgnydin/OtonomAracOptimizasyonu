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
    private const double SensorVisibilityToleranceMeters = 2d;

    private readonly Road _road;
    private readonly SimulationEngine _engine;
    private readonly int _maxTicks;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Label _statusLabel;
    private readonly ListBox _logListBox;

    private IReadOnlyCollection<VehicleTickState> _currentStates = Array.Empty<VehicleTickState>();
    private int _currentTick;

    public SimulationVisualizerForm(Road road, SimulationEngine engine, int maxTicks, double tickDurationSeconds)
    {
        ArgumentNullException.ThrowIfNull(road);
        ArgumentNullException.ThrowIfNull(engine);

        _road = road;
        _engine = engine;
        _maxTicks = maxTicks;

        Text = "Otonom Arac Simulasyon Gorsellestirme";
        Width = 980;
        Height = 420;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        BackColor = Color.White;

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

        _timer = new System.Windows.Forms.Timer
        {
            Interval = Math.Max(50, (int)Math.Round(tickDurationSeconds * 1000d, MidpointRounding.AwayFromZero))
        };
        _timer.Tick += OnTimerTick;

        UpdateStatusText("Hazir");
        Shown += (_, _) => _timer.Start();
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
        DrawVisibleVehicles(g);
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_currentTick >= _maxTicks)
        {
            _timer.Stop();
            UpdateStatusText("Maksimum tick sayisina ulasildi");
            return;
        }

        var report = _engine.Tick();
        _currentTick = report.TickIndex;
        _currentStates = report.VehicleStates;

        UpdateStatusText($"Tick: {_currentTick}/{_maxTicks}  |  Simulasyon Saati: {report.SimulationTime:hh\\:mm\\:ss}");

        Invalidate();

        if (_currentStates.All(state => state.ReachedTargetDepot))
        {
            _timer.Stop();
            UpdateStatusText($"Tum araclar hedefe ulasti (tick {_currentTick})");
        }
    }

    private void DrawRoad(Graphics graphics)
    {
        var x1 = RoadMarginLeft;
        var x2 = ClientSize.Width - RoadMarginRight;
        using var roadPen = new Pen(Color.Black, 3);
        graphics.DrawLine(roadPen, x1, RoadY, x2, RoadY);

        using var font = new Font("Segoe UI", 9);
        graphics.DrawString("0m", font, Brushes.DimGray, x1 - 10, RoadY + 14);
        graphics.DrawString($"{_road.LengthMeters}m", font, Brushes.DimGray, x2 - 20, RoadY + 14);
    }

    private void DrawSensors(Graphics graphics)
    {
        using var sensorBrush = new SolidBrush(Color.DarkSlateGray);
        using var font = new Font("Segoe UI", 8);

        foreach (var sensor in _road.Sensors.OrderBy(s => s.PositionMeters))
        {
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

    private void DrawVisibleVehicles(Graphics graphics)
    {
        var visibleVehicles = _currentStates
            .Where(state => IsVehicleVisibleAtAnySensor(state.PositionMeters))
            .OrderBy(state => state.VehicleId)
            .ToList();

        for (var i = 0; i < visibleVehicles.Count; i++)
        {
            var vehicle = visibleVehicles[i];
            var x = MeterToPixel(vehicle.PositionMeters);
            var y = RoadY - 22 - ((i % 3) * 16);
            using var brush = new SolidBrush(GetColorForVehicle(vehicle.VehicleId));
            graphics.FillEllipse(
                brush,
                x - (VehicleDotSize / 2),
                y - (VehicleDotSize / 2),
                VehicleDotSize,
                VehicleDotSize);
        }
    }

    private bool IsVehicleVisibleAtAnySensor(double vehiclePositionMeters)
    {
        return _road.Sensors.Any(sensor => Math.Abs(vehiclePositionMeters - sensor.PositionMeters) <= SensorVisibilityToleranceMeters);
    }

    private int MeterToPixel(double meters)
    {
        var roadWidthPixels = Math.Max(1, ClientSize.Width - RoadMarginLeft - RoadMarginRight);
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
