using Godot;

public interface IShotCameraRig
{
	Vector3 GlobalPosition { get; set; }
	Basis GlobalBasis { get; }

	void LookAt(Vector3 worldPoint, Vector3 up);
	void SetFollowNone();
	void SetLookAtNone();
	void SetSimpleFollow(Node3D target, Vector3 offset, bool damping);
	void SetSimpleLookAt(Node3D target);
	void TeleportPosition();
}
