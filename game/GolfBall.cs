using Godot;
using Godot.Collections;

/// <summary>
/// Golf ball game object with physics simulation.
/// Manages ball state, collisions, and delegates physics calculations
/// to the BallPhysics class.
/// </summary>
public partial class GolfBall : CharacterBody3D
{
    // Signals
    [Signal]
    public delegate void BallAtRestEventHandler();

    // State
    public const float START_HEIGHT = 0.02f;
    public PhysicsEnums.BallState State { get; set; } = PhysicsEnums.BallState.Rest;
    public Vector3 Omega { get; set; } = Vector3.Zero;  // Angular velocity (rad/s)
    public bool OnGround { get; set; } = false;
    public Vector3 FloorNormal { get; set; } = Vector3.Up;

    // Surface parameters
    public PhysicsEnums.SurfaceType SurfaceType { get; set; } = PhysicsEnums.SurfaceType.Fairway;
    private float _kineticFriction = 0.42f;
    private float _rollingFriction = 0.18f;
    private float _grassViscosity = 0.0020f;
    private float _criticalAngle = 0.30f;  // radians

    // Environment
    private float _airDensity;
    private float _airViscosity;
    private float _dragScale = 1.0f;
    private float _liftScale = 1.0f;

    // Shot tracking
    public Vector3 ShotStartPos { get; set; } = Vector3.Zero;
    public Vector3 ShotDirection { get; set; } = new Vector3(1.0f, 0.0f, 0.0f);  // Normalized horizontal direction
    public float LaunchSpinRpm { get; set; } = 0.0f;  // Stored for bounce calculations
    public float RolloutImpactSpinRpm { get; set; } = 0.0f;  // Spin when first landing (for friction calculation)

    public override void _Ready()
    {
        ConnectSettings();
        UpdateEnvironment();
        ApplySurfaceParams();
    }

    private void ConnectSettings()
    {
        var settings = GetNode<GlobalSettings>("/root/GlobalSettings").RangeSettings;
        settings.Temperature.SettingChanged += OnEnvironmentChanged;
        settings.Altitude.SettingChanged += OnEnvironmentChanged;
        settings.RangeUnits.SettingChanged += OnEnvironmentChanged;
        settings.DragScale.SettingChanged += OnDragScaleChanged;
        settings.LiftScale.SettingChanged += OnLiftScaleChanged;
        _dragScale = (float)settings.DragScale.Value;
        _liftScale = (float)settings.LiftScale.Value;
    }

    private void UpdateEnvironment()
    {
        var settings = GetNode<GlobalSettings>("/root/GlobalSettings").RangeSettings;
        var units = (PhysicsEnums.Units)(int)settings.RangeUnits.Value;
        _airDensity = Aerodynamics.GetAirDensity(
            (float)settings.Altitude.Value,
            (float)settings.Temperature.Value,
            units
        );
        _airViscosity = Aerodynamics.GetDynamicViscosity(
            (float)settings.Temperature.Value,
            units
        );
    }

    private void OnEnvironmentChanged(Variant value)
    {
        UpdateEnvironment();
    }

    private void OnDragScaleChanged(Variant value)
    {
        _dragScale = (float)GetNode<GlobalSettings>("/root/GlobalSettings").RangeSettings.DragScale.Value;
    }

    private void OnLiftScaleChanged(Variant value)
    {
        _liftScale = (float)GetNode<GlobalSettings>("/root/GlobalSettings").RangeSettings.LiftScale.Value;
    }

    /// <summary>
    /// Set the surface type and update friction parameters
    /// </summary>
    public void SetSurface(PhysicsEnums.SurfaceType surface)
    {
        SurfaceType = surface;
        ApplySurfaceParams();
    }

    private void ApplySurfaceParams()
    {
        var parameters = Surface.GetParams(SurfaceType);
        _kineticFriction = (float)parameters["u_k"];
        _rollingFriction = (float)parameters["u_kr"];
        _grassViscosity = (float)parameters["nu_g"];
        _criticalAngle = (float)parameters["theta_c"];
    }

    /// <summary>
    /// Get downrange distance in yards (along initial shot direction)
    /// </summary>
    public float GetDownrangeYards()
    {
        Vector3 delta = Position - ShotStartPos;
        float meters = delta.Dot(ShotDirection);
        return meters * 1.09361f;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (State == PhysicsEnums.BallState.Rest)
            return;

        bool wasOnGround = OnGround;
        Vector3 prevVelocity = Velocity;

        // Calculate forces and torques using BallPhysics
        var parameters = CreatePhysicsParams();
        Vector3 totalForce = BallPhysics.CalculateForces(Velocity, Omega, wasOnGround, parameters);
        Vector3 totalTorque = BallPhysics.CalculateTorques(Velocity, Omega, wasOnGround, parameters);

        // Update velocity and angular velocity
        Velocity += (totalForce / BallPhysics.MASS) * (float)delta;
        Omega += (totalTorque / BallPhysics.MOMENT_OF_INERTIA) * (float)delta;

        // Safety bounds check
        if (CheckOutOfBounds())
            return;

        // Move and handle collisions
        var collision = MoveAndCollide(Velocity * (float)delta);
        HandleCollision(collision, wasOnGround, prevVelocity);

        // Check for rest
        if (Velocity.Length() < 0.1f && State != PhysicsEnums.BallState.Rest)
        {
            EnterRestState();
        }
    }

    private BallPhysics.PhysicsParams CreatePhysicsParams()
    {
        return new BallPhysics.PhysicsParams(
            _airDensity,
            _airViscosity,
            _dragScale,
            _liftScale,
            _kineticFriction,
            _rollingFriction,
            _grassViscosity,
            _criticalAngle,
            FloorNormal,
            RolloutImpactSpinRpm
        );
    }

    private bool CheckOutOfBounds()
    {
        if (Mathf.Abs(Position.X) > 1000.0f || Mathf.Abs(Position.Z) > 1000.0f)
        {
            GD.Print($"WARNING: Ball out of bounds at: {Position}");
            EnterRestState();
            return true;
        }

        if (Position.Y < -0.5f)
        {
            GD.Print($"WARNING: Ball fell through ground at: {Position}");
            var pos = Position;
            pos.Y = 0.0f;
            Position = pos;
            EnterRestState();
            return true;
        }

        return false;
    }

    private void HandleCollision(KinematicCollision3D collision, bool wasOnGround, Vector3 prevVelocity)
    {
        if (collision != null)
        {
            Vector3 normal = collision.GetNormal();

            if (IsGroundNormal(normal))
            {
                FloorNormal = normal;
                bool isLanding = (State == PhysicsEnums.BallState.Flight) || prevVelocity.Y < -0.5f;

                if (isLanding)
                {
                    if (State == PhysicsEnums.BallState.Flight)
                    {
                        PrintImpactDebug();
                        // Capture impact spin for friction calculation during rollout
                        // This preserves the "bite" effect even as spin decays
                        RolloutImpactSpinRpm = Omega.Length() / 0.10472f;
                    }

                    var parameters = CreatePhysicsParams();
                    var bounceResult = BallPhysics.CalculateBounce(Velocity, Omega, normal, State, parameters);
                    Velocity = bounceResult.NewVelocity;
                    Omega = bounceResult.NewOmega;
                    State = bounceResult.NewState;

                    GD.Print($"  Velocity after bounce: {Velocity} ({Velocity.Length():F2} m/s)");

                    // If the bounce resulted in very low vertical velocity (damped bounce),
                    // keep the ball on the ground instead of letting it bounce again
                    if (Mathf.Abs(Velocity.Y) < 0.5f && State == PhysicsEnums.BallState.Rollout)
                    {
                        OnGround = true;
                        var vel = Velocity;
                        vel.Y = 0;
                        Velocity = vel;
                        GD.Print($"  -> Ball grounded, continuing roll at {vel.Length():F2} m/s");
                    }
                    else
                    {
                        OnGround = false;
                    }
                }
                else
                {
                    OnGround = true;
                    if (Velocity.Y < 0)
                    {
                        var vel = Velocity;
                        vel.Y = 0;
                        Velocity = vel;
                    }
                }
            }
            else
            {
                // Wall collision - damped reflection
                OnGround = false;
                FloorNormal = Vector3.Up;
                Velocity = Velocity.Bounce(normal) * 0.30f;
            }
        }
        else
        {
            // No collision - check rolling continuity
            if (State != PhysicsEnums.BallState.Flight && wasOnGround && Position.Y < 0.02f && Velocity.Y <= 0.0f)
            {
                OnGround = true;
            }
            else
            {
                OnGround = false;
                FloorNormal = Vector3.Up;
            }
        }
    }

    private bool IsGroundNormal(Vector3 normal)
    {
        return normal.Y > 0.7f;
    }

    private void PrintImpactDebug()
    {
        GD.Print($"FIRST IMPACT at pos: {Position}, downrange: {GetDownrangeYards():F2} yds");
        GD.Print($"  Velocity at impact: {Velocity} ({Velocity.Length():F2} m/s)");
        GD.Print($"  Spin at impact: {Omega} ({Omega.Length() / 0.10472f:F0} rpm)");
        GD.Print($"  Normal: {FloorNormal}");
    }

    private void EnterRestState()
    {
        State = PhysicsEnums.BallState.Rest;
        Velocity = Vector3.Zero;
        Omega = Vector3.Zero;
        EmitSignal(SignalName.BallAtRest);
    }

    /// <summary>
    /// Reset ball to starting position
    /// </summary>
    public void Reset()
    {
        Position = new Vector3(0.0f, START_HEIGHT, 0.0f);
        Velocity = Vector3.Zero;
        Omega = Vector3.Zero;
        LaunchSpinRpm = 0.0f;
        RolloutImpactSpinRpm = 0.0f;
        State = PhysicsEnums.BallState.Rest;
        OnGround = false;
    }

    /// <summary>
    /// Hit ball with default test data
    /// </summary>
    public void Hit()
    {
        var data = new Dictionary
        {
            { "Speed", 100.0f },
            { "VLA", 22.0f },
            { "HLA", -3.1f },
            { "TotalSpin", 6000.0f },
            { "SpinAxis", 3.5f }
        };
        HitFromData(data);
    }

    /// <summary>
    /// Hit ball with provided launch data
    /// </summary>
    public void HitFromData(Dictionary data)
    {
        float speedMps = (float)(data.ContainsKey("Speed") ? data["Speed"] : 0.0f) * 0.44704f;  // mph to m/s
        float vlaDeg = (float)(data.ContainsKey("VLA") ? data["VLA"] : 0.0f);
        float hlaDeg = (float)(data.ContainsKey("HLA") ? data["HLA"] : 0.0f);

        // Parse spin data (handle both backspin/sidespin and totalspin/axis formats)
        var spinData = ParseSpinData(data);
        float totalSpin = (float)spinData["total"];
        float spinAxis = (float)spinData["axis"];

        // Set state
        State = PhysicsEnums.BallState.Flight;
        OnGround = false;
        RolloutImpactSpinRpm = 0.0f;
        Position = new Vector3(0.0f, START_HEIGHT, 0.0f);

        // Calculate initial velocity
        Velocity = new Vector3(speedMps, 0, 0)
            .Rotated(Vector3.Forward, Mathf.DegToRad(-vlaDeg))
            .Rotated(Vector3.Up, Mathf.DegToRad(-hlaDeg));

        // Set shot tracking
        ShotStartPos = Position;
        Vector3 flatVelocity = new Vector3(Velocity.X, 0.0f, Velocity.Z);
        ShotDirection = flatVelocity.Length() > 0.001f ? flatVelocity.Normalized() : Vector3.Right;

        // Set angular velocity
        Omega = new Vector3(0.0f, 0.0f, totalSpin * 0.10472f)
            .Rotated(Vector3.Right, Mathf.DegToRad(spinAxis));
        LaunchSpinRpm = totalSpin;

        PrintLaunchDebug(data, speedMps, vlaDeg, hlaDeg, totalSpin, spinAxis);
    }

    private Dictionary ParseSpinData(Dictionary data)
    {
        bool hasBackspin = data.ContainsKey("BackSpin");
        bool hasSidespin = data.ContainsKey("SideSpin");
        bool hasTotal = data.ContainsKey("TotalSpin");
        bool hasAxis = data.ContainsKey("SpinAxis");

        float backspin = (float)(data.ContainsKey("BackSpin") ? data["BackSpin"] : 0.0f);
        float sidespin = (float)(data.ContainsKey("SideSpin") ? data["SideSpin"] : 0.0f);
        float totalSpin = (float)(data.ContainsKey("TotalSpin") ? data["TotalSpin"] : 0.0f);
        float spinAxis = (float)(data.ContainsKey("SpinAxis") ? data["SpinAxis"] : 0.0f);

        // Calculate missing values
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

    private void PrintLaunchDebug(Dictionary data, float speedMps, float vla, float hla, float spin, float axis)
    {
        GD.Print("=== SHOT DEBUG ===");
        GD.Print($"Speed: {(data.ContainsKey("Speed") ? data["Speed"] : 0.0f):F2} mph ({speedMps:F2} m/s)");
        GD.Print($"VLA: {vla:F2}°, HLA: {hla:F2}°");
        GD.Print($"Spin: {spin:F0} rpm, Axis: {axis:F2}°");
        GD.Print($"drag_cf: {_dragScale:F2}, lift_cf: {_liftScale:F2}");
        GD.Print($"Air density: {_airDensity:F4} kg/m³");
        GD.Print($"Dynamic viscosity: {_airViscosity:F11}");

        float ReInitial = _airDensity * speedMps * BallPhysics.RADIUS * 2.0f / _airViscosity;
        float spinRatio = speedMps > 0.1f ? (spin * 0.10472f) * BallPhysics.RADIUS / speedMps : 0.0f;
        float ClInitial = Aerodynamics.GetCl(ReInitial, spinRatio);
        GD.Print($"Reynolds number: {ReInitial:F0}");
        GD.Print($"Spin ratio: {spinRatio:F3}");
        GD.Print($"Cl (before scale): {ClInitial:F3}, after: {ClInitial * _liftScale:F3}");
        GD.Print($"Initial velocity: {Velocity}");
        GD.Print($"Initial omega: {Omega} ({Omega.Length() / 0.10472f:F0} rpm)");
        GD.Print($"Shot direction: {ShotDirection}");
        GD.Print("===================");
    }
}
