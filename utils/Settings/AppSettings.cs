using Godot.Collections;

/// <summary>
/// Application settings shared across menu and gameplay.
/// </summary>
public partial class AppSettings : SettingCollector
{
    public const string DefaultPlayerName = "JesseInCode";
    public const string DefaultResolutionPreset = "1728x972";
    public const float DefaultCameraOrbitDistance = 2.5f;
    public const float DefaultCameraFollowDelaySeconds = 0.0f;

    public Setting PlayerName { get; private set; }
    public Setting DisplayResolutionPreset { get; private set; }
    public Setting DisplayFullscreen { get; private set; }
    public Setting CameraOrbitDistance { get; private set; }
    public Setting CameraFollowDelaySeconds { get; private set; }

    public AppSettings()
    {
        PlayerName = new Setting(DefaultPlayerName);
        DisplayResolutionPreset = new Setting(DefaultResolutionPreset);
        DisplayFullscreen = new Setting(false);
        CameraOrbitDistance = new Setting(DefaultCameraOrbitDistance, 1.0f, 8.0f);
        CameraFollowDelaySeconds = new Setting(DefaultCameraFollowDelaySeconds, 0.0f, 2.0f);

        Settings = new Dictionary<string, Setting>
        {
            { "player_name", PlayerName },
            { "display_resolution_preset", DisplayResolutionPreset },
            { "display_fullscreen", DisplayFullscreen },
            { "camera_orbit_distance", CameraOrbitDistance },
            { "camera_follow_delay_seconds", CameraFollowDelaySeconds }
        };
    }
}
