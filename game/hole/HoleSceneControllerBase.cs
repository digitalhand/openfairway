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
	private const float ROUND_END_SCORE_DURATION_SECONDS = 4.0f;

	[ExportGroup("Scene Nodes")]
	[Export] public NodePath ShotTrackerPath { get; set; } = new NodePath("ShotTracker");
	[Export] public NodePath GameplayUiPath { get; set; } = new NodePath("GameplayUI");
	[Export] public NodePath BallPath { get; set; } = new NodePath("ShotTracker/Ball");
	[Export] public NodePath PhantomCameraPath { get; set; } = new NodePath("PhantomCamera3D");
	[Export] public NodePath MainCameraPath { get; set; } = new NodePath("Camera3D");
	[Export] public NodePath TcpServerPath { get; set; } = new NodePath("TCPServer");
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
	private GameSettings _gameSettings;
	private AppSettings _appSettings;
	private Setting _cameraOrbitDistanceSetting;
	private Setting _cameraFollowDelaySetting;
	private GameProgressStore _progressStore;
	private string _sceneId = string.Empty;
	private ShotMarkerController _shotMarkerController = new();
	private bool _didLogMissingFlagPole = false;
	private readonly System.Collections.Generic.List<CourseGoalZone> _goalZones = new();

	private readonly ShotDisplaySession _displaySession = new();
	private readonly HoleRoundState _holeRoundState = new();
	private GoalCompletionFlow _goalCompletionFlow;
	private TargetReferenceResolver _targetResolver;

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
		_shotTracker = GetNodeOrNull<ShotTracker>(ShotTrackerPath);
		_gameplayUi = GetNodeOrNull<GameplayUI>(GameplayUiPath);
		_phantomCamera = GetNodeOrNull<Node3D>(PhantomCameraPath);
		_mainCamera = GetNodeOrNull<Camera3D>(MainCameraPath);
		_ball = GetNodeOrNull<GolfBall>(BallPath);

		if (_ball == null && _shotTracker != null)
			_ball = _shotTracker.GetNodeOrNull<GolfBall>("Ball");

		if (!ValidateRequiredNodes())
			return;

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
		ConfigureConsistentAudioLevels();
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
		var tcpServer = GetNodeOrNull<TcpServer>(TcpServerPath);
		if (tcpServer != null)
		{
			tcpServer.HitBall += OnTcpClientHitBall;
		}

		var globalSettings = GetNode<GlobalSettings>("/root/GlobalSettings");
		_gameSettings = globalSettings.GameSettings;
		_appSettings = globalSettings.AppSettings;
		_gameSettings.CameraFollowMode.SettingChanged += OnCameraFollowChanged;
		_gameSettings.SurfaceType.SettingChanged += OnSurfaceChanged;
		_cameraOrbitDistanceSetting = _appSettings?.CameraOrbitDistance;
		_cameraFollowDelaySetting = _appSettings?.CameraFollowDelaySeconds;
		if (_cameraOrbitDistanceSetting != null)
			_cameraOrbitDistanceSetting.SettingChanged += OnCameraOrbitDistanceChanged;
		if (_cameraFollowDelaySetting != null)
			_cameraFollowDelaySetting.SettingChanged += OnCameraFollowDelayChanged;
		ConnectGoalZones();
		InitializeGoalCompletionFlow();

		// Always start fresh at the tee on scene load.
		// Saved progress can be restored later through an explicit resume flow.
		SetStrokeCount(0);
		_gameplayUi?.SetData(_displaySession.Current.ToDictionary());
		_gameplayUi.SetMarkerCamera(_mainCamera);
		InitializeShotMarkerController();
		InitializeShotCameraController();
		RefreshTargetHud();

		SetCameraToStartImmediate();
		OnCameraFollowChanged(_gameSettings.CameraFollowMode.Value);
		ApplySurfaceToBall();
		_shotMarkerController.OnRoundReset();
		_shotMarkerController.Tick();
		OnHoleReadyAfterInit();
	}

	public override void _ExitTree()
	{
		if (_ball != null)
		{
			_ball.BallAtRest -= OnGolfBallRest;
			_ball.BallLanded -= OnGolfBallLanded;
		}
		if (_gameplayUi != null)
			_gameplayUi.HitShot -= OnGameplayUiHitShot;
		if (_shotTracker != null)
			_shotTracker.TestHitRequested -= OnTestHitRequested;
		var tcpServer = GetNodeOrNull<TcpServer>(TcpServerPath);
		if (tcpServer != null)
			tcpServer.HitBall -= OnTcpClientHitBall;
		if (_gameSettings != null)
		{
			_gameSettings.CameraFollowMode.SettingChanged -= OnCameraFollowChanged;
			_gameSettings.SurfaceType.SettingChanged -= OnSurfaceChanged;
		}
		if (_cameraOrbitDistanceSetting != null)
			_cameraOrbitDistanceSetting.SettingChanged -= OnCameraOrbitDistanceChanged;
		if (_cameraFollowDelaySetting != null)
			_cameraFollowDelaySetting.SettingChanged -= OnCameraFollowDelayChanged;

		_resetCts?.Cancel();
		_goalCompletionFlow?.Cancel();
		_shotCameraController.CancelTransientTweens();
		_goalZones.Clear();
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
		CallDeferred(nameof(SetCameraToStartImmediate));
		QueueFlagMarkerResetToTarget();
	}

	public override void _Process(double delta)
	{
		if (_shotTracker == null || _ball == null)
			return;

		RefreshTargetHud();
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
			await ToSignal(GetTree().CreateTimer(delay), SceneTreeTimer.SignalName.Timeout);

			if (token.IsCancellationRequested)
				return;

			await ResetCameraToStart();

			if (token.IsCancellationRequested)
				return;

			// Auto-reset ball if enabled
			if ((bool)_gameSettings.AutoBallReset.Value)
			{
				_displaySession.Reset();
				_gameplayUi.SetData(_displaySession.Current.ToDictionary());
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

	private void OnGameplayUiHitShot(Dictionary data)
	{
		LaunchShot(data, useTcpTracker: false, logPayload: false);
	}

	private void OnTestHitRequested()
	{
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

	private void OnCameraFollowChanged(Variant value)
	{
		_shotCameraController.SetFollowEnabled((bool)value);
	}

	private async System.Threading.Tasks.Task ResetCameraToStart()
	{
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

	private Vector3 SnapPointToTerrain(Vector3 worldPoint)
	{
		if (_targetResolver == null)
			return worldPoint;

		return _targetResolver.SnapPointToTerrain(worldPoint);
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
		if (_shotTracker != null && _shotTracker.HasNode("Ball"))
		{
			var surfaceType = (PhysicsEnums.SurfaceType)(int)_gameSettings.SurfaceType.Value;
			_ball.SetSurface(surfaceType);
		}
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
