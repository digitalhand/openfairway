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
        PhysicsLogger.LogLevel = PhysicsLogger.Level.Verbose;
        RangeSettings = new RangeSettings();
    }

    public void ResetDefaults()
    {
        RangeSettings.ResetDefaults();
        EmitSignal(SignalName.SettingsChanged);
    }
}
