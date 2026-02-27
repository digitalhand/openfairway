using Godot.Collections;

/// <summary>
/// Application settings shared across menu and gameplay.
/// </summary>
public partial class AppSettings : SettingCollector
{
    public const string DefaultPlayerName = "JesseInCode";
    public const bool DefaultTestShotsEnabled = true;
    public const string DefaultResolutionPreset = "1728x972";
    public const float DefaultCameraOrbitDistance = 2.5f;
    public const float DefaultCameraFollowDelaySeconds = 0.0f;
    public const int DefaultTcpPort = 55000;

    public Setting PlayerName { get; private set; }
    public Setting TestShotsEnabled { get; private set; }
    public Setting DisplayResolutionPreset { get; private set; }
    public Setting DisplayFullscreen { get; private set; }
    public Setting CameraOrbitDistance { get; private set; }
    public Setting CameraFollowDelaySeconds { get; private set; }
    public Setting TcpPort { get; private set; }

    public AppSettings()
    {
        PlayerName = new Setting(DefaultPlayerName);
        TestShotsEnabled = new Setting(DefaultTestShotsEnabled);
        DisplayResolutionPreset = new Setting(DefaultResolutionPreset);
        DisplayFullscreen = new Setting(false);
        CameraOrbitDistance = new Setting(DefaultCameraOrbitDistance, 1.0f, 8.0f);
        CameraFollowDelaySeconds = new Setting(DefaultCameraFollowDelaySeconds, 0.0f, 2.0f);
        TcpPort = new Setting(DefaultTcpPort, 1, 65535);

        Settings = new Dictionary<string, Setting>
        {
            { "player_name", PlayerName },
            { "test_shots_enabled", TestShotsEnabled },
            { "display_resolution_preset", DisplayResolutionPreset },
            { "display_fullscreen", DisplayFullscreen },
            { "camera_orbit_distance", CameraOrbitDistance },
            { "camera_follow_delay_seconds", CameraFollowDelaySeconds },
            { "tcp_port", TcpPort }
        };
    }
}
