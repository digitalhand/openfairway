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
            GD.PrintErr($"ShotLoader: Path is null or empty");
            return new Dictionary();
        }

        var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PrintErr($"ShotLoader: Failed to open file: {path}");
            return new Dictionary();
        }

        string jsonText = file.GetAsText();
        file.Close();

        var json = new Json();
        if (json.Parse(jsonText) != Error.Ok)
        {
            GD.PrintErr($"ShotLoader: Failed to parse JSON from {path}");
            return new Dictionary();
        }

        var parsed = json.Data;
        if (parsed.VariantType != Variant.Type.Dictionary)
        {
            GD.PrintErr($"ShotLoader: JSON root is not a Dictionary in {path}");
            return new Dictionary();
        }

        var dict = (Dictionary)parsed;
        if (!dict.ContainsKey("BallData"))
        {
            GD.PrintErr($"ShotLoader: JSON missing 'BallData' key in {path}");
            return new Dictionary();
        }

        return ((Dictionary)dict["BallData"]).Duplicate();
    }
}
