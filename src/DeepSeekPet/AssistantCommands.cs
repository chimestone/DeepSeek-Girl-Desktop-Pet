using System.Text.RegularExpressions;

namespace DeepSeekPet;

public sealed record AssistantCommand(string Name, string Argument = "", int Number = 0);

// Local abilities work without sending personal todos to the cloud.
public static class AssistantCommands
{
    public static AssistantCommand? Parse(string input)
    {
        var text = input.Trim().TrimEnd('。', '！', '!', '？', '?');
        if (text is "帮助" or "功能" or "你能做什么") return new("help");
        if (text is "待办" or "查看待办" or "我的待办" or "待办列表") return new("todos");
        if (text is "清除已完成待办" or "清空已完成待办") return new("clear-done");
        var add = Regex.Match(text, @"^(?:添加待办|新增待办|记个待办|待办)\s*[:：]?\s*(.+)$");
        if (add.Success) return new("add-todo", add.Groups[1].Value.Trim());
        var todo = Regex.Match(text, @"^(完成|删除)\s*(?:待办)?\s*(\d+)$");
        if (todo.Success && int.TryParse(todo.Groups[2].Value, out var index)) return new(todo.Groups[1].Value == "完成" ? "complete-todo" : "delete-todo", Number: index);
        if (text is "暂停计时" or "暂停番茄钟") return new("pause");
        if (text is "继续计时" or "继续番茄钟" or "恢复计时") return new("resume");
        if (text is "重置计时" or "重置番茄钟" or "停止计时") return new("reset");
        if (text is "计时状态" or "剩余时间" or "番茄钟状态") return new("timer-status");
        var timer = Regex.Match(text, @"^(?:开始)?(专注|休息|番茄钟|计时)\s*(\d+)?\s*(?:分钟|分)?$");
        if (timer.Success)
        {
            if (timer.Groups[2].Success && !int.TryParse(timer.Groups[2].Value, out _)) return new("invalid-timer");
            return new(timer.Groups[1].Value == "休息" ? "break" : "focus", Number: timer.Groups[2].Success ? int.Parse(timer.Groups[2].Value) : -1);
        }
        var weather = Regex.Match(text, @"^(?:查询|查一下|看看|查看)?\s*(.*?)\s*(?:今天|现在|当前)?(?:的)?天气(?:怎么样|如何)?$");
        if (weather.Success) return new("weather", weather.Groups[1].Value.Trim());
        var weatherPrefix = Regex.Match(text, @"^(?:查天气|查询天气|天气)\s*[:：]?\s*(.+)$");
        if (weatherPrefix.Success) return new("weather", weatherPrefix.Groups[1].Value.Trim());
        return null;
    }
}
