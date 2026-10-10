using System.Text;

namespace DeepSeekPet;

// Display history is independent from the context sent to the cloud.
public sealed class RpgDialogueHistory
{
    public sealed class Reply
    {
        public string Role { get; }
        public string Text { get; internal set; }
        internal Reply(string role, string text) { Role = role; Text = text; }
    }
    public string SpeakerName { get; set; } = "偶鲸鲸";
    private readonly List<Reply> _messages = [];
    public Reply BeginTurn(string prompt)
    {
        _messages.Add(new Reply("你", prompt));
        var reply = new Reply("鲸鲸", "");
        _messages.Add(reply);
        return reply;
    }
    public void Append(Reply reply, string text) { if (_messages.Contains(reply)) reply.Text += text; }
    public void AddReply(string text) => _messages.Add(new Reply("鲸鲸", text));
    public string CompactText => _messages.LastOrDefault(m => m.Role == "鲸鲸" && m.Text.Length > 0)?.Text ?? "";
    public string HistoryText => string.Join("\n\n", _messages.Select(m => $"{(m.Role == "鲸鲸" ? SpeakerName : m.Role)}：{(m.Text.Length == 0 ? "正在思考…" : m.Text)}"));
    public void Clear() => _messages.Clear();
}
