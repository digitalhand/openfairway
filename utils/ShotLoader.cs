using Godot;
using Godot.Collections;

/// <summary>
/// Utility class for loading shot data from JSON files.
/// Handles all file I/O and JSON parsing in one place.
/// </summary>
public static class ShotLoader
{
    /// <summary>
    /// Load shot data from a JSON file.
    /// Expects the JSON to have a "BallData" object containing shot parameters.
    /// </summary>
    /// <param name="path">Path to the JSON file</param>
    /// <returns>Dictionary containing BallData, or empty Dictionary on error</returns>
    public static Dictionary LoadShotFromFile(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            PhysicsLogger.Error($"ShotLoader: Path is null or empty");
            return new Dictionary();
        }

        var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            PhysicsLogger.Error($"ShotLoader: Failed to open file: {path}");
            return new Dictionary();
        }

        string jsonText = file.GetAsText();
        file.Close();

        var json = new Json();
        if (json.Parse(jsonText) != Error.Ok)
        {
            PhysicsLogger.Error($"ShotLoader: Failed to parse JSON from {path}");
            return new Dictionary();
        }

        var parsed = json.Data;
        if (parsed.VariantType != Variant.Type.Dictionary)
        {
            PhysicsLogger.Error($"ShotLoader: JSON root is not a Dictionary in {path}");
            return new Dictionary();
        }

        var dict = (Dictionary)parsed;
        if (!dict.ContainsKey("BallData"))
        {
            PhysicsLogger.Error($"ShotLoader: JSON missing 'BallData' key in {path}");
            return new Dictionary();
        }

        if (dict["BallData"].VariantType != Variant.Type.Dictionary)
        {
            PhysicsLogger.Error($"ShotLoader: 'BallData' is not a Dictionary in {path}");
            return new Dictionary();
        }

        return ((Dictionary)dict["BallData"]).Duplicate();
    }
}
