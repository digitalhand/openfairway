using Godot;

/// <summary>
/// Pure physics calculations for golf ball motion.
/// Contains all force, torque, and bounce calculations separated from
/// the game object (CharacterBody3D) implementation.
/// </summary>
public static partial class BallPhysics
{
    // Ball physical properties
    public const float MASS = 0.04592623f;  // kg (regulation golf ball)
    public const float RADIUS = 0.021335f;  // m (regulation golf ball)
    public const float CROSS_SECTION = Mathf.Pi * RADIUS * RADIUS;  // m²
    public const float MOMENT_OF_INERTIA = 0.4f * MASS * RADIUS * RADIUS;  // kg*m²
    public const float SPIN_DECAY_TAU = 3.0f;  // Spin decay time constant (seconds)

    /// <summary>
    /// Physics parameters structure
    /// </summary>
    public partial class PhysicsParams : RefCounted
    {
        public float AirDensity { get; set; }
        public float AirViscosity { get; set; }
        public float DragScale { get; set; }
        public float LiftScale { get; set; }
        public float KineticFriction { get; set; }
        public float RollingFriction { get; set; }
        public float GrassViscosity { get; set; }
        public float CriticalAngle { get; set; }
        public Vector3 FloorNormal { get; set; }

        public PhysicsParams(
            float airDensity,
            float airViscosity,
            float dragScale,
            float liftScale,
            float kineticFriction,
            float rollingFriction,
            float grassViscosity,
            float criticalAngle,
            Vector3 floorNormal)
        {
            AirDensity = airDensity;
            AirViscosity = airViscosity;
            DragScale = dragScale;
            LiftScale = liftScale;
            KineticFriction = kineticFriction;
            RollingFriction = rollingFriction;
            GrassViscosity = grassViscosity;
            CriticalAngle = criticalAngle;
            FloorNormal = floorNormal;
        }
    }

    /// <summary>
    /// Bounce calculation result
    /// </summary>
    public partial class BounceResult : RefCounted
    {
        public Vector3 NewVelocity { get; set; }
        public Vector3 NewOmega { get; set; }
        public PhysicsEnums.BallState NewState { get; set; }

        public BounceResult(Vector3 vel, Vector3 omg, PhysicsEnums.BallState st)
        {
            NewVelocity = vel;
            NewOmega = omg;
            NewState = st;
        }
    }

    /// <summary>
    /// Calculate total forces acting on the ball
    /// </summary>
    public static Vector3 CalculateForces(
        Vector3 velocity,
        Vector3 omega,
        bool onGround,
        PhysicsParams parameters)
    {
        Vector3 gravity = new Vector3(0.0f, -9.81f * MASS, 0.0f);

        if (onGround)
        {
            return gravity + CalculateGroundForces(velocity, omega, parameters);
        }
        else
        {
            return gravity + CalculateAirForces(velocity, omega, parameters);
        }
    }

    /// <summary>
    /// Calculate ground friction and drag forces
    /// </summary>
    public static Vector3 CalculateGroundForces(
        Vector3 velocity,
        Vector3 omega,
        PhysicsParams parameters)
    {
        // Grass drag
        Vector3 grassDrag = velocity * (-6.0f * Mathf.Pi * RADIUS * parameters.GrassViscosity);
        grassDrag.Y = 0.0f;

        // Contact point velocity for friction calculation
        Vector3 contactVelocity = velocity + omega.Cross(-parameters.FloorNormal * RADIUS);
        Vector3 tangentVelocity = contactVelocity - parameters.FloorNormal * contactVelocity.Dot(parameters.FloorNormal);

        Vector3 friction = Vector3.Zero;
        float tangentVelMag = tangentVelocity.Length();

        // Debug: print every 60 frames (~1 second) when on ground
        bool shouldDebug = Engine.GetPhysicsFrames() % 60 == 0;

        if (tangentVelMag < 0.05f)
        {
            // Pure rolling - use rolling resistance from surface parameters
            Vector3 flatVelocity = velocity - parameters.FloorNormal * velocity.Dot(parameters.FloorNormal);
            Vector3 frictionDir = flatVelocity.Length() > 0.01f ? flatVelocity.Normalized() : Vector3.Zero;
            friction = frictionDir * (-parameters.RollingFriction * MASS * 9.81f);
            if (shouldDebug)
            {
                GD.Print($"  ROLLING: vel={velocity.Length():F2} m/s, spin={omega.Length() / 0.10472f:F0} rpm, c_rr={parameters.RollingFriction:F3}");
            }
        }
        else
        {
            // Slipping - use blended friction for smooth transition
            // For low-velocity rollout, reduce friction to allow more rollout
            float velocityMag = velocity.Length();
            float effectiveFriction;

            if (velocityMag < 15.0f)
            {
                // Blend between rolling resistance and kinetic friction based on velocity
                // At v=0: use rolling resistance, at v=15: use kinetic friction
                float blendFactor = Mathf.Clamp(velocityMag / 15.0f, 0.0f, 1.0f);
                effectiveFriction = Mathf.Lerp(parameters.RollingFriction, parameters.KineticFriction, blendFactor);
            }
            else
            {
                effectiveFriction = parameters.KineticFriction;
            }

            Vector3 slipDir = tangentVelocity.Normalized();
            friction = slipDir * (-effectiveFriction * MASS * 9.81f);
            if (shouldDebug)
            {
                GD.Print($"  SLIPPING: vel={velocityMag:F2} m/s, spin={omega.Length() / 0.10472f:F0} rpm, tangent_vel={tangentVelMag:F2}, μ_eff={effectiveFriction:F3}");
            }
        }

        return grassDrag + friction;
    }

    /// <summary>
    /// Calculate aerodynamic drag and Magnus forces
    /// </summary>
    public static Vector3 CalculateAirForces(
        Vector3 velocity,
        Vector3 omega,
        PhysicsParams parameters)
    {
        float speed = velocity.Length();
        if (speed < 0.5f)
            return Vector3.Zero;

        float spinRatio = omega.Length() * RADIUS / speed;
        float reynolds = parameters.AirDensity * speed * RADIUS * 2.0f / parameters.AirViscosity;

        float cd = Aerodynamics.GetCd(reynolds) * parameters.DragScale;
        float cl = Aerodynamics.GetCl(reynolds, spinRatio) * parameters.LiftScale;

        // Drag force (opposite to velocity)
        Vector3 drag = -0.5f * cd * parameters.AirDensity * CROSS_SECTION * velocity * speed;

        // Magnus force (perpendicular to velocity and spin axis)
        Vector3 magnus = Vector3.Zero;
        float omegaLen = omega.Length();
        if (omegaLen > 0.1f)
        {
            Vector3 omegaCrossVel = omega.Cross(velocity);
            magnus = 0.5f * cl * parameters.AirDensity * CROSS_SECTION * omegaCrossVel * speed / omegaLen;
        }

        return drag + magnus;
    }

    /// <summary>
    /// Calculate total torques acting on the ball
    /// </summary>
    public static Vector3 CalculateTorques(
        Vector3 velocity,
        Vector3 omega,
        bool onGround,
        PhysicsParams parameters)
    {
        if (onGround)
        {
            return CalculateGroundTorques(velocity, omega, parameters);
        }
        else
        {
            // Spin decay torque (exponential decay model)
            return -MOMENT_OF_INERTIA * omega / SPIN_DECAY_TAU;
        }
    }

    /// <summary>
    /// Calculate ground friction torques
    /// </summary>
    public static Vector3 CalculateGroundTorques(
        Vector3 velocity,
        Vector3 omega,
        PhysicsParams parameters)
    {
        Vector3 frictionTorque = Vector3.Zero;
        Vector3 grassTorque = -6.0f * Mathf.Pi * parameters.GrassViscosity * RADIUS * omega;

        // Calculate friction for torque
        Vector3 contactVelocity = velocity + omega.Cross(-parameters.FloorNormal * RADIUS);
        Vector3 tangentVelocity = contactVelocity - parameters.FloorNormal * contactVelocity.Dot(parameters.FloorNormal);

        Vector3 frictionForce = Vector3.Zero;
        float tangentVelMag = tangentVelocity.Length();

        if (tangentVelMag < 0.05f)
        {
            // Pure rolling - use rolling resistance from surface parameters
            Vector3 flatVelocity = velocity - parameters.FloorNormal * velocity.Dot(parameters.FloorNormal);
            Vector3 frictionDir = flatVelocity.Length() > 0.01f ? flatVelocity.Normalized() : Vector3.Zero;
            frictionForce = frictionDir * (-parameters.RollingFriction * MASS * 9.81f);
        }
        else
        {
            // Slipping - use blended friction (same as in CalculateGroundForces)
            float velocityMag = velocity.Length();
            float effectiveFriction;

            if (velocityMag < 15.0f)
            {
                float blendFactor = Mathf.Clamp(velocityMag / 15.0f, 0.0f, 1.0f);
                effectiveFriction = Mathf.Lerp(parameters.RollingFriction, parameters.KineticFriction, blendFactor);
            }
            else
            {
                effectiveFriction = parameters.KineticFriction;
            }

            Vector3 slipDir = tangentVelocity.Normalized();
            frictionForce = slipDir * (-effectiveFriction * MASS * 9.81f);
        }

        if (frictionForce.Length() > 0.001f)
        {
            frictionTorque = (-parameters.FloorNormal * RADIUS).Cross(frictionForce);
        }

        return frictionTorque + grassTorque;
    }

    /// <summary>
    /// Calculate bounce physics when ball impacts surface
    /// </summary>
    public static BounceResult CalculateBounce(
        Vector3 vel,
        Vector3 omega,
        Vector3 normal,
        PhysicsEnums.BallState currentState,
        PhysicsParams parameters)
    {
        PhysicsEnums.BallState newState = currentState == PhysicsEnums.BallState.Flight
            ? PhysicsEnums.BallState.Rollout
            : currentState;

        // Decompose velocity
        Vector3 velNormal = vel.Project(normal);
        float speedNormal = velNormal.Length();
        Vector3 velTangent = vel - velNormal;
        float speedTangent = velTangent.Length();

        // Decompose angular velocity
        Vector3 omegaNormal = omega.Project(normal);
        Vector3 omegaTangent = omega - omegaNormal;

        // Calculate impact angle from the SURFACE (not from the normal)
        // vel.AngleTo(normal) gives angle to normal, but Penner's critical angle is from surface
        float angleToNormal = vel.AngleTo(normal);
        float impactAngle = Mathf.Abs(angleToNormal - Mathf.Pi / 2.0f);

        // Use tangential spin magnitude for bounce calculation (backspin creates reverse velocity)
        float omegaTangentMagnitude = omegaTangent.Length();

        // Tangential retention based on spin
        float currentSpinRpm = omega.Length() / 0.10472f;

        float tangentialRetention;

        if (currentState == PhysicsEnums.BallState.Flight)
        {
            // First bounce from flight: Use spin-based penalty
            float spinFactor = Mathf.Clamp(1.0f - (currentSpinRpm / 8000.0f), 0.40f, 1.0f);
            tangentialRetention = 0.55f * spinFactor;
        }
        else
        {
            // Rollout bounces: Higher retention, no spin penalty
            // Use spin ratio to determine how much velocity to keep
            float ballSpeed = vel.Length();
            float spinRatio = ballSpeed > 0.1f ? (omega.Length() * RADIUS) / ballSpeed : 0.0f;

            // Low spin ratio = more rollout retention
            if (spinRatio < 0.20f)
            {
                tangentialRetention = Mathf.Lerp(0.85f, 0.70f, spinRatio / 0.20f);
            }
            else
            {
                tangentialRetention = 0.70f;
            }
        }

        if (newState == PhysicsEnums.BallState.Rollout)
        {
            GD.Print($"  Bounce: spin={currentSpinRpm:F0} rpm, retention={tangentialRetention:F3}");
        }

        // Calculate new tangential speed
        float newTangentSpeed;

        if (currentState == PhysicsEnums.BallState.Flight)
        {
            // First bounce from flight
            // The Penner model only works when impactAngle > criticalAngle (steep impacts with high spin)
            // For shallow-angle driver shots (impactAngle < criticalAngle), use simple retention model
            float impactAngleDeg = Mathf.RadToDeg(impactAngle);
            float criticalAngleDeg = Mathf.RadToDeg(parameters.CriticalAngle);

            if (impactAngle < parameters.CriticalAngle)
            {
                // Shallow impact (driver/wood): preserve tangential velocity with retention factor
                // This is appropriate for low-spin, low-trajectory shots that should roll out
                newTangentSpeed = speedTangent * tangentialRetention;
                GD.Print($"  Bounce: Shallow angle ({impactAngleDeg:F2}° < {criticalAngleDeg:F2}°) - using simple retention");
                GD.Print($"    speedTangent={speedTangent:F2} m/s, newTangentSpeed={newTangentSpeed:F2} m/s");
            }
            else
            {
                // Steep impact (wedge): Use Penner model - backspin creates reverse velocity
                newTangentSpeed = tangentialRetention * vel.Length() * Mathf.Sin(impactAngle - parameters.CriticalAngle) -
                    2.0f * RADIUS * omegaTangentMagnitude / 7.0f;
                GD.Print($"  Bounce: Steep angle ({impactAngleDeg:F2}° > {criticalAngleDeg:F2}°) - using Penner model");
                GD.Print($"    speedTangent={speedTangent:F2} m/s, newTangentSpeed={newTangentSpeed:F2} m/s");
            }
        }
        else
        {
            // Subsequent bounces during rollout: Simple friction factor (like libgolf)
            // Don't subtract spin - just apply friction to existing tangential velocity
            newTangentSpeed = speedTangent * tangentialRetention;
        }

        if (speedTangent < 0.01f || newTangentSpeed <= 0.0f)
        {
            velTangent = Vector3.Zero;
        }
        else
        {
            velTangent = velTangent.LimitLength(newTangentSpeed);
        }

        // Update tangential angular velocity
        if (currentState == PhysicsEnums.BallState.Flight)
        {
            // First bounce: compute omega from tangent speed
            float newOmegaTangent = newTangentSpeed / RADIUS;
            if (omegaTangent.Length() < 0.1f || newOmegaTangent <= 0.0f)
            {
                omegaTangent = Vector3.Zero;
            }
            else
            {
                omegaTangent = omegaTangent.LimitLength(newOmegaTangent);
            }
        }
        else
        {
            // Rollout: preserve existing spin, don't force it to match rolling velocity
            // The ball will slip initially, but forcing high spin kills rollout energy
            // Natural spin decay will occur through ground torques
            if (newTangentSpeed > 0.1f)
            {
                // Keep existing spin magnitude but ensure it's in the right direction
                float existingSpinMag = omegaTangent.Length();
                Vector3 tangentDir = velTangent.Length() > 0.01f ? velTangent.Normalized() : Vector3.Right;
                Vector3 rollingAxis = normal.Cross(tangentDir).Normalized();

                // Gradually adjust spin toward rolling direction, but don't increase magnitude
                if (existingSpinMag > 0.1f)
                {
                    omegaTangent = rollingAxis * existingSpinMag;
                }
                else
                {
                    omegaTangent = Vector3.Zero;
                }
            }
            else
            {
                omegaTangent = Vector3.Zero;
            }
        }

        // Coefficient of restitution (speed-dependent)
        float cor;
        if (currentState == PhysicsEnums.BallState.Flight)
        {
            // First bounce from flight: use full COR
            cor = GetCoefficientOfRestitution(speedNormal);
        }
        else
        {
            // Rollout bounces: kill small bounces aggressively to settle into roll
            if (speedNormal < 4.0f)
            {
                cor = 0.0f;  // Kill small rollout bounces completely
            }
            else
            {
                cor = GetCoefficientOfRestitution(speedNormal) * 0.5f;  // Halve COR for rollout
            }
        }

        if (newState == PhysicsEnums.BallState.Rollout && speedNormal > 0.5f)
        {
            GD.Print($"    speedNormal={speedNormal:F2} m/s, COR={cor:F3}, velNormal will be {speedNormal * cor:F2} m/s");
        }

        velNormal = velNormal * -cor;

        Vector3 newOmega = omegaNormal + omegaTangent;
        Vector3 newVelocity = velNormal + velTangent;

        return new BounceResult(newVelocity, newOmega, newState);
    }

    /// <summary>
    /// Get coefficient of restitution based on impact speed
    /// </summary>
    public static float GetCoefficientOfRestitution(float speedNormal)
    {
        if (speedNormal > 20.0f)
            return 0.25f;  // High speed impacts
        else if (speedNormal < 2.0f)
            return 0.0f;  // Kill very small bounces
        else
        {
            // Typical COR curve for golf ball on turf
            return 0.45f - 0.0100f * speedNormal + 0.0002f * speedNormal * speedNormal;
        }
    }
}
