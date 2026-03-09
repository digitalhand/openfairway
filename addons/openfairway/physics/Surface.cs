using Godot;
using Godot.Collections;

/// <summary>
/// Utility class for ground surface physics parameters.
/// Provides friction coefficients and interaction parameters for different
/// playing surfaces based on golf physics research.
/// Reference: https://raypenner.com/golf-physics.pdf
/// </summary>
[GlobalClass]
public partial class Surface : RefCounted
{
    /// <summary>
    /// Returns ground interaction parameters for a given surface type.
    /// Parameters returned:
    /// - u_k: Kinetic friction coefficient (sliding)
    /// - u_kr: Rolling friction coefficient
    /// - nu_g: Grass drag viscosity
    /// - theta_c: Critical bounce angle in radians (from Penner's golf physics)
    /// </summary>
    public Dictionary GetParams(PhysicsEnums.SurfaceType surface)
    {
        return surface switch
        {
            PhysicsEnums.SurfaceType.Rough =>
                // Tall grass rough: shortest rollout.
                new Dictionary
                {
                    { "u_k", 0.62f },      // High slip friction
                    { "u_kr", 0.095f },    // High rolling resistance
                    { "nu_g", 0.0032f },   // Strong vegetation drag
                    { "theta_c", 0.35f },  // ~20°
                    { "spinback_theta_boost_max", 0.0f },
                    { "spinback_spin_start_rpm", 0.0f },
                    { "spinback_spin_end_rpm", 0.0f },
                    { "spinback_speed_start_mps", 0.0f },
                    { "spinback_speed_end_mps", 0.0f }
                },

            PhysicsEnums.SurfaceType.Fairway =>
                // Default fairway tuned to roll ~40% less than prior baseline.
                new Dictionary
                {
                    { "u_k", 0.50f },      // Higher slip friction than prior baseline
                    { "u_kr", 0.050f },    // ~1.67x rolling resistance vs prior baseline
                    { "nu_g", 0.0017f },   // ~1.7x grass drag vs prior baseline
                    { "theta_c", 0.29f },  // ~17°
                    { "spinback_theta_boost_max", 0.0f },
                    { "spinback_spin_start_rpm", 0.0f },
                    { "spinback_spin_end_rpm", 0.0f },
                    { "spinback_speed_start_mps", 0.0f },
                    { "spinback_speed_end_mps", 0.0f }
                },

            PhysicsEnums.SurfaceType.FairwaySoft =>
                // Soft/wet fairway: slower than normal fairway but faster than rough.
                new Dictionary
                {
                    { "u_k", 0.56f },
                    { "u_kr", 0.070f },
                    { "nu_g", 0.0024f },
                    { "theta_c", 0.32f },  // ~18°
                    { "spinback_theta_boost_max", 0.0f },
                    { "spinback_spin_start_rpm", 0.0f },
                    { "spinback_spin_end_rpm", 0.0f },
                    { "spinback_speed_start_mps", 0.0f },
                    { "spinback_speed_end_mps", 0.0f }
                },

            PhysicsEnums.SurfaceType.Firm =>
                // Hard pavement/concrete style lie.
                // Intentionally set to the prior fairway baseline (fast rollout).
                new Dictionary
                {
                    { "u_k", 0.30f },
                    { "u_kr", 0.030f },
                    { "nu_g", 0.0010f },
                    { "theta_c", 0.25f },  // ~14°
                    { "spinback_theta_boost_max", 0.0f },
                    { "spinback_spin_start_rpm", 0.0f },
                    { "spinback_spin_end_rpm", 0.0f },
                    { "spinback_speed_start_mps", 0.0f },
                    { "spinback_speed_end_mps", 0.0f }
                },

            PhysicsEnums.SurfaceType.Green =>
                // Putting green:
                // - Higher impact grip (u_k/theta_c) supports check/spin-back for steep high-spin wedges.
                // - Lower rolling resistance/vegetation drag keeps post-check motion realistic for short-cut turf.
                // - Spinback parameters enable check/reversal on steep high-spin impacts.
                new Dictionary
                {
                    { "u_k", 0.58f },
                    { "u_kr", 0.028f },
                    { "nu_g", 0.0009f },
                    { "theta_c", 0.36f },  // ~21°
                    { "spinback_theta_boost_max", 0.12f },       // rad (~6.9°)
                    { "spinback_spin_start_rpm", 3500.0f },
                    { "spinback_spin_end_rpm", 5500.0f },
                    { "spinback_speed_start_mps", 8.0f },
                    { "spinback_speed_end_mps", 20.0f }
                },

            _ =>
                // Default to current normal fairway tuning
                new Dictionary
                {
                    { "u_k", 0.50f },
                    { "u_kr", 0.050f },
                    { "nu_g", 0.0017f },
                    { "theta_c", 0.29f },
                    { "spinback_theta_boost_max", 0.0f },
                    { "spinback_spin_start_rpm", 0.0f },
                    { "spinback_spin_end_rpm", 0.0f },
                    { "spinback_speed_start_mps", 0.0f },
                    { "spinback_speed_end_mps", 0.0f }
                }
        };
    }
}
