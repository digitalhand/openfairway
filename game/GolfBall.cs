using System;
using Godot;
using Godot.Collections;

/// <summary>
/// Golf ball game object with physics simulation.
/// Manages ball state, collisions, and delegates physics calculations
/// to the BallPhysics class.
/// </summary>

public partial class GolfBall : CharacterBody3D // Player
{
	// Keep a small collision recovery distance so the ball can settle into terrain lows.
	private const float COLLISION_SAFE_MARGIN = 0.0005f;
	private const float BELOW_GROUND_RECOVERY_Y = -0.5f;
	private const float FALLTHROUGH_FAILSAFE_Y = -5.0f;
	private const float GROUND_SNAP_OFFSET = 0.001f;
	private const float GROUND_RAYCAST_UP = 2.0f;
	private const float GROUND_RAYCAST_DOWN = 8.0f;
	private const float GROUND_PROBE_DISTANCE = 0.08f;
	private const float PHYSICS_SUBSTEP_DT = BallPhysics.SIMULATION_DT;
	private const int MAX_SUBSTEPS_PER_FRAME = 12;

	// Signals
	[Signal]
	public delegate void BallAtRestEventHandler();
	[Signal]
	public delegate void BallLandedEventHandler();

	// Physics instances
	private readonly BallPhysics _ballPhysics = new();
	private readonly Aerodynamics _aerodynamics = new();
	private readonly PhysicsParamsFactory _physicsParamsFactory = new();
	private readonly ShotSetup _shotSetup = new();

	// State — ball center sits one radius above ground so it rests on the surface
	public const float TEE_HEIGHT = BallPhysics.RADIUS;
	public static readonly Vector3 START_POSITION = new Vector3(1.0f, TEE_HEIGHT, 0.0f);
	public PhysicsEnums.BallState State { get; set; } = PhysicsEnums.BallState.Rest;
	public Vector3 Omega { get; set; } = Vector3.Zero;  // Angular velocity (rad/s)
	public bool OnGround { get; set; } = false;
	public Vector3 FloorNormal { get; set; } = Vector3.Up;

	// Settings reference for signal cleanup
	private GameSettings _gameSettings;
	private float _substepAccumulator = 0.0f;

	// Terrain3D data reference for height queries (cached on _Ready)
	private GodotObject _terrainData;

	// Cached raycast exclude array (avoids per-frame allocation)
	private Array<Rid> _raycastExclude;

	public PhysicsEnums.SurfaceType SurfaceType { get; private set; } = PhysicsEnums.SurfaceType.Fairway;
	public BallPhysicsProfile BallProfile { get; set; } = new();
	public Func<Node, Vector3, PhysicsEnums.SurfaceType> ResolveLieSurface { get; set; }
	public Func<string> DescribeLieSurfaceResolution { get; set; }

	// Environment
	private float _airDensity;
	private float _airViscosity;
	private float _dragScale = 1.0f;
	private float _liftScale = 1.0f;

	// Shot tracking
	public Vector3 ShotStartPos { get; set; } = Vector3.Zero;
	public Vector3 ShotDirection { get; set; } = new Vector3(1.0f, 0.0f, 0.0f);  // Normalized horizontal direction
	public float AimYawOffsetDeg { get; set; } = 0.0f;  // Camera/world rotation offset applied at launch
	public float LaunchAngleDeg { get; private set; } = 0.0f;
	public float LaunchSpinRpm { get; set; } = 0.0f;  // Stored for bounce calculations
	public float RolloutImpactSpinRpm { get; set; } = 0.0f;  // Spin when first landing (for friction calculation)

	public override void _Ready()
	{
		CacheTerrainData();
		ConnectSettings();
		UpdateEnvironment();
	}

	private Array<Rid> GetRaycastExclude()
	{
		_raycastExclude ??= new Array<Rid> { GetRid() };
		return _raycastExclude;
	}

	private void CacheTerrainData()
	{
		var terrain = GetTree().Root.FindChild("Terrain3D", true, false);
		if (terrain == null)
			return;

		var data = terrain.Get("data");
		if (data.Obj is GodotObject obj)
			_terrainData = obj;
	}

	private void ConnectSettings()
	{
		var globalSettings = GetNodeOrNull<GlobalSettings>("/root/GlobalSettings");
		if (globalSettings?.GameSettings == null)
		{
			GD.PushError($"{nameof(GolfBall)}: GlobalSettings.GameSettings not found at /root/GlobalSettings. Physics environment updates will be disabled.");
			return;
		}

		_gameSettings = globalSettings.GameSettings;
		_gameSettings.Temperature.SettingChanged += OnEnvironmentChanged;
		_gameSettings.Altitude.SettingChanged += OnEnvironmentChanged;
		_gameSettings.GameUnits.SettingChanged += OnEnvironmentChanged;
		_gameSettings.DragScale.SettingChanged += OnDragScaleChanged;
		_gameSettings.LiftScale.SettingChanged += OnLiftScaleChanged;
		_dragScale = (float)_gameSettings.DragScale.Value;
		_liftScale = (float)_gameSettings.LiftScale.Value;
	}

	public override void _ExitTree()
	{
		if (_gameSettings != null)
		{
			_gameSettings.Temperature.SettingChanged -= OnEnvironmentChanged;
			_gameSettings.Altitude.SettingChanged -= OnEnvironmentChanged;
			_gameSettings.GameUnits.SettingChanged -= OnEnvironmentChanged;
			_gameSettings.DragScale.SettingChanged -= OnDragScaleChanged;
			_gameSettings.LiftScale.SettingChanged -= OnLiftScaleChanged;
		}
	}

	private void UpdateEnvironment()
	{
		if (_gameSettings == null)
			return;

		var units = (PhysicsEnums.Units)(int)_gameSettings.GameUnits.Value;
		_airDensity = _aerodynamics.GetAirDensity(
			(float)_gameSettings.Altitude.Value,
			(float)_gameSettings.Temperature.Value,
			units
		);
		_airViscosity = _aerodynamics.GetDynamicViscosity(
			(float)_gameSettings.Temperature.Value,
			units
		);
	}

	private void OnEnvironmentChanged(Variant value)
	{
		UpdateEnvironment();
	}

	private void OnDragScaleChanged(Variant value)
	{
		if (_gameSettings != null)
			_dragScale = (float)_gameSettings.DragScale.Value;
	}

	private void OnLiftScaleChanged(Variant value)
	{
		if (_gameSettings != null)
			_liftScale = (float)_gameSettings.LiftScale.Value;
	}

	public void SetLieSurface(PhysicsEnums.SurfaceType surface)
	{
		if (SurfaceType == surface)
			return;

		SurfaceType = surface;
		PhysicsLogger.Info($"[Surface] Active={SurfaceType}");
	}

	private void UpdateLieSurfaceFromContact(Node collider, Vector3 worldPoint)
	{
		if (ResolveLieSurface == null)
			return;

		SetLieSurface(ResolveLieSurface(collider, worldPoint));
	}

	private void RefreshLieSurfaceFromGroundProbe()
	{
		if (TryProbeGround(out _, out Node groundCollider, out Vector3 groundPoint))
			UpdateLieSurfaceFromContact(groundCollider, groundPoint);
	}

	private void LogLandingSurfaceReaction(PhysicsParams parameters, Vector3 velocity, Vector3 omega, Vector3 normal)
	{
		float speed = velocity.Length();
		float impactSpinRpm = omega.Length() / ShotSetup.RAD_PER_RPM;
		float angleToNormal = velocity.AngleTo(normal);
		float impactAngleDeg = Mathf.RadToDeg(Mathf.Abs(angleToNormal - Mathf.Pi / 2.0f));
		float criticalAngleDeg = Mathf.RadToDeg(parameters.CriticalAngle);
		float thetaBoostDeg = Mathf.RadToDeg(parameters.SpinbackThetaBoostMax);
		string resolutionSource = DescribeLieSurfaceResolution?.Invoke();
		string sourceSegment = string.IsNullOrWhiteSpace(resolutionSource)
			? string.Empty
			: $" {resolutionSource}";

		PhysicsLogger.Info(
			$"[LandingSurface] surface={parameters.SurfaceType} speed={speed:F2}m/s impact_spin={impactSpinRpm:F0}rpm " +
			$"rollout_spin={RolloutImpactSpinRpm:F0}rpm angle={impactAngleDeg:F1}deg theta_c={criticalAngleDeg:F1}deg " +
			$"spin_scale={parameters.SpinbackResponseScale:F2} theta_boost={thetaBoostDeg:F1}deg{sourceSegment}"
		);
	}

	/// <summary>
	/// Get downrange distance in meters (along initial shot direction)
	/// </summary>
	public float GetDownrangeMeters()
	{
		Vector3 delta = Position - ShotStartPos;
		return delta.Dot(ShotDirection);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (State == PhysicsEnums.BallState.Rest)
		{
			_substepAccumulator = 0.0f;
			return;
		}

		_substepAccumulator += (float)delta;
		int substeps = 0;
		while (_substepAccumulator >= PHYSICS_SUBSTEP_DT && substeps < MAX_SUBSTEPS_PER_FRAME)
		{
			if (!StepPhysics(PHYSICS_SUBSTEP_DT))
			{
				_substepAccumulator = 0.0f;
				return;
			}

			_substepAccumulator -= PHYSICS_SUBSTEP_DT;
			substeps++;

			if (State == PhysicsEnums.BallState.Rest)
			{
				_substepAccumulator = 0.0f;
				return;
			}
		}

		// Prevent runaway catch-up loops under stalls while preserving continuity.
		if (substeps == MAX_SUBSTEPS_PER_FRAME && _substepAccumulator > PHYSICS_SUBSTEP_DT)
		{
			_substepAccumulator = PHYSICS_SUBSTEP_DT;
		}
	}

	private bool StepPhysics(float dt)
	{
		bool wasOnGround = OnGround;
		Vector3 prevVelocity = Velocity;

		var parameters = CreatePhysicsParams();
		Vector3 velocity = Velocity;
		Vector3 omega = Omega;
		_ballPhysics.IntegrateStep(ref velocity, ref omega, wasOnGround, parameters, dt);
		Velocity = velocity;
		Omega = omega;

		if (CheckOutOfBounds())
			return false;

		var collision = MoveAndCollide(
			Velocity * dt,
			testOnly: false,
			safeMargin: COLLISION_SAFE_MARGIN
		);
		HandleCollision(collision, wasOnGround, prevVelocity);

		if (Velocity.Length() < 0.1f && State != PhysicsEnums.BallState.Rest)
		{
			EnterRestState();
		}

		return true;
	}

	private PhysicsParams CreatePhysicsParams()
	{
		return _physicsParamsFactory.Create(
			_airDensity,
			_airViscosity,
			_dragScale,
			_liftScale,
			SurfaceType,
			FloorNormal,
			rolloutImpactSpin: RolloutImpactSpinRpm,
			ballProfile: BallProfile,
			initialLaunchAngleDeg: LaunchAngleDeg
		).ToPhysicsParams();
	}

	private bool CheckOutOfBounds()
	{
		if (Mathf.Abs(Position.X) > 1000.0f || Mathf.Abs(Position.Z) > 1000.0f)
		{
			PhysicsLogger.Info($"WARNING: Ball out of bounds at: {Position}");
			EnterRestState();
			return true;
		}

		if (GlobalPosition.Y < BELOW_GROUND_RECOVERY_Y)
		{
			if (TryRecoverToGround())
				return false;

			if (GlobalPosition.Y > FALLTHROUGH_FAILSAFE_Y)
				return false;

			// This of course depends on const values preset. In some cases, these should be higher. 
			// Example ball falling in a course where canyon is very high elevation?
			PhysicsLogger.Info($"WARNING: Ball fell through ground at: {GlobalPosition}");
			EnterRestState();
			return true;
		}

		return false;
	}

	private bool TryRecoverToGround()
	{
		var world = GetWorld3D();
		if (world == null)
			return false;

		Vector3 rayStart = GlobalPosition + Vector3.Up * GROUND_RAYCAST_UP;
		Vector3 rayEnd = GlobalPosition + Vector3.Down * GROUND_RAYCAST_DOWN;

		var query = PhysicsRayQueryParameters3D.Create(rayStart, rayEnd);
		query.CollideWithAreas = false;
		query.CollideWithBodies = true;
		query.Exclude = GetRaycastExclude();

		var hit = world.DirectSpaceState.IntersectRay(query);
		if (hit.Count == 0)
			return false;

		Vector3 hitPosition = (Vector3)hit["position"];
		Vector3 hitNormal = ((Vector3)hit["normal"]).Normalized();
		Node hitCollider = hit.ContainsKey("collider") && hit["collider"].Obj is Node collider
			? collider
			: null;
		if (hitNormal.LengthSquared() < 0.000001f)
			hitNormal = Vector3.Up;

		GlobalPosition = hitPosition + hitNormal * (BallPhysics.RADIUS + GROUND_SNAP_OFFSET);
		FloorNormal = hitNormal;
		Velocity = RemoveVelocityAlongNormal(Velocity, hitNormal, removeBothDirections: false);
		OnGround = true;
		UpdateLieSurfaceFromContact(hitCollider, hitPosition);

		if (State == PhysicsEnums.BallState.Flight)
		{
			State = PhysicsEnums.BallState.Rollout;
			EmitSignal(SignalName.BallLanded);
		}

		PhysicsLogger.Verbose($"Recovered ball-to-ground at {GlobalPosition} (normal: {hitNormal})");
		return true;
	}

	private bool TryProbeGround(out Vector3 groundNormal, out Node groundCollider, out Vector3 groundPoint)
	{
		groundNormal = Vector3.Up;
		groundCollider = null;
		groundPoint = GlobalPosition;

		var world = GetWorld3D();
		if (world == null)
			return false;

		Vector3 rayStart = GlobalPosition + Vector3.Up * 0.05f;
		Vector3 rayEnd = GlobalPosition + Vector3.Down * (BallPhysics.RADIUS + GROUND_PROBE_DISTANCE);

		var query = PhysicsRayQueryParameters3D.Create(rayStart, rayEnd);
		query.CollideWithAreas = false;
		query.CollideWithBodies = true;
		query.Exclude = GetRaycastExclude();

		var hit = world.DirectSpaceState.IntersectRay(query);
		if (hit.Count == 0)
			return false;

		groundPoint = (Vector3)hit["position"];
		groundNormal = ((Vector3)hit["normal"]).Normalized();
		groundCollider = hit.ContainsKey("collider") && hit["collider"].Obj is Node collider
			? collider
			: null;
		if (groundNormal.LengthSquared() < 0.000001f)
			groundNormal = Vector3.Up;

		return true;
	}

	private void HandleCollision(KinematicCollision3D collision, bool wasOnGround, Vector3 prevVelocity)
	{
		if (collision != null)
		{
			Vector3 normal = collision.GetNormal();
			Node hitCollider = collision.GetCollider() as Node;
			Vector3 hitPosition = collision.GetPosition();

			if (IsGroundNormal(normal))
			{
				FloorNormal = normal;
				UpdateLieSurfaceFromContact(hitCollider, hitPosition);
				float prevNormalVelocity = prevVelocity.Dot(normal);
				bool landedFromFlight = State == PhysicsEnums.BallState.Flight;
				bool isLanding = landedFromFlight || prevNormalVelocity < -0.5f;

				if (isLanding)
				{
					if (landedFromFlight)
					{
						PrintImpactDebug();
						// Capture impact spin for friction calculation during rollout
						// This preserves the "bite" effect even as spin decays
						RolloutImpactSpinRpm = Omega.Length() / ShotSetup.RAD_PER_RPM;
					}

					var parameters = CreatePhysicsParams();
					if (landedFromFlight)
						LogLandingSurfaceReaction(parameters, Velocity, Omega, normal);
					var bounceResult = _ballPhysics.CalculateBounce(Velocity, Omega, normal, State, parameters);
					Velocity = bounceResult.NewVelocity;
					Omega = bounceResult.NewOmega;
					State = bounceResult.NewState;
					if (landedFromFlight && State == PhysicsEnums.BallState.Rollout)
						EmitSignal(SignalName.BallLanded);

					PhysicsLogger.Verbose($"  Velocity after bounce: {Velocity} ({Velocity.Length():F2} m/s)");

					// If the bounce resulted in very low vertical velocity (damped bounce),
					// keep the ball on the ground instead of letting it bounce again
					float normalVelocity = Velocity.Dot(normal);
					if (Mathf.Abs(normalVelocity) < 0.5f && State == PhysicsEnums.BallState.Rollout)
					{
						OnGround = true;
						Velocity = RemoveVelocityAlongNormal(Velocity, normal, removeBothDirections: true);
						PhysicsLogger.Verbose($"  -> Ball grounded, continuing roll at {Velocity.Length():F2} m/s");
					}
					else
					{
						OnGround = false;
					}
				}
				else
				{
					OnGround = true;
					Velocity = RemoveVelocityAlongNormal(Velocity, normal, removeBothDirections: false);
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
			// No collision - only stay grounded if terrain is still directly beneath the ball.
			if (State != PhysicsEnums.BallState.Flight &&
				wasOnGround &&
				TryProbeGround(out Vector3 groundNormal, out Node groundCollider, out Vector3 groundPoint))
			{
				OnGround = true;
				FloorNormal = groundNormal;
				UpdateLieSurfaceFromContact(groundCollider, groundPoint);
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

	private static Vector3 RemoveVelocityAlongNormal(Vector3 velocity, Vector3 normal, bool removeBothDirections)
	{
		Vector3 floorNormal = normal.LengthSquared() > 0.000001f ? normal.Normalized() : Vector3.Up;
		float normalComponent = velocity.Dot(floorNormal);

		if (!removeBothDirections && normalComponent >= 0.0f)
			return velocity;

		return velocity - floorNormal * normalComponent;
	}

	private void PrintImpactDebug()
	{
		PhysicsLogger.Info($"FIRST IMPACT at pos: {Position}, downrange: {GetDownrangeMeters() * ShotSetup.YARDS_PER_METER:F2} yds");
		PhysicsLogger.Info($"  Velocity at impact: {Velocity} ({Velocity.Length():F2} m/s)");
		PhysicsLogger.Info($"  Spin at impact: {Omega} ({Omega.Length() / ShotSetup.RAD_PER_RPM:F0} rpm)");
		PhysicsLogger.Info($"  Normal: {FloorNormal}");
	}

	private void EnterRestState()
	{
		State = PhysicsEnums.BallState.Rest;
		Velocity = Vector3.Zero;
		Omega = Vector3.Zero;
		_substepAccumulator = 0.0f;
		EmitSignal(SignalName.BallAtRest);
	}

	/// <summary>
	/// Query terrain height at the ball's current X,Z and place it
    /// TEE_HEIGHT metres above the surface.
    /// Uses Terrain3D data API first (works immediately, no physics frame needed),
    /// falls back to physics raycast.
    /// </summary>
    public void SnapToGround()
    {
        // Lazy-cache: Terrain3D data may not be ready during _Ready(),
        // so retry on first actual use.
        if (_terrainData == null)
            CacheTerrainData();

        // Terrain3D data API — queries the heightmap directly, no physics step required.
        if (_terrainData != null)
        {
            float height = (float)_terrainData.Call("get_height", GlobalPosition);
            if (!float.IsNaN(height))
            {
                GlobalPosition = new Vector3(GlobalPosition.X, height + TEE_HEIGHT, GlobalPosition.Z);
                return;
            }
        }

        // Fallback: physics raycast (requires collision shapes to be ready).
        var world = GetWorld3D();
        if (world == null)
            return;

        Vector3 rayStart = GlobalPosition + Vector3.Up * 50.0f;
        Vector3 rayEnd = GlobalPosition + Vector3.Down * 50.0f;

        var query = PhysicsRayQueryParameters3D.Create(rayStart, rayEnd);
        query.CollideWithAreas = false;
        query.CollideWithBodies = true;
        query.Exclude = GetRaycastExclude();

        var hit = world.DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
            return;

        Vector3 hitPosition = (Vector3)hit["position"];
        GlobalPosition = new Vector3(GlobalPosition.X, hitPosition.Y + TEE_HEIGHT, GlobalPosition.Z);
    }

    /// <summary>
    /// Reset ball to starting position
    /// </summary>
    public void Reset()
    {
        Position = START_POSITION;
        SnapToGround();
        Velocity = Vector3.Zero;
        Omega = Vector3.Zero;
        _substepAccumulator = 0.0f;
        AimYawOffsetDeg = 0.0f;
        LaunchSpinRpm = 0.0f;
        RolloutImpactSpinRpm = 0.0f;
        RefreshLieSurfaceFromGroundProbe();
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
        float speedMph = (float)(data.ContainsKey("Speed") ? data["Speed"] : 0.0f);
        float vlaDeg = (float)(data.ContainsKey("VLA") ? data["VLA"] : 0.0f);
        float hlaDeg = (float)(data.ContainsKey("HLA") ? data["HLA"] : 0.0f);

        // Parse spin data (handle both backspin/sidespin and totalspin/axis formats)
        var spinData = _shotSetup.ParseSpin(data);
        float totalSpin = (float)spinData["total"];
        float spinAxis = (float)spinData["axis"];

        // Build launch vectors from monitor data
        var launch = _shotSetup.BuildLaunchVectors(speedMph, vlaDeg, hlaDeg, totalSpin, spinAxis);
        Vector3 launchVelocity = (Vector3)launch["velocity"];
        Vector3 launchOmega = (Vector3)launch["omega"];
        Vector3 launchDirection = (Vector3)launch["shot_direction"];

        // Apply camera/world yaw without mutating launch monitor data.
        if (Mathf.Abs(AimYawOffsetDeg) > 0.0001f)
        {
            float aimYawRad = Mathf.DegToRad(AimYawOffsetDeg);
            launchVelocity = launchVelocity.Rotated(Vector3.Up, aimYawRad);
            launchOmega = launchOmega.Rotated(Vector3.Up, aimYawRad);
            launchDirection = launchDirection.Rotated(Vector3.Up, aimYawRad);
        }
        launchDirection.Y = 0.0f;
        if (launchDirection.LengthSquared() < 0.000001f)
        {
            launchDirection = Vector3.Right;
        }
        launchDirection = launchDirection.Normalized();

        // Set state
        State = PhysicsEnums.BallState.Flight;
        OnGround = false;
        _substepAccumulator = 0.0f;
        RolloutImpactSpinRpm = 0.0f;
        // Launch from the current lie so course play can continue from where the ball stopped.
        SnapToGround();
        RefreshLieSurfaceFromGroundProbe();

        Velocity = launchVelocity;
        Omega = launchOmega;
        ShotStartPos = Position;
        ShotDirection = launchDirection;
        LaunchAngleDeg = vlaDeg;
        LaunchSpinRpm = totalSpin;

        PrintLaunchDebug(data, speedMph * ShotSetup.MPS_PER_MPH, vlaDeg, hlaDeg, totalSpin, spinAxis);
    }

    private void PrintLaunchDebug(Dictionary data, float speedMps, float vla, float hla, float spin, float axis)
    {
        PhysicsLogger.Info("=== SHOT DEBUG ===");
        PhysicsLogger.Info($"Speed: {(data.ContainsKey("Speed") ? data["Speed"] : 0.0f):F2} mph ({speedMps:F2} m/s)");
        PhysicsLogger.Info($"VLA: {vla:F2}°, HLA: {hla:F2}°");
        PhysicsLogger.Info($"Aim yaw offset: {AimYawOffsetDeg:F2}°");
        PhysicsLogger.Info($"Spin: {spin:F0} rpm, Axis: {axis:F2}°");
        PhysicsLogger.Info($"drag_cf: {_dragScale:F2}, lift_cf: {_liftScale:F2}");
        PhysicsLogger.Info($"Air density: {_airDensity:F4} kg/m³");
        PhysicsLogger.Info($"Dynamic viscosity: {_airViscosity:F11}");

        float ReInitial = _airDensity * speedMps * BallPhysics.RADIUS * 2.0f / _airViscosity;
        float spinRatio = speedMps > 0.1f ? (spin * ShotSetup.RAD_PER_RPM) * BallPhysics.RADIUS / speedMps : 0.0f;
        float ClInitial = _aerodynamics.GetCl(ReInitial, spinRatio);
        float lowLaunchLiftScale = BallPhysics.GetLowLaunchLiftScale(vla, spinRatio, ReInitial);
        PhysicsLogger.Info($"Reynolds number: {ReInitial:F0}");
        PhysicsLogger.Info($"Spin ratio: {spinRatio:F3}");
        PhysicsLogger.Info($"Cl (before scale): {ClInitial:F3}, after: {ClInitial * _liftScale:F3}");
        PhysicsLogger.Info($"Low-launch lift scale: {lowLaunchLiftScale:F3}");
        PhysicsLogger.Info($"Initial velocity: {Velocity}");
        PhysicsLogger.Info($"Initial omega: {Omega} ({Omega.Length() / ShotSetup.RAD_PER_RPM:F0} rpm)");
        PhysicsLogger.Info($"Shot direction: {ShotDirection}");
        PhysicsLogger.Info("===================");
    }
}
