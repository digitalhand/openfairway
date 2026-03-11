/// <summary>
/// Ball-specific physics modifiers. Defaults are neutral so current
/// behavior is preserved until a non-default profile is supplied.
/// </summary>
public sealed class BallPhysicsProfile
{
    public float DragScaleMultiplier { get; set; } = 1.0f;
    public float LiftScaleMultiplier { get; set; } = 1.0f;
    public float KineticFrictionMultiplier { get; set; } = 1.0f;
    public float RollingFrictionMultiplier { get; set; } = 1.0f;
    public float GrassViscosityMultiplier { get; set; } = 1.0f;
    public float CriticalAngleOffsetRadians { get; set; } = 0.0f;
    public float SpinbackThetaBoostMultiplier { get; set; } = 1.0f;

    public FlightProfile Flight { get; set; }
    public BounceProfile Bounce { get; set; }
    public RolloutProfile Rollout { get; set; }

    public FlightProfile ResolvedFlight => Flight ?? FlightProfile.Default;
    public BounceProfile ResolvedBounce => Bounce ?? BounceProfile.Default;
    public RolloutProfile ResolvedRollout => Rollout ?? RolloutProfile.Default;
}
