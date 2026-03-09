using Godot.Collections;

/// <summary>
/// Typed surface tuning values used to build physics parameters.
/// </summary>
public sealed class SurfacePhysicsSettings
{
    public PhysicsEnums.SurfaceType SurfaceType { get; }
    public float KineticFriction { get; }
    public float RollingFriction { get; }
    public float GrassViscosity { get; }
    public float CriticalAngle { get; }
    public float SpinbackResponseScale { get; }
    public float SpinbackThetaBoostMax { get; }
    public float SpinbackSpinStartRpm { get; }
    public float SpinbackSpinEndRpm { get; }
    public float SpinbackSpeedStartMps { get; }
    public float SpinbackSpeedEndMps { get; }

    public SurfacePhysicsSettings(
        PhysicsEnums.SurfaceType surfaceType,
        float kineticFriction,
        float rollingFriction,
        float grassViscosity,
        float criticalAngle,
        float spinbackResponseScale,
        float spinbackThetaBoostMax,
        float spinbackSpinStartRpm,
        float spinbackSpinEndRpm,
        float spinbackSpeedStartMps,
        float spinbackSpeedEndMps)
    {
        SurfaceType = surfaceType;
        KineticFriction = kineticFriction;
        RollingFriction = rollingFriction;
        GrassViscosity = grassViscosity;
        CriticalAngle = criticalAngle;
        SpinbackResponseScale = spinbackResponseScale;
        SpinbackThetaBoostMax = spinbackThetaBoostMax;
        SpinbackSpinStartRpm = spinbackSpinStartRpm;
        SpinbackSpinEndRpm = spinbackSpinEndRpm;
        SpinbackSpeedStartMps = spinbackSpeedStartMps;
        SpinbackSpeedEndMps = spinbackSpeedEndMps;
    }

    public Dictionary ToDictionary()
    {
        return new Dictionary
        {
            { "u_k", KineticFriction },
            { "u_kr", RollingFriction },
            { "nu_g", GrassViscosity },
            { "theta_c", CriticalAngle },
            { "spinback_response_scale", SpinbackResponseScale },
            { "spinback_theta_boost_max", SpinbackThetaBoostMax },
            { "spinback_spin_start_rpm", SpinbackSpinStartRpm },
            { "spinback_spin_end_rpm", SpinbackSpinEndRpm },
            { "spinback_speed_start_mps", SpinbackSpeedStartMps },
            { "spinback_speed_end_mps", SpinbackSpeedEndMps }
        };
    }
}
