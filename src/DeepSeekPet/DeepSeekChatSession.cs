using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DeepSeekPet;

public sealed class ChatConfiguration
{
    public string Model { get; set; } = "deepseek-flash";
    public string ProtectedKey { get; set; } = "";
    private static string FilePath => Path.Combine(PetSettings.SettingsDirectory, "chat.json");
    public static ChatConfiguration Load()
    {
        try { return JsonSerializer.Deserialize<ChatConfiguration>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }
    public string ReadKey() => string.IsNullOrEmpty(ProtectedKey) ? "" : Encoding.UTF8.GetString(
        ProtectedData.Unprotect(Convert.FromBase64String(ProtectedKey), null, DataProtectionScope.CurrentUser));
    public void Save(string key, string model)
    {
        if (string.IsNullOrWhiteSpace(model)) throw new InvalidOperationException("请输入模型名称。");
        var encrypted = string.IsNullOrWhiteSpace(key) ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), null, DataProtectionScope.CurrentUser));
        Directory.CreateDirectory(PetSettings.SettingsDirectory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(new ChatConfiguration { Model = model.Trim(), ProtectedKey = encrypted }));
        Model = model.Trim();
        ProtectedKey = encrypted;
    }
}

public sealed record ChatMessage(string role, string content);

// UI-independent streaming boundary for member B. Only completed turns enter history.
public sealed class DeepSeekChatSession
{
    private static readonly HttpClient DefaultHttp = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly HttpClient _http;
    public DeepSeekChatSession(HttpClient? http = null) => _http = http ?? DefaultHttp;
    private readonly List<ChatMessage> _history = [];
    private int _busy;
    public void Clear() => _history.Clear();
    public async Task SendAsync(string key, string model, string prompt, Action<string> onText, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _busy, 1) != 0) throw new InvalidOperationException("请等待当前回复结束。");
        try
        {
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("请先保存 API Key。");
            if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 8000) throw new InvalidOperationException("请输入 1–8000 字的问题。");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            var messages = new List<ChatMessage> { new("system", "你是偶鲸鲸桌宠助手。用简明中文回答，不要声称已执行没有工具支持的操作。") };
            messages.AddRange(_history);
            messages.Add(new("user", prompt));
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.deepseek.com/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            request.Content = JsonContent.Create(new { model, messages, stream = true, max_tokens = 2048, thinking = new { type = "disabled" } });
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException((int)response.StatusCode switch
                {
                    401 => "API Key 无效，请重新配置。", 402 => "账户余额不足。", 429 => "请求过于频繁，请稍后重试。",
                    400 => "请求参数或模型名称不受支持，请检查配置。", _ => $"服务暂不可用（HTTP {(int)response.StatusCode}），请稍后重试。"
                });
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token));
            var answer = new StringBuilder();
            var complete = false;
            while (await reader.ReadLineAsync(timeout.Token) is { } line)
            {
                if (!line.StartsWith("data:")) continue;
                var data = line[5..].Trim();
                if (data == "[DONE]") { complete = true; break; }
                if (data.Length == 0) continue;
                using var json = JsonDocument.Parse(data);
                if (json.RootElement.TryGetProperty("error", out _)) throw new InvalidOperationException("服务返回错误，请重试。");
                if (!json.RootElement.TryGetProperty("choices", out var choices)) continue;
                foreach (var choice in choices.EnumerateArray())
                {
                    if (choice.TryGetProperty("finish_reason", out var reason) && reason.GetString() == "length")
                        throw new InvalidOperationException("回复达到长度上限，请缩小问题范围后重试。");
                    if (choice.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var content) && content.GetString() is { Length: > 0 } chunk)
                    {
                        answer.Append(chunk);
                        onText(chunk);
                    }
                }
            }
            if (!complete || answer.Length == 0) throw new InvalidOperationException("回复未完整返回，请重试。");
            timeout.Token.ThrowIfCancellationRequested();
            _history.Add(new("user", prompt));
            _history.Add(new("assistant", answer.ToString()));
            while (_history.Count > 12 || _history.Sum(m => m.content.Length) > 24000) _history.RemoveRange(0, 2);
        }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }
}
