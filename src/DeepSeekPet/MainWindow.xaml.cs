using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace DeepSeekPet;

public partial class MainWindow : Window
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Random _random = new();
    private PetSettings _settings = PetSettings.Load();
    private double _lastFrameSeconds;
    private double _behaviorUntil;
    private double _reactionStart = -10;
    private int _direction = -1;
    private bool _isWalking;
    private bool _dragging;
    private bool _dragMoved;
    private readonly bool _qaVisible;
    private System.Windows.Point _dragStartScreen;
    private System.Windows.Point _dragStartWindow;
    private MenuItem? _walkMenuItem;

    public MainWindow()
    {
        InitializeComponent();
        // Makes the frameless window discoverable by automated UI checks without
        // changing normal desktop-pet behavior.
        _qaVisible = Environment.GetCommandLineArgs().Contains("--qa-visible");
        if (_qaVisible)
        {
            ShowInTaskbar = true;
        }
        BuildContextMenu();

        Loaded += OnLoaded;
        Closed += (_, _) => CompositionTarget.Rendering -= OnRendering;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseRightButtonUp += (_, _) => Root.ContextMenu!.IsOpen = true;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplySettings(initial: true);
        if (_qaVisible)
        {
            _settings.AutoWalk = false;
            var work = SystemParameters.WorkArea;
            Left = work.Left + (work.Width - Width) / 2;
            Top = work.Top + (work.Height - Height) / 2;
        }
        CompositionTarget.Rendering += OnRendering;
        _lastFrameSeconds = _clock.Elapsed.TotalSeconds;
        ScheduleNextBehavior(_lastFrameSeconds, startWalking: false);
    }

    private void BuildContextMenu()
    {
        var menu = new ContextMenu();
        var settingsItem = new MenuItem { Header = "设置…" };
        settingsItem.Click += (_, _) => OpenSettings();

        _walkMenuItem = new MenuItem { Header = "自动散步", IsCheckable = true };
        _walkMenuItem.Click += (_, _) =>
        {
            _settings.AutoWalk = _walkMenuItem.IsChecked;
            _settings.Save();
            if (!_settings.AutoWalk) _isWalking = false;
        };

        var resetItem = new MenuItem { Header = "回到屏幕右下角" };
        resetItem.Click += (_, _) => ResetPosition();

        var exitItem = new MenuItem { Header = "退出" };
        exitItem.Click += (_, _) => Close();

        menu.Items.Add(settingsItem);
        menu.Items.Add(_walkMenuItem);
        menu.Items.Add(resetItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);
        menu.Opened += (_, _) => _walkMenuItem.IsChecked = _settings.AutoWalk;
        Root.ContextMenu = menu;
    }

    private void OpenSettings()
    {
        var dialog = new SettingsWindow(_settings.Clone()) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        _settings = dialog.Result;
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Save();
        ApplySettings(initial: false);
    }

    private void ApplySettings(bool initial)
    {
        Width = _settings.PetSize;
        Height = _settings.PetSize * 1.04;
        Topmost = _settings.AlwaysOnTop;
        _walkMenuItem!.IsChecked = _settings.AutoWalk;

        if (initial && _settings.Left is double left && _settings.Top is double top)
        {
            Left = left;
            Top = top;
            ClampToVisibleWorkArea();
        }
        else if (initial)
        {
            ResetPosition(save: false);
        }
        else
        {
            ClampToVisibleWorkArea();
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = Math.Clamp(now - _lastFrameSeconds, 0, 0.05);
        _lastFrameSeconds = now;

        if (!_dragging)
        {
            UpdateBehavior(now, dt);
        }

        UpdateAnimation(now);
    }

    private void UpdateBehavior(double now, double dt)
    {
        if (!_settings.AutoWalk)
        {
            _isWalking = false;
            return;
        }

        if (now >= _behaviorUntil)
        {
            ScheduleNextBehavior(now, startWalking: !_isWalking);
        }

        if (!_isWalking) return;

        var work = GetCurrentWorkArea();
        Left += _direction * _settings.WalkSpeed * dt;

        if (Left <= work.Left)
        {
            Left = work.Left;
            _direction = 1;
        }
        else if (Left + ActualWidth >= work.Right)
        {
            Left = work.Right - ActualWidth;
            _direction = -1;
        }
    }

    private void ScheduleNextBehavior(double now, bool startWalking)
    {
        _isWalking = startWalking && _settings.AutoWalk;
        _behaviorUntil = now + (_isWalking
            ? 3.5 + _random.NextDouble() * 4.5
            : 3.0 + _random.NextDouble() * 6.0);

        if (_isWalking && _random.NextDouble() < 0.35)
        {
            _direction *= -1;
        }
    }

    private void UpdateAnimation(double now)
    {
        var idleWave = Math.Sin(now * Math.PI * 2 / 2.8);
        var scaleX = 1.0 - idleWave * 0.004;
        var scaleY = 1.0 + idleWave * 0.008;
        var translateY = -1.2 * (idleWave + 1) / 2;
        var angle = 0.0;

        if (_isWalking && !_dragging)
        {
            var step = now * 7.2;
            var sway = Math.Sin(step);
            var lift = Math.Abs(Math.Sin(step));
            scaleX *= 1.0 + lift * 0.012;
            scaleY *= 1.0 - lift * 0.009;
            translateY -= lift * 3.4;
            angle = sway * 1.5;
        }

        var reactionTime = now - _reactionStart;
        if (reactionTime is >= 0 and < 0.9)
        {
            var envelope = Math.Exp(-4.2 * reactionTime);
            var bounce = Math.Sin(reactionTime * 24) * envelope;
            scaleX *= 1.0 + bounce * 0.11;
            scaleY *= 1.0 - bounce * 0.09;
            translateY -= Math.Max(0, Math.Sin(reactionTime * Math.PI / 0.9)) * 11;
            angle += Math.Sin(reactionTime * 18) * envelope * 2.2;
        }

        PetScale.ScaleX = _direction * scaleX;
        PetScale.ScaleY = scaleY;
        PetTranslate.Y = translateY;
        PetRotate.Angle = angle * _direction;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _dragMoved = false;
        _dragStartScreen = PointToScreen(e.GetPosition(this));
        _dragStartWindow = new System.Windows.Point(Left, Top);
        _isWalking = false;
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;

        var current = PointToScreen(e.GetPosition(this));
        var dx = current.X - _dragStartScreen.X;
        var dy = current.Y - _dragStartScreen.Y;
        if (Math.Abs(dx) + Math.Abs(dy) > 4) _dragMoved = true;

        Left = _dragStartWindow.X + dx;
        Top = _dragStartWindow.Y + dy;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;

        _dragging = false;
        ReleaseMouseCapture();
        if (_dragMoved)
        {
            SnapAndClamp();
            SavePosition();
        }
        else
        {
            _reactionStart = _clock.Elapsed.TotalSeconds;
        }

        ScheduleNextBehavior(_clock.Elapsed.TotalSeconds + 1.4, startWalking: false);
        e.Handled = true;
    }

    private void SnapAndClamp()
    {
        var work = GetCurrentWorkArea();
        Left = Math.Clamp(Left, work.Left, work.Right - ActualWidth);
        Top = Math.Clamp(Top, work.Top, work.Bottom - ActualHeight);
        if (!_settings.EdgeSnap) return;

        const double snapDistance = 42;
        var candidates = new[]
        {
            (Distance: Math.Abs(Left - work.Left), Axis: 0, Value: work.Left),
            (Distance: Math.Abs(Left + ActualWidth - work.Right), Axis: 0, Value: work.Right - ActualWidth),
            (Distance: Math.Abs(Top - work.Top), Axis: 1, Value: work.Top),
            (Distance: Math.Abs(Top + ActualHeight - work.Bottom), Axis: 1, Value: work.Bottom - ActualHeight)
        };

        var nearest = candidates.MinBy(candidate => candidate.Distance);
        if (nearest.Distance > snapDistance) return;
        if (nearest.Axis == 0) Left = nearest.Value;
        else Top = nearest.Value;
    }

    private void ResetPosition(bool save = true)
    {
        var work = SystemParameters.WorkArea;
        Left = work.Right - Width - 24;
        Top = work.Bottom - Height - 8;
        if (save) SavePosition();
    }

    private void ClampToVisibleWorkArea()
    {
        var work = GetCurrentWorkArea();
        Left = Math.Clamp(Left, work.Left, Math.Max(work.Left, work.Right - Width));
        Top = Math.Clamp(Top, work.Top, Math.Max(work.Top, work.Bottom - Height));
    }

    private Rect GetCurrentWorkArea()
    {
        var center = new System.Windows.Point(Left + Math.Max(1, ActualWidth) / 2, Top + Math.Max(1, ActualHeight) / 2);
        var screen = Forms.Screen.FromPoint(new System.Drawing.Point((int)center.X, (int)center.Y));
        var area = screen.WorkingArea;
        return new Rect(area.Left, area.Top, area.Width, area.Height);
    }

    private void SavePosition()
    {
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Save();
    }
}
