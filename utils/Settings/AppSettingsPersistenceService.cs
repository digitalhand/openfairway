using Godot;

public static class AppSettingsPersistenceService
{
    public const string SavePath = "user://app_settings.cfg";
    private const int SaveVersion = 1;

    public static void LoadInto(AppSettings appSettings)
    {
        if (appSettings == null)
            return;

        var config = new ConfigFile();
        if (config.Load(SavePath) != Error.Ok)
            return;

        SetIfPresent(config, "player", "name", appSettings.PlayerName);
        SetIfPresent(config, "player", "test_shots_enabled", appSettings.TestShotsEnabled);
        SetIfPresent(config, "display", "resolution_preset", appSettings.DisplayResolutionPreset);
        SetIfPresent(config, "display", "fullscreen", appSettings.DisplayFullscreen);
        SetIfPresent(config, "game", "camera_orbit_distance", appSettings.CameraOrbitDistance);
        SetIfPresent(config, "game", "camera_follow_delay_seconds", appSettings.CameraFollowDelaySeconds);
        SetIfPresent(config, "game", "tcp_port", appSettings.TcpPort);
        SetIfPresent(config, "game", "shot_recording_enabled", appSettings.ShotRecordingEnabled);
        SetIfPresent(config, "game", "shot_recording_path", appSettings.ShotRecordingPath);
    }

    public static void Save(AppSettings appSettings)
    {
        if (appSettings == null)
            return;

        var config = new ConfigFile();
        config.SetValue("meta", "version", SaveVersion);
        config.SetValue("player", "name", appSettings.PlayerName.Value);
        config.SetValue("player", "test_shots_enabled", appSettings.TestShotsEnabled.Value);
        config.SetValue("display", "resolution_preset", appSettings.DisplayResolutionPreset.Value);
        config.SetValue("display", "fullscreen", appSettings.DisplayFullscreen.Value);
        config.SetValue("game", "camera_orbit_distance", appSettings.CameraOrbitDistance.Value);
        config.SetValue("game", "camera_follow_delay_seconds", appSettings.CameraFollowDelaySeconds.Value);
        config.SetValue("game", "tcp_port", appSettings.TcpPort.Value);
        config.SetValue("game", "shot_recording_enabled", appSettings.ShotRecordingEnabled.Value);
        config.SetValue("game", "shot_recording_path", appSettings.ShotRecordingPath.Value);

        Error error = config.Save(SavePath);
        if (error != Error.Ok)
            PhysicsLogger.Error($"AppSettingsPersistenceService: failed saving {SavePath} ({error})");
    }

    private static void SetIfPresent(ConfigFile config, string section, string key, Setting setting)
    {
        if (setting == null || !config.HasSectionKey(section, key))
            return;

        setting.SetValue(config.GetValue(section, key));
    }
}
