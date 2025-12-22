using Godot.Collections;

/// <summary>
/// Range/practice session settings.
/// </summary>
public partial class RangeSettings : SettingCollector
{
    public Setting RangeUnits { get; private set; }
    public Setting CameraFollowMode { get; private set; }
    public Setting ShotInjectorEnabled { get; private set; }
    public Setting AutoBallReset { get; private set; }
    public Setting BallResetTimer { get; private set; }
    public Setting Temperature { get; private set; }
    public Setting Altitude { get; private set; }
    public Setting DragScale { get; private set; }
    public Setting LiftScale { get; private set; }
    public Setting SurfaceType { get; private set; }
    public Setting ShotTracerCount { get; private set; }

    public RangeSettings()
    {
        RangeUnits = new Setting((int)PhysicsEnums.Units.Imperial);
        CameraFollowMode = new Setting(false);
        ShotInjectorEnabled = new Setting(false);
        AutoBallReset = new Setting(false);
        BallResetTimer = new Setting(3.0f, 1.0f, 15.0f);
        Temperature = new Setting(75, -40, 120);
        Altitude = new Setting(0.0f, -1000.0f, 10000.0f);
        DragScale = new Setting(1.0f, 0.5f, 1.5f);
        LiftScale = new Setting(1.0f, 0.8f, 2.0f);
        SurfaceType = new Setting((int)PhysicsEnums.SurfaceType.Fairway);
        ShotTracerCount = new Setting(1, 0, 4);

        Settings = new Dictionary<string, Setting>
        {
            { "range_units", RangeUnits },
            { "camera_follow_mode", CameraFollowMode },
            { "shot_injector_enabled", ShotInjectorEnabled },
            { "auto_ball_reset", AutoBallReset },
            { "ball_reset_timer", BallResetTimer },
            { "temperature", Temperature },
            { "altitude", Altitude },
            { "drag_scale", DragScale },
            { "lift_scale", LiftScale },
            { "surface_type", SurfaceType },
            { "shot_tracer_count", ShotTracerCount }
        };
    }
}
