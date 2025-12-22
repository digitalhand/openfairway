using Godot;

/// <summary>
/// Global autoload singleton for application-wide settings.
/// </summary>
public partial class GlobalSettings : Node
{
    [Signal]
    public delegate void SettingsChangedEventHandler();

    // Range Settings
    public RangeSettings RangeSettings { get; private set; }

    public override void _Ready()
    {
        RangeSettings = new RangeSettings();
    }

    public void ResettDefaults()
    {
        RangeSettings.ResetDefaults();
        EmitSignal(SignalName.SettingsChanged);
    }
}
