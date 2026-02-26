using System.Threading;
using Godot;
using Godot.Collections;

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

	private const float CLICK_RAY_DISTANCE = 5000.0f;
	private const float ROUND_END_SCORE_DURATION_SECONDS = 4.0f;

	private CancellationTokenSource _resetCts;

	private ShotTracker _shotTracker;
	private RangeUI _rangeUi;
	private Node3D _phantomCamera;
	private IShotCameraRig _shotCameraRig;
	private ShotCameraController _shotCameraController = new();
	private Camera3D _mainCamera;
	private Node3D _basicGreenTarget;
	private MeshInstance3D _flagPoleTarget;
	private GodotObject _terrainData;
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
	private ShotMarkerController _shotMarkerController = new();
	private bool _didLogMissingFlagPole = false;
	private readonly System.Collections.Generic.List<CourseGoalZone> _goalZones = new();

	public override void _Ready()
	{
		_shotTracker = GetNode<ShotTracker>("ShotTracker");
		_rangeUi = GetNode<RangeUI>("RangeUI");
		_phantomCamera = GetNode<Node3D>("PhantomCamera3D");
		_shotCameraRig = new PhantomShotCameraRig(_phantomCamera);
		_mainCamera = GetNodeOrNull<Camera3D>("Camera3D");
		_basicGreenTarget = GetNodeOrNull<Node3D>("GimmeCircle");
		if (_basicGreenTarget == null)
			_basicGreenTarget = GetNodeOrNull<Node3D>("BasicGreen");
		_flagPoleTarget = GetNodeOrNull<MeshInstance3D>("flag_osg_Imported/flag_pole");
		if (_flagPoleTarget == null)
			_flagPoleTarget = GetNodeOrNull<MeshInstance3D>("flag_osg_Imported/Cylinder");
		_ball = GetNode<GolfBall>("ShotTracker/Ball");
		_audioDriverHit = GetNodeOrNull<AudioStreamPlayer3D>("audio_iron_hit");
		_audioBackgroundBirds = GetNodeOrNull<AudioStreamPlayer3D>("audio_background_birds");
		_audioGolfBallLanding = GetNodeOrNull<AudioStreamPlayer3D>("audio_golf_ball_landing");
		ConfigureConsistentAudioLevels();
		_progressStore = GetNodeOrNull<GameProgressStore>("/root/GameProgressStore");
		_sceneId = GetSceneId();
		ResolveCourseCard();
		CacheTerrainData();
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
		ConnectGoalZones();
		// Always start fresh at the tee on scene load.
		// Saved progress can be restored later through an explicit resume flow.
		SetStrokeCount(0);
		_rangeUi.SetStrokeCount(_strokeCount);
		_rangeUi.SetMarkerCamera(_mainCamera);
		InitializeShotMarkerController();
		InitializeShotCameraController();
		RefreshTargetHud();

		SetCameraToStartImmediate();
		OnCameraFollowChanged(_rangeSettings.CameraFollowMode.Value);
		ApplySurfaceToBall();
		_shotMarkerController.OnRoundReset();
		_shotMarkerController.Tick();
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
		_shotCameraController.CancelTransientTweens();
		_goalZones.Clear();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("reset"))
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
			|| _goalCompletionCountdownRunning)
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
		RefreshTargetHud();
		_shotMarkerController.Tick();

		if (_shotCameraController.IsShotLaunching || _shotTracker.GetBallState() != PhysicsEnums.BallState.Rest)
		{
			UpdateBallDisplay();
			return;
		}

		_shotCameraController.Tick(
			delta,
			Input.IsActionPressed("ui_left"),
			Input.IsActionPressed("ui_right")
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

			if (_goalCompletionCountdownRunning)
			{
				FreezeCameraOnBall();
				return;
			}

			if (IsBallOnGoalZone())
			{
				PhysicsLogger.Info("Ball settled on GimmeCircle. Ending round in 3 seconds.");
				_rangeUi?.SetFinalStrokeCount(_strokeCount);
				FreezeCameraOnBall();
				StartGoalCompletionCountdown();
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
		if (_goalCompletionCountdownRunning)
			return;

		_resetCts?.Cancel();
		_shotMarkerController.OnShotLaunched();
		float shotHlaDeg = data.ContainsKey("HLA") ? (float)data["HLA"] : 0.0f;
		ShotCameraLaunchData launchCameraData = _shotCameraController.BeginShotLaunch(shotHlaDeg);
		_ball.AimYawOffsetDeg = launchCameraData.WorldYawOffsetDeg;

		if (logPayload)
			PhysicsLogger.Info($"Launch monitor payload: {Json.Stringify(data)}");

		_rawBallData = data.Duplicate();
		UpdateBallDisplay();
		PlayDriverHitAudio();
		IncrementStrokeCount();

		if (useTcpTracker)
			_shotTracker.OnTcpClientHitBall(data);
		else
			_shotTracker.OnRangeUiHitShot(data);

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
				CameraLookOffset = new Vector3(0.0f, 1.5f, 0.0f),
				OrbitRadius = 2.5f,
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
			IsGoalCountdownProvider = () => _goalCompletionCountdownRunning,
			InitialYawTargetProvider = ResolveInitialYawTarget,
			DefaultYawAnchorProvider = ResolveFlagReferencePoint,
			ClickWorldPointResolver = ResolveClickWorldPoint,
			GroundSnapper = SnapPointToTerrain,
			PlayerMarkerSelectionSetter = worldPoint => _shotMarkerController.SetPlayerSelection(worldPoint),
			SyncMainCamera = SyncMainCameraToPhantom
		});
	}

	private Vector3? ResolveInitialYawTarget()
	{
		if (_basicGreenTarget == null)
			return null;

		return _basicGreenTarget.GlobalPosition;
	}

	private Vector3 SnapPointToTerrain(Vector3 worldPoint)
	{
		if (TrySampleTerrainHeight(worldPoint, out float terrainHeight))
			return new Vector3(worldPoint.X, terrainHeight, worldPoint.Z);

		return worldPoint;
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
		_rangeUi?.ShowRoundEndScore(GetRoundEndScoreOverlayText());

		await ToSignal(GetTree().CreateTimer(ROUND_END_SCORE_DURATION_SECONDS), SceneTreeTimer.SignalName.Timeout);
		if (token.IsCancellationRequested)
		{
			_rangeUi?.HideRoundEndScore();
			_goalCompletionCountdownRunning = false;
			return;
		}

		_rangeUi?.HideRoundEndScore();
		CompleteGoalRound();
		_goalCompletionCountdownRunning = false;
	}

	private string GetRoundEndScoreOverlayText()
	{
		if (_strokeCount <= 0)
			return "Par";

		ScoreResult finalScore = ScoreMapper.MapScore(_strokeCount, _coursePar);
		return finalScore.Label;
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
		QueueFlagMarkerResetToTarget();
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
		_shotCameraController.OnRoundReset();
		_resetCts?.Cancel();
		_goalCompletionCountdownRunning = false;
		_rangeUi?.HideRoundEndScore();
		_shotMarkerController.OnRoundReset();
		ResetDisplayData();
		_rangeUi.SetData(_displayData);
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

	private void InitializeShotMarkerController()
	{
		_shotMarkerController.Initialize(new ShotMarkerInit
		{
			BallPositionProvider = () => _ball.GlobalPosition,
			BallStateProvider = () => _shotTracker != null ? _shotTracker.GetBallState() : PhysicsEnums.BallState.Rest,
			IsShotLaunchingProvider = () => _shotCameraController.IsShotLaunching,
			IsGoalCountdownProvider = () => _goalCompletionCountdownRunning,
			FlagReferencePointProvider = ResolveFlagReferencePoint,
			ClickWorldPointResolver = ResolveClickWorldPoint,
			OnMarkerSnapshotChanged = ApplyMarkerSnapshot,
			ClearPlayerSelectionOnShotLaunch = true
		});
	}

	private Vector3? ResolveFlagReferencePoint()
	{
		if (!TryGetTargetMarkerPoint(out Vector3 worldPoint))
			return null;

		return worldPoint;
	}

	private Vector3? ResolveClickWorldPoint(Vector2 mousePosition)
	{
		if (!TryGetWorldClickPoint(mousePosition, out Vector3 worldPoint))
			return null;

		return worldPoint;
	}

	private void ApplyMarkerSnapshot(MarkerSnapshot snapshot)
	{
		if (_rangeUi == null)
			return;

		_rangeUi.ApplyMarkerSnapshot(snapshot);
	}

	private bool TryGetWorldClickPoint(Vector2 mousePosition, out Vector3 worldPoint)
	{
		worldPoint = Vector3.Zero;
		if (_mainCamera == null)
			return false;

		var world = GetWorld3D();
		if (world == null)
			return false;

		Vector3 rayOrigin = _mainCamera.ProjectRayOrigin(mousePosition);
		Vector3 rayDirection = _mainCamera.ProjectRayNormal(mousePosition);
		var query = PhysicsRayQueryParameters3D.Create(rayOrigin, rayOrigin + rayDirection * CLICK_RAY_DISTANCE);
		query.CollideWithBodies = true;
		query.CollideWithAreas = true;

		if (_ball != null)
		{
			var exclude = new Godot.Collections.Array<Rid>();
			exclude.Add(_ball.GetRid());
			query.Exclude = exclude;
		}

		Dictionary hit = world.DirectSpaceState.IntersectRay(query);
		if (hit.Count > 0 && hit.ContainsKey("position"))
		{
			worldPoint = (Vector3)hit["position"];
			return true;
		}

		return TryResolveTerrainPointFromRay(mousePosition, out worldPoint);
	}

	private bool TryGetTargetMarkerPoint(out Vector3 worldPoint)
	{
		return TryGetDistanceReferencePoint(out worldPoint);
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

	private bool TryGetDistanceReferencePoint(out Vector3 worldPoint)
	{
		if (TryGetFlagPoleBottomPoint(out worldPoint))
		{
			return true;
		}

		if (!_didLogMissingFlagPole)
		{
			PhysicsLogger.Info("Range: flag_pole not found. Falling back to GimmeCircle/BasicGreen for distance/elevation reference.");
			_didLogMissingFlagPole = true;
		}

		worldPoint = Vector3.Zero;
		if (_basicGreenTarget == null)
			return false;

		worldPoint = _basicGreenTarget.GlobalPosition;
		return true;
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
		if (TrySampleTerrainHeight(bottomWorldPoint, out float terrainHeight))
			bottomWorldPoint.Y = terrainHeight;

		worldPoint = bottomWorldPoint;
		return true;
	}

	private bool TrySampleTerrainHeight(Vector3 worldPoint, out float terrainHeight)
	{
		terrainHeight = 0.0f;
		if (_terrainData == null)
			CacheTerrainData();

		if (_terrainData == null)
			return false;

		float height = (float)_terrainData.Call("get_height", worldPoint);
		if (float.IsNaN(height))
			return false;

		terrainHeight = height;
		return true;
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

	private bool TryResolveTerrainPointFromRay(Vector2 mousePosition, out Vector3 worldPoint)
	{
		worldPoint = Vector3.Zero;
		if (_mainCamera == null || _ball == null)
			return false;

		if (_terrainData == null)
			CacheTerrainData();

		if (_terrainData == null)
			return false;

		Vector3 rayOrigin = _mainCamera.ProjectRayOrigin(mousePosition);
		Vector3 rayDirection = _mainCamera.ProjectRayNormal(mousePosition);
		if (Mathf.Abs(rayDirection.Y) < 0.00001f)
			return false;

		// Intersect the click ray with the ball-height horizontal plane,
		// then sample actual terrain height at that X/Z.
		float t = (_ball.GlobalPosition.Y - rayOrigin.Y) / rayDirection.Y;
		if (t <= 0.0f)
			return false;

		Vector3 planePoint = rayOrigin + rayDirection * t;
		float height = (float)_terrainData.Call("get_height", planePoint);
		if (float.IsNaN(height))
			return false;

		worldPoint = new Vector3(planePoint.X, height, planePoint.Z);
		return true;
	}

	private void RefreshTargetHud()
	{
		UpdateTargetYardageDisplay();
		UpdateTargetElevationDisplay();
	}

	private void UpdateTargetYardageDisplay()
	{
		if (_rangeUi == null)
			return;

		if (_ball == null || !TryGetDistanceReferencePoint(out Vector3 referencePoint))
		{
			_rangeUi.SetTargetYardageUnknown();
			return;
		}

		string distanceText = MeasurementUtils.FormatHorizontalDistanceShortAware(_ball.GlobalPosition, referencePoint);
		_rangeUi.SetTargetDistanceText(distanceText);
	}

	private void UpdateTargetElevationDisplay()
	{
		if (_rangeUi == null)
			return;

		if (!TryGetTargetElevationFeet(out int feet))
		{
			_rangeUi.SetTargetElevationUnknown();
			return;
		}

		_rangeUi.SetTargetElevationFeet(feet);
	}

	private bool TryGetTargetElevationFeet(out int feet)
	{
		feet = 0;
		if (_ball == null || !TryGetDistanceReferencePoint(out Vector3 referencePoint))
			return false;

		feet = MeasurementUtils.VerticalDeltaFeet(_ball.GlobalPosition, referencePoint);
		return true;
	}

}
