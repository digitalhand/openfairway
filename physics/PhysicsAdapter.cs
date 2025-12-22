using System;
using Godot;
using Godot.Collections;

/// <summary>
/// Adapter/utility for simulating shots from JSON data (headless simulation)
/// </summary>
public partial class PhysicsAdapter : Node
{
    private const float MPS_PER_MPH = 0.44704f;
    private const float YARDS_PER_METER = 1.09361f;
    private const float START_HEIGHT = 0.02f;
    private const float DEFAULT_TEMP_F = 75.0f;
    private const float DEFAULT_ALT_FT = 0.0f;
    private const float MAX_TIME = 12.0f;
    private const float DT = 1.0f / 240.0f;

    /// <summary>
    /// Simulate a shot from JSON data and return carry/total distances
    /// </summary>
    public static Dictionary SimulateShotFromJson(Dictionary shot)
    {
        var ballDict = shot.ContainsKey("BallData") ? (Dictionary)shot["BallData"] : shot;
        if (ballDict == null || ballDict.Count == 0)
        {
            GD.PushError("Shot JSON missing BallData");
            return new Dictionary();
        }

        float speedMps = (float)(ballDict.ContainsKey("Speed") ? ballDict["Speed"] : 0.0) * MPS_PER_MPH;
        float vla = (float)(ballDict.ContainsKey("VLA") ? ballDict["VLA"] : 0.0);
        float hla = (float)(ballDict.ContainsKey("HLA") ? ballDict["HLA"] : 0.0);
        var spinData = ParseSpin(ballDict);
        float totalSpin = (float)spinData["total"];
        float spinAxis = (float)spinData["axis"];

        Vector3 velocity = new Vector3(speedMps, 0, 0)
            .Rotated(Vector3.Forward, Mathf.DegToRad(-vla))
            .Rotated(Vector3.Up, Mathf.DegToRad(-hla));

        Vector3 omega = new Vector3(0.0f, 0.0f, totalSpin * 0.10472f)
            .Rotated(Vector3.Right, Mathf.DegToRad(spinAxis));

        Vector3 flatVelocity = new Vector3(velocity.X, 0.0f, velocity.Z);
        Vector3 shotDir = flatVelocity.Length() > 0.001f ? flatVelocity.Normalized() : Vector3.Right;

        var parameters = CreateParams(Vector3.Up, PhysicsEnums.SurfaceType.Fairway);

        Vector3 pos = new Vector3(0.0f, START_HEIGHT, 0.0f);
        PhysicsEnums.BallState state = PhysicsEnums.BallState.Flight;
        bool onGround = false;
        float carryM = 0.0f;
        bool carryRecorded = false;

        int steps = (int)(MAX_TIME / DT);
        for (int i = 0; i < steps; i++)
        {
            Vector3 force = BallPhysics.CalculateForces(velocity, omega, onGround, parameters);
            Vector3 torque = BallPhysics.CalculateTorques(velocity, omega, onGround, parameters);

            velocity += (force / BallPhysics.MASS) * DT;
            omega += (torque / BallPhysics.MOMENT_OF_INERTIA) * DT;

            pos += velocity * DT;

            bool hasImpact = pos.Y <= 0.0f && (velocity.Y < -0.01f || state == PhysicsEnums.BallState.Flight);
            if (hasImpact)
            {
                pos.Y = 0.0f;
                var bounce = BallPhysics.CalculateBounce(velocity, omega, Vector3.Up, state, parameters);
                velocity = bounce.NewVelocity;
                omega = bounce.NewOmega;
                state = bounce.NewState;
                onGround = state != PhysicsEnums.BallState.Flight;
                velocity.Y = Mathf.Max(velocity.Y, 0.0f);

                if (!carryRecorded)
                {
                    carryM = Mathf.Max(pos.Dot(shotDir), 0.0f);
                    carryRecorded = true;
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
            { "total_yd", totalM * YARDS_PER_METER }
        };
    }

    private static Dictionary ParseSpin(Dictionary data)
    {
        bool hasBackspin = data.ContainsKey("BackSpin");
        bool hasSidespin = data.ContainsKey("SideSpin");
        bool hasTotal = data.ContainsKey("TotalSpin");
        bool hasAxis = data.ContainsKey("SpinAxis");

        float backspin = (float)(data.ContainsKey("BackSpin") ? data["BackSpin"] : 0.0);
        float sidespin = (float)(data.ContainsKey("SideSpin") ? data["SideSpin"] : 0.0);
        float totalSpin = (float)(data.ContainsKey("TotalSpin") ? data["TotalSpin"] : 0.0);
        float spinAxis = (float)(data.ContainsKey("SpinAxis") ? data["SpinAxis"] : 0.0);

        if (totalSpin == 0.0f && (hasBackspin || hasSidespin))
        {
            totalSpin = Mathf.Sqrt(backspin * backspin + sidespin * sidespin);
        }

        if (!hasAxis && (hasBackspin || hasSidespin))
        {
            spinAxis = Mathf.RadToDeg(Mathf.Atan2(sidespin, backspin));
        }

        if (hasTotal && hasAxis)
        {
            if (!hasBackspin)
            {
                backspin = totalSpin * Mathf.Cos(Mathf.DegToRad(spinAxis));
            }
            if (!hasSidespin)
            {
                sidespin = totalSpin * Mathf.Sin(Mathf.DegToRad(spinAxis));
            }
        }

        return new Dictionary
        {
            { "backspin", backspin },
            { "sidespin", sidespin },
            { "total", totalSpin },
            { "axis", spinAxis }
        };
    }

    private static BallPhysics.PhysicsParams CreateParams(Vector3 floorNormal, PhysicsEnums.SurfaceType surface)
    {
        var surfaceParams = Surface.GetParams(surface);
        float airDensity = Aerodynamics.GetAirDensity(DEFAULT_ALT_FT, DEFAULT_TEMP_F, PhysicsEnums.Units.Imperial);
        float airViscosity = Aerodynamics.GetDynamicViscosity(DEFAULT_TEMP_F, PhysicsEnums.Units.Imperial);

        return new BallPhysics.PhysicsParams(
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
