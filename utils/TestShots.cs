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
        { "Bump & Run", "res://assets/data/bump_and_run.json" },
        { "Bump & Run SL", "res://assets/data/bump_and_run_slow.json" },
        { "Approach", "res://assets/data/approach_test_shot.json" },
        { "Topped", "res://assets/data/topped_test_shot.json" },
        { "Checked", "res://assets/data/checked_test_shot.json" },
        { "Flop", "res://assets/data/flop_test_shot.json" },
        { "Chip", "res://assets/data/chip_test_shot.json" },
        { "Putt 5ft", "res://assets/data/putt_five_feet.json" },
        { "Putt 10ft", "res://assets/data/putt_ten_feet.json" },
    };

    public const string DefaultShot = "res://assets/data/drive_test_shot.json";
}
