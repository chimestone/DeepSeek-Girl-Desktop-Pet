using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace DeepSeekPet;

public partial class AssistantWindow : Window
{
    public event EventHandler? AttentionRequested;
    public event EventHandler? HideRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? LayoutRequested;
    public event Action<double, double>? MoveRequested;
    private readonly AssistantData _data = AssistantData.Load();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTimeOffset? _deadline;
    private TimeSpan _remaining;
    private bool _isFocusPhase = true;
    private bool _allowClose;
    private double _petSize = 250;
    private bool _movingHeader;
    private System.Windows.Point _headerCursor;
    public bool IsExpanded { get; private set; }

    public AssistantWindow()
    {
        InitializeComponent();
        _data.FocusMinutes = Math.Clamp(_data.FocusMinutes, 1, 180);
        _data.BreakMinutes = Math.Clamp(_data.BreakMinutes, 1, 180);
        _remaining = TimeSpan.FromMinutes(_data.FocusMinutes);
        _timer.Tick += Timer_Tick;
        InitializeChat();
        Closing += (_, e) => { if (!_allowClose) { e.Cancel = true; HideRequested?.Invoke(this, EventArgs.Empty); } };
    }
    public void CloseForAppExit()
    {
        _chatCancellation?.Cancel();
        _timer.Stop();
        _allowClose = true;
        Close();
    }
    public void ApplyScale(double petSize)
    {
        _petSize = petSize;
        Width = petSize;
        Height = petSize * 1.04 * (IsExpanded ? 1 : 0.5);
    }
    public void SetExpanded(bool expanded)
    {
        IsExpanded = expanded;
        ExpandButton.Content = expanded ? "⤡" : "⤢";
        ExpandButton.ToolTip = expanded ? "收回至下半身范围" : "展开至桌宠全身范围";
        ApplyScale(_petSize);
        RenderDialogue(scrollToStart: !expanded);
        LayoutRequested?.Invoke(this, EventArgs.Empty);
    }
    private void Expand_Click(object sender, RoutedEventArgs e) => SetExpanded(!IsExpanded);
    public void PrepareManualOpen()
    {
        ClearNotice();
        RenderDialogue(scrollToStart: true);
        UpdateTimerStatus();
    }
    public void ApplyCharacter(string name)
    {
        _dialogue.SpeakerName = name;
        HeaderTitle.Text = name;
        Title = $"{name}对话";
        RenderDialogue();
    }
    public void ClearNotice() { NoticeBorder.Visibility = Visibility.Collapsed; HeaderTitle.Text = _dialogue.SpeakerName; HeaderTitle.ToolTip = null; }
    private void HideButton_Click(object sender, RoutedEventArgs e) => HideRequested?.Invoke(this, EventArgs.Empty);
    private void SettingsButton_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
    private void ChatInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        e.Handled = true;
        SendChat_Click(sender, e);
    }
    private void HeaderDrag_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not System.Windows.Controls.TextBlock && e.OriginalSource != HeaderBar) return;
        _movingHeader = true;
        _headerCursor = PointToScreen(e.GetPosition(this));
        HeaderBar.CaptureMouse();
        e.Handled = true;
    }
    private void HeaderDrag_Move(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_movingHeader || e.LeftButton != MouseButtonState.Pressed) return;
        var cursor = PointToScreen(e.GetPosition(this));
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        var dx = (cursor.X - _headerCursor.X) / dpi.DpiScaleX;
        var dy = (cursor.Y - _headerCursor.Y) / dpi.DpiScaleY;
        _headerCursor = cursor;
        MoveRequested?.Invoke(dx, dy);
    }
    private void HeaderDrag_End(object sender, MouseButtonEventArgs e)
    {
        _movingHeader = false;
        HeaderBar.ReleaseMouseCapture();
    }
    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_deadline is null) return;
        _remaining = _deadline.Value - DateTimeOffset.UtcNow;
        if (_remaining > TimeSpan.Zero) { UpdateTimerStatus(); return; }
        _remaining = TimeSpan.Zero;
        _deadline = null;
        _timer.Stop();
        var notice = _isFocusPhase ? "专注时间结束，休息一下吧！" : "休息结束，可以开始下一轮啦！";
        _isFocusPhase = !_isFocusPhase;
        _remaining = TimeSpan.FromMinutes(_isFocusPhase ? _data.FocusMinutes : _data.BreakMinutes);
        SetExpanded(false);
        NoticeText.Text = notice;
        HeaderTitle.Text = _dialogue.SpeakerName;
        HeaderTitle.ToolTip = notice;
        System.Media.SystemSounds.Asterisk.Play();
        AttentionRequested?.Invoke(this, EventArgs.Empty);
        _dialogue.AddReply(notice);
        RenderDialogue(scrollToStart: true);

        UpdateTimerStatus();
    }
    private void UpdateTimerStatus()
    {
        if (_chatCancellation != null) return;
        if (_deadline != null) ChatStatus.Text = $"{(_isFocusPhase ? "专注" : "休息")} · {Math.Max(0, (int)Math.Ceiling(_remaining.TotalSeconds)) / 60:00}:{Math.Max(0, (int)Math.Ceiling(_remaining.TotalSeconds)) % 60:00}";
        else ChatStatus.Text = string.Empty;
    }
}
