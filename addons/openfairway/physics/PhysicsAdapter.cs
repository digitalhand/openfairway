using System;
using Godot;
using Godot.Collections;

/// <summary>
/// Adapter/utility for simulating shots from JSON data (headless simulation)
/// </summary>
[GlobalClass]
public partial class PhysicsAdapter : RefCounted
{
    private const float MPS_PER_MPH = 0.44704f;
    private const float YARDS_PER_METER = 1.09361f;
    private const float FEET_PER_METER = 3.28084f;
    private const float START_HEIGHT = 0.02f;
    private const float DEFAULT_TEMP_F = 75.0f;
    private const float DEFAULT_ALT_FT = 0.0f;
    private const float MAX_TIME = 12.0f;
    private const float DT = 1.0f / 240.0f;

    private readonly BallPhysics _physics = new();
    private readonly Aerodynamics _aero = new();
    private readonly Surface _surface = new();
    private readonly ShotSetup _shotSetup = new();

    /// <summary>
    /// Simulate a shot from JSON data and return carry/total distances
    /// </summary>
    public Dictionary SimulateShotFromJson(Dictionary shot)
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
        float totalSpin = (float)spinData["total"];
        float spinAxis = (float)spinData["axis"];

        var launch = _shotSetup.BuildLaunchVectors(speedMph, vla, hla, totalSpin, spinAxis);
        Vector3 velocity = (Vector3)launch["velocity"];
        Vector3 omega = (Vector3)launch["omega"];
        Vector3 shotDir = (Vector3)launch["shot_direction"];

        var parameters = CreateParams(Vector3.Up, PhysicsEnums.SurfaceType.Fairway);

        Vector3 pos = new Vector3(0.0f, START_HEIGHT, 0.0f);
        PhysicsEnums.BallState state = PhysicsEnums.BallState.Flight;
        bool onGround = false;
        float carryM = 0.0f;
        bool carryRecorded = false;
        float hangTimeS = 0.0f;
        float apexM = pos.Y;

        float initialSpeed = velocity.Length();
        float initialSpinRatio = initialSpeed > 0.001f ? omega.Length() * BallPhysics.RADIUS / initialSpeed : 0.0f;
        float initialRe = parameters.AirDensity * initialSpeed * BallPhysics.RADIUS * 2.0f / parameters.AirViscosity;
        float initialSpinDragMultiplier = 1.0f + BallPhysics.SPIN_DRAG_MULTIPLIER_COEFF * initialSpinRatio * initialSpinRatio;
        initialSpinDragMultiplier = Mathf.Min(initialSpinDragMultiplier, BallPhysics.SPIN_DRAG_MULTIPLIER_MAX);
        float initialCd = _aero.GetCd(initialRe) * initialSpinDragMultiplier * parameters.DragScale;
        float initialCl = _aero.GetCl(initialRe, initialSpinRatio) * parameters.LiftScale;
        float peakCl = 0.0f;

        int steps = (int)(MAX_TIME / DT);
        for (int i = 0; i < steps; i++)
        {
            if (!onGround)
            {
                float aeroSpeed = velocity.Length();
                if (aeroSpeed > 0.001f)
                {
                    float spinRatio = omega.Length() * BallPhysics.RADIUS / aeroSpeed;
                    float reynolds = parameters.AirDensity * aeroSpeed * BallPhysics.RADIUS * 2.0f / parameters.AirViscosity;
                    float cl = _aero.GetCl(reynolds, spinRatio) * parameters.LiftScale;
                    peakCl = Mathf.Max(peakCl, cl);
                }
            }

            Vector3 force = _physics.CalculateForces(velocity, omega, onGround, parameters);
            Vector3 torque = _physics.CalculateTorques(velocity, omega, onGround, parameters);

            velocity += (force / BallPhysics.MASS) * DT;
            omega += (torque / BallPhysics.MOMENT_OF_INERTIA) * DT;

            pos += velocity * DT;
            apexM = Mathf.Max(apexM, pos.Y);

            bool hasImpact = pos.Y <= 0.0f && (velocity.Y < -0.01f || state == PhysicsEnums.BallState.Flight);
            if (hasImpact)
            {
                pos.Y = 0.0f;
                var bounce = _physics.CalculateBounce(velocity, omega, Vector3.Up, state, parameters);
                velocity = bounce.NewVelocity;
                omega = bounce.NewOmega;
                state = bounce.NewState;
                onGround = state != PhysicsEnums.BallState.Flight;
                velocity.Y = Mathf.Max(velocity.Y, 0.0f);

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
            { "initial_re", initialRe },
            { "initial_spin_ratio", initialSpinRatio },
            { "initial_cd", initialCd },
            { "initial_cl", initialCl },
            { "peak_cl", peakCl }
        };
    }

    private PhysicsParams CreateParams(Vector3 floorNormal, PhysicsEnums.SurfaceType surface)
    {
        var surfaceParams = _surface.GetParams(surface);
        float airDensity = _aero.GetAirDensity(DEFAULT_ALT_FT, DEFAULT_TEMP_F, PhysicsEnums.Units.Imperial);
        float airViscosity = _aero.GetDynamicViscosity(DEFAULT_TEMP_F, PhysicsEnums.Units.Imperial);

        return new PhysicsParams(
            airDensity,
            airViscosity,
            1.0f,
            1.0f,
            (float)surfaceParams["u_k"],
            (float)surfaceParams["u_kr"],
            (float)surfaceParams["nu_g"],
            (float)surfaceParams["theta_c"],
            floorNormal
        );
    }
}
