using Microsoft.Win32;
using System.IO;

namespace DeepSeekPet;

public static class AutoStartManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DeepSeekPet";

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey, writable: true);

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var expectedExe = Path.Combine(AppContext.BaseDirectory, "DeepSeekPet.exe");
        var executable = File.Exists(expectedExe) ? expectedExe : Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executable))
        {
            key.SetValue(ValueName, $"\"{executable}\"");
        }
    }
}
