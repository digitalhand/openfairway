using Godot;
using Godot.Collections;

/// <summary>
/// Centralized validation for shot data from TCP launch monitors and UI injection.
/// Clamps values to physically reasonable ranges and rejects invalid data.
/// </summary>
public static class ShotValidator
{
    // Speed: 0 to 250 mph (world long drive record is ~230 mph)
    public const float MIN_SPEED = 0.0f;
    public const float MAX_SPEED = 250.0f;

    // Vertical launch angle: -10 to 90 degrees
    public const float MIN_VLA = -10.0f;
    public const float MAX_VLA = 90.0f;

    // Horizontal launch angle: -90 to 90 degrees
    public const float MIN_HLA = -90.0f;
    public const float MAX_HLA = 90.0f;

    // Total spin: 0 to 15000 RPM
    public const float MIN_SPIN = 0.0f;
    public const float MAX_SPIN = 15000.0f;

    // Spin axis: -90 to 90 degrees
    public const float MIN_SPIN_AXIS = -90.0f;
    public const float MAX_SPIN_AXIS = 90.0f;

    // Backspin/sidespin component limits
    public const float MAX_COMPONENT_SPIN = 15000.0f;

    /// <summary>
    /// Validate and clamp shot data values to physically reasonable ranges.
    /// Returns true if data is usable (possibly after clamping), false if fundamentally invalid.
    /// Modifies the dictionary in-place to clamp out-of-range values.
    /// </summary>
    public static bool ValidateAndClamp(Dictionary data)
    {
        if (data == null || data.Count == 0)
            return false;

        // Speed is required and must be positive
        if (data.ContainsKey("Speed"))
        {
            float speed = (float)data["Speed"];
            if (float.IsNaN(speed) || float.IsInfinity(speed))
            {
                PhysicsLogger.Error("ShotValidator: Speed is NaN or Infinity, rejecting shot");
                return false;
            }
            data["Speed"] = Mathf.Clamp(speed, MIN_SPEED, MAX_SPEED);
        }

        if (data.ContainsKey("VLA"))
        {
            float vla = (float)data["VLA"];
            if (float.IsNaN(vla) || float.IsInfinity(vla))
            {
                PhysicsLogger.Error("ShotValidator: VLA is NaN or Infinity, rejecting shot");
                return false;
            }
            data["VLA"] = Mathf.Clamp(vla, MIN_VLA, MAX_VLA);
        }

        if (data.ContainsKey("HLA"))
        {
            float hla = (float)data["HLA"];
            if (float.IsNaN(hla) || float.IsInfinity(hla))
            {
                PhysicsLogger.Error("ShotValidator: HLA is NaN or Infinity, rejecting shot");
                return false;
            }
            data["HLA"] = Mathf.Clamp(hla, MIN_HLA, MAX_HLA);
        }

        if (data.ContainsKey("TotalSpin"))
        {
            float spin = (float)data["TotalSpin"];
            if (float.IsNaN(spin) || float.IsInfinity(spin))
            {
                PhysicsLogger.Error("ShotValidator: TotalSpin is NaN or Infinity, rejecting shot");
                return false;
            }
            data["TotalSpin"] = Mathf.Clamp(spin, MIN_SPIN, MAX_SPIN);
        }

        if (data.ContainsKey("SpinAxis"))
        {
            float axis = (float)data["SpinAxis"];
            if (float.IsNaN(axis) || float.IsInfinity(axis))
            {
                PhysicsLogger.Error("ShotValidator: SpinAxis is NaN or Infinity, rejecting shot");
                return false;
            }
            data["SpinAxis"] = Mathf.Clamp(axis, MIN_SPIN_AXIS, MAX_SPIN_AXIS);
        }

        if (data.ContainsKey("BackSpin"))
        {
            float back = (float)data["BackSpin"];
            if (float.IsNaN(back) || float.IsInfinity(back))
            {
                PhysicsLogger.Error("ShotValidator: BackSpin is NaN or Infinity, rejecting shot");
                return false;
            }
            data["BackSpin"] = Mathf.Clamp(back, -MAX_COMPONENT_SPIN, MAX_COMPONENT_SPIN);
        }

        if (data.ContainsKey("SideSpin"))
        {
            float side = (float)data["SideSpin"];
            if (float.IsNaN(side) || float.IsInfinity(side))
            {
                PhysicsLogger.Error("ShotValidator: SideSpin is NaN or Infinity, rejecting shot");
                return false;
            }
            data["SideSpin"] = Mathf.Clamp(side, -MAX_COMPONENT_SPIN, MAX_COMPONENT_SPIN);
        }

        return true;
    }
}
