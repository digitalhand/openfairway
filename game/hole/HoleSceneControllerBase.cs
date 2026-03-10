using System;
using System.Threading;
using Godot;
using Godot.Collections;

/// <summary>
/// Reusable hole scene controller that orchestrates shot flow, camera, markers, HUD, and round state.
/// Scene-specific holes derive from this class and override only hole-specific behavior.
/// </summary>
public abstract partial class HoleSceneControllerBase : Node3D
{
    private static readonly Vector3 BALL_START_POSITION = GolfBall.START_POSITION;

    private const float CLICK_RAY_DISTANCE = 5000.0f;
    private const float LIE_SURFACE_RAYCAST_UP = 0.05f;
    private const float LIE_SURFACE_RAYCAST_DOWN = BallPhysics.RADIUS + 0.08f;
    private const float ROUND_END_SCORE_DURATION_SECONDS = 4.0f;
    private const double TARGET_HUD_REFRESH_INTERVAL_SECONDS = 0.10;
    private const float TARGET_HUD_MIN_MOVE_METERS = 0.15f;

    [ExportGroup("Scene Nodes")]
    [Export] public NodePath ShotTrackerPath { get; set; } = new NodePath("ShotTracker");
    [Export] public NodePath GameplayUiPath { get; set; } = new NodePath("GameplayUI");
    [Export] public NodePath BallPath { get; set; } = new NodePath("ShotTracker/Ball");
    [Export] public NodePath PhantomCameraPath { get; set; } = new NodePath("PhantomCamera3D");
    [Export] public NodePath MainCameraPath { get; set; } = new NodePath("Camera3D");
    [Export] public NodePath TcpServerPath { get; set; } = new NodePath("/root/TcpServerService");
    [Export] public NodePath PrimaryTargetPath { get; set; } = new NodePath("GimmeCircle");
    [Export] public NodePath FallbackTargetPath { get; set; } = new NodePath("BasicGreen");
    [Export] public NodePath FlagPolePath { get; set; } = new NodePath("flag_osg_Imported/flag_pole");
    [Export] public NodePath FlagPoleFallbackPath { get; set; } = new NodePath("flag_osg_Imported/Cylinder");
    [Export] public NodePath DriverHitAudioPath { get; set; } = new NodePath("audio_iron_hit");
    [Export] public NodePath AmbientAudioPath { get; set; } = new NodePath("audio_background_birds");
    [Export] public NodePath BallLandingAudioPath { get; set; } = new NodePath("audio_golf_ball_landing");

    [ExportGroup("Input Actions")]
    [Export] public string OrbitLeftAction { get; set; } = "ui_left";
    [Export] public string OrbitRightAction { get; set; } = "ui_right";
    [Export] public string ResetAction { get; set; } = "reset";

    private CancellationTokenSource _resetCts;
    private CancellationTokenSource _lifecycleCts;
    private bool _isShuttingDown;

    private ShotTracker _shotTracker;
    private GameplayUI _gameplayUi;
    private Node3D _phantomCamera;
    private IShotCameraRig _shotCameraRig;
    private ShotCameraController _shotCameraController = new();
    private Camera3D _mainCamera;
    private Node3D _primaryTarget;
    private MeshInstance3D _flagPoleTarget;
    private GodotObject _terrainData;
    private GolfBall _ball;
    private AudioStreamPlayer3D _audioDriverHit;
    private AudioStreamPlayer3D _audioBackgroundBirds;
    private AudioStreamPlayer3D _audioGolfBallLanding;
    private TcpServer _tcpServer;
    private GameSettings _gameSettings;
    private AppSettings _appSettings;
    private Setting _cameraOrbitDistanceSetting;
    private Setting _cameraFollowDelaySetting;
    private GameProgressStore _progressStore;
    private string _sceneId = string.Empty;
    private ShotMarkerController _shotMarkerController = new();
    private bool _didLogMissingFlagPole = false;
    private readonly System.Collections.Generic.List<CourseGoalZone> _goalZones = new();
    private readonly System.Collections.Generic.List<SurfaceZone> _surfaceZones = new();
    private readonly System.Collections.Generic.List<GridMap> _surfaceGridMaps = new();
    private readonly LieSurfaceResolver _lieSurfaceResolver = new();

    private readonly ShotDisplaySession _displaySession = new();
    private readonly HoleRoundState _holeRoundState = new();
    private GoalCompletionFlow _goalCompletionFlow;
    private TargetReferenceResolver _targetResolver;
    private double _targetHudRefreshTimer;
    private Vector3 _lastTargetHudBallPosition;
    private bool _hasTargetHudBallPosition;
    private StartupStage _startupStage = StartupStage.NotStarted;

    private enum StartupStage
    {
        NotStarted,
        Core,
        Deferred,
        Background,
        Complete
    }

    private bool IsGoalCountdownRunning => _goalCompletionFlow != null && _goalCompletionFlow.IsRunning;

    protected virtual void OnHoleReadyAfterInit()
    {
    }

    protected virtual void OnHoleRoundCompleted()
    {
    }

    protected virtual Vector3? ResolveInitialYawTarget()
    {
        if (_primaryTarget == null)
            return null;

        return _primaryTarget.GlobalPosition;
    }

    protected virtual Vector3? ResolveDistanceReferencePoint()
    {
        if (TryGetFlagPoleBottomPoint(out Vector3 worldPoint))
            return worldPoint;

        if (!_didLogMissingFlagPole)
        {
            PhysicsLogger.Info($"{GetType().Name}: flag pole not found. Falling back to primary target for distance/elevation reference.");
            _didLogMissingFlagPole = true;
        }

        if (_primaryTarget == null)
            return null;

        return _primaryTarget.GlobalPosition;
    }

    private bool ValidateRequiredNodes()
    {
        bool valid = true;
        valid &= ValidateRequiredNode(_shotTracker, nameof(ShotTrackerPath), ShotTrackerPath);
        valid &= ValidateRequiredNode(_gameplayUi, nameof(GameplayUiPath), GameplayUiPath);
        valid &= ValidateRequiredNode(_phantomCamera, nameof(PhantomCameraPath), PhantomCameraPath);
        valid &= ValidateRequiredNode(_mainCamera, nameof(MainCameraPath), MainCameraPath);
        valid &= ValidateRequiredNode(_ball, nameof(BallPath), BallPath);

        if (valid)
            return true;

        SetProcess(false);
        SetPhysicsProcess(false);
        SetProcessInput(false);
        SetProcessUnhandledInput(false);
        return false;
    }

    private bool ValidateRequiredNode(Node node, string propertyName, NodePath configuredPath)
    {
        if (node != null)
            return true;

        GD.PushError($"{GetType().Name}: required node path '{propertyName}' is missing or invalid. Path='{configuredPath}'.");
        return false;
    }

    public override void _Ready()
    {
        _isShuttingDown = false;
        _lifecycleCts?.Cancel();
        _lifecycleCts?.Dispose();
        _lifecycleCts = new CancellationTokenSource();

        _shotTracker = GetNodeOrNull<ShotTracker>(ShotTrackerPath);
        _gameplayUi = GetNodeOrNull<GameplayUI>(GameplayUiPath);
        _phantomCamera = GetNodeOrNull<Node3D>(PhantomCameraPath);
        _mainCamera = GetNodeOrNull<Camera3D>(MainCameraPath);
        _ball = GetNodeOrNull<GolfBall>(BallPath);

        if (_ball == null && _shotTracker != null)
            _ball = _shotTracker.GetNodeOrNull<GolfBall>("Ball");

        if (!ValidateRequiredNodes())
            return;

        _targetHudRefreshTimer = TARGET_HUD_REFRESH_INTERVAL_SECONDS;
        InitializeCoreStage();
        CallDeferred(nameof(BeginDeferredStartupStage));
    }

    private void InitializeCoreStage()
    {
        _startupStage = StartupStage.Core;
        _shotCameraRig = new PhantomShotCameraRig(_phantomCamera);
        _primaryTarget = GetNodeOrNull<Node3D>(PrimaryTargetPath);
        if (_primaryTarget == null)
            _primaryTarget = GetNodeOrNull<Node3D>(FallbackTargetPath);

        _flagPoleTarget = GetNodeOrNull<MeshInstance3D>(FlagPolePath);
        if (_flagPoleTarget == null)
            _flagPoleTarget = GetNodeOrNull<MeshInstance3D>(FlagPoleFallbackPath);

        _audioDriverHit = GetNodeOrNull<AudioStreamPlayer3D>(DriverHitAudioPath);
        _audioBackgroundBirds = GetNodeOrNull<AudioStreamPlayer3D>(AmbientAudioPath);
        _audioGolfBallLanding = GetNodeOrNull<AudioStreamPlayer3D>(BallLandingAudioPath);
        ConfigureConsistentAudioLevels(startAmbientAudio: false);
        _progressStore = GetNodeOrNull<GameProgressStore>("/root/GameProgressStore");
        _sceneId = GetSceneId();
        ResolveCourseCard();
        CacheTerrainData();
        InitializeTargetResolver();
        ResetBallToStart();
        // Physics world may not have collision shapes ready in _Ready;
        // re-snap once deferred so the terrain raycast succeeds.
        _ball.CallDeferred("SnapToGround");

        // Connect signals
        _ball.BallAtRest += OnGolfBallRest;
        _ball.BallLanded += OnGolfBallLanded;
        _gameplayUi.HitShot += OnGameplayUiHitShot;
        _shotTracker.TestHitRequested += OnTestHitRequested;

        // Connect TCP server signal if it exists
        _tcpServer = GetNodeOrNull<TcpServer>(TcpServerPath);
        if (_tcpServer != null)
            _tcpServer.HitBall += OnTcpClientHitBall;

        var globalSettings = GetNode<GlobalSettings>("/root/GlobalSettings");
        _gameSettings = globalSettings.GameSettings;
        _appSettings = globalSettings.AppSettings;
        _gameSettings.CameraFollowMode.SettingChanged += OnCameraFollowChanged;
        _gameSettings.SurfaceType.SettingChanged += OnSurfaceChanged;
        _ball.ResolveLieSurface = ResolveLieSurfaceAtContact;
        _ball.DescribeLieSurfaceResolution = () => _lieSurfaceResolver.DescribeLastResolution();
        _cameraOrbitDistanceSetting = _appSettings?.CameraOrbitDistance;
        _cameraFollowDelaySetting = _appSettings?.CameraFollowDelaySeconds;
        if (_cameraOrbitDistanceSetting != null)
            _cameraOrbitDistanceSetting.SettingChanged += OnCameraOrbitDistanceChanged;
        if (_cameraFollowDelaySetting != null)
            _cameraFollowDelaySetting.SettingChanged += OnCameraFollowDelayChanged;

        // Always start fresh at the tee on scene load.
        // Saved progress can be restored later through an explicit resume flow.
        SetStrokeCount(0);
        _gameplayUi?.SetData(_displaySession.Current.ToDictionary());
        _gameplayUi.SetMarkerCamera(_mainCamera);
        InitializeShotCameraController();
        MaybeRefreshTargetHud(delta: TARGET_HUD_REFRESH_INTERVAL_SECONDS, force: true);

        SetCameraToStartImmediate();
        OnCameraFollowChanged(_gameSettings.CameraFollowMode.Value);
        ApplySurfaceToBall();
    }

    private void BeginDeferredStartupStage()
    {
        CancellationToken lifecycleToken = _lifecycleCts != null ? _lifecycleCts.Token : default;
        if (!CanContinueLifecycleWork(lifecycleToken))
            return;

        _startupStage = StartupStage.Deferred;
        ConnectGoalZones();
        ConnectSurfaceZones();
        InitializeGoalCompletionFlow();
        InitializeShotMarkerController();
        _shotMarkerController.OnRoundReset();
        _shotMarkerController.Tick();
        MaybeRefreshTargetHud(delta: TARGET_HUD_REFRESH_INTERVAL_SECONDS, force: true);

        if (!CanContinueLifecycleWork(lifecycleToken))
            return;

        CallDeferred(nameof(BeginBackgroundStartupStage));
    }

    private void BeginBackgroundStartupStage()
    {
        CancellationToken lifecycleToken = _lifecycleCts != null ? _lifecycleCts.Token : default;
        if (!CanContinueLifecycleWork(lifecycleToken))
            return;

        _startupStage = StartupStage.Background;
        ConfigureNonAttenuated3DAudio(_audioBackgroundBirds, ensurePlaying: true);

        if (!CanContinueLifecycleWork(lifecycleToken))
            return;

        _startupStage = StartupStage.Complete;
        OnHoleReadyAfterInit();
    }

    public override void _ExitTree()
    {
        _isShuttingDown = true;
        PhysicsLogger.Info($"{GetType().Name}: beginning hole teardown.");
        SetProcess(false);
        SetPhysicsProcess(false);
        SetProcessInput(false);
        SetProcessUnhandledInput(false);

        if (_ball != null)
        {
            _ball.BallAtRest -= OnGolfBallRest;
            _ball.BallLanded -= OnGolfBallLanded;
            _ball.ResolveLieSurface = null;
            _ball.DescribeLieSurfaceResolution = null;
        }
        if (_gameplayUi != null)
            _gameplayUi.HitShot -= OnGameplayUiHitShot;
        if (_shotTracker != null)
            _shotTracker.TestHitRequested -= OnTestHitRequested;
        if (_tcpServer != null)
            _tcpServer.HitBall -= OnTcpClientHitBall;
        if (_gameSettings != null)
        {
            _gameSettings.CameraFollowMode.SettingChanged -= OnCameraFollowChanged;
            _gameSettings.SurfaceType.SettingChanged -= OnSurfaceChanged;
        }
        if (_cameraOrbitDistanceSetting != null)
            _cameraOrbitDistanceSetting.SettingChanged -= OnCameraOrbitDistanceChanged;
        if (_cameraFollowDelaySetting != null)
            _cameraFollowDelaySetting.SettingChanged -= OnCameraFollowDelayChanged;

        _lifecycleCts?.Cancel();
        _lifecycleCts?.Dispose();
        _lifecycleCts = null;
        _resetCts?.Cancel();
        _goalCompletionFlow?.Cancel();
        _shotCameraController.CancelTransientTweens();
        _goalZones.Clear();
        DisconnectSurfaceZones();
        DisconnectSurfaceGridMaps();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(ResetAction))
        {
            TriggerRoundReset();
            return;
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent
            && keyEvent.Pressed
            && !keyEvent.Echo
            && keyEvent.Keycode == Key.Tab)
        {
            TriggerRoundReset();
            return;
        }

        if (@event is not InputEventMouseButton mouseButton
            || !mouseButton.Pressed
            || mouseButton.ButtonIndex != MouseButton.Left
            || IsGoalCountdownRunning)
        {
            return;
        }

        // Ignore clicks over interactive UI controls.
        if (GetViewport()?.GuiGetHoveredControl() != null)
            return;

        _shotCameraController.TryHandleLeftClick(mouseButton.Position);
    }

    private void TriggerRoundReset()
    {
        ResetRoundView();
        SetStrokeCount(0);
        ClearRoundProgress();
        ResetLieSurfaceAfterTeleport();
        CallDeferred(nameof(SetCameraToStartImmediate));
        QueueFlagMarkerResetToTarget();
    }

    public override void _Process(double delta)
    {
        if (_shotTracker == null || _ball == null)
            return;

        MaybeRefreshTargetHud(delta);
        _shotMarkerController.Tick();

        if (_shotCameraController.IsShotLaunching || _shotTracker.GetBallState() != PhysicsEnums.BallState.Rest)
        {
            UpdateBallDisplay();
            return;
        }

        _shotCameraController.Tick(
            delta,
            Input.IsActionPressed(OrbitLeftAction),
            Input.IsActionPressed(OrbitRightAction)
        );
    }

    private void OnTcpClientHitBall(Dictionary data)
    {
        LaunchShot(data, useTcpTracker: true, logPayload: true);
    }

    private async void OnGolfBallRest()
    {
        try
        {
            if (!CanContinueLifecycleWork())
                return;

            UpdateBallDisplay();

            if (IsGoalCountdownRunning)
            {
                FreezeCameraOnBall();
                return;
            }

            if (_goalCompletionFlow != null && _goalCompletionFlow.TryStartIfBallOnGoal(this, ROUND_END_SCORE_DURATION_SECONDS))
            {
                PhysicsLogger.Info("Ball settled on goal zone. Ending round in 3 seconds.");
                _gameplayUi?.SetFinalStrokeCount(_holeRoundState.StrokeCount);
                FreezeCameraOnBall();
                return;
            }

            _shotMarkerController.OnBallRested();
            SaveRoundProgress();

            // Freeze camera at its current spot on rest to avoid drift/overshoot
            FreezeCameraOnBall();

            // Cancel any previous reset timer and start a new one
            _resetCts?.Cancel();
            _resetCts = new CancellationTokenSource();
            var token = _resetCts.Token;

            // Reset camera after delay
            float delay = (float)_gameSettings.BallResetTimer.Value;
            SceneTree tree = GetTree();
            if (tree == null)
                return;

            await ToSignal(tree.CreateTimer(delay), SceneTreeTimer.SignalName.Timeout);

            if (token.IsCancellationRequested || !CanContinueLifecycleWork())
                return;

            await ResetCameraToStart();

            if (token.IsCancellationRequested || !CanContinueLifecycleWork())
                return;

            // Auto-reset ball if enabled
            if ((bool)_gameSettings.AutoBallReset.Value)
            {
                _displaySession.Reset();
                _gameplayUi.SetData(_displaySession.Current.ToDictionary());
                _shotTracker.ResetBall();
                ResetLieSurfaceAfterTeleport();
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

    private void OnGameplayUiHitShot(Dictionary data)
    {
        if (!IsTestShotsEnabled())
            return;

        LaunchShot(data, useTcpTracker: false, logPayload: false);
    }

    private void OnTestHitRequested()
    {
        if (!IsTestShotsEnabled())
            return;

        var data = new Dictionary
        {
            { "Speed", 100.0f },
            { "VLA", 22.0f },
            { "HLA", 0.0f },
            { "TotalSpin", 6000.0f },
            { "SpinAxis", 3.5f }
        };
        LaunchShot(data, useTcpTracker: false, logPayload: false);
    }

    private void PlayDriverHitAudio()
    {
        if (_audioDriverHit == null)
            return;

        if (_audioDriverHit.Playing)
            _audioDriverHit.Stop();

        _audioDriverHit.Play();
    }

    private void LaunchShot(Dictionary data, bool useTcpTracker, bool logPayload)
    {
        if (IsGoalCountdownRunning)
            return;

        _resetCts?.Cancel();
        _shotMarkerController.OnShotLaunched();
        float shotHlaDeg = data.ContainsKey("HLA") ? (float)data["HLA"] : 0.0f;
        ShotCameraLaunchData launchCameraData = _shotCameraController.BeginShotLaunch(shotHlaDeg);
        _ball.AimYawOffsetDeg = launchCameraData.WorldYawOffsetDeg;

        if (logPayload)
            PhysicsLogger.Info($"Launch monitor payload: {Json.Stringify(data)}");

        _displaySession.SetRawPayload(data);
        UpdateBallDisplay();
        PlayDriverHitAudio();
        IncrementStrokeCount();

        if (useTcpTracker)
            _shotTracker.OnTcpClientHitBall(data);
        else
            _shotTracker.OnGameplayUiHitShot(data);

        // Enable follow after deferred HitFromData has applied launch state.
        _ = _shotCameraController.EnableFollowDeferredAsync();
    }

    private void ConfigureConsistentAudioLevels(bool startAmbientAudio)
    {
        ConfigureNonAttenuated3DAudio(_audioBackgroundBirds, ensurePlaying: startAmbientAudio);
        ConfigureNonAttenuated3DAudio(_audioDriverHit, ensurePlaying: false);
        ConfigureNonAttenuated3DAudio(_audioGolfBallLanding, ensurePlaying: false);
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

    private void OnCameraFollowChanged(Variant value)
    {
        _shotCameraController.SetFollowEnabled((bool)value);
    }

    private async System.Threading.Tasks.Task ResetCameraToStart()
    {
        if (!CanContinueLifecycleWork())
            return;

        _ball.AimYawOffsetDeg = 0.0f;
        await _shotCameraController.ResetToStartAsync();
    }

    private void FreezeCameraOnBall()
    {
        _shotCameraController.Freeze();
    }

    private void SetCameraToStartImmediate()
    {
        _shotCameraController.SetToStartImmediate();
    }

    private void InitializeShotCameraController()
    {
        _shotCameraController.Initialize(new ShotCameraInit
        {
            TweenHost = this,
            CameraRig = _shotCameraRig,
            Config = new ShotCameraConfig
            {
                FollowBack = 8.5f,
                FollowHeight = 2.0f,
                FollowStartDelaySeconds = GetCameraFollowDelaySecondsSetting(),
                CameraLookOffset = new Vector3(0.0f, 1.5f, 0.0f),
                OrbitRadius = GetCameraOrbitDistanceSetting(),
                OrbitHeight = 1.5f,
                OrbitSpeedDegPerSec = 30.0f,
                YawIndicatorDistance = 30.0f,
                ClickLookTweenDuration = 0.24f,
                ResetTweenDuration = 1.2f
            },
            BallNode = _ball,
            BallPositionProvider = () => _ball.GlobalPosition,
            BallVelocityProvider = () => _ball.Velocity,
            BallShotDirectionProvider = () => _ball.ShotDirection,
            BallStateProvider = () => _shotTracker != null ? _shotTracker.GetBallState() : PhysicsEnums.BallState.Rest,
            IsGoalCountdownProvider = () => IsGoalCountdownRunning,
            InitialYawTargetProvider = ResolveInitialYawTarget,
            DefaultYawAnchorProvider = ResolveFlagReferencePoint,
            ClickWorldPointResolver = ResolveClickWorldPoint,
            GroundSnapper = SnapPointToTerrain,
            PlayerMarkerSelectionSetter = worldPoint => _shotMarkerController.SetPlayerSelection(worldPoint),
            SyncMainCamera = SyncMainCameraToPhantom
        });
    }

    private void ApplyShotCameraSettings()
    {
        _shotCameraController.UpdateRuntimeSettings(
            GetCameraOrbitDistanceSetting(),
            GetCameraFollowDelaySecondsSetting()
        );
    }

    private float GetCameraOrbitDistanceSetting()
    {
        if (_appSettings == null)
            return AppSettings.DefaultCameraOrbitDistance;

        return (float)_appSettings.CameraOrbitDistance.Value;
    }

    private float GetCameraFollowDelaySecondsSetting()
    {
        if (_appSettings == null)
            return AppSettings.DefaultCameraFollowDelaySeconds;

        return (float)_appSettings.CameraFollowDelaySeconds.Value;
    }

    private bool IsTestShotsEnabled()
    {
        if (_appSettings == null)
            return AppSettings.DefaultTestShotsEnabled;

        return (bool)_appSettings.TestShotsEnabled.Value;
    }

    private Vector3 SnapPointToTerrain(Vector3 worldPoint)
    {
        if (_targetResolver == null)
            return worldPoint;

        return _targetResolver.SnapPointToTerrain(worldPoint);
    }

    private void ResetBallToStart()
    {
        _lieSurfaceResolver.ClearZones();
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

    private void ConnectSurfaceZones()
    {
        DisconnectSurfaceZones();
        ConnectSurfaceGridMaps();

        foreach (Node node in GetTree().GetNodesInGroup(SurfaceZone.GroupName))
        {
            if (node is not SurfaceZone surfaceZone)
                continue;

            _surfaceZones.Add(surfaceZone);
            surfaceZone.BallEnteredSurfaceZone += OnBallEnteredSurfaceZone;
            surfaceZone.BallExitedSurfaceZone += OnBallExitedSurfaceZone;
        }
    }

    private void DisconnectSurfaceZones()
    {
        foreach (var surfaceZone in _surfaceZones)
        {
            if (surfaceZone == null)
                continue;

            surfaceZone.BallEnteredSurfaceZone -= OnBallEnteredSurfaceZone;
            surfaceZone.BallExitedSurfaceZone -= OnBallExitedSurfaceZone;
        }

        _surfaceZones.Clear();
    }

    private void ConnectSurfaceGridMaps()
    {
        DisconnectSurfaceGridMaps();
        Node root = GetTree().CurrentScene ?? this;
        CollectSurfaceGridMaps(root);

        string gridMapNames = _surfaceGridMaps.Count == 0
            ? "none"
            : string.Join(", ", _surfaceGridMaps.ConvertAll(gridMap => gridMap.Name.ToString()));
        PhysicsLogger.Info($"[SurfaceGridMaps] registered={_surfaceGridMaps.Count} names={gridMapNames}");
    }

    private void DisconnectSurfaceGridMaps()
    {
        _surfaceGridMaps.Clear();
        _lieSurfaceResolver.ClearGridMaps();
    }

    private void CollectSurfaceGridMaps(Node node)
    {
        if (node is GridMap gridMap && _lieSurfaceResolver.RegisterGridMap(gridMap))
            _surfaceGridMaps.Add(gridMap);

        foreach (Node child in node.GetChildren())
            CollectSurfaceGridMaps(child);
    }

    private void OnBallEnteredSurfaceZone(GolfBall ball, int surfaceTypeValue)
    {
        if (ball != _ball)
            return;

        _lieSurfaceResolver.EnterZone((PhysicsEnums.SurfaceType)surfaceTypeValue);
        RefreshBallLieSurfaceAtCurrentPosition();
    }

    private void OnBallExitedSurfaceZone(GolfBall ball, int surfaceTypeValue)
    {
        if (ball != _ball)
            return;

        _lieSurfaceResolver.ExitZone((PhysicsEnums.SurfaceType)surfaceTypeValue);
        RefreshBallLieSurfaceAtCurrentPosition();
    }

    private void InitializeGoalCompletionFlow()
    {
        if (_goalZones.Count == 0)
        {
            _goalCompletionFlow = null;
            return;
        }

        _goalCompletionFlow = new GoalCompletionFlow();
        _goalCompletionFlow.Initialize(new GoalCompletionConfig
        {
            IsBallOnGoalProvider = IsBallOnGoalZone,
            StrokeProvider = () => _holeRoundState.StrokeCount,
            ParProvider = () => _holeRoundState.Par,
            ShowOverlay = label => _gameplayUi?.ShowRoundEndScore(label),
            HideOverlay = () => _gameplayUi?.HideRoundEndScore(),
            OnCompleteRound = CompleteGoalRound
        });
    }

    private void CompleteGoalRound()
    {
        if (_holeRoundState.TryGetScore(out ScoreResult finalScore))
        {
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
        ResetLieSurfaceAfterTeleport();
        CallDeferred(nameof(SetCameraToStartImmediate));
        QueueFlagMarkerResetToTarget();
        OnHoleRoundCompleted();
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
            Strokes = _holeRoundState.StrokeCount,
            Completed = false
        });
    }

    private void ClearRoundProgress()
    {
        _progressStore?.ClearSlot();
    }

    private void IncrementStrokeCount()
    {
        SetStrokeCount(_holeRoundState.StrokeCount + 1);
    }

    private void SetStrokeCount(int strokes)
    {
        _holeRoundState.SetStrokes(strokes);
        _gameplayUi?.SetStrokeCount(_holeRoundState.StrokeCount);
        UpdateScoreLabelFromStrokes();
    }

    private void ResetRoundView()
    {
        _shotCameraController.OnRoundReset();
        _resetCts?.Cancel();
        _goalCompletionFlow?.Cancel();
        _shotMarkerController.OnRoundReset();
        _displaySession.Reset();
        _gameplayUi?.SetData(_displaySession.Current.ToDictionary());
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
        bool foundCourseCard = _holeRoundState.Initialize(_sceneId);
        if (!foundCourseCard)
            PhysicsLogger.Info($"No course header configured for scene '{_sceneId}'. Using defaults.");

        CourseCardInfo courseCard = _holeRoundState.CourseCard;
        _gameplayUi?.SetCourseHeader(courseCard.CourseName, courseCard.HoleNumber, courseCard.Par, courseCard.Yardage);
    }

    private void UpdateScoreLabelFromStrokes()
    {
        if (_gameplayUi == null)
            return;

        if (!_holeRoundState.TryGetScore(out ScoreResult score))
        {
            _gameplayUi.SetScoreUnknown();
            return;
        }

        _gameplayUi.SetScoreLabel(score.Label);
    }

    private void SyncMainCameraToPhantom()
    {
        // Intentionally empty: PhantomCameraHost owns Camera3D transform updates.
    }

    private void OnSurfaceChanged(Variant value)
    {
        ApplySurfaceToBall();
    }

    private void OnCameraOrbitDistanceChanged(Variant value)
    {
        ApplyShotCameraSettings();
    }

    private void OnCameraFollowDelayChanged(Variant value)
    {
        ApplyShotCameraSettings();
    }

    private void ApplySurfaceToBall()
    {
        if (_ball == null || _gameSettings == null)
            return;

        _lieSurfaceResolver.SetDefaultSurface((PhysicsEnums.SurfaceType)(int)_gameSettings.SurfaceType.Value);
        RefreshBallLieSurfaceAtCurrentPosition();
    }

    private PhysicsEnums.SurfaceType ResolveLieSurfaceAtContact(Node collider, Vector3 worldPoint)
    {
        return _lieSurfaceResolver.Resolve(collider, worldPoint);
    }

    private void RefreshBallLieSurfaceAtCurrentPosition()
    {
        if (_ball == null)
            return;

        _ball.SetLieSurface(ResolveBallLieSurfaceAtCurrentPosition());
    }

    private PhysicsEnums.SurfaceType ResolveBallLieSurfaceAtCurrentPosition()
    {
        if (TryGetBallGroundContact(out Node collider, out Vector3 worldPoint))
            return ResolveLieSurfaceAtContact(collider, worldPoint);

        return _lieSurfaceResolver.Resolve(null, _ball != null ? _ball.GlobalPosition : Vector3.Zero);
    }

    private bool TryGetBallGroundContact(out Node collider, out Vector3 worldPoint)
    {
        collider = null;
        worldPoint = _ball != null ? _ball.GlobalPosition : Vector3.Zero;
        if (_ball == null)
            return false;

        World3D world = _ball.GetWorld3D();
        if (world == null)
            return false;

        Vector3 rayStart = _ball.GlobalPosition + Vector3.Up * LIE_SURFACE_RAYCAST_UP;
        Vector3 rayEnd = _ball.GlobalPosition + Vector3.Down * LIE_SURFACE_RAYCAST_DOWN;
        var query = PhysicsRayQueryParameters3D.Create(rayStart, rayEnd);
        query.CollideWithAreas = false;
        query.CollideWithBodies = true;
        query.Exclude = new Array<Rid> { _ball.GetRid() };

        var hit = world.DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
            return false;

        worldPoint = (Vector3)hit["position"];
        collider = hit.ContainsKey("collider") && hit["collider"].Obj is Node node
            ? node
            : null;
        return true;
    }

    private void ResetLieSurfaceAfterTeleport()
    {
        _lieSurfaceResolver.ClearZones();
        CallDeferred(nameof(ApplySurfaceToBall));
    }

    private void UpdateBallDisplay()
    {
        bool showDistance = true;
        var units = (PhysicsEnums.Units)(int)_gameSettings.GameUnits.Value;
        ShotDisplaySnapshot snapshot = _displaySession.Refresh(_shotTracker, units, showDistance);
        _gameplayUi?.SetData(snapshot.ToDictionary());
    }

    private void InitializeShotMarkerController()
    {
        _shotMarkerController.Initialize(new ShotMarkerInit
        {
            BallPositionProvider = () => _ball.GlobalPosition,
            BallStateProvider = () => _shotTracker != null ? _shotTracker.GetBallState() : PhysicsEnums.BallState.Rest,
            IsShotLaunchingProvider = () => _shotCameraController.IsShotLaunching,
            IsGoalCountdownProvider = () => IsGoalCountdownRunning,
            FlagReferencePointProvider = ResolveFlagReferencePoint,
            ClickWorldPointResolver = ResolveClickWorldPoint,
            OnMarkerSnapshotChanged = ApplyMarkerSnapshot,
            ClearPlayerSelectionOnShotLaunch = true
        });
    }

    private Vector3? ResolveFlagReferencePoint()
    {
        if (_targetResolver == null)
            return null;

        if (!_targetResolver.TryGetDistanceReferencePoint(out Vector3 worldPoint))
            return null;

        return worldPoint;
    }

    private Vector3? ResolveClickWorldPoint(Vector2 mousePosition)
    {
        if (_targetResolver == null)
            return null;

        if (!_targetResolver.TryGetWorldClickPoint(mousePosition, out Vector3 worldPoint))
            return null;

        return worldPoint;
    }

    private void ApplyMarkerSnapshot(MarkerSnapshot snapshot)
    {
        if (_gameplayUi == null)
            return;

        _gameplayUi.ApplyMarkerSnapshot(snapshot);
    }

    private void QueueFlagMarkerResetToTarget()
    {
        CallDeferred(nameof(RefreshShotMarkerController));
    }

    private void RefreshShotMarkerController()
    {
        _shotMarkerController.OnRoundReset();
        _shotMarkerController.Tick();
    }

    private bool TryGetFlagPoleBottomPoint(out Vector3 worldPoint)
    {
        worldPoint = Vector3.Zero;
        if (_flagPoleTarget == null)
            return false;

        if (_flagPoleTarget.Mesh == null)
        {
            worldPoint = _flagPoleTarget.GlobalPosition;
            return true;
        }

        Aabb localBounds = _flagPoleTarget.Mesh.GetAabb();
        Vector3 localBottomCenter = new Vector3(
            localBounds.Position.X + (localBounds.Size.X * 0.5f),
            localBounds.Position.Y,
            localBounds.Position.Z + (localBounds.Size.Z * 0.5f)
        );

        Vector3 bottomWorldPoint = _flagPoleTarget.ToGlobal(localBottomCenter);
        if (_targetResolver != null && _targetResolver.TrySampleTerrainHeight(bottomWorldPoint, out float terrainHeight))
            bottomWorldPoint.Y = terrainHeight;

        worldPoint = bottomWorldPoint;
        return true;
    }

    private void InitializeTargetResolver()
    {
        _targetResolver = new TargetReferenceResolver(
            ball: _ball,
            camera: _mainCamera,
            worldProvider: GetWorld3D,
            terrainDataProvider: GetTerrainData,
            distanceReferencePointProvider: ResolveDistanceReferencePoint,
            clickRayDistance: CLICK_RAY_DISTANCE
        );
    }

    private GodotObject GetTerrainData()
    {
        if (_terrainData == null)
            CacheTerrainData();

        return _terrainData;
    }

    private void CacheTerrainData()
    {
        if (_terrainData != null)
            return;

        var terrain = GetTree()?.Root?.FindChild("Terrain3D", true, false);
        if (terrain == null)
            return;

        Variant data = terrain.Get("data");
        if (data.Obj is GodotObject obj)
            _terrainData = obj;
    }

    private bool CanContinueLifecycleWork(CancellationToken token = default)
    {
        if (_isShuttingDown || !IsInsideTree())
            return false;

        return !token.CanBeCanceled || !token.IsCancellationRequested;
    }

    private void MaybeRefreshTargetHud(double delta, bool force = false)
    {
        if (_ball == null)
            return;

        _targetHudRefreshTimer += delta;
        float minMoveSquared = TARGET_HUD_MIN_MOVE_METERS * TARGET_HUD_MIN_MOVE_METERS;
        Vector3 ballPosition = _ball.GlobalPosition;
        bool movedEnough = !_hasTargetHudBallPosition
            || ballPosition.DistanceSquaredTo(_lastTargetHudBallPosition) >= minMoveSquared;

        if (!force && _targetHudRefreshTimer < TARGET_HUD_REFRESH_INTERVAL_SECONDS && !movedEnough)
            return;

        RefreshTargetHud();
        _targetHudRefreshTimer = 0.0;
        _lastTargetHudBallPosition = ballPosition;
        _hasTargetHudBallPosition = true;
    }

    private void RefreshTargetHud()
    {
        UpdateTargetYardageDisplay();
        UpdateTargetElevationDisplay();
    }

    private void UpdateTargetYardageDisplay()
    {
        if (_gameplayUi == null)
            return;

        if (_targetResolver == null || !_targetResolver.TryGetTargetDistanceText(out string distanceText))
        {
            _gameplayUi.SetTargetYardageUnknown();
            return;
        }

        _gameplayUi.SetTargetDistanceText(distanceText);
    }

    private void UpdateTargetElevationDisplay()
    {
        if (_gameplayUi == null)
            return;

        if (_targetResolver == null || !_targetResolver.TryGetTargetElevationFeet(out int feet))
        {
            _gameplayUi.SetTargetElevationUnknown();
            return;
        }

        _gameplayUi.SetTargetElevationFeet(feet);
    }
}
