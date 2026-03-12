using Godot;

/// <summary>
/// Resolves environment, surface, and ball-profile inputs into the final
/// parameters consumed by the physics engine.
/// </summary>
public sealed class PhysicsParamsFactory
{
    public ResolvedPhysicsParams Create(
        float airDensity,
        float airViscosity,
        float dragScale,
        float liftScale,
        PhysicsEnums.SurfaceType surfaceType,
        Vector3 floorNormal,
        float rolloutImpactSpin = 0.0f,
        BallPhysicsProfile ballProfile = null,
        float initialLaunchAngleDeg = 0.0f)
    {
        BallPhysicsProfile profile = ballProfile ?? new BallPhysicsProfile();
        SurfacePhysicsSettings surface = SurfacePhysicsCatalog.Get(surfaceType);

        return new ResolvedPhysicsParams(
            airDensity,
            airViscosity,
            dragScale * profile.DragScaleMultiplier,
            liftScale * profile.LiftScaleMultiplier,
            surface.KineticFriction * profile.KineticFrictionMultiplier,
            surface.RollingFriction * profile.RollingFrictionMultiplier,
            surface.GrassViscosity * profile.GrassViscosityMultiplier,
            surface.CriticalAngle + profile.CriticalAngleOffsetRadians,
            surfaceType,
            floorNormal,
            rolloutImpactSpin,
            surface.SpinbackResponseScale,
            surface.SpinbackThetaBoostMax * profile.SpinbackThetaBoostMultiplier,
            surface.SpinbackSpinStartRpm,
            surface.SpinbackSpinEndRpm,
            surface.SpinbackSpeedStartMps,
            surface.SpinbackSpeedEndMps,
            initialLaunchAngleDeg,
            profile.ResolvedFlight
        );
    }
}
