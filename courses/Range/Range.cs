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
	private const float CLICK_RAY_DISTANCE = 5000.0f;

	// Orbit camera constants
	private const float CAMERA_ORBIT_RADIUS = 2.5f;
	private const float CAMERA_ORBIT_HEIGHT = 1.5f;
	private const float CAMERA_ORBIT_SPEED = 60.0f; // degrees per second
	private const float YAW_INDICATOR_DISTANCE = 30.0f;
	private const float CLICK_LOOK_TWEEN_DURATION = 0.24f;
	private const float CAMERA_RESET_TWEEN_DURATION = 1.2f;
	private const float ROUND_END_SCORE_DURATION_SECONDS = 4.0f;

	// Current horizontal aim angle in degrees; 0 = directly behind ball
	private float _cameraYaw = 0.0f;
	private Vector3 _launchFollowDirection = Vector3.Right;
	private CancellationTokenSource _resetCts;
	private bool _isShotLaunching = false;
	private Tween _clickLookTween;
	private Vector3 _pendingFollowOffset = new Vector3(-CAMERA_FOLLOW_BACK, CAMERA_FOLLOW_HEIGHT, 0.0f);

	private ShotTracker _shotTracker;
	private RangeUI _rangeUi;
	private Node3D _phantomCamera;
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
			QueueFlagMarkerResetToTarget();
			return;
		}
	}

	public override void _Input(InputEvent @event)
	{
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

		if (!TryGetWorldClickPoint(mouseButton.Position, out Vector3 worldPoint))
			return;

		if (_shotMarkerController.SetPlayerSelection(worldPoint))
			CenterCameraOnPoint(worldPoint);
	}

	public override void _Process(double delta)
	{
		RefreshTargetHud();
		_shotMarkerController.Tick();

		if (_isShotLaunching || _shotTracker.GetBallState() != PhysicsEnums.BallState.Rest)
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
			_cameraYaw = Mathf.Wrap(_cameraYaw, -180f, 180f);
			_phantomCamera.Set("global_position", GetOrbitPosition());
			_phantomCamera.Call("look_at", _ball.GlobalPosition + CAMERA_LOOK_OFFSET, Vector3.Up);
			SyncMainCameraToPhantom();
			UpdatePlayerMarkerFromYaw();
		}
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

		StopClickCenterTween();
		_resetCts?.Cancel();
		_shotMarkerController.OnShotLaunched();
		PrepareShotLaunchOrientation(data);
		DisableCameraFollow();

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
		EnableCameraFollowDeferred();
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
		StopClickCenterTween();
		_isShotLaunching = false;
		_ball.AimYawOffsetDeg = 0.0f;

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
		StopClickCenterTween();
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
		StopClickCenterTween();
		_resetCts?.Cancel();
		_goalCompletionCountdownRunning = false;
		_isShotLaunching = false;
		_rangeUi?.HideRoundEndScore();
		_shotMarkerController.OnRoundReset();
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

	private void UpdatePlayerMarkerFromYaw()
	{
		if (!TryGetYawIndicatorPoint(out Vector3 worldPoint))
			return;

		_shotMarkerController.SetPlayerSelection(worldPoint);
	}

	private bool TryGetYawIndicatorPoint(out Vector3 worldPoint)
	{
		worldPoint = Vector3.Zero;
		if (_ball == null)
			return false;

		Vector3 worldAimPoint = GetBallStartGlobalPosition() + (GetCurrentAimDirection() * YAW_INDICATOR_DISTANCE);
		if (TrySampleTerrainHeight(worldAimPoint, out float terrainHeight))
			worldAimPoint.Y = terrainHeight;

		worldPoint = worldAimPoint;
		return true;
	}

	private void CenterCameraOnPoint(Vector3 worldPoint)
	{
		if (_phantomCamera == null)
			return;

		Vector3 toTarget = worldPoint - _phantomCamera.GlobalPosition;
		if (toTarget.LengthSquared() < 0.000001f)
			return;

		StopClickCenterTween();
		_phantomCamera.Set("follow_mode", (int)FollowMode3D.None);
		_phantomCamera.Set("look_at_mode", (int)LookAtMode.None);

		float startLookDistance = Mathf.Max(4.0f, toTarget.Length());
		Vector3 startLookPoint = _phantomCamera.GlobalPosition + (-_phantomCamera.GlobalBasis.Z * startLookDistance);
		_clickLookTween = CreateTween();
		_clickLookTween.SetTrans(Tween.TransitionType.Cubic);
		_clickLookTween.SetEase(Tween.EaseType.Out);
		_clickLookTween.TweenMethod(Callable.From<Vector3>((lookPoint) =>
		{
			_phantomCamera.Call("look_at", lookPoint, Vector3.Up);
			SyncMainCameraToPhantom();
		}), startLookPoint, worldPoint, CLICK_LOOK_TWEEN_DURATION);
		_clickLookTween.TweenCallback(Callable.From(() =>
		{
			_clickLookTween = null;
		}));
	}

	private void StopClickCenterTween()
	{
		_clickLookTween?.Kill();
		_clickLookTween = null;
	}

	private Vector3 GetCurrentAimDirection()
	{
		float aimHlaRad = Mathf.DegToRad(GetCameraAimHla());
		Vector3 dir = new Vector3(Mathf.Cos(aimHlaRad), 0.0f, Mathf.Sin(aimHlaRad));
		if (dir.LengthSquared() < 0.000001f)
			return Vector3.Right;
		return dir.Normalized();
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

	private void InitializeShotMarkerController()
	{
		_shotMarkerController.Initialize(new ShotMarkerInit
		{
			BallPositionProvider = () => _ball.GlobalPosition,
			BallStateProvider = () => _shotTracker != null ? _shotTracker.GetBallState() : PhysicsEnums.BallState.Rest,
			IsShotLaunchingProvider = () => _isShotLaunching,
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

		float yards = GetYardsToTarget();
		if (yards < 0.0f)
		{
			_rangeUi.SetTargetYardageUnknown();
			return;
		}

		_rangeUi.SetTargetYardage(yards);
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

	private float GetYardsToTarget()
	{
		if (_ball == null || !TryGetDistanceReferencePoint(out Vector3 referencePoint))
			return -1.0f;

		return MeasurementUtils.HorizontalDistanceYardsFloat(_ball.GlobalPosition, referencePoint);
	}
}
