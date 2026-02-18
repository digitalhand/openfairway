using Godot;
using Godot.Collections;
using PhantomCamera;

/// <summary>
/// Main range scene controller.
/// Manages the connection between TCP server, shot tracker, and UI.
/// </summary>
public partial class Range : Node3D
{
    private static readonly Vector3 BALL_START_POSITION = new Vector3(0.0f, GolfBall.START_HEIGHT, 0.0f);

    private Dictionary _displayData = new()
    {
        { "Distance", "---" },
        { "Carry", "---" },
        { "Offline", "---" },
        { "Apex", "---" },
        { "VLA", "---" },
        { "HLA", "---" },
        { "Speed", "---" },
        { "BackSpin", "---" },
        { "SideSpin", "---" },
        { "TotalSpin", "---" },
        { "SpinAxis", "---" }
    };

    private Dictionary _rawBallData = new();
    private Dictionary _lastDisplay = new();

    private const float CAMERA_FOLLOW_BACK = 8.0f;
    private const float CAMERA_FOLLOW_HEIGHT = 2.0f;
    private static readonly Vector3 CAMERA_LOOK_OFFSET = new Vector3(0.0f, 1.5f, 0.0f);
    private const float CAMERA_START_TWEEN_DURATION = 2.0f;
    private const float CAMERA_START_CLOSE_BACK = 0.9f;
    private const float CAMERA_START_CLOSE_HEIGHT = 0.25f;
    private const float CAMERA_START_CLOSE_LOOK_HEIGHT = 0.1f;

    // Orbit camera constants
    private const float CAMERA_ORBIT_RADIUS = 2.5f;
    private const float CAMERA_ORBIT_HEIGHT = 1.5f;
    private const float CAMERA_ORBIT_SPEED = 60.0f; // degrees per second

    // Current horizontal aim angle in degrees; 0 = directly behind ball
    private float _cameraYaw = 0.0f;
    private Vector3 _launchFollowDirection = Vector3.Right;

    private ShotTracker _shotTracker;
    private RangeUI _rangeUi;
    private Node3D _phantomCamera;
    private Camera3D _camera3D;
    private GolfBall _ball;
    private RangeSettings _rangeSettings;

    public override void _Ready()
    {
        _shotTracker = GetNode<ShotTracker>("ShotTracker");
        _rangeUi = GetNode<RangeUI>("RangeUI");
        _phantomCamera = GetNode<Node3D>("PhantomCamera3D");
        _camera3D = GetNode<Camera3D>("Camera3D");
        _ball = GetNode<GolfBall>("ShotTracker/Ball");

        // Connect signals
        _ball.BallAtRest += OnGolfBallRest;
        _rangeUi.HitShot += OnRangeUiHitShot;

        // Connect TCP server signal if it exists
        if (HasNode("TCPServer"))
        {
            var tcpServer = GetNode<TcpServer>("TCPServer");
            tcpServer.HitBall += OnTcpClientHitBall;
        }

        _rangeSettings = GetNode<GlobalSettings>("/root/GlobalSettings").RangeSettings;
        _rangeSettings.CameraFollowMode.SettingChanged += OnCameraFollowChanged;
        _rangeSettings.SurfaceType.SettingChanged += OnSurfaceChanged;

        bool followEnabled = (bool)_rangeSettings.CameraFollowMode.Value;
        if (followEnabled)
        {
            SetCameraToStartImmediate();
        }
        else
        {
            TweenCameraFromCloseToStart();
        }
        OnCameraFollowChanged(_rangeSettings.CameraFollowMode.Value);
        ApplySurfaceToBall();
    }

    public override void _ExitTree()
    {
        if (_ball != null)
            _ball.BallAtRest -= OnGolfBallRest;
        if (_rangeUi != null)
            _rangeUi.HitShot -= OnRangeUiHitShot;
        if (_rangeSettings != null)
        {
            _rangeSettings.CameraFollowMode.SettingChanged -= OnCameraFollowChanged;
            _rangeSettings.SurfaceType.SettingChanged -= OnSurfaceChanged;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("reset"))
        {
            ResetDisplayData();
            _rangeUi.SetData(_displayData);
            _cameraYaw = 0.0f;
            _launchFollowDirection = Vector3.Right;
            SetCameraToStartImmediate();
        }
    }

    public override void _Process(double delta)
    {
        if (_shotTracker.GetBallState() != PhysicsEnums.BallState.Rest)
        {
            UpdateBallDisplay();
            return;
        }

        bool moved = false;
        if (Input.IsActionPressed("ui_left"))
        {
            _cameraYaw += CAMERA_ORBIT_SPEED * (float)delta;
            moved = true;
        }
        if (Input.IsActionPressed("ui_right"))
        {
            _cameraYaw -= CAMERA_ORBIT_SPEED * (float)delta;
            moved = true;
        }

        if (moved)
        {
            _phantomCamera.Set("global_position", GetOrbitPosition());
            _phantomCamera.Call("look_at", _ball.GlobalPosition + CAMERA_LOOK_OFFSET, Vector3.Up);
            SyncMainCameraToPhantom();
        }
    }

    private void OnTcpClientHitBall(Dictionary data)
    {
        ApplyCameraAimToShotData(data);
        _launchFollowDirection = GetLaunchFollowDirection(data);
        PhysicsLogger.Info($"Launch monitor payload: {Json.Stringify(data)}");
        _rawBallData = data.Duplicate();
        UpdateBallDisplay();

        // Forward to ShotTracker to actually hit the ball
        _shotTracker.OnTcpClientHitBall(data);

        // Enable camera follow when shot is hit
        OnCameraFollowChanged(true);
    }

    private async void OnGolfBallRest()
    {
        UpdateBallDisplay();

        var settings = GetNode<GlobalSettings>("/root/GlobalSettings").RangeSettings;

        // Freeze camera at its current spot on rest to avoid drift/overshoot
        FreezeCameraOnBall();

        // Reset camera after delay
        float delay = (float)settings.BallResetTimer.Value;
        await ToSignal(GetTree().CreateTimer(delay), SceneTreeTimer.SignalName.Timeout);
        await ResetCameraToStart();

        // Auto-reset ball if enabled
        if ((bool)settings.AutoBallReset.Value)
        {
            ResetDisplayData();
            _rangeUi.SetData(_displayData);
            _shotTracker.ResetBall();
        }
    }

    private void OnRangeUiHitShot(Dictionary data)
    {
        ApplyCameraAimToShotData(data);
        _launchFollowDirection = GetLaunchFollowDirection(data);

        _rawBallData = data.Duplicate();
        UpdateBallDisplay();

        // Forward to ShotTracker to actually hit the ball
        _shotTracker.OnRangeUiHitShot(data);

        // Enable camera follow when shot is hit
        OnCameraFollowChanged(true);
    }

    private void ApplyCameraAimToShotData(Dictionary data)
    {
        float existingHla = data.ContainsKey("HLA") ? (float)data["HLA"] : 0.0f;
        float cameraAimHla = GetCameraAimHla();
        data["HLA"] = existingHla + cameraAimHla;
    }

    private float GetCameraAimHla()
    {
        Vector3 forward = -_camera3D.GlobalBasis.Z;
        Vector3 flatForward = new Vector3(forward.X, 0.0f, forward.Z);
        if (flatForward.LengthSquared() < 0.000001f)
        {
            return -_cameraYaw;
        }

        flatForward = flatForward.Normalized();
        return Mathf.RadToDeg(Mathf.Atan2(flatForward.Z, flatForward.X));
    }

    private void OnCameraFollowChanged(Variant value)
    {
        bool followEnabled = (bool)value;
        if (followEnabled)
        {
            StartCameraFollow();
        }
        else
        {
            _phantomCamera.Set("follow_mode", (int)FollowMode3D.None);
        }
    }

    private async System.Threading.Tasks.Task ResetCameraToStart()
    {
        _cameraYaw = 0.0f;
        _ball.Position = BALL_START_POSITION;
        _ball.Velocity = Vector3.Zero;
        _ball.Omega = Vector3.Zero;
        _ball.State = PhysicsEnums.BallState.Rest;

        // Use the same path as manual reset ("r") so start position/facing are identical.
        SetCameraToStartImmediate();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void StartCameraFollow()
    {
        _phantomCamera.Set("follow_mode", (int)FollowMode3D.Simple);
        _phantomCamera.Set("follow_target", _ball);
        _phantomCamera.Set("follow_offset", ComputeFollowOffset());
        _phantomCamera.Set("follow_damping", true);
        _phantomCamera.Set("look_at_mode", (int)LookAtMode.Simple);
        _phantomCamera.Set("look_at_target", _ball);
    }

    private Vector3 ComputeFollowOffset()
    {
        Vector3 dir = _ball.Velocity;
        if (dir.Length() < 0.5f)
        {
            dir = _launchFollowDirection;
        }
        if (dir.Length() < 0.5f)
        {
            dir = _ball.ShotDirection;
        }
        dir.Y = 0.0f;
        if (dir.LengthSquared() < 0.000001f)
        {
            dir = Vector3.Right;
        }
        dir = dir.Normalized();

        Vector3 back = -dir * CAMERA_FOLLOW_BACK;
        Vector3 up = Vector3.Up * CAMERA_FOLLOW_HEIGHT;
        return back + up;
    }

    private Vector3 GetLaunchFollowDirection(Dictionary data)
    {
        float hlaDeg = data.ContainsKey("HLA") ? (float)data["HLA"] : 0.0f;
        float hlaRad = Mathf.DegToRad(hlaDeg);
        Vector3 dir = new Vector3(Mathf.Cos(hlaRad), 0.0f, Mathf.Sin(hlaRad));
        if (dir.LengthSquared() < 0.000001f)
        {
            return Vector3.Right;
        }
        return dir.Normalized();
    }

    private void FreezeCameraOnBall()
    {
        _phantomCamera.Set("follow_mode", (int)FollowMode3D.None);
        _phantomCamera.Set("look_at_mode", (int)LookAtMode.None);
    }

    private void SetCameraToStartImmediate()
    {
        _phantomCamera.Set("follow_mode", (int)FollowMode3D.None);
        _phantomCamera.Set("look_at_mode", (int)LookAtMode.None);
        _phantomCamera.Set("global_position", GetOrbitPosition(BALL_START_POSITION));
        _phantomCamera.Call("look_at", BALL_START_POSITION + CAMERA_LOOK_OFFSET, Vector3.Up);
        SyncMainCameraToPhantom();
    }

    private void TweenCameraFromCloseToStart()
    {
        _phantomCamera.Set("follow_mode", (int)FollowMode3D.None);
        _phantomCamera.Set("look_at_mode", (int)LookAtMode.None);

        Vector3 ballLookPos = BALL_START_POSITION + CAMERA_LOOK_OFFSET;
        Vector3 closeLookPos = BALL_START_POSITION + Vector3.Up * CAMERA_START_CLOSE_LOOK_HEIGHT;
        Vector3 startPos = GetOrbitPosition(BALL_START_POSITION);
        Vector3 shotDir = _ball.ShotDirection;
        if (shotDir.Length() < 0.5f)
        {
            shotDir = Vector3.Right;
        }
        shotDir = shotDir.Normalized();
        Vector3 closePos = BALL_START_POSITION - shotDir * CAMERA_START_CLOSE_BACK
            + Vector3.Up * CAMERA_START_CLOSE_HEIGHT;

        _phantomCamera.Set("global_position", closePos);
        _phantomCamera.Call("look_at", closeLookPos, Vector3.Up);
        SyncMainCameraToPhantom();

        var tween = CreateTween();
        tween.SetTrans(Tween.TransitionType.Cubic);
        tween.SetEase(Tween.EaseType.InOut);
        tween.Parallel().TweenProperty(_phantomCamera, "global_position", startPos, CAMERA_START_TWEEN_DURATION);
        tween.Parallel().TweenMethod(Callable.From<Vector3>((pos) =>
        {
            _phantomCamera.Call("look_at", pos, Vector3.Up);
        }), closeLookPos, ballLookPos, CAMERA_START_TWEEN_DURATION);
    }

    /// <summary>
    /// Computes the camera orbit position around the ball tee for the current yaw angle.
    /// At yaw=0 the camera sits directly behind the ball (the default start position).
    /// Left/right arrow keys decrement/increment yaw, orbiting the camera horizontally.
    /// </summary>
    private Vector3 GetOrbitPosition()
    {
        return GetOrbitPosition(_ball.GlobalPosition);
    }

    private Vector3 GetOrbitPosition(Vector3 center)
    {
        float rad = Mathf.DegToRad(_cameraYaw);
        return center + new Vector3(
            -Mathf.Cos(rad) * CAMERA_ORBIT_RADIUS,
            CAMERA_ORBIT_HEIGHT,
            Mathf.Sin(rad) * CAMERA_ORBIT_RADIUS
        );
    }

    private void SyncMainCameraToPhantom()
    {
        _camera3D.GlobalTransform = _phantomCamera.GlobalTransform;
    }

    private void OnSurfaceChanged(Variant value)
    {
        ApplySurfaceToBall();
    }

    private void ApplySurfaceToBall()
    {
        if (_shotTracker != null && _shotTracker.HasNode("Ball"))
        {
            var surfaceType = (PhysicsEnums.SurfaceType)(int)GetNode<GlobalSettings>("/root/GlobalSettings").RangeSettings.SurfaceType.Value;
            _ball.SetSurface(surfaceType);
        }
    }

    private void ResetDisplayData()
    {
        _rawBallData.Clear();
        _lastDisplay.Clear();
        _displayData["Distance"] = "---";
        _displayData["Carry"] = "---";
        _displayData["Offline"] = "---";
        _displayData["Apex"] = "---";
        _displayData["VLA"] = "---";
        _displayData["HLA"] = "---";
        _displayData["Speed"] = "---";
        _displayData["BackSpin"] = "---";
        _displayData["SideSpin"] = "---";
        _displayData["TotalSpin"] = "---";
        _displayData["SpinAxis"] = "---";
    }

    private void UpdateBallDisplay()
    {
        bool showDistance = true;
        var units = (PhysicsEnums.Units)(int)GetNode<GlobalSettings>("/root/GlobalSettings").RangeSettings.RangeUnits.Value;
        _displayData = ShotFormatter.FormatBallDisplay(
            _rawBallData,
            _shotTracker,
            units,
            showDistance,
            _displayData
        );
        _lastDisplay = _displayData.Duplicate();
        _rangeUi.SetData(_displayData);
    }
}
