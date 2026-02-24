using System.Threading;
using Godot;
using Godot.Collections;
using PhantomCamera;

/// <summary>
/// Main range scene controller.
/// Manages the connection between TCP server, shot tracker, and UI.
/// </summary>
public partial class Range : Node3D
{
    private static readonly Vector3 BALL_START_POSITION = GolfBall.START_POSITION;

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

// Moving it forward a bit to not enable another array block in terrain3d behind it. 
    private const float CAMERA_FOLLOW_BACK = 8.5f; 
    private const float CAMERA_FOLLOW_HEIGHT = 2.0f;
    private static readonly Vector3 CAMERA_LOOK_OFFSET = new Vector3(0.0f, 1.5f, 0.0f);
    private const float CAMERA_START_TWEEN_DURATION = 2.0f;
    private const float CAMERA_START_CLOSE_BACK = 0.9f;
    private const float CAMERA_START_CLOSE_HEIGHT = 0.25f;
    private const float CAMERA_START_CLOSE_LOOK_HEIGHT = 0.1f;
    private const float METERS_TO_YARDS = 1.09361f;

    // Orbit camera constants
    private const float CAMERA_ORBIT_RADIUS = 2.5f;
    private const float CAMERA_ORBIT_HEIGHT = 1.5f;
    private const float CAMERA_ORBIT_SPEED = 60.0f; // degrees per second
    private const float AIM_MARKER_DISTANCE = 30.0f;
    private const float AIM_MARKER_Y_OFFSET = 0.10f;
    private const float AIM_MARKER_RADIUS = 0.90f;
    private const float AIM_MARKER_THICKNESS = 0.04f;
    private const float AIM_MARKER_HOLD_TIME = 0.18f;
    private const float CAMERA_RESET_TWEEN_DURATION = 1.2f;
    private const float GOAL_COMPLETE_DELAY_SECONDS = 3.0f;

    // Current horizontal aim angle in degrees; 0 = directly behind ball
    private float _cameraYaw = 0.0f;
    private Vector3 _launchFollowDirection = Vector3.Right;
    private CancellationTokenSource _resetCts;
    private bool _isShotLaunching = false;
    private Node3D _aimMarker;
    private float _aimMarkerTimer = 0.0f;
    private Vector3 _pendingFollowOffset = new Vector3(-CAMERA_FOLLOW_BACK, CAMERA_FOLLOW_HEIGHT, 0.0f);

    private ShotTracker _shotTracker;
    private RangeUI _rangeUi;
    private Node3D _phantomCamera;
    private Node3D _basicGreenTarget;
    private GolfBall _ball;
    private AudioStreamPlayer3D _audioDriverHit;
    private AudioStreamPlayer3D _audioBackgroundBirds;
    private AudioStreamPlayer3D _audioGolfBallLanding;
    private RangeSettings _rangeSettings;
    private GameProgressStore _progressStore;
    private string _sceneId = string.Empty;
    private CourseCardInfo _courseCard = CourseCatalog.DefaultCourseCard;
    private int _coursePar = CourseCatalog.DefaultPar;
    private int _strokeCount = 0;
    private bool _goalCompletionCountdownRunning = false;
    private readonly System.Collections.Generic.List<CourseGoalZone> _goalZones = new();

    public override void _Ready()
    {
        _shotTracker = GetNode<ShotTracker>("ShotTracker");
        _rangeUi = GetNode<RangeUI>("RangeUI");
        _phantomCamera = GetNode<Node3D>("PhantomCamera3D");
        _basicGreenTarget = GetNodeOrNull<Node3D>("BasicGreen");
        _ball = GetNode<GolfBall>("ShotTracker/Ball");
        _audioDriverHit = GetNodeOrNull<AudioStreamPlayer3D>("audio_iron_hit");
        _audioBackgroundBirds = GetNodeOrNull<AudioStreamPlayer3D>("audio_background_birds");
        _audioGolfBallLanding = GetNodeOrNull<AudioStreamPlayer3D>("audio_golf_ball_landing");
        ConfigureConsistentAudioLevels();
        _progressStore = GetNodeOrNull<GameProgressStore>("/root/GameProgressStore");
        _sceneId = GetSceneId();
        ResolveCourseCard();
        ResetBallToStart();
        // Physics world may not have collision shapes ready in _Ready;
        // re-snap once deferred so the terrain raycast succeeds.
        _ball.CallDeferred("SnapToGround");

        // Connect signals
        _ball.BallAtRest += OnGolfBallRest;
        _ball.BallLanded += OnGolfBallLanded;
        _rangeUi.HitShot += OnRangeUiHitShot;
        _shotTracker.TestHitRequested += OnTestHitRequested;

        // Connect TCP server signal if it exists
        if (HasNode("TCPServer"))
        {
            var tcpServer = GetNode<TcpServer>("TCPServer");
            tcpServer.HitBall += OnTcpClientHitBall;
        }

        _rangeSettings = GetNode<GlobalSettings>("/root/GlobalSettings").RangeSettings;
        _rangeSettings.CameraFollowMode.SettingChanged += OnCameraFollowChanged;
        _rangeSettings.SurfaceType.SettingChanged += OnSurfaceChanged;
        CreateAimMarker();
        ConnectGoalZones();
        // Always start fresh at the tee on scene load.
        // Saved progress can be restored later through an explicit resume flow.
        SetStrokeCount(0);
        _rangeUi.SetStrokeCount(_strokeCount);
        UpdateTargetYardageDisplay();

        SetCameraToStartImmediate();
        OnCameraFollowChanged(_rangeSettings.CameraFollowMode.Value);
        ApplySurfaceToBall();
    }

    public override void _ExitTree()
    {
        if (_ball != null)
        {
            _ball.BallAtRest -= OnGolfBallRest;
            _ball.BallLanded -= OnGolfBallLanded;
        }
        if (_rangeUi != null)
            _rangeUi.HitShot -= OnRangeUiHitShot;
        if (_shotTracker != null)
            _shotTracker.TestHitRequested -= OnTestHitRequested;
        if (_rangeSettings != null)
        {
            _rangeSettings.CameraFollowMode.SettingChanged -= OnCameraFollowChanged;
            _rangeSettings.SurfaceType.SettingChanged -= OnSurfaceChanged;
        }
        _goalZones.Clear();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("reset"))
        {
            ResetRoundView();
            SetStrokeCount(0);
            ClearRoundProgress();
            CallDeferred(nameof(SetCameraToStartImmediate));
        }
    }

    public override void _Process(double delta)
    {
        UpdateAimMarkerTimer((float)delta);
        UpdateTargetYardageDisplay();

        if (_isShotLaunching || _shotTracker.GetBallState() != PhysicsEnums.BallState.Rest)
        {
            HideAimMarker();
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
            _cameraYaw = Mathf.Wrap(_cameraYaw, -180f, 180f);
            _phantomCamera.Set("global_position", GetOrbitPosition());
            _phantomCamera.Call("look_at", _ball.GlobalPosition + CAMERA_LOOK_OFFSET, Vector3.Up);
            SyncMainCameraToPhantom();
            ShowAimMarker();
        }
    }

    private void OnTcpClientHitBall(Dictionary data)
    {
        if (_goalCompletionCountdownRunning)
            return;

        _resetCts?.Cancel();
        HideAimMarker();
        PrepareShotLaunchOrientation(data);
        DisableCameraFollow();
        PhysicsLogger.Info($"Launch monitor payload: {Json.Stringify(data)}");
        _rawBallData = data.Duplicate();
        UpdateBallDisplay();
        PlayDriverHitAudio();
        IncrementStrokeCount();

        // Forward to ShotTracker to actually hit the ball
        _shotTracker.OnTcpClientHitBall(data);

        // Enable follow after deferred HitFromData has applied launch state.
        EnableCameraFollowDeferred();
    }

    private async void OnGolfBallRest()
    {
        try
        {
            UpdateBallDisplay();

            if (_goalCompletionCountdownRunning)
            {
                FreezeCameraOnBall();
                return;
            }

            if (IsBallOnGoalZone())
            {
                PhysicsLogger.Info("Ball settled on BasicGreen. Ending round in 3 seconds.");
                FreezeCameraOnBall();
                StartGoalCompletionCountdown();
                return;
            }

            SaveRoundProgress();

            // Freeze camera at its current spot on rest to avoid drift/overshoot
            FreezeCameraOnBall();

            // Cancel any previous reset timer and start a new one
            _resetCts?.Cancel();
            _resetCts = new CancellationTokenSource();
            var token = _resetCts.Token;

            // Reset camera after delay
            float delay = (float)_rangeSettings.BallResetTimer.Value;
            await ToSignal(GetTree().CreateTimer(delay), SceneTreeTimer.SignalName.Timeout);

            if (token.IsCancellationRequested)
                return;

            await ResetCameraToStart();

            if (token.IsCancellationRequested)
                return;

            // Auto-reset ball if enabled
            if ((bool)_rangeSettings.AutoBallReset.Value)
            {
                ResetDisplayData();
                _rangeUi.SetData(_displayData);
                _shotTracker.ResetBall();
                CallDeferred(nameof(SetCameraToStartImmediate));
            }
        }
        catch (System.Exception ex)
        {
            GD.PushError($"OnGolfBallRest failed: {ex}");
        }
    }

    private void OnGolfBallLanded()
    {
        PlayGolfBallLandingAudio();
    }

    private void OnRangeUiHitShot(Dictionary data)
    {
        if (_goalCompletionCountdownRunning)
            return;

        _resetCts?.Cancel();
        HideAimMarker();
        PrepareShotLaunchOrientation(data);
        DisableCameraFollow();

        _rawBallData = data.Duplicate();
        UpdateBallDisplay();
        PlayDriverHitAudio();
        IncrementStrokeCount();

        // Forward to ShotTracker to actually hit the ball
        _shotTracker.OnRangeUiHitShot(data);

        // Enable follow after deferred HitFromData has applied launch state.
        EnableCameraFollowDeferred();
    }

    private void OnTestHitRequested()
    {
        if (_goalCompletionCountdownRunning)
            return;

        _resetCts?.Cancel();
        HideAimMarker();
        var data = new Dictionary
        {
            { "Speed", 100.0f },
            { "VLA", 22.0f },
            { "HLA", 0.0f },
            { "TotalSpin", 6000.0f },
            { "SpinAxis", 3.5f }
        };
        PrepareShotLaunchOrientation(data);
        DisableCameraFollow();

        _rawBallData = data.Duplicate();
        UpdateBallDisplay();
        PlayDriverHitAudio();
        IncrementStrokeCount();

        _shotTracker.OnRangeUiHitShot(data);

        EnableCameraFollowDeferred();
    }

    private void PlayDriverHitAudio()
    {
        if (_audioDriverHit == null)
            return;

        if (_audioDriverHit.Playing)
            _audioDriverHit.Stop();

        _audioDriverHit.Play();
    }

    private void ConfigureConsistentAudioLevels()
    {
        ConfigureNonAttenuated3DAudio(_audioBackgroundBirds, ensurePlaying: true);
        ConfigureNonAttenuated3DAudio(_audioDriverHit, ensurePlaying: false);
    }

    private void ConfigureNonAttenuated3DAudio(AudioStreamPlayer3D player, bool ensurePlaying)
    {
        if (player == null)
            return;

        player.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled;
        player.DopplerTracking = AudioStreamPlayer3D.DopplerTrackingEnum.Disabled;

        if (ensurePlaying && !player.Playing)
            player.Play();
    }

    private void PlayGolfBallLandingAudio()
    {
        if (_audioGolfBallLanding == null)
            return;

        _audioGolfBallLanding.GlobalPosition = _ball.GlobalPosition;
        if (_audioGolfBallLanding.Playing)
            _audioGolfBallLanding.Stop();

        _audioGolfBallLanding.Play();
    }

    private void PrepareShotLaunchOrientation(Dictionary data)
    {
        float worldYawOffsetDeg = -GetCameraAimHla();
        _ball.AimYawOffsetDeg = worldYawOffsetDeg;
        _launchFollowDirection = GetLaunchFollowDirection(data, worldYawOffsetDeg);
    }

    private float GetCameraAimHla()
    {
        Vector3 forward = -_phantomCamera.GlobalBasis.Z;
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
        if ((bool)value)
            EnableCameraFollow();
        else
            DisableCameraFollow();
    }

    private void EnableCameraFollow()
    {
        StartCameraFollow();
    }

    private async void EnableCameraFollowDeferred()
    {
        _isShotLaunching = true;
        HideAimMarker();

        // ShotTracker uses CallDeferred(HitFromData), so wait until launch state exists.
        for (int i = 0; i < 4; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_ball.State != PhysicsEnums.BallState.Rest || _ball.Velocity.LengthSquared() > 0.0001f)
            {
                break;
            }
        }

        _pendingFollowOffset = ComputeFollowOffset();
        StartCameraFollow();
        _isShotLaunching = false;
    }

    private void DisableCameraFollow()
    {
        _phantomCamera.Set("follow_mode", (int)FollowMode3D.None);
    }

    private async System.Threading.Tasks.Task ResetCameraToStart()
    {
        _isShotLaunching = false;
        _ball.AimYawOffsetDeg = 0.0f;
        HideAimMarker();

        // Keep the ball at the current lie; only reset on explicit new-round actions.
        _phantomCamera.Set("follow_mode", (int)FollowMode3D.None);
        _phantomCamera.Set("look_at_mode", (int)LookAtMode.None);

        Vector3 ballStartGlobal = GetBallStartGlobalPosition();
        AlignCameraYawToTarget(ballStartGlobal);
        Vector3 startPos = GetOrbitPosition(ballStartGlobal);
        Vector3 endLookPos = ballStartGlobal + CAMERA_LOOK_OFFSET;
        Vector3 startLookPos = _phantomCamera.GlobalPosition + (-_phantomCamera.GlobalBasis.Z * 15.0f);

        var tween = CreateTween();
        tween.SetTrans(Tween.TransitionType.Cubic);
        tween.SetEase(Tween.EaseType.InOut);
        tween.Parallel().TweenProperty(_phantomCamera, "global_position", startPos, CAMERA_RESET_TWEEN_DURATION);
        tween.Parallel().TweenMethod(Callable.From<Vector3>((lookPos) =>
        {
            _phantomCamera.Call("look_at", lookPos, Vector3.Up);
        }), startLookPos, endLookPos, CAMERA_RESET_TWEEN_DURATION);

        await ToSignal(tween, Tween.SignalName.Finished);
        _phantomCamera.Call("look_at", endLookPos, Vector3.Up);
    }

    private void StartCameraFollow()
    {
        Vector3 offset = _pendingFollowOffset;
        if (offset.LengthSquared() < 0.000001f)
        {
            offset = ComputeFollowOffset();
        }

        // Snap the camera to the follow position before enabling follow mode.
        // Without this, damping causes the camera to visibly drift backwards
        // (or swing around) from the orbit position to the follow offset.
        _phantomCamera.Set("follow_mode", (int)FollowMode3D.None);
        _phantomCamera.Set("look_at_mode", (int)LookAtMode.None);
        _phantomCamera.Set("global_position", _ball.GlobalPosition + offset);
        _phantomCamera.Call("look_at", _ball.GlobalPosition + CAMERA_LOOK_OFFSET, Vector3.Up);
        SyncMainCameraToPhantom();

        _phantomCamera.Set("follow_mode", (int)FollowMode3D.Simple);
        _phantomCamera.Set("follow_target", _ball);
        _phantomCamera.Set("follow_offset", offset);
        _phantomCamera.Set("follow_damping", false);
        _phantomCamera.Set("look_at_mode", (int)LookAtMode.Simple);
        _phantomCamera.Set("look_at_target", _ball);
        _phantomCamera.Call("teleport_position");
        SyncMainCameraToPhantom();
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

    private Vector3 GetLaunchFollowDirection(Dictionary data, float worldYawOffsetDeg)
    {
        float shotHlaDeg = data.ContainsKey("HLA") ? (float)data["HLA"] : 0.0f;
        float worldHlaDeg = shotHlaDeg + worldYawOffsetDeg;
        float hlaRad = Mathf.DegToRad(worldHlaDeg);
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
        Vector3 ballStartGlobal = GetBallStartGlobalPosition();
        AlignCameraYawToTarget(ballStartGlobal);
        _phantomCamera.Set("follow_mode", (int)FollowMode3D.None);
        _phantomCamera.Set("look_at_mode", (int)LookAtMode.None);
        _phantomCamera.Set("global_position", GetOrbitPosition(ballStartGlobal));
        _phantomCamera.Call("look_at", ballStartGlobal + CAMERA_LOOK_OFFSET, Vector3.Up);
        SyncMainCameraToPhantom();
    }

    private void ResetBallToStart()
    {
        _ball.Position = BALL_START_POSITION;
        _ball.SnapToGround();
        _ball.Velocity = Vector3.Zero;
        _ball.Omega = Vector3.Zero;
        _ball.State = PhysicsEnums.BallState.Rest;
    }

    private void ConnectGoalZones()
    {
        _goalZones.Clear();

        foreach (Node node in GetTree().GetNodesInGroup("course_goal_zone"))
        {
            if (node is not CourseGoalZone goalZone)
                continue;

            _goalZones.Add(goalZone);
        }
    }

    private async void StartGoalCompletionCountdown()
    {
        if (_goalCompletionCountdownRunning)
            return;

        _goalCompletionCountdownRunning = true;
        _resetCts?.Cancel();
        _resetCts = new CancellationTokenSource();
        var token = _resetCts.Token;

        await ToSignal(GetTree().CreateTimer(GOAL_COMPLETE_DELAY_SECONDS), SceneTreeTimer.SignalName.Timeout);
        if (token.IsCancellationRequested)
        {
            _goalCompletionCountdownRunning = false;
            return;
        }

        CompleteGoalRound();
        _goalCompletionCountdownRunning = false;
    }

    private void CompleteGoalRound()
    {
        if (_strokeCount > 0)
        {
            ScoreResult finalScore = ScoreMapper.MapScore(_strokeCount, _coursePar);
            string relative = finalScore.RelativeToPar > 0 ? $"+{finalScore.RelativeToPar}" : finalScore.RelativeToPar.ToString();
            PhysicsLogger.Info($"Goal countdown complete. Final score: {finalScore.Label} ({finalScore.Strokes} strokes, par {finalScore.Par}, {relative}). Starting a new round.");
        }
        else
        {
            PhysicsLogger.Info("Goal countdown complete. Starting a new round.");
        }
        ResetRoundView();
        SetStrokeCount(0);
        ClearRoundProgress();
        _shotTracker.ResetBall();
        CallDeferred(nameof(SetCameraToStartImmediate));
    }

    private bool IsBallOnGoalZone()
    {
        foreach (var goalZone in _goalZones)
        {
            if (goalZone == null)
                continue;

            if (goalZone.IsBallOnZone(_ball))
                return true;
        }

        return false;
    }

    private void LoadRoundProgress()
    {
        SetStrokeCount(0);

        if (_progressStore == null || string.IsNullOrWhiteSpace(_sceneId))
            return;

        if (!_progressStore.TryGetSlotForScene(_sceneId, out var slot))
            return;

        _ball.GlobalPosition = slot.BallPosition;
        _ball.SnapToGround();
        _ball.Velocity = Vector3.Zero;
        _ball.Omega = Vector3.Zero;
        _ball.State = PhysicsEnums.BallState.Rest;
        _ball.OnGround = false;
        _ball.AimYawOffsetDeg = 0.0f;
        _ball.LaunchSpinRpm = 0.0f;
        _ball.RolloutImpactSpinRpm = 0.0f;

        SetStrokeCount(slot.Strokes);
    }

    private void SaveRoundProgress()
    {
        if (_progressStore == null)
            return;

        if (string.IsNullOrWhiteSpace(_sceneId))
            _sceneId = GetSceneId();

        if (string.IsNullOrWhiteSpace(_sceneId))
            return;

        _progressStore.SaveSlot(new CourseProgressSlot
        {
            SceneId = _sceneId,
            BallPosition = _ball.GlobalPosition,
            Strokes = _strokeCount,
            Completed = false
        });
    }

    private void ClearRoundProgress()
    {
        _progressStore?.ClearSlot();
    }

    private void IncrementStrokeCount()
    {
        SetStrokeCount(_strokeCount + 1);
    }

    private void SetStrokeCount(int strokes)
    {
        _strokeCount = Mathf.Max(0, strokes);
        _rangeUi?.SetStrokeCount(_strokeCount);
        UpdateScoreLabelFromStrokes();
    }

    private void ResetRoundView()
    {
        _resetCts?.Cancel();
        _goalCompletionCountdownRunning = false;
        _isShotLaunching = false;
        HideAimMarker();
        ResetDisplayData();
        _rangeUi.SetData(_displayData);
        _launchFollowDirection = Vector3.Right;
    }

    private string GetSceneId()
    {
        var scene = GetTree()?.CurrentScene;
        if (scene == null)
            return string.Empty;

        return string.IsNullOrWhiteSpace(scene.SceneFilePath) ? scene.Name : scene.SceneFilePath;
    }

    private void ResolveCourseCard()
    {
        _courseCard = CourseCatalog.DefaultCourseCard;
        if (CourseCatalog.TryGetCourseCard(_sceneId, out CourseCardInfo resolvedCard))
        {
            _courseCard = resolvedCard;
        }
        else
        {
            PhysicsLogger.Info($"No course header configured for scene '{_sceneId}'. Using defaults.");
        }

        _coursePar = _courseCard.Par;
        _rangeUi?.SetCourseHeader(_courseCard.CourseName, _courseCard.HoleNumber, _courseCard.Par, _courseCard.Yardage);
    }

    private void UpdateScoreLabelFromStrokes()
    {
        if (_rangeUi == null)
            return;

        if (_strokeCount <= 0)
        {
            _rangeUi.SetScoreUnknown();
            return;
        }

        ScoreResult score = ScoreMapper.MapScore(_strokeCount, _coursePar);
        _rangeUi.SetScoreLabel(score.Label);
    }

    private Vector3 GetBallStartGlobalPosition()
    {
        return _ball.GlobalPosition;
    }

    private void CreateAimMarker()
    {
        _aimMarker = GetNodeOrNull<Node3D>("AimMarker");
        if (_aimMarker != null)
        {
            HideAimMarker();
            return;
        }

        var markerMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(1.0f, 0.45f, 0.15f, 1.0f),
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.45f, 0.15f),
            EmissionEnergyMultiplier = 6.0f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Disabled
        };

        var markerRoot = new Node3D
        {
            Name = "AimMarker",
            TopLevel = true
        };

        var markerDisc = new MeshInstance3D
        {
            Name = "Disc",
            Mesh = new CylinderMesh
            {
                TopRadius = AIM_MARKER_RADIUS,
                BottomRadius = AIM_MARKER_RADIUS,
                Height = AIM_MARKER_THICKNESS
            },
            MaterialOverride = markerMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };

        // Add a small cross on top of the disc for easier aiming visibility.
        var crossX = new MeshInstance3D
        {
            Mesh = new BoxMesh
            {
                Size = new Vector3(AIM_MARKER_RADIUS * 1.7f, AIM_MARKER_THICKNESS * 0.9f, AIM_MARKER_THICKNESS * 0.35f)
            },
            MaterialOverride = markerMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = Vector3.Up * (AIM_MARKER_THICKNESS * 0.8f)
        };
        var crossZ = new MeshInstance3D
        {
            Mesh = new BoxMesh
            {
                Size = new Vector3(AIM_MARKER_THICKNESS * 0.35f, AIM_MARKER_THICKNESS * 0.9f, AIM_MARKER_RADIUS * 1.7f)
            },
            MaterialOverride = markerMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = Vector3.Up * (AIM_MARKER_THICKNESS * 0.8f)
        };

        markerRoot.AddChild(markerDisc);
        markerRoot.AddChild(crossX);
        markerRoot.AddChild(crossZ);
        AddChild(markerRoot);
        _aimMarker = markerRoot;
        HideAimMarker();
    }

    private void ShowAimMarker()
    {
        if (_aimMarker == null)
            return;

        Vector3 aimDir = GetCurrentAimDirection();
        _aimMarker.GlobalPosition = GetBallStartGlobalPosition() + aimDir * AIM_MARKER_DISTANCE + Vector3.Up * AIM_MARKER_Y_OFFSET;
        _aimMarker.Visible = true;
        _aimMarkerTimer = AIM_MARKER_HOLD_TIME;
    }

    private void HideAimMarker()
    {
        _aimMarkerTimer = 0.0f;
        if (_aimMarker != null)
            _aimMarker.Visible = false;
    }

    private void UpdateAimMarkerTimer(float delta)
    {
        if (_aimMarkerTimer <= 0.0f)
            return;

        _aimMarkerTimer -= delta;
        if (_aimMarkerTimer <= 0.0f && _aimMarker != null)
            _aimMarker.Visible = false;
    }

    private Vector3 GetCurrentAimDirection()
    {
        float aimHlaRad = Mathf.DegToRad(GetCameraAimHla());
        Vector3 dir = new Vector3(Mathf.Cos(aimHlaRad), 0.0f, Mathf.Sin(aimHlaRad));
        if (dir.LengthSquared() < 0.000001f)
            return Vector3.Right;
        return dir.Normalized();
    }

    private void TweenCameraFromCloseToStart()
    {
        _phantomCamera.Set("follow_mode", (int)FollowMode3D.None);
        _phantomCamera.Set("look_at_mode", (int)LookAtMode.None);

        Vector3 ballStartGlobal = GetBallStartGlobalPosition();
        AlignCameraYawToTarget(ballStartGlobal);
        Vector3 ballLookPos = ballStartGlobal + CAMERA_LOOK_OFFSET;
        Vector3 closeLookPos = ballStartGlobal + Vector3.Up * CAMERA_START_CLOSE_LOOK_HEIGHT;
        Vector3 startPos = GetOrbitPosition(ballStartGlobal);
        Vector3 shotDir = _ball.ShotDirection;
        if (shotDir.Length() < 0.5f)
        {
            shotDir = Vector3.Right;
        }
        shotDir = shotDir.Normalized();
        Vector3 closePos = ballStartGlobal - shotDir * CAMERA_START_CLOSE_BACK
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

    private void AlignCameraYawToTarget(Vector3 ballPos)
    {
        _cameraYaw = GetTargetAlignedYawDeg(ballPos);
    }

    private float GetTargetAlignedYawDeg(Vector3 ballPos)
    {
        if (_basicGreenTarget == null)
            return 0.0f;

        Vector3 toTarget = _basicGreenTarget.GlobalPosition - ballPos;
        toTarget.Y = 0.0f;
        if (toTarget.LengthSquared() < 0.000001f)
            return 0.0f;

        float targetAimDeg = Mathf.RadToDeg(Mathf.Atan2(toTarget.Z, toTarget.X));
        return Mathf.Wrap(-targetAimDeg, -180.0f, 180.0f);
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
        // Intentionally empty: PhantomCameraHost owns Camera3D transform updates.
    }

    private void OnSurfaceChanged(Variant value)
    {
        ApplySurfaceToBall();
    }

    private void ApplySurfaceToBall()
    {
        if (_shotTracker != null && _shotTracker.HasNode("Ball"))
        {
            var surfaceType = (PhysicsEnums.SurfaceType)(int)_rangeSettings.SurfaceType.Value;
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
        var units = (PhysicsEnums.Units)(int)_rangeSettings.RangeUnits.Value;
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

    private void UpdateTargetYardageDisplay()
    {
        if (_rangeUi == null)
            return;

        float yards = GetYardsToTarget();
        if (yards < 0.0f)
        {
            _rangeUi.SetTargetYardageUnknown();
            return;
        }

        _rangeUi.SetTargetYardage(yards);
    }

    private float GetYardsToTarget()
    {
        if (_basicGreenTarget == null || _ball == null)
            return -1.0f;

        Vector3 toTarget = _basicGreenTarget.GlobalPosition - _ball.GlobalPosition;
        toTarget.Y = 0.0f;
        return Mathf.Max(0.0f, toTarget.Length() * METERS_TO_YARDS);
    }
}
