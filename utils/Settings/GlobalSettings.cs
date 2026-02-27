using Godot;

/// <summary>
/// Global autoload singleton for application-wide settings.
/// </summary>
public partial class GlobalSettings : Node
{
    [Signal]
    public delegate void SettingsChangedEventHandler();

    public GameSettings GameSettings { get; private set; }
    public AppSettings AppSettings { get; private set; }

    public override void _Ready()
    {
        PhysicsLogger.LogLevel = PhysicsLogger.Level.Info;
        GameSettings = new GameSettings();
        AppSettings = new AppSettings();
        AppSettingsPersistenceService.LoadInto(AppSettings);
        ConnectAppSettingsSignals();
        AppSettingsDisplayService.Apply(AppSettings, GetWindow());
    }

    public override void _ExitTree()
    {
        DisconnectAppSettingsSignals();
    }

    public void ResetAllSettingsToDefaults()
    {
        GameSettings.ResetDefaults();
        AppSettings.ResetDefaults();
        AppSettingsPersistenceService.Save(AppSettings);
        EmitSignal(SignalName.SettingsChanged);
    }

    public void SaveAppSettings()
    {
        AppSettingsPersistenceService.Save(AppSettings);
    }

    private void ConnectAppSettingsSignals()
    {
        if (AppSettings?.Settings == null)
            return;

        foreach (Setting setting in AppSettings.Settings.Values)
            setting.SettingChanged += OnAnyAppSettingChanged;

        AppSettings.DisplayResolutionPreset.SettingChanged += OnDisplaySettingChanged;
        AppSettings.DisplayFullscreen.SettingChanged += OnDisplaySettingChanged;
    }

    private void DisconnectAppSettingsSignals()
    {
        if (AppSettings?.Settings == null)
            return;

        foreach (Setting setting in AppSettings.Settings.Values)
            setting.SettingChanged -= OnAnyAppSettingChanged;

        AppSettings.DisplayResolutionPreset.SettingChanged -= OnDisplaySettingChanged;
        AppSettings.DisplayFullscreen.SettingChanged -= OnDisplaySettingChanged;
    }

    private void OnAnyAppSettingChanged(Variant _value)
    {
        AppSettingsPersistenceService.Save(AppSettings);
        EmitSignal(SignalName.SettingsChanged);
    }

    private void OnDisplaySettingChanged(Variant _value)
    {
        AppSettingsDisplayService.Apply(AppSettings, GetWindow());
    }
}
