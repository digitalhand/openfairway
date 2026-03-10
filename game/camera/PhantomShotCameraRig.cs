using System;
using Godot;
using PhantomCamera;

public sealed class PhantomShotCameraRig : IShotCameraRig
{
    private readonly Node3D _phantomCamera;

    public PhantomShotCameraRig(Node3D phantomCamera)
    {
        _phantomCamera = phantomCamera ?? throw new ArgumentNullException(nameof(phantomCamera));
    }

    public Vector3 GlobalPosition
    {
        get => _phantomCamera.GlobalPosition;
        set => _phantomCamera.GlobalPosition = value;
    }

    public Basis GlobalBasis => _phantomCamera.GlobalBasis;

    public void LookAt(Vector3 worldPoint, Vector3 up)
    {
        _phantomCamera.Call("look_at", worldPoint, up);
    }

    public void SetFollowNone()
    {
        _phantomCamera.Set("follow_mode", (int)FollowMode3D.None);
    }

    public void SetLookAtNone()
    {
        _phantomCamera.Set("look_at_mode", (int)LookAtMode.None);
    }

    public void SetSimpleFollow(Node3D target, Vector3 offset, bool damping)
    {
        _phantomCamera.Set("follow_mode", (int)FollowMode3D.Simple);
        _phantomCamera.Set("follow_target", target);
        _phantomCamera.Set("follow_offset", offset);
        _phantomCamera.Set("follow_damping", damping);
    }

    public void SetSimpleLookAt(Node3D target)
    {
        _phantomCamera.Set("look_at_mode", (int)LookAtMode.Simple);
        _phantomCamera.Set("look_at_target", target);
    }

    public void TeleportPosition()
    {
        _phantomCamera.Call("teleport_position");
    }
}
