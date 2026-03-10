using Godot;
using Godot.Collections;

/// <summary>
/// Adapter/utility for simulating shots from JSON data (headless simulation)
/// </summary>
[GlobalClass]
public partial class PhysicsAdapter : RefCounted
{
    private const float YARDS_PER_METER = ShotSetup.YARDS_PER_METER;
    private const float FEET_PER_METER = ShotSetup.FEET_PER_METER;
    private const float START_HEIGHT = 0.02f;
    private const float DEFAULT_TEMP_F = 75.0f;
    private const float DEFAULT_ALT_FT = 0.0f;
    private const float MAX_TIME = 12.0f;
    private const float DT = BallPhysics.SIMULATION_DT;

    private readonly BallPhysics _physics = new();
    private readonly Aerodynamics _aero = new();
    private readonly PhysicsParamsFactory _physicsParamsFactory = new();
    private readonly ShotSetup _shotSetup = new();
    private readonly BallPhysicsProfile _ballProfile = new();

    /// <summary>
    /// Simulate a shot from JSON data and return carry/total distances
    /// </summary>
    public Dictionary SimulateShotFromJson(Dictionary shot)
    {
        return SimulateShotFromJson(shot, PhysicsEnums.SurfaceType.Fairway, Vector3.Up);
    }

    /// <summary>
    /// Simulate a shot from JSON data on a specific surface and floor normal.
    /// Useful for regression checks such as green/slope-specific rollout behavior.
    /// </summary>
    public Dictionary SimulateShotFromJson(Dictionary shot, PhysicsEnums.SurfaceType surface, Vector3 floorNormal)
    {
        var ballDict = shot.ContainsKey("BallData") ? (Dictionary)shot["BallData"] : shot;
        if (ballDict == null || ballDict.Count == 0)
        {
            PhysicsLogger.PushError("Shot JSON missing BallData");
            return new Dictionary();
        }

        float speedMph = (float)(ballDict.ContainsKey("Speed") ? ballDict["Speed"] : 0.0);
        float vla = (float)(ballDict.ContainsKey("VLA") ? ballDict["VLA"] : 0.0);
        float hla = (float)(ballDict.ContainsKey("HLA") ? ballDict["HLA"] : 0.0);
        var spinData = _shotSetup.ParseSpin(ballDict);
        float backspin = (float)spinData["backspin"];
        float sidespin = (float)spinData["sidespin"];
        float totalSpin = (float)spinData["total"];
        float spinAxis = (float)spinData["axis"];

        var launch = _shotSetup.BuildLaunchVectorsFromComponents(speedMph, vla, hla, backspin, sidespin);
        Vector3 velocity = (Vector3)launch["velocity"];
        Vector3 omega = (Vector3)launch["omega"];
        Vector3 shotDir = (Vector3)launch["shot_direction"];

        Vector3 contactNormal = floorNormal.LengthSquared() > 0.000001f ? floorNormal.Normalized() : Vector3.Up;
        var parameters = CreateParams(contactNormal, surface, vla);

        Vector3 pos = new Vector3(0.0f, START_HEIGHT, 0.0f);
        PhysicsEnums.BallState state = PhysicsEnums.BallState.Flight;
        bool onGround = false;
        float carryM = 0.0f;
        bool carryRecorded = false;
        float hangTimeS = 0.0f;
        float apexM = pos.Y;
        bool firstImpactSpinback = false;
        float landingSpeedMps = 0.0f;
        float landingAngleDeg = 0.0f;
        float firstImpactTangentIn = 0.0f;
        float firstImpactTangentOut = 0.0f;

        FlightAerodynamicsSample initialAirSample = BallPhysics.SampleFlightAerodynamics(
            velocity,
            omega,
            parameters.AirDensity,
            parameters.AirViscosity,
            parameters.DragScale,
            parameters.LiftScale,
            parameters.InitialLaunchAngleDeg
        );
        float peakCl = 0.0f;

        int steps = (int)(MAX_TIME / DT);
        for (int i = 0; i < steps; i++)
        {
            if (!onGround)
            {
                FlightAerodynamicsSample airSample = BallPhysics.SampleFlightAerodynamics(
                    velocity,
                    omega,
                    parameters.AirDensity,
                    parameters.AirViscosity,
                    parameters.DragScale,
                    parameters.LiftScale,
                    parameters.InitialLaunchAngleDeg
                );
                if (airSample.HasAerodynamics)
                {
                    peakCl = Mathf.Max(peakCl, airSample.LiftCoefficient);
                }
            }

            _physics.IntegrateStep(ref velocity, ref omega, onGround, parameters, DT);

            pos += velocity * DT;
            apexM = Mathf.Max(apexM, pos.Y);

            bool hasImpact = pos.Y <= 0.0f && (velocity.Y < -0.01f || state == PhysicsEnums.BallState.Flight);
            if (hasImpact)
            {
                pos.Y = 0.0f;
                float preImpactSpeed = velocity.Length();
                float preImpactNormalSpeed = Mathf.Abs(velocity.Dot(contactNormal));
                Vector3 preImpactTangent = velocity - contactNormal * velocity.Dot(contactNormal);
                var bounce = _physics.CalculateBounce(velocity, omega, contactNormal, state, parameters);
                velocity = bounce.NewVelocity;
                omega = bounce.NewOmega;
                state = bounce.NewState;
                onGround = state != PhysicsEnums.BallState.Flight;
                velocity.Y = Mathf.Max(velocity.Y, 0.0f);

                if (!carryRecorded)
                {
                    Vector3 postImpactTangent = velocity - contactNormal * velocity.Dot(contactNormal);
                    float preTanMag = preImpactTangent.Length();
                    float postTanMag = postImpactTangent.Length();

                    firstImpactTangentIn = preTanMag;
                    firstImpactTangentOut = postTanMag;
                    landingSpeedMps = preImpactSpeed;
                    landingAngleDeg = Mathf.RadToDeg(Mathf.Atan2(preImpactNormalSpeed, Mathf.Max(preTanMag, 0.0001f)));

                    if (preTanMag > 0.01f && postTanMag > 0.01f)
                    {
                        float directionDot = preImpactTangent.Normalized().Dot(postImpactTangent.Normalized());
                        firstImpactSpinback = directionDot < -0.001f;
                        if (firstImpactSpinback)
                        {
                            firstImpactTangentOut = -postTanMag;
                        }
                    }
                }

                if (!carryRecorded)
                {
                    carryM = Mathf.Max(pos.Dot(shotDir), 0.0f);
                    carryRecorded = true;
                    hangTimeS = (i + 1) * DT;
                }
            }
            else
            {
                if (pos.Y < 0.0f)
                {
                    pos.Y = 0.0f;
                    velocity.Y = Mathf.Max(velocity.Y, 0.0f);
                }
                onGround = state != PhysicsEnums.BallState.Flight && pos.Y <= 0.02f;
            }

            float speed = velocity.Length();
            if (onGround && speed < 0.05f && omega.Length() < 0.5f)
            {
                state = PhysicsEnums.BallState.Rest;
                velocity = Vector3.Zero;
                omega = Vector3.Zero;
                break;
            }
        }

        float totalM = Mathf.Max(pos.Dot(shotDir), 0.0f);
        if (!carryRecorded)
        {
            carryM = totalM;
        }

        return new Dictionary
        {
            { "carry_yd", carryM * YARDS_PER_METER },
            { "total_yd", totalM * YARDS_PER_METER },
            { "carry_yd_first_impact", carryM * YARDS_PER_METER },
            { "apex_ft", apexM * FEET_PER_METER },
            { "hang_time_s", hangTimeS },
            { "flight_time_s", hangTimeS },
            { "first_impact_time_s", hangTimeS },
            { "landing_speed_mps", landingSpeedMps },
            { "landing_angle_deg", landingAngleDeg },
            { "initial_re", initialAirSample.Reynolds },
            { "initial_spin_ratio", initialAirSample.SpinRatio },
            { "initial_launch_angle_deg", vla },
            { "initial_low_launch_lift_scale", initialAirSample.LowLaunchLiftScale },
            { "initial_spin_drag_multiplier", initialAirSample.SpinDragMultiplier },
            { "initial_backspin_rpm", backspin },
            { "initial_sidespin_rpm", sidespin },
            { "initial_total_spin_rpm", totalSpin },
            { "initial_spin_axis_deg", spinAxis },
            { "initial_cd", initialAirSample.DragCoefficient },
            { "initial_cl", initialAirSample.LiftCoefficient },
            { "peak_cl", peakCl },
            { "surface", surface.ToString() },
            { "first_impact_spinback", firstImpactSpinback },
            { "first_impact_tangent_in_mps", firstImpactTangentIn },
            { "first_impact_tangent_out_mps", firstImpactTangentOut }
        };
    }

    private PhysicsParams CreateParams(Vector3 floorNormal, PhysicsEnums.SurfaceType surface, float initialLaunchAngleDeg)
    {
        float airDensity = _aero.GetAirDensity(DEFAULT_ALT_FT, DEFAULT_TEMP_F, PhysicsEnums.Units.Imperial);
        float airViscosity = _aero.GetDynamicViscosity(DEFAULT_TEMP_F, PhysicsEnums.Units.Imperial);

        return _physicsParamsFactory.Create(
            airDensity,
            airViscosity,
            1.0f,
            1.0f,
            surface,
            floorNormal,
            rolloutImpactSpin: 0.0f,
            ballProfile: _ballProfile,
            initialLaunchAngleDeg: initialLaunchAngleDeg
        ).ToPhysicsParams();
    }
}
