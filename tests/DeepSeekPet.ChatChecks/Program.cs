using System.Net.Http;
using DeepSeekPet;
using System.Net;
using System.Text.Json;

var handler = new FakeHandler();
using var http = new HttpClient(handler);
var session = new DeepSeekChatSession(http);
var text = "";
await session.SendAsync("fake-key", "test-model", "第一问", s => text += s, default);
Check(text == "你好", "stream chunks");
await session.SendAsync("fake-key", "test-model", "第二问", _ => {}, default);
Check(handler.Counts.Last() == 4, "completed history");
handler.Fail = true;
try { await session.SendAsync("fake-key", "test-model", "失败问题", _ => {}, default); throw new Exception("expected error"); }
catch (InvalidOperationException ex) { Check(!ex.Message.Contains("fake-key"), "safe error"); }
handler.Fail = false;
await session.SendAsync("fake-key", "test-model", "重试", _ => {}, default);
Check(handler.Counts.Last() == 6, "failed turn excluded");
session.Clear();
handler.Truncated = true;
try { await session.SendAsync("fake-key", "test-model", "中断", _ => {}, default); throw new Exception("expected truncation"); }
catch (InvalidOperationException) { }
handler.Truncated = false;
await session.SendAsync("fake-key", "test-model", "新问题", _ => {}, default);
Check(handler.Counts.Last() == 2, "truncated turn excluded and clear");
using var cancel = new CancellationTokenSource();
try { await session.SendAsync("fake-key", "test-model", "取消", _ => cancel.Cancel(), cancel.Token); throw new Exception("expected cancel"); }
catch (OperationCanceledException) { }
await session.SendAsync("fake-key", "test-model", "取消后", _ => {}, default);
Check(handler.Counts.Last() == 4, "cancelled turn excluded");
for (var i = 0; i < 10; i++) await session.SendAsync("fake-key", "test-model", "问", _ => {}, default);
Check(handler.Counts.Last() == 14, "history bounded to six turns");
Console.WriteLine("PASS: streaming, authentication, context, failure, truncation, cancellation, clear, bounded history.");
Check(AssistantCommands.Parse("上海天气") == new AssistantCommand("weather", "上海"), "weather city");
Check(AssistantCommands.Parse("查询天气 北京") == new AssistantCommand("weather", "北京"), "weather prefix");
Check(AssistantCommands.Parse("开始专注25分钟") == new AssistantCommand("focus", Number: 25), "focus command");
Check(AssistantCommands.Parse("休息5分钟") == new AssistantCommand("break", Number: 5), "break command");
Check(AssistantCommands.Parse("暂停计时")?.Name == "pause", "pause command");
Check(AssistantCommands.Parse("添加待办 买牛奶") == new AssistantCommand("add-todo", "买牛奶"), "todo add");
Check(AssistantCommands.Parse("完成待办1") == new AssistantCommand("complete-todo", Number: 1), "todo complete");
Check(AssistantCommands.Parse("你好") == null, "ordinary chat fallback");
UiChecks.Run();
static void Check(bool condition, string label) { if (!condition) throw new Exception(label); }
sealed class FakeHandler : HttpMessageHandler
{
    public bool Fail;
    public bool Truncated;
    public List<int> Counts = [];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (request.RequestUri!.ToString() != "https://api.deepseek.com/chat/completions" || request.Headers.Authorization?.Parameter != "fake-key") throw new Exception("request");
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
        Counts.Add(body.RootElement.GetProperty("messages").GetArrayLength());
        if (Fail) return new(HttpStatusCode.Unauthorized) { Content = new StringContent("fake-key") };
        var sse = "data: {\"choices\":[{\"delta\":{\"content\":\"你\"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"好\"}}]}\n\n";
        return new(HttpStatusCode.OK) { Content = new StringContent(sse + (Truncated ? "" : "data: [DONE]\n\n")) };
    }
}
