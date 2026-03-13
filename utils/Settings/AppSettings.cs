using Godot.Collections;

/// <summary>
/// Application settings shared across menu and gameplay.
/// </summary>
public partial class AppSettings : SettingCollector
{
    public const string DefaultPlayerName = "JesseInCode";
    public const bool DefaultTestShotsEnabled = true;
    public const string DefaultResolutionPreset = "1728x972";
    private const float FeetPerCameraDistanceUnit = 3.28084f;
    public const float DefaultCameraOrbitDistance = 7.0f / FeetPerCameraDistanceUnit;
    public const float DefaultCameraFollowDelaySeconds = 3.0f;
    public const int DefaultTcpPort = 55000;
    public const bool DefaultShotRecordingEnabled = false;
    public const string DefaultShotRecordingPath = "";

    public Setting PlayerName { get; private set; }
    public Setting TestShotsEnabled { get; private set; }
    public Setting DisplayResolutionPreset { get; private set; }
    public Setting DisplayFullscreen { get; private set; }
    public Setting CameraOrbitDistance { get; private set; }
    public Setting CameraFollowDelaySeconds { get; private set; }
    public Setting TcpPort { get; private set; }
    public Setting ShotRecordingEnabled { get; private set; }
    public Setting ShotRecordingPath { get; private set; }

    public AppSettings()
    {
        PlayerName = new Setting(DefaultPlayerName);
        TestShotsEnabled = new Setting(DefaultTestShotsEnabled);
        DisplayResolutionPreset = new Setting(DefaultResolutionPreset);
        DisplayFullscreen = new Setting(false);
        CameraOrbitDistance = new Setting(DefaultCameraOrbitDistance, 1.0f, 8.0f);
        CameraFollowDelaySeconds = new Setting(DefaultCameraFollowDelaySeconds, 0.0f, 5.0f);
        TcpPort = new Setting(DefaultTcpPort, 1, 65535);
        ShotRecordingEnabled = new Setting(DefaultShotRecordingEnabled);
        ShotRecordingPath = new Setting(DefaultShotRecordingPath);

        Settings = new Dictionary<string, Setting>
        {
            { "player_name", PlayerName },
            { "test_shots_enabled", TestShotsEnabled },
            { "display_resolution_preset", DisplayResolutionPreset },
            { "display_fullscreen", DisplayFullscreen },
            { "camera_orbit_distance", CameraOrbitDistance },
            { "camera_follow_delay_seconds", CameraFollowDelaySeconds },
            { "tcp_port", TcpPort },
            { "shot_recording_enabled", ShotRecordingEnabled },
            { "shot_recording_path", ShotRecordingPath }
        };
    }
}
