using System.IO;
using System.Text.Json;

namespace DeepSeekPet;

public sealed class PetSettings
{
    public bool AutoWalk { get; set; } = true;
    public double WalkSpeed { get; set; } = 46;
    public double PetSize { get; set; } = 250;
    public bool AlwaysOnTop { get; set; } = true;
    public bool EdgeSnap { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public string SkinId { get; set; } = SkinCatalog.DefaultId;
    public double? Left { get; set; }
    public double? Top { get; set; }

    public static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DeepSeekPet");

    private static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static PetSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<PetSettings>(json) ?? new PetSettings();
            }
        }
        catch
        {
            // A malformed settings file should never keep the pet from starting.
        }

        return new PetSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    public PetSettings Clone() => new()
    {
        AutoWalk = AutoWalk,
        WalkSpeed = WalkSpeed,
        PetSize = PetSize,
        AlwaysOnTop = AlwaysOnTop,
        EdgeSnap = EdgeSnap,
        StartWithWindows = StartWithWindows,
        SkinId = SkinId,
        Left = Left,
        Top = Top
    };
}
