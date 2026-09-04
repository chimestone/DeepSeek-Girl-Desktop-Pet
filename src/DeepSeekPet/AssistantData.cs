using System.IO;
using System.Text.Json;

namespace DeepSeekPet;

public sealed class AssistantData
{
    public string WeatherCity { get; set; } = "上海";
    public int FocusMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;
    public List<TodoItem> Todos { get; set; } = [];

    private static string DataPath => Path.Combine(PetSettings.SettingsDirectory, "assistant.json");

    public static AssistantData Load()
    {
        try
        {
            if (File.Exists(DataPath))
            {
                return JsonSerializer.Deserialize<AssistantData>(File.ReadAllText(DataPath))
                    ?? new AssistantData();
            }
        }
        catch
        {
            // Invalid assistant data should not prevent the pet from starting.
        }

        return new AssistantData();
    }

    public void Save()
    {
        Directory.CreateDirectory(PetSettings.SettingsDirectory);
        File.WriteAllText(DataPath, JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }
}

public sealed class TodoItem
{
    public string Text { get; set; } = string.Empty;
    public bool IsDone { get; set; }
}
