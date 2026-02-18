using Godot;

/// <summary>
/// Area-based surface override for local terrain patches.
/// TODO: After fixing bug, I need to figure out how to more surface types
/// 1. Example would be SAND_HARD, SAND_SOFT, etc. 
/// 2. This will help debug/troubleshoot when ball enters, exists such surface. 
/// 3. In some cases enters and never leaves, WATER :P
/// </summary>
public partial class SurfaceZone : Area3D
{
	[Export]
	public PhysicsEnums.SurfaceType SurfaceType { get; set; } = PhysicsEnums.SurfaceType.Fairway;

	[Export]
	public bool ShowDebugVisual { get; set; } = true;

	[Export]
	public bool LogTransitions { get; set; } = true;

	public override void _Ready()
	{
		EnsureDebugVisual();
		BodyEntered += OnBodyEntered;
		BodyExited += OnBodyExited;
	}

	public override void _ExitTree()
	{
		BodyEntered -= OnBodyEntered;
		BodyExited -= OnBodyExited;
	}

	private void OnBodyEntered(Node3D body)
	{
		if (body is GolfBall ball)
		{
			ball.EnterSurfaceZone(SurfaceType);
			if (LogTransitions)
				PhysicsLogger.INFO(
					$"[SurfaceZone] entered '{Name}' zone={SurfaceType} active={ball.SurfaceType} " +
					$"downrange={ball.GetDownrangeMeters() * 1.09361f:F2}yd speed={ball.Velocity.Length():F2}m/s pos={ball.Position}"
				);
		}
	}

	private void OnBodyExited(Node3D body)
	{
		if (body is GolfBall ball)
		{
			ball.ExitSurfaceZone(SurfaceType);
			if (LogTransitions)
				PhysicsLogger.INFO(
					$"[SurfaceZone] exited '{Name}' zone={SurfaceType} active={ball.SurfaceType} " +
					$"downrange={ball.GetDownrangeMeters() * 1.09361f:F2}yd speed={ball.Velocity.Length():F2}m/s pos={ball.Position}"
				);
		}
	}

	private void EnsureDebugVisual()
	{
		var existing = GetNodeOrNull<MeshInstance3D>("DebugMesh");
		if (!ShowDebugVisual)
		{
			if (existing != null)
				existing.Visible = false;
			return;
		}

		var collisionShape = GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
		if (collisionShape == null || collisionShape.Shape == null)
			return;

		MeshInstance3D meshNode = existing ?? new MeshInstance3D { Name = "DebugMesh" };
		meshNode.Visible = true;
		meshNode.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		meshNode.Transform = collisionShape.Transform;
		meshNode.Mesh = CreateMeshForShape(collisionShape.Shape);
		meshNode.MaterialOverride = CreateDebugMaterial(GetSurfaceColor(SurfaceType));

		if (existing == null)
			AddChild(meshNode);
	}

	private static Mesh CreateMeshForShape(Shape3D shape)
	{
		if (shape is BoxShape3D box)
		{
			return new BoxMesh { Size = box.Size };
		}

		if (shape is SphereShape3D sphere)
		{
			return new SphereMesh { Radius = sphere.Radius, Height = sphere.Radius * 2.0f };
		}

		if (shape is CylinderShape3D cylinder)
		{
			return new CylinderMesh
			{
				TopRadius = cylinder.Radius,
				BottomRadius = cylinder.Radius,
				Height = cylinder.Height
			};
		}

		if (shape is CapsuleShape3D capsule)
		{
			return new CapsuleMesh { Radius = capsule.Radius, Height = capsule.Height };
		}

		return new BoxMesh { Size = new Vector3(1.0f, 1.0f, 1.0f) };
	}

	private static StandardMaterial3D CreateDebugMaterial(Color baseColor)
	{
		return new StandardMaterial3D
		{
			AlbedoColor = baseColor,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			EmissionEnabled = true,
			Emission = new Color(baseColor.R, baseColor.G, baseColor.B),
			EmissionEnergyMultiplier = 0.4f
		};
	}

	private static Color GetSurfaceColor(PhysicsEnums.SurfaceType surfaceType)
	{
		return surfaceType switch
		{
			PhysicsEnums.SurfaceType.Fairway => new Color(0.2f, 0.8f, 0.25f, 0.20f),
			PhysicsEnums.SurfaceType.FairwaySoft => new Color(0.1f, 0.45f, 0.95f, 0.20f),
			PhysicsEnums.SurfaceType.Rough => new Color(0.9f, 0.35f, 0.08f, 0.24f),
			PhysicsEnums.SurfaceType.Firm => new Color(0.75f, 0.75f, 0.75f, 0.20f),
			_ => new Color(1.0f, 1.0f, 1.0f, 0.20f)
		};
	}
}
