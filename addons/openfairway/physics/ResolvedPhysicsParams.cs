using Godot;

/// <summary>
/// Plain resolved physics values that can be validated in tests before
/// being materialized into a Godot PhysicsParams resource at runtime.
/// </summary>
public sealed class ResolvedPhysicsParams
{
    public float AirDensity { get; }
    public float AirViscosity { get; }
    public float DragScale { get; }
    public float LiftScale { get; }
    public float KineticFriction { get; }
    public float RollingFriction { get; }
    public float GrassViscosity { get; }
    public float CriticalAngle { get; }
    public PhysicsEnums.SurfaceType SurfaceType { get; }
    public Vector3 FloorNormal { get; }
    public float RolloutImpactSpin { get; }
    public float SpinbackResponseScale { get; }
    public float SpinbackThetaBoostMax { get; }
    public float SpinbackSpinStartRpm { get; }
    public float SpinbackSpinEndRpm { get; }
    public float SpinbackSpeedStartMps { get; }
    public float SpinbackSpeedEndMps { get; }
    public float InitialLaunchAngleDeg { get; }

    public ResolvedPhysicsParams(
        float airDensity,
        float airViscosity,
        float dragScale,
        float liftScale,
        float kineticFriction,
        float rollingFriction,
        float grassViscosity,
        float criticalAngle,
        PhysicsEnums.SurfaceType surfaceType,
        Vector3 floorNormal,
        float rolloutImpactSpin,
        float spinbackResponseScale,
        float spinbackThetaBoostMax,
        float spinbackSpinStartRpm,
        float spinbackSpinEndRpm,
        float spinbackSpeedStartMps,
        float spinbackSpeedEndMps,
        float initialLaunchAngleDeg)
    {
        AirDensity = airDensity;
        AirViscosity = airViscosity;
        DragScale = dragScale;
        LiftScale = liftScale;
        KineticFriction = kineticFriction;
        RollingFriction = rollingFriction;
        GrassViscosity = grassViscosity;
        CriticalAngle = criticalAngle;
        SurfaceType = surfaceType;
        FloorNormal = floorNormal;
        RolloutImpactSpin = rolloutImpactSpin;
        SpinbackResponseScale = spinbackResponseScale;
        SpinbackThetaBoostMax = spinbackThetaBoostMax;
        SpinbackSpinStartRpm = spinbackSpinStartRpm;
        SpinbackSpinEndRpm = spinbackSpinEndRpm;
        SpinbackSpeedStartMps = spinbackSpeedStartMps;
        SpinbackSpeedEndMps = spinbackSpeedEndMps;
        InitialLaunchAngleDeg = initialLaunchAngleDeg;
    }

    public PhysicsParams ToPhysicsParams()
    {
        return new PhysicsParams(
            AirDensity,
            AirViscosity,
            DragScale,
            LiftScale,
            KineticFriction,
            RollingFriction,
            GrassViscosity,
            CriticalAngle,
            SurfaceType,
            FloorNormal,
            RolloutImpactSpin,
            SpinbackResponseScale,
            SpinbackThetaBoostMax,
            SpinbackSpinStartRpm,
            SpinbackSpinEndRpm,
            SpinbackSpeedStartMps,
            SpinbackSpeedEndMps,
            InitialLaunchAngleDeg
        );
    }
}
