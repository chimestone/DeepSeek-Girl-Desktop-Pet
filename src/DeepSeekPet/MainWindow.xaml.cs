using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
    private readonly bool _qaSettings;
    private readonly bool _qaAssistant;
    private string? _currentSkinId;
    private System.Windows.Point _dragStartScreen;
    private System.Windows.Point _dragStartWindow;
    private MenuItem? _walkMenuItem;
    private AssistantWindow? _assistantWindow;
    private readonly DispatcherTimer _assistantHideTimer = new();
    private int _assistantAnimationGeneration;
    private bool _assistantAutoDismiss;

    public MainWindow()
    {
        InitializeComponent();
        // Makes the frameless window discoverable by automated UI checks without
        // changing normal desktop-pet behavior.
        var arguments = Environment.GetCommandLineArgs();
        _qaSettings = arguments.Contains("--qa-settings");
        _qaAssistant = arguments.Contains("--qa-assistant");
        _qaVisible = _qaSettings || _qaAssistant || arguments.Contains("--qa-visible");
        if (_qaVisible)
        {
            ShowInTaskbar = true;
        }
        BuildContextMenu();

        _assistantHideTimer.Tick += (_, _) =>
        {
            _assistantHideTimer.Stop();
            if (!(_assistantWindow?.IsMouseOver ?? false))
                HideAssistant();
        };

        Loaded += OnLoaded;
        LocationChanged += (_, _) => PositionAssistant();
        SizeChanged += (_, _) => PositionAssistant();
        Closed += (_, _) =>
        {
            CompositionTarget.Rendering -= OnRendering;
            _assistantWindow?.CloseForAppExit();
        };
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
            Topmost = true;
            var work = SystemParameters.WorkArea;
            Left = work.Left + (work.Width - Width) / 2;
            Top = work.Top + (work.Height - Height) / 2;
        }
        CompositionTarget.Rendering += OnRendering;
        _lastFrameSeconds = _clock.Elapsed.TotalSeconds;
        ScheduleNextBehavior(_lastFrameSeconds, startWalking: false);
        if (_qaSettings)
        {
            Dispatcher.BeginInvoke(new Action(OpenSettings));
        }
        else if (_qaAssistant)
        {
            Dispatcher.BeginInvoke(new Action(OpenAssistant));
        }
    }

    private void BuildContextMenu()
    {
        var menu = new ContextMenu();
        var assistantItem = new MenuItem { Header = "显示/隐藏小助手" };
        assistantItem.Click += (_, _) => ToggleAssistant();

        var settingsItem = new MenuItem { Header = "设置…" };
        settingsItem.Click += (_, _) => OpenSettings();

        _walkMenuItem = new MenuItem { Header = "自动散步", IsCheckable = true };
        _walkMenuItem.Click += (_, _) =>
        {
            _settings.AutoWalk = _walkMenuItem.IsChecked;
            _settings.Save();
            if (!_settings.AutoWalk || (_assistantWindow?.IsVisible ?? false)) _isWalking = false;
        };

        var resetItem = new MenuItem { Header = "回到屏幕右下角" };
        resetItem.Click += (_, _) => ResetPosition();

        var exitItem = new MenuItem { Header = "退出" };
        exitItem.Click += (_, _) => Close();

        menu.Items.Add(assistantItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(_walkMenuItem);
        menu.Items.Add(resetItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);
        menu.Opened += (_, _) => _walkMenuItem.IsChecked = _settings.AutoWalk;
        Root.ContextMenu = menu;
    }

    private void EnsureAssistant()
    {
        if (_assistantWindow is not null) return;

        _assistantWindow = new AssistantWindow { Owner = this, Topmost = _settings.AlwaysOnTop };
        _assistantWindow.ApplyScale(_settings.PetSize);
        _assistantWindow.ApplyCharacter(SkinCatalog.Find(_settings.SkinId).DisplayName);
        if (_qaAssistant)
        {
            _assistantWindow.ShowInTaskbar = true;
            _assistantWindow.ShowActivated = true;
            _assistantWindow.Topmost = true;
        }
        _assistantWindow.MouseEnter += (_, _) => _assistantHideTimer.Stop();
        _assistantWindow.MouseLeave += (_, _) =>
        {
            if (_assistantAutoDismiss) ScheduleAssistantHide(TimeSpan.FromSeconds(2));
        };
        _assistantWindow.LayoutRequested += (_, _) => PositionAssistant();
        _assistantWindow.SettingsRequested += (_, _) => OpenSettings();
        _assistantWindow.MoveRequested += (dx, dy) => { Left += dx; Top += dy; ClampToVisibleWorkArea(); PositionAssistant(); SavePosition(); };
        _assistantWindow.HideRequested += (_, _) => HideAssistant(immediate: true);
        _assistantWindow.AttentionRequested += (_, _) =>
        {
            ShowAssistant(manualOpen: false);
            _assistantAutoDismiss = false;
        };
    }

    private void OpenAssistant() => ShowAssistant();

    private void ToggleAssistant()
    {
        EnsureAssistant();
        if (_assistantWindow!.IsVisible) HideAssistant(immediate: true);
        else ShowAssistant();
    }

    private void ShowAssistant(bool manualOpen = true)
    {
        EnsureAssistant();
        _assistantHideTimer.Stop();
        _assistantAutoDismiss = false;
        if (manualOpen) _assistantWindow!.PrepareManualOpen();
        _isWalking = false;
        PositionAssistant();

        var generation = ++_assistantAnimationGeneration;
        if (!_assistantWindow!.IsVisible)
        {
            _assistantWindow.Opacity = 0;
            _assistantWindow.Show();
        }

        var fadeIn = new DoubleAnimation(_assistantWindow.Opacity, 1, TimeSpan.FromMilliseconds(140));
        fadeIn.Completed += (_, _) =>
        {
            if (generation == _assistantAnimationGeneration) _assistantWindow.Opacity = 1;
        };
        _assistantWindow.BeginAnimation(OpacityProperty, fadeIn, HandoffBehavior.SnapshotAndReplace);
    }

    private void ScheduleAssistantHide(TimeSpan delay)
    {
        if (_qaAssistant) return;
        _assistantHideTimer.Stop();
        _assistantHideTimer.Interval = delay;
        _assistantHideTimer.Start();
    }

    private void HideAssistant(bool immediate = false)
    {
        if (_qaAssistant && !immediate) return;
        _assistantHideTimer.Stop();
        if (_assistantWindow is not { IsVisible: true }) return;

        var generation = ++_assistantAnimationGeneration;
        if (immediate)
        {
            _assistantWindow.Hide();
            _assistantWindow.Opacity = 1;
            _assistantWindow.ClearNotice();
            _assistantAutoDismiss = false;
            return;
        }

        var fadeOut = new DoubleAnimation(_assistantWindow.Opacity, 0, TimeSpan.FromMilliseconds(120));
        fadeOut.Completed += (_, _) =>
        {
            if (generation != _assistantAnimationGeneration || _assistantWindow.IsMouseOver) return;
            _assistantWindow.Hide();
            _assistantWindow.Opacity = 1;
            _assistantWindow.ClearNotice();
            _assistantAutoDismiss = false;
        };
        _assistantWindow.BeginAnimation(OpacityProperty, fadeOut, HandoffBehavior.SnapshotAndReplace);
    }

    private void PositionAssistant()
    {
        if (_assistantWindow is null) return;

        _assistantWindow.Left = Left;
        _assistantWindow.Top = Top + Height - _assistantWindow.Height;
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
        if (_assistantWindow is not null)
        {
            _assistantWindow.Topmost = _settings.AlwaysOnTop;
            _assistantWindow.ApplyScale(_settings.PetSize);
            PositionAssistant();
        }
        _walkMenuItem!.IsChecked = _settings.AutoWalk;
        ApplySkin(animate: !initial);

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

    private void ApplySkin(bool animate)
    {
        var skin = SkinCatalog.Find(_settings.SkinId);
        _settings.SkinId = skin.Id;
        Title = $"{skin.DisplayName}桌宠";
        _assistantWindow?.ApplyCharacter(skin.DisplayName);

        if (_currentSkinId == skin.Id && PetImage.Source is not null) return;

        var nextSource = new BitmapImage(skin.ResourceUri);
        _currentSkinId = skin.Id;

        if (!animate || PetImage.Source is null)
        {
            PetImage.Source = nextSource;
            PetImage.Opacity = 1;
            PetImageIncoming.Source = null;
            PetImageIncoming.Opacity = 0;
            return;
        }

        PetImageIncoming.Source = nextSource;
        PetImageIncoming.Opacity = 0;
        var duration = new Duration(TimeSpan.FromMilliseconds(160));
        var fadeOut = new DoubleAnimation(1, 0, duration);
        var fadeIn = new DoubleAnimation(0, 1, duration);
        fadeIn.Completed += (_, _) =>
        {
            PetImage.BeginAnimation(OpacityProperty, null);
            PetImageIncoming.BeginAnimation(OpacityProperty, null);
            PetImage.Source = nextSource;
            PetImage.Opacity = 1;
            PetImageIncoming.Source = null;
            PetImageIncoming.Opacity = 0;
        };

        PetImage.BeginAnimation(OpacityProperty, fadeOut, HandoffBehavior.SnapshotAndReplace);
        PetImageIncoming.BeginAnimation(OpacityProperty, fadeIn, HandoffBehavior.SnapshotAndReplace);
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
        if (!_settings.AutoWalk || (_assistantWindow?.IsVisible ?? false))
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
        if (e.ClickCount >= 2)
        {
            if (_dragging)
            {
                _dragging = false;
                ReleaseMouseCapture();
            }
            ToggleAssistant();
            e.Handled = true;
            return;
        }

        _dragging = true;
        _dragMoved = false;
        // Window.Left/Top are measured in WPF device-independent pixels, while
        // PointToScreen returns physical pixels. Mixing the two makes dragging
        // faster than the pointer whenever Windows display scaling is above 100%.
        _dragStartScreen = GetCursorPositionInDips();
        _dragStartWindow = new System.Windows.Point(Left, Top);
        _isWalking = false;
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;

        var current = GetCursorPositionInDips();
        var dx = current.X - _dragStartScreen.X;
        var dy = current.Y - _dragStartScreen.Y;
        if (Math.Abs(dx) + Math.Abs(dy) > 4 && !_dragMoved)
        {
            _dragMoved = true;
        }

        Left = _dragStartWindow.X + dx;
        Top = _dragStartWindow.Y + dy;
        ClampToVisibleWorkArea();
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
        var work = GetCurrentWorkArea();
        Left = work.Right - Width - 24;
        // Sit directly on the taskbar/work-area boundary by default.
        Top = work.Bottom - Height;
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
        var deviceCenter = PointToScreen(new System.Windows.Point(
            Math.Max(1, ActualWidth) / 2,
            Math.Max(1, ActualHeight) / 2));
        var screen = Forms.Screen.FromPoint(new System.Drawing.Point((int)deviceCenter.X, (int)deviceCenter.Y));
        var area = screen.WorkingArea;
        var topLeft = DeviceToDip(new System.Windows.Point(area.Left, area.Top));
        var bottomRight = DeviceToDip(new System.Windows.Point(area.Right, area.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private System.Windows.Point GetCursorPositionInDips()
    {
        var cursor = Forms.Cursor.Position;
        return DeviceToDip(new System.Windows.Point(cursor.X, cursor.Y));
    }

    private System.Windows.Point DeviceToDip(System.Windows.Point point)
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null) return point;

        // Anchor the conversion to this window instead of scaling absolute
        // desktop coordinates. This avoids jumps when monitors have different
        // DPI scales or non-zero virtual-screen origins.
        var deviceOrigin = PointToScreen(new System.Windows.Point(0, 0));
        var dpi = VisualTreeHelper.GetDpi(this);
        return new System.Windows.Point(
            Left + (point.X - deviceOrigin.X) / dpi.DpiScaleX,
            Top + (point.Y - deviceOrigin.Y) / dpi.DpiScaleY);
    }

    private void SavePosition()
    {
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Save();
    }
}
