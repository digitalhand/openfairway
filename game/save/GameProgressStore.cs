using System;
using Godot;
using Godot.Collections;

/// <summary>
/// Single-slot course progress persistence backed by JSON in user://.
/// </summary>
public partial class GameProgressStore : Node
{
    public const string SavePath = "user://game_progress.json";
    private const int SaveVersion = 1;

    public CourseProgressSlot Load()
    {
        if (!TryLoadRoot(out var root))
            return null;

        return TryParseSlot(root, out var slot) ? slot : null;
    }

    public bool TryGetSlotForScene(string sceneId, out CourseProgressSlot slot)
    {
        slot = null;
        if (string.IsNullOrWhiteSpace(sceneId))
            return false;

        var loadedSlot = Load();
        if (loadedSlot == null)
            return false;

        if (loadedSlot.Completed)
            return false;

        if (!string.Equals(loadedSlot.SceneId, sceneId, StringComparison.Ordinal))
            return false;

        slot = loadedSlot;
        return true;
    }

    public void SaveSlot(CourseProgressSlot slot)
    {
        if (slot == null || string.IsNullOrWhiteSpace(slot.SceneId))
            return;

        slot.Strokes = Mathf.Max(0, slot.Strokes);
        slot.UpdatedUtc = DateTime.UtcNow.ToString("O");

        var root = new Dictionary
        {
            { "version", SaveVersion },
            { "slot", SerializeSlot(slot) }
        };

        var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            PhysicsLogger.Error($"GameProgressStore: failed opening {SavePath} for write");
            return;
        }

        file.StoreString(Json.Stringify(root, "\t"));
    }

    public void ClearSlot()
    {
        if (!FileAccess.FileExists(SavePath))
            return;

        string absolute = ProjectSettings.GlobalizePath(SavePath);
        Error error = DirAccess.RemoveAbsolute(absolute);
        if (error != Error.Ok)
        {
            PhysicsLogger.Error($"GameProgressStore: failed clearing save {SavePath} ({error})");
        }
    }

    private bool TryLoadRoot(out Dictionary root)
    {
        root = null;
        if (!FileAccess.FileExists(SavePath))
            return false;

        var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            PhysicsLogger.Error($"GameProgressStore: failed opening {SavePath} for read");
            return false;
        }

        string text = file.GetAsText();
        var json = new Json();
        Error parseResult = json.Parse(text);
        if (parseResult != Error.Ok)
        {
            PhysicsLogger.Error($"GameProgressStore: invalid JSON in {SavePath}");
            return false;
        }

        if (json.Data.VariantType != Variant.Type.Dictionary)
        {
            PhysicsLogger.Error($"GameProgressStore: root JSON is not an object in {SavePath}");
            return false;
        }

        root = (Dictionary)json.Data;

        if (!root.ContainsKey("version"))
            return false;

        int version = VariantToInt(root["version"], 0);
        if (version != SaveVersion)
        {
            PhysicsLogger.Info($"GameProgressStore: unsupported save version {version}");
            return false;
        }

        return true;
    }

    private bool TryParseSlot(Dictionary root, out CourseProgressSlot slot)
    {
        slot = null;
        if (!root.ContainsKey("slot"))
            return false;

        Variant slotVariant = root["slot"];
        if (slotVariant.VariantType != Variant.Type.Dictionary)
            return false;

        var slotData = (Dictionary)slotVariant;
        if (!slotData.ContainsKey("scene_id"))
            return false;

        string sceneId = slotData["scene_id"].ToString();
        if (string.IsNullOrWhiteSpace(sceneId))
            return false;

        if (!TryParseVector3(slotData, "ball_position", out Vector3 ballPosition))
            return false;

        int strokes = slotData.ContainsKey("strokes") ? VariantToInt(slotData["strokes"], 0) : 0;
        bool completed = slotData.ContainsKey("completed") && VariantToBool(slotData["completed"], false);
        string updatedUtc = slotData.ContainsKey("updated_utc") ? slotData["updated_utc"].ToString() : string.Empty;

        slot = new CourseProgressSlot
        {
            SceneId = sceneId,
            BallPosition = ballPosition,
            Strokes = Mathf.Max(0, strokes),
            Completed = completed,
            UpdatedUtc = updatedUtc
        };

        return true;
    }

    private static Dictionary SerializeSlot(CourseProgressSlot slot)
    {
        return new Dictionary
        {
            { "scene_id", slot.SceneId },
            { "ball_position", new Dictionary
                {
                    { "x", slot.BallPosition.X },
                    { "y", slot.BallPosition.Y },
                    { "z", slot.BallPosition.Z }
                }
            },
            { "strokes", slot.Strokes },
            { "completed", slot.Completed },
            { "updated_utc", slot.UpdatedUtc }
        };
    }

    private static bool TryParseVector3(Dictionary source, string key, out Vector3 value)
    {
        value = Vector3.Zero;
        if (!source.ContainsKey(key))
            return false;

        Variant vectorVariant = source[key];
        if (vectorVariant.VariantType == Variant.Type.Vector3)
        {
            value = (Vector3)vectorVariant;
            return true;
        }

        if (vectorVariant.VariantType != Variant.Type.Dictionary)
            return false;

        var data = (Dictionary)vectorVariant;
        if (!data.ContainsKey("x") || !data.ContainsKey("y") || !data.ContainsKey("z"))
            return false;

        value = new Vector3(
            VariantToFloat(data["x"], 0.0f),
            VariantToFloat(data["y"], 0.0f),
            VariantToFloat(data["z"], 0.0f)
        );
        return true;
    }

    private static int VariantToInt(Variant value, int fallback)
    {
        return value.VariantType switch
        {
            Variant.Type.Int => (int)value,
            Variant.Type.Float => Mathf.RoundToInt((float)value),
            Variant.Type.String => int.TryParse((string)value, out int parsed) ? parsed : fallback,
            _ => fallback
        };
    }

    private static float VariantToFloat(Variant value, float fallback)
    {
        return value.VariantType switch
        {
            Variant.Type.Float => (float)value,
            Variant.Type.Int => (int)value,
            Variant.Type.String => float.TryParse((string)value, out float parsed) ? parsed : fallback,
            _ => fallback
        };
    }

    private static bool VariantToBool(Variant value, bool fallback)
    {
        return value.VariantType switch
        {
            Variant.Type.Bool => (bool)value,
            Variant.Type.Int => (int)value != 0,
            Variant.Type.Float => !Mathf.IsZeroApprox((float)value),
            Variant.Type.String => bool.TryParse((string)value, out bool parsed) ? parsed : fallback,
            _ => fallback
        };
    }
}

public sealed class CourseProgressSlot
{
    public string SceneId { get; set; } = string.Empty;
    public Vector3 BallPosition { get; set; } = Vector3.Zero;
    public int Strokes { get; set; } = 0;
    public bool Completed { get; set; } = false;
    public string UpdatedUtc { get; set; } = string.Empty;
}
