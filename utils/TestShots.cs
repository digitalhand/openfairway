using Godot.Collections;

/// <summary>
/// Centralized repository of test shot file paths.
/// Single source of truth for all test shot JSON files.
/// </summary>
public static class TestShots
{
    public static readonly Dictionary<string, string> Shots = new()
    {
        { "Drive", "res://assets/data/drive_test_shot.json" },
        { "Wood Low", "res://assets/data/wood_low_test_shot.json" },
        { "Wedge", "res://assets/data/wedge_test_shot.json" },
        { "Bump", "res://assets/data/bump_test_shot.json" },
        { "Approach", "res://assets/data/approach_test_shot.json" },
        { "Topped", "res://assets/data/topped_test_shot.json" }
    };

    public const string DefaultShot = "res://assets/data/drive_test_shot.json";
}
