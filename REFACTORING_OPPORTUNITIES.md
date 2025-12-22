# Code Redundancy Analysis

## 1. Duplicate Test Shot File Paths ⚠️ HIGH PRIORITY

**Location**: RangeUI.cs (lines 10-18) and ShotInjector.cs (lines 32-40)

**Issue**: Both classes maintain the same dictionary of test shot files.

**Current Code**:
```csharp
// RangeUI.cs
private readonly Dictionary<string, string> _shotPayloads = new()
{
    { "Drive", "res://assets/data/drive_test_shot.json" },
    { "Wood Low", "res://assets/data/wood_low_test_shot.json" },
    { "Wedge", "res://assets/data/wedge_test_shot.json" },
    { "Bump", "res://assets/data/bump_test_shot.json" },
    { "Approach", "res://assets/data/approach_test_shot.json" },
    { "Topped", "res://assets/data/topped_test_shot.json" }
};

// ShotInjector.cs - DUPLICATE
var payloads = new Dictionary<string, string>
{
    { "Drive test shot", "res://assets/data/drive_test_shot.json" },
    { "Wood Low test shot", "res://assets/data/wood_low_test_shot.json" },
    { "Wedge test shot", "res://assets/data/wedge_test_shot.json" },
    { "Bump test shot", "res://assets/data/bump_test_shot.json" },
    { "Approach", "res://assets/data/approach_test_shot.json" },
    { "Topped", "res://assets/data/topped_test_shot.json" }
};
```

**Suggested Fix**: Create a static class to centralize test shot definitions.
```csharp
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
}
```

**Impact**: Reduces maintenance burden - only need to update in one place when adding/removing test shots.

---

## 2. Duplicate JSON Loading Logic ⚠️ MEDIUM PRIORITY

**Location**: RangeUI.cs (lines 139-156) and ShotInjector.cs (lines 65-84)

**Issue**: Both classes load JSON files with nearly identical code.

**Current Code**:
```csharp
// RangeUI.cs
var file = FileAccess.Open(_selectedShotPath, FileAccess.ModeFlags.Read);
if (file != null)
{
    string jsonText = file.GetAsText();
    var json = new Json();
    if (json.Parse(jsonText) == Error.Ok)
    {
        var parsed = json.Data;
        if (parsed.VariantType == Variant.Type.Dictionary)
        {
            var dict = (Dictionary)parsed;
            if (dict.ContainsKey("BallData"))
            {
                data = ((Dictionary)dict["BallData"]).Duplicate();
            }
        }
    }
}

// ShotInjector.cs - DUPLICATE (lines 65-84)
```

**Suggested Fix**: Create a utility method to load shot data.
```csharp
public static class ShotLoader
{
    public static Dictionary LoadShotFromFile(string path)
    {
        var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
            return new Dictionary();

        string jsonText = file.GetAsText();
        var json = new Json();
        if (json.Parse(jsonText) != Error.Ok)
            return new Dictionary();

        var parsed = json.Data;
        if (parsed.VariantType != Variant.Type.Dictionary)
            return new Dictionary();

        var dict = (Dictionary)parsed;
        if (!dict.ContainsKey("BallData"))
            return new Dictionary();

        return ((Dictionary)dict["BallData"]).Duplicate();
    }
}
```

**Impact**: Centralizes error handling and reduces code duplication.

---

## 3. Duplicate SetData Logic ⚠️ MEDIUM PRIORITY

**Location**: RangeUI.cs (lines 42-79)

**Issue**: Imperial and Metric branches have nearly identical code. Only difference is Speed units ("mph" vs "m/s").

**Current Code**:
```csharp
if (units == PhysicsEnums.Units.Imperial)
{
    GetNode<DataPanel>("GridCanvas/Distance").SetData(data["Distance"].ToString());
    GetNode<DataPanel>("GridCanvas/Carry").SetData(data["Carry"].ToString());
    // ... 15 more lines
    GetNode<DataPanel>("GridCanvas/Speed").SetUnits("mph");
    // ... more lines
}
else
{
    GetNode<DataPanel>("GridCanvas/Distance").SetData(data["Distance"].ToString());
    GetNode<DataPanel>("GridCanvas/Carry").SetData(data["Carry"].ToString());
    // ... 15 more lines (DUPLICATE)
    GetNode<DataPanel>("GridCanvas/Speed").SetUnits("m/s");
    // ... more lines
}
```

**Suggested Fix**: Extract common code and only change what differs.
```csharp
public void SetData(Dictionary data)
{
    var units = (PhysicsEnums.Units)(int)GetNode<GlobalSettings>("/root/GlobalSettings").RangeSettings.RangeUnits.Value;
    string speedUnit = (units == PhysicsEnums.Units.Imperial) ? "mph" : "m/s";

    GetNode<DataPanel>("GridCanvas/Distance").SetData(data["Distance"].ToString());
    GetNode<DataPanel>("GridCanvas/Carry").SetData(data["Carry"].ToString());
    GetNode<DataPanel>("GridCanvas/Side").SetData(data["Offline"].ToString());
    GetNode<DataPanel>("GridCanvas/Apex").SetData(data["Apex"].ToString());
    GetNode<DataPanel>("GridCanvas/Speed").SetUnits(speedUnit);
    GetNode<DataPanel>("GridCanvas/Speed").SetData(data["Speed"].ToString());
    GetNode<DataPanel>("GridCanvas/BackSpin").SetUnits("rpm");
    GetNode<DataPanel>("GridCanvas/BackSpin").SetData(data["BackSpin"].ToString());
    GetNode<DataPanel>("GridCanvas/SideSpin").SetUnits("rpm");
    GetNode<DataPanel>("GridCanvas/SideSpin").SetData(data["SideSpin"].ToString());
    GetNode<DataPanel>("GridCanvas/TotalSpin").SetUnits("rpm");
    GetNode<DataPanel>("GridCanvas/TotalSpin").SetData(data["TotalSpin"].ToString());
    GetNode<DataPanel>("GridCanvas/SpinAxis").SetUnits("deg");
    GetNode<DataPanel>("GridCanvas/SpinAxis").SetData(data["SpinAxis"].ToString());
    GetNode<DataPanel>("GridCanvas/VLA").SetData(FormatAngle(data.ContainsKey("VLA") ? data["VLA"] : 0.0f));
    GetNode<DataPanel>("GridCanvas/HLA").SetData(FormatAngle(data.ContainsKey("HLA") ? data["HLA"] : 0.0f));
}
```

**Impact**: Removes 38 lines of duplicate code (from 79 lines to 41 lines).

---

## 4. Repeated GetNode Calls ⚠️ LOW PRIORITY (Performance)

**Location**: RangeUI.cs SetData() method

**Issue**: Each DataPanel is retrieved with GetNode multiple times.

**Example**:
```csharp
GetNode<DataPanel>("GridCanvas/Speed").SetUnits("mph");
GetNode<DataPanel>("GridCanvas/Speed").SetData(data["Speed"].ToString());
// GetNode called twice for same node
```

**Suggested Fix**: Cache the node references in _Ready() or use local variables.
```csharp
private DataPanel _distancePanel;
private DataPanel _carryPanel;
// ... etc

public override void _Ready()
{
    // ... existing code
    _distancePanel = GetNode<DataPanel>("GridCanvas/Distance");
    _carryPanel = GetNode<DataPanel>("GridCanvas/Carry");
    // ... etc
}

public void SetData(Dictionary data)
{
    _distancePanel.SetData(data["Distance"].ToString());
    _carryPanel.SetData(data["Carry"].ToString());
    // ... etc
}
```

**Impact**: Minor performance improvement. Less critical than other redundancies.

---

## 5. Duplicate Range.Call("look_at") Pattern

**Location**: Range.cs (lines 143, 197)

**Issue**: PhantomCamera look_at is called with reflection twice.

**Current Code**:
```csharp
_phantomCamera.Call("look_at", ballLookPos, Vector3.Up);  // Line 143
_phantomCamera.Call("look_at", _ball.GlobalPosition + CAMERA_LOOK_OFFSET, Vector3.Up);  // Line 197
```

**Note**: This is unavoidable since PhantomCamera is a GDScript addon. The reflection calls are necessary for interop.

**Impact**: No change recommended - this is expected for GDScript addon integration.

---

## Summary

| Priority | Issue | Lines Saved | Maintainability Impact |
|----------|-------|-------------|------------------------|
| HIGH | Duplicate test shot paths | ~12 lines | High - Single source of truth |
| MEDIUM | Duplicate JSON loading | ~20 lines | High - Centralized error handling |
| MEDIUM | Duplicate SetData logic | ~38 lines | Medium - Simplified branching |
| LOW | Repeated GetNode calls | ~0 lines | Low - Minor perf improvement |

**Total Estimated Line Reduction**: ~70 lines of duplicate code

**Recommendation**: Implement fixes #1, #2, and #3 in that order. Fix #4 is optional for performance-sensitive scenarios.
