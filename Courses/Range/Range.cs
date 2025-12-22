using Godot;
using Godot.Collections;
using PhantomCamera;

/// <summary>
/// Main range scene controller.
/// Manages the connection between TCP server, shot tracker, and UI.
/// </summary>
public partial class Range : Node3D
{
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
    private static readonly Vector3 CAMERA_START_POS = new Vector3(-2.5f, 1.5f, 0.0f);
    private static readonly Vector3 CAMERA_LOOK_OFFSET = new Vector3(0.0f, 1.5f, 0.0f);

    private ShotTracker _shotTracker;
    private RangeUI _rangeUi;
    private Node3D _phantomCamera;
    private Camera3D _camera3D;
    private GolfBall _ball;

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

        var settings = GetNode<GlobalSettings>("/root/GlobalSettings").RangeSettings;
        settings.CameraFollowMode.SettingChanged += OnCameraFollowChanged;
        settings.SurfaceType.SettingChanged += OnSurfaceChanged;

        SetCameraToStartImmediate();
        OnCameraFollowChanged(settings.CameraFollowMode.Value);
        ApplySurfaceToBall();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("reset"))
        {
            ResetDisplayData();
            _rangeUi.SetData(_displayData);
            SetCameraToStartImmediate();
        }
    }

    public override void _Process(double delta)
    {
        // Update UI during flight/rollout
        if (_shotTracker.GetBallState() != PhysicsEnums.BallState.Rest)
        {
            UpdateBallDisplay();
        }
    }

    private void OnTcpClientHitBall(Dictionary data)
    {
        GD.Print($"Launch monitor payload: {Json.Stringify(data)}");
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
        _rawBallData = data.Duplicate();
        UpdateBallDisplay();

        // Forward to ShotTracker to actually hit the ball
        _shotTracker.OnRangeUiHitShot(data);

        // Enable camera follow when shot is hit
        OnCameraFollowChanged(true);
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
        _phantomCamera.Set("follow_mode", (int)FollowMode3D.None);

        // Point camera at ball start position BEFORE tweening to avoid rotation snap
        Vector3 ballLookPos = _ball.GlobalPosition + CAMERA_LOOK_OFFSET;
        _phantomCamera.Call("look_at", ballLookPos, Vector3.Up);
        SyncMainCameraToPhantom();

        Vector3 startPos = CAMERA_START_POS;
        var tween = CreateTween();
        tween.SetTrans(Tween.TransitionType.Cubic);
        tween.SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(_phantomCamera, "global_position", startPos, 1.5f);

        await ToSignal(tween, Tween.SignalName.Finished);

        // Reset ball position for next shot visibility
        _ball.Position = new Vector3(0.0f, GolfBall.START_HEIGHT, 0.0f);
        _ball.Velocity = Vector3.Zero;
        _ball.Omega = Vector3.Zero;
        _ball.State = PhysicsEnums.BallState.Rest;
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
            dir = _ball.ShotDirection;
        }
        dir = dir.Normalized();

        Vector3 back = -dir * CAMERA_FOLLOW_BACK;
        Vector3 up = Vector3.Up * CAMERA_FOLLOW_HEIGHT;
        return back + up;
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
        _phantomCamera.Set("global_position", CAMERA_START_POS);
        // Point the camera toward the ball start position
        _phantomCamera.Call("look_at", _ball.GlobalPosition + CAMERA_LOOK_OFFSET, Vector3.Up);
        SyncMainCameraToPhantom();
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
