using System.Text.Json;
using Microsoft.Win32;
using HL.Optimizer.Core.Interfaces;
using HL.Optimizer.Core.Models;
namespace HL.Optimizer.Infrastructure.Registry;
public sealed class UserSetting(string id, string title, string path, string name, int target, string description) : IReversibleSetting
{
    private sealed record State(bool Exists, string? Kind, string? Value);
    public TaskDefinition Definition => new(id, title, description, "Registry / current user", RiskLevel.Low, "Windows 10 / 11", "Preference change; no guaranteed performance gain.", true);
    public string Target => JsonSerializer.Serialize(new State(true, "DWord", target.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    public string Read()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(path);
        if (key is null || !key.GetValueNames().Contains(name)) return JsonSerializer.Serialize(new State(false, null, null));
        var kind = key.GetValueKind(name);
        if (kind != RegistryValueKind.DWord) throw new InvalidOperationException("Unexpected value type; no changes made.");
        return JsonSerializer.Serialize(new State(true, "DWord", key.GetValue(name)!.ToString()));
    }
    public void Write(string value)
    {
        var state = JsonSerializer.Deserialize<State>(value) ?? throw new InvalidDataException("Invalid backup");
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(path, true);
        if (!state.Exists) key.DeleteValue(name, false);
        else if (state.Kind == "DWord") key.SetValue(name, int.Parse(state.Value!, System.Globalization.CultureInfo.InvariantCulture), RegistryValueKind.DWord);
        else throw new InvalidDataException("Unsupported registry type");
    }
    public static IReversibleSetting[] Catalog() =>
    [
        new UserSetting("transparency", "Disable transparency", @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0, "Disables transparency for the current user. Some surfaces may require signing out. Previous value or absence is saved."),
        new UserSetting("captures", "Disable background game capture", @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0, "Disables app game capture for the current user. Recording workflows may be affected. Restart the game after applying.")
    ];
}
