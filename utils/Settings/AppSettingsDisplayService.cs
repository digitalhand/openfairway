using System;
using Godot;

public static class AppSettingsDisplayService
{
    public static readonly string[] Presets =
    {
        "1280x720",
        "1600x900",
        "1728x972",
        "1920x1080"
    };

    public static void Apply(AppSettings appSettings, Window window)
    {
        if (appSettings == null || window == null)
            return;

        string preset = appSettings.DisplayResolutionPreset.Value.ToString();
        if (!TryParseResolutionPreset(preset, out Vector2I size))
            size = new Vector2I(1728, 972);

        window.Size = size;

        bool fullscreen = (bool)appSettings.DisplayFullscreen.Value;
        window.Mode = fullscreen ? Window.ModeEnum.Fullscreen : Window.ModeEnum.Windowed;
    }

    public static bool TryParseResolutionPreset(string preset, out Vector2I size)
    {
        size = new Vector2I(1728, 972);
        if (string.IsNullOrWhiteSpace(preset))
            return false;

        string[] parts = preset.Trim().ToLowerInvariant().Split('x', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return false;

        if (!int.TryParse(parts[0], out int width) || !int.TryParse(parts[1], out int height))
            return false;

        if (width < 320 || height < 240)
            return false;

        size = new Vector2I(width, height);
        return true;
    }
}
