using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace DeepSeekPet;

public partial class AssistantWindow : Window
{
    public event EventHandler? AttentionRequested;
    public event EventHandler? HideRequested;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly AssistantData _data = AssistantData.Load();
    private readonly ObservableCollection<TodoItem> _todos;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _typingTimer = new() { Interval = TimeSpan.FromMilliseconds(38) };
    private TimeSpan _remaining;
    private bool _isFocusPhase = true;
    private bool _timerRunning;
    private bool _allowClose;
    private TextBlock? _typingTarget;
    private string _typingContent = string.Empty;
    private int _typingIndex;

    public AssistantWindow()
    {
        InitializeComponent();
        CityBox.Text = _data.WeatherCity;
        _data.FocusMinutes = Math.Clamp(_data.FocusMinutes, 1, 180);
        _data.BreakMinutes = Math.Clamp(_data.BreakMinutes, 1, 180);
        FocusMinutesBox.Text = _data.FocusMinutes.ToString();
        BreakMinutesBox.Text = _data.BreakMinutes.ToString();
        _todos = new ObservableCollection<TodoItem>(_data.Todos);
        TodoList.ItemsSource = _todos;
        _remaining = TimeSpan.FromMinutes(_data.FocusMinutes);
        _timer.Tick += Timer_Tick;
        _typingTimer.Tick += TypingTimer_Tick;
        UpdateTimerDisplay();
        Closing += (_, e) =>
        {
            if (_allowClose) return;
            e.Cancel = true;
            Hide();
        };
    }

    public void CloseForAppExit()
    {
        _allowClose = true;
        Close();
    }

    public void PointTailTowardPet(bool bubbleIsRightOfPet)
    {
        LeftTail.Visibility = bubbleIsRightOfPet ? Visibility.Visible : Visibility.Collapsed;
        RightTail.Visibility = bubbleIsRightOfPet ? Visibility.Collapsed : Visibility.Visible;
    }

    public void ApplyScale(double petSize)
    {
        var scale = Math.Clamp(petSize / 250.0, 0.6, 1.68);
        Width = 460 * scale;
        Height = 430 * scale;
    }

    public void PrepareManualOpen()
    {
        ClearNotice();
        BeginTyping(TypingText, "今天也一起加油吧，需要我做什么？");
    }

    public void ClearNotice() => NoticeBorder.Visibility = Visibility.Collapsed;

    private void HideButton_Click(object sender, RoutedEventArgs e) => HideRequested?.Invoke(this, EventArgs.Empty);

    private async void RefreshWeather_Click(object sender, RoutedEventArgs e)
    {
        var city = CityBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(city)) return;

        WeatherCityText.Text = "正在查询…";
        WeatherTemperatureText.Text = "--°C";
        WeatherDetailText.Text = string.Empty;

        try
        {
            var geoUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language=zh&format=json";
            using var geo = JsonDocument.Parse(await Http.GetStringAsync(geoUrl));
            if (!geo.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                throw new InvalidOperationException("没有找到这个城市");

            var place = results[0];
            var latitude = place.GetProperty("latitude").GetDouble();
            var longitude = place.GetProperty("longitude").GetDouble();
            var displayName = place.GetProperty("name").GetString() ?? city;
            if (place.TryGetProperty("admin1", out var admin))
            {
                var adminName = admin.GetString();
                if (!string.IsNullOrWhiteSpace(adminName) && adminName != displayName)
                    displayName += $" · {adminName}";
            }

            var forecastUrl = $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m&timezone=auto";
            using var forecast = JsonDocument.Parse(await Http.GetStringAsync(forecastUrl));
            var current = forecast.RootElement.GetProperty("current");
            var temperature = current.GetProperty("temperature_2m").GetDouble();
            var apparent = current.GetProperty("apparent_temperature").GetDouble();
            var weatherCode = current.GetProperty("weather_code").GetInt32();
            var wind = current.GetProperty("wind_speed_10m").GetDouble();

            WeatherCityText.Text = displayName;
            WeatherTemperatureText.Text = $"{temperature:0.#}°C";
            BeginTyping(WeatherDetailText,
                $"{DescribeWeather(weatherCode)}  ·  体感 {apparent:0.#}°C  ·  风速 {wind:0.#} km/h");
            _data.WeatherCity = city;
            _data.Save();
        }
        catch (Exception ex)
        {
            WeatherCityText.Text = "天气查询失败";
            WeatherDetailText.Text = ex is HttpRequestException or TaskCanceledException
                ? "请检查网络后重试"
                : ex.Message;
        }
    }

    private void StartPause_Click(object sender, RoutedEventArgs e)
    {
        SaveTimerSettings();
        if (!_timerRunning && Equals(StartPauseButton.Content, "开始"))
        {
            _remaining = TimeSpan.FromMinutes(_isFocusPhase ? _data.FocusMinutes : _data.BreakMinutes);
            UpdateTimerDisplay();
        }
        _timerRunning = !_timerRunning;
        if (_timerRunning) _timer.Start();
        else _timer.Stop();
        StartPauseButton.Content = _timerRunning ? "暂停" : "继续";
    }

    private void ResetTimer_Click(object sender, RoutedEventArgs e)
    {
        SaveTimerSettings();
        _timer.Stop();
        _timerRunning = false;
        _remaining = TimeSpan.FromMinutes(_isFocusPhase ? _data.FocusMinutes : _data.BreakMinutes);
        StartPauseButton.Content = "开始";
        UpdateTimerDisplay();
    }

    private void SwitchPhase_Click(object sender, RoutedEventArgs e)
    {
        _isFocusPhase = !_isFocusPhase;
        ResetTimer_Click(sender, e);
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_remaining > TimeSpan.Zero) _remaining -= TimeSpan.FromSeconds(1);
        UpdateTimerDisplay();
        if (_remaining > TimeSpan.Zero) return;

        _timer.Stop();
        _timerRunning = false;
        StartPauseButton.Content = "开始";
        System.Media.SystemSounds.Asterisk.Play();
        var notice = _isFocusPhase
            ? "专注时间结束，休息一下吧！"
            : "休息结束，可以开始下一轮啦！";
        NoticeBorder.Visibility = Visibility.Visible;
        BeginTyping(NoticeText, notice);
        AssistantTabs.SelectedIndex = 1;
        _isFocusPhase = !_isFocusPhase;
        _remaining = TimeSpan.FromMinutes(_isFocusPhase ? _data.FocusMinutes : _data.BreakMinutes);
        UpdateTimerDisplay();
        AttentionRequested?.Invoke(this, EventArgs.Empty);
    }

    private void BeginTyping(TextBlock target, string content)
    {
        _typingTimer.Stop();
        _typingTarget = target;
        _typingContent = content;
        _typingIndex = 0;
        target.Text = string.Empty;
        _typingTimer.Start();
    }

    private void TypingTimer_Tick(object? sender, EventArgs e)
    {
        if (_typingTarget is null || _typingIndex >= _typingContent.Length)
        {
            _typingTimer.Stop();
            return;
        }

        _typingTarget.Text += _typingContent[_typingIndex++];
        if (_typingIndex >= _typingContent.Length) _typingTimer.Stop();
    }

    private void UpdateTimerDisplay()
    {
        TimerPhaseText.Text = _isFocusPhase ? "专注时间" : "休息时间";
        TimerText.Text = $"{(int)_remaining.TotalMinutes:00}:{_remaining.Seconds:00}";
    }

    private void SaveTimerSettings()
    {
        _data.FocusMinutes = ParseMinutes(FocusMinutesBox.Text, 25);
        _data.BreakMinutes = ParseMinutes(BreakMinutesBox.Text, 5);
        FocusMinutesBox.Text = _data.FocusMinutes.ToString();
        BreakMinutesBox.Text = _data.BreakMinutes.ToString();
        _data.Save();
    }

    private static int ParseMinutes(string text, int fallback) =>
        int.TryParse(text, out var minutes) ? Math.Clamp(minutes, 1, 180) : fallback;

    private void AddTodo_Click(object sender, RoutedEventArgs e) => AddTodo();

    private void TodoInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        AddTodo();
        e.Handled = true;
    }

    private void AddTodo()
    {
        var text = TodoInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        _todos.Add(new TodoItem { Text = text });
        TodoInput.Clear();
        SaveTodos();
    }

    private void DeleteTodo_Click(object sender, RoutedEventArgs e)
    {
        if (TodoList.SelectedItem is TodoItem item) _todos.Remove(item);
        SaveTodos();
    }

    private void ClearCompleted_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _todos.Where(item => item.IsDone).ToList()) _todos.Remove(item);
        SaveTodos();
    }

    private void TodoChanged(object sender, RoutedEventArgs e) => SaveTodos();

    private void SaveTodos()
    {
        _data.Todos = _todos.ToList();
        _data.Save();
    }

    private static string DescribeWeather(int code) => code switch
    {
        0 => "晴朗",
        1 or 2 => "少云",
        3 => "阴天",
        45 or 48 => "有雾",
        51 or 53 or 55 or 56 or 57 => "毛毛雨",
        61 or 63 or 65 or 66 or 67 => "下雨",
        71 or 73 or 75 or 77 => "下雪",
        80 or 81 or 82 => "阵雨",
        85 or 86 => "阵雪",
        95 or 96 or 99 => "雷雨",
        _ => "天气变化中"
    };
}
