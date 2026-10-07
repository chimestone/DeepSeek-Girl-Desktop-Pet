using System.Net.Http;
using System.Text.Json;
using System.Windows;

namespace DeepSeekPet;

public partial class AssistantWindow
{
    private readonly DeepSeekChatSession _chatSession = new();
    private CancellationTokenSource? _chatCancellation;
    private int _conversationGeneration;
    private static readonly HttpClient WeatherHttp = new() { Timeout = TimeSpan.FromSeconds(15) };
    private void InitializeChat()
    {
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) return;
            ++_conversationGeneration;
            _chatCancellation?.Cancel();
            _chatSession.Clear();
            ChatOutput.Clear();
            ChatInput.Clear();
            SetExpanded(false);
        };
    }
    private async void SendChat_Click(object sender, RoutedEventArgs e)
    {
        if (_chatCancellation != null) return;
        var prompt = ChatInput.Text.Trim();
        if (prompt.Length == 0) return;
        ClearNotice();
        var generation = _conversationGeneration;
        using var cancellation = new CancellationTokenSource();
        _chatCancellation = cancellation;
        ChatSend.Visibility = Visibility.Collapsed;
        ChatCancel.Visibility = Visibility.Visible;
        ChatInput.IsEnabled = false;
        ChatOutput.AppendText($"\n你：{prompt}\n鲸鲸：");
        ChatStatus.Text = "处理中…";
        var completed = false;
        try
        {
            var command = AssistantCommands.Parse(prompt);
            if (command != null)
            {
                var result = await ExecuteCommandAsync(command, cancellation.Token);
                if (generation == _conversationGeneration) ChatOutput.AppendText(result);
            }
            else
            {
                var config = ChatConfiguration.Load();
                await _chatSession.SendAsync(config.ReadKey(), config.Model, prompt, chunk =>
                {
                    if (generation != _conversationGeneration) return;
                    ChatStatus.Text = "正在回复…";
                    ChatOutput.AppendText(chunk);
                    ChatOutput.ScrollToEnd();
                }, cancellation.Token);
            }
            if (generation == _conversationGeneration) ChatInput.Clear();
            completed = true;
            ChatStatus.Text = "完成";
        }
        catch (OperationCanceledException) { ChatStatus.Text = "已取消或超时，可重新发送"; }
        catch (HttpRequestException) { ChatStatus.Text = "网络连接失败，可重新发送"; }
        catch (InvalidOperationException ex) { ChatStatus.Text = ex.Message; }
        catch { ChatStatus.Text = "操作失败，请检查网络或设置"; }
        finally
        {
            if (generation == _conversationGeneration)
                ChatOutput.AppendText(completed ? "\n" : $"\n[未完成：{ChatStatus.Text}]\n");
            _chatCancellation = null;
            ChatSend.Visibility = Visibility.Visible;
            ChatCancel.Visibility = Visibility.Collapsed;
            ChatInput.IsEnabled = true;
            ChatOutput.ScrollToEnd();
            if (completed) UpdateTimerStatus();
        }
    }
    private void CancelChat_Click(object sender, RoutedEventArgs e) => _chatCancellation?.Cancel();
    private void ClearChat_Click(object sender, RoutedEventArgs e)
    {
        ++_conversationGeneration;
        _chatCancellation?.Cancel();
        _chatSession.Clear();
        ChatOutput.Clear();
        ClearNotice();
        UpdateTimerStatus();
    }
    private async Task<string> ExecuteCommandAsync(AssistantCommand command, CancellationToken token)
    {
        switch (command.Name)
        {
            case "help":
                return "天气：上海天气 / 查询天气 北京\n计时：开始专注25分钟 / 休息5分钟 / 暂停计时 / 继续计时 / 重置计时 / 剩余时间\n待办：添加待办 买牛奶 / 查看待办 / 完成待办1 / 删除待办1 / 清除已完成待办\n这些能力由本机执行；其他问题交给大模型。收起不影响计时，到点会自动提醒。";
            case "weather": return await QueryWeatherAsync(string.IsNullOrWhiteSpace(command.Argument) ? _data.WeatherCity : command.Argument, token);
            case "todos": return _data.Todos.Count == 0 ? "暂无待办。可以说：添加待办 买牛奶" : string.Join("\n", _data.Todos.Select((t, i) => $"{i + 1}. {(t.IsDone ? "✓" : "○")} {t.Text}"));
            case "add-todo":
                _data.Todos.Add(new TodoItem { Text = command.Argument });
                _data.Save();
                return $"已添加待办 {_data.Todos.Count}：{command.Argument}";
            case "complete-todo":
            case "delete-todo":
                if (command.Number < 1 || command.Number > _data.Todos.Count) return "没有这个待办编号，请先查看待办。";
                var todo = _data.Todos[command.Number - 1];
                if (command.Name == "complete-todo") todo.IsDone = true;
                else _data.Todos.RemoveAt(command.Number - 1);
                _data.Save();
                return $"已{(command.Name == "complete-todo" ? "完成" : "删除")}：{todo.Text}";
            case "clear-done":
                var count = _data.Todos.RemoveAll(t => t.IsDone);
                _data.Save();
                return $"已清除 {count} 条完成的待办。";
            case "invalid-timer": return "计时时长需为1–180分钟。";
            case "focus":
            case "break":
                var minutes = command.Number == -1 ? (command.Name == "focus" ? _data.FocusMinutes : _data.BreakMinutes) : command.Number;
                if (minutes < 1 || minutes > 180) return "计时时长需为1–180分钟。";
                _isFocusPhase = command.Name == "focus";
                if (_isFocusPhase) _data.FocusMinutes = minutes;
                else _data.BreakMinutes = minutes;
                _data.Save();
                _remaining = TimeSpan.FromMinutes(minutes);
                _deadline = DateTimeOffset.UtcNow + _remaining;
                _timer.Start();
                return $"已开始{(_isFocusPhase ? "专注" : "休息")} {minutes} 分钟；收起后继续计时，到点会唤起气泡。";
            case "pause":
                if (_deadline is null) return "当前没有正在运行的计时。";
                _remaining = _deadline.Value - DateTimeOffset.UtcNow;
                if (_remaining <= TimeSpan.Zero) { Timer_Tick(null, EventArgs.Empty); return "计时已到点。"; }
                _deadline = null;
                _timer.Stop();
                return "已暂停计时。说“继续计时”可恢复。";
            case "resume":
                if (_deadline != null) return "计时已经在运行。";
                _deadline = DateTimeOffset.UtcNow + _remaining;
                _timer.Start();
                return "已继续计时。";
            case "reset":
                _deadline = null;
                _timer.Stop();
                _remaining = TimeSpan.FromMinutes(_isFocusPhase ? _data.FocusMinutes : _data.BreakMinutes);
                return "计时已停止并重置。";
            case "timer-status":
                var remaining = _deadline is { } deadline ? deadline - DateTimeOffset.UtcNow : _remaining;
                return $"{(_isFocusPhase ? "专注" : "休息")}计时{(_deadline == null ? "未运行" : "进行中")}，剩余 {Math.Max(0, (int)remaining.TotalMinutes)} 分 {Math.Max(0, remaining.Seconds)} 秒。";
            default: throw new InvalidOperationException("暂不支持此操作。");
        }
    }
    private async Task<string> QueryWeatherAsync(string city, CancellationToken token)
    {
        using var geo = JsonDocument.Parse(await WeatherHttp.GetStringAsync($"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language=zh&format=json", token));
        if (!geo.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0) return "没有找到这个城市，请试试：上海天气";
        var place = results[0];
        var latitude = place.GetProperty("latitude").GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var longitude = place.GetProperty("longitude").GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var forecast = JsonDocument.Parse(await WeatherHttp.GetStringAsync($"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m&timezone=auto", token));
        var current = forecast.RootElement.GetProperty("current");
        token.ThrowIfCancellationRequested();
        _data.WeatherCity = city;
        _data.Save();
        var code = current.GetProperty("weather_code").GetInt32();
        var condition = code switch { 0 => "晴", 1 or 2 => "少云", 3 => "阴", 45 or 48 => "有雾", >= 51 and <= 57 => "毛毛雨", >= 61 and <= 67 => "下雨", >= 71 and <= 77 => "下雪", >= 80 and <= 82 => "阵雨", 85 or 86 => "阵雪", >= 95 => "雷雨", _ => "天气变化中" };
        return $"{place.GetProperty("name").GetString()} · {condition}\n气温 {current.GetProperty("temperature_2m").GetDouble():0.#}°C，体感 {current.GetProperty("apparent_temperature").GetDouble():0.#}°C\n风速 {current.GetProperty("wind_speed_10m").GetDouble():0.#} km/h\n来源：Open-Meteo（当前天气）";
    }
}
