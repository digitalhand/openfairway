using Godot;
using Godot.Collections;

/// <summary>
/// Container for multiple settings with dictionary-based access.
/// </summary>
public partial class SettingCollector : RefCounted
{
    [Signal]
    public delegate void SettingsChangedEventHandler();

    public Dictionary<string, Setting> Settings { get; protected set; } = new();

    public SettingCollector(Dictionary<string, Setting> sets = null)
    {
        Settings = sets ?? new Dictionary<string, Setting>();
    }

    public void Init(Dictionary<string, Setting> sets = null)
    {
        Settings = sets ?? new Dictionary<string, Setting>();
    }

    public void ResetDefaults()
    {
        foreach (string name in Settings.Keys)
        {
            Settings[name].ResetDefault();
        }

        EmitSignal(SignalName.SettingsChanged);
    }

    public void SetValue(string settingName, Variant settingValue)
    {
        if (!Settings.TryGetValue(settingName, out var setting))
        {
            PhysicsLogger.Error($"SettingCollector.SetValue: unknown setting '{settingName}'");
            return;
        }
        setting.SetValue(settingValue);
        EmitSignal(SignalName.SettingsChanged);
    }

    public void SetDefault(string settingName, Variant settingDefault)
    {
        if (!Settings.TryGetValue(settingName, out var setting))
        {
            PhysicsLogger.Error($"SettingCollector.SetDefault: unknown setting '{settingName}'");
            return;
        }
        setting.SetDefault(settingDefault);
        EmitSignal(SignalName.SettingsChanged);
    }
}
