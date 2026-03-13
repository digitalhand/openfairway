using System;
using System.Threading.Tasks;
using Godot;

public sealed class ShotCameraConfig
{
    public float FollowBack { get; set; } = 8.5f;
    public float FollowHeight { get; set; } = 2.0f;
    public float FollowStartDelaySeconds { get; set; } = 0.0f;
    public Vector3 CameraLookOffset { get; set; } = new(0.0f, 1.5f, 0.0f);
    public float OrbitRadius { get; set; } = 2.5f;
    public float OrbitHeight { get; set; } = 1.5f;
    public float OrbitSpeedDegPerSec { get; set; } = 60.0f;
    public float YawIndicatorDistance { get; set; } = 30.0f;
    public float ClickLookTweenDuration { get; set; } = 0.24f;
    public float ResetTweenDuration { get; set; } = 1.2f;
}

public readonly struct ShotCameraLaunchData
{
    public ShotCameraLaunchData(float worldYawOffsetDeg, Vector3 launchFollowDirection)
    {
        WorldYawOffsetDeg = worldYawOffsetDeg;
        LaunchFollowDirection = launchFollowDirection;
    }

    public float WorldYawOffsetDeg { get; }
    public Vector3 LaunchFollowDirection { get; }
}

public sealed class ShotCameraInit
{
    public Node TweenHost { get; set; }
    public IShotCameraRig CameraRig { get; set; }
    public ShotCameraConfig Config { get; set; } = new();
    public Node3D BallNode { get; set; }
    public Func<Vector3> BallPositionProvider { get; set; }
    public Func<Vector3> BallVelocityProvider { get; set; }
    public Func<Vector3> BallShotDirectionProvider { get; set; }
    public Func<PhysicsEnums.BallState> BallStateProvider { get; set; }
    public Func<bool> IsGoalCountdownProvider { get; set; } = () => false;
    public Func<Vector3?> InitialYawTargetProvider { get; set; }
    public Func<Vector3?> DefaultYawAnchorProvider { get; set; }
    public Func<Vector2, Vector3?> ClickWorldPointResolver { get; set; }
    public Func<Vector3, Vector3> GroundSnapper { get; set; }
    public Func<Vector3, bool> PlayerMarkerSelectionSetter { get; set; }
    public Action SyncMainCamera { get; set; }
}

public sealed class ShotCameraController
{
    private ShotCameraInit _init;
    private bool _isInitialized;
    private float _cameraYaw;
    private Vector3 _launchFollowDirection = Vector3.Right;
    private bool _isShotLaunching;
    private Tween _clickLookTween;
    private Vector3 _pendingFollowOffset = Vector3.Zero;
    private bool _hasYawAnchor;
    private Vector3 _yawAnchorVectorFromBall = Vector3.Zero;
    private float _yawBaselineDeg;

    public bool IsShotLaunching => _isShotLaunching;

    public void Initialize(ShotCameraInit init)
    {
        if (init == null)
            throw new ArgumentNullException(nameof(init));

        if (init.TweenHost == null)
            throw new ArgumentNullException(nameof(init.TweenHost));

        if (init.CameraRig == null)
            throw new ArgumentNullException(nameof(init.CameraRig));

        if (init.BallNode == null)
            throw new ArgumentNullException(nameof(init.BallNode));

        if (init.BallPositionProvider == null)
            throw new ArgumentNullException(nameof(init.BallPositionProvider));

        if (init.BallVelocityProvider == null)
            throw new ArgumentNullException(nameof(init.BallVelocityProvider));

        if (init.BallShotDirectionProvider == null)
            throw new ArgumentNullException(nameof(init.BallShotDirectionProvider));

        if (init.BallStateProvider == null)
            throw new ArgumentNullException(nameof(init.BallStateProvider));

        _init = init;
        _isInitialized = true;
        _cameraYaw = 0.0f;
        _launchFollowDirection = Vector3.Right;
        _isShotLaunching = false;
        _clickLookTween = null;
        _pendingFollowOffset = new Vector3(-_init.Config.FollowBack, _init.Config.FollowHeight, 0.0f);
        _hasYawAnchor = false;
        _yawAnchorVectorFromBall = Vector3.Zero;
        _yawBaselineDeg = _cameraYaw;
    }

    public void Tick(double delta, bool yawLeftPressed, bool yawRightPressed)
    {
        if (!_isInitialized)
            return;

        if (_isShotLaunching || _init.BallStateProvider() != PhysicsEnums.BallState.Rest)
            return;

        EnsureDefaultYawAnchor();

        bool moved = false;
        float step = _init.Config.OrbitSpeedDegPerSec * (float)delta;
        if (yawLeftPressed)
        {
            _cameraYaw += step;
            moved = true;
        }

        if (yawRightPressed)
        {
            _cameraYaw -= step;
            moved = true;
        }

        if (!moved)
            return;

        Vector3 ballPosition = _init.BallPositionProvider();
        _cameraYaw = Mathf.Wrap(_cameraYaw, -180f, 180f);
        _init.CameraRig.GlobalPosition = GetOrbitPosition(ballPosition);
        _init.CameraRig.LookAt(ballPosition + _init.Config.CameraLookOffset, Vector3.Up);
        SyncMainCamera();
        UpdatePlayerMarkerFromYaw();
    }

    public bool TryHandleLeftClick(Vector2 screenPosition)
    {
        if (!_isInitialized || _init.ClickWorldPointResolver == null || _init.PlayerMarkerSelectionSetter == null)
            return false;
        if (_init.IsGoalCountdownProvider())
            return false;

        Vector3? worldPoint = _init.ClickWorldPointResolver(screenPosition);
        if (!worldPoint.HasValue)
            return false;

        if (!_init.PlayerMarkerSelectionSetter(worldPoint.Value))
            return false;

        Vector3 ballPosition = _init.BallPositionProvider();
        _cameraYaw = GetTargetAlignedYawDeg(ballPosition, worldPoint.Value);
        _cameraYaw = Mathf.Wrap(_cameraYaw, -180.0f, 180.0f);
        SetYawAnchor(worldPoint.Value, resetBaseline: true);
        CenterCameraBehindBall();
        return true;
    }

    public ShotCameraLaunchData BeginShotLaunch(float shotHlaDeg)
    {
        if (!_isInitialized)
            return new ShotCameraLaunchData(0.0f, Vector3.Right);

        StopClickCenterTween();
        _isShotLaunching = true;
        DisableFollow();
        ClearYawAnchor();
        float worldYawOffsetDeg = -GetCameraAimHla();
        _launchFollowDirection = GetLaunchFollowDirection(shotHlaDeg, worldYawOffsetDeg);
        return new ShotCameraLaunchData(worldYawOffsetDeg, _launchFollowDirection);
    }

    public async Task EnableFollowDeferredAsync()
    {
        if (!_isInitialized)
            return;

        _isShotLaunching = true;
        SceneTree tree = _init.TweenHost.GetTree();
        if (tree == null)
        {
            _isShotLaunching = false;
            return;
        }

        if (_init.Config.FollowStartDelaySeconds > 0.0f)
        {
            SceneTreeTimer delayTimer = tree.CreateTimer(_init.Config.FollowStartDelaySeconds);
            await _init.TweenHost.ToSignal(delayTimer, SceneTreeTimer.SignalName.Timeout);
        }

        for (int i = 0; i < 4; i++)
        {
            await _init.TweenHost.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (_init.BallStateProvider() != PhysicsEnums.BallState.Rest || _init.BallVelocityProvider().LengthSquared() > 0.0001f)
            {
                break;
            }
        }

        _pendingFollowOffset = ComputeFollowOffset();
        StartCameraFollow();
        _isShotLaunching = false;
    }

    public void UpdateRuntimeSettings(float orbitRadius, float followStartDelaySeconds)
    {
        if (!_isInitialized)
            return;

        _init.Config.OrbitRadius = orbitRadius;
        _init.Config.FollowStartDelaySeconds = followStartDelaySeconds;

        if (_isShotLaunching || _init.BallStateProvider() != PhysicsEnums.BallState.Rest)
            return;

        Vector3 ballPosition = _init.BallPositionProvider();
        _init.CameraRig.GlobalPosition = GetOrbitPosition(ballPosition);
        _init.CameraRig.LookAt(ballPosition + _init.Config.CameraLookOffset, Vector3.Up);
        SyncMainCamera();
    }

    public async Task ResetToStartAsync()
    {
        if (!_isInitialized)
            return;

        StopClickCenterTween();
        _isShotLaunching = false;
        _init.CameraRig.SetFollowNone();
        _init.CameraRig.SetLookAtNone();

        Vector3 ballStartGlobal = _init.BallPositionProvider();
        AlignCameraYawToTarget(ballStartGlobal);
        ClearYawAnchor();
        Vector3 endPos = GetOrbitPosition(ballStartGlobal);
        Vector3 endLookPos = ballStartGlobal + _init.Config.CameraLookOffset;
        Vector3 startLookPos = _init.CameraRig.GlobalPosition + (-_init.CameraRig.GlobalBasis.Z * 15.0f);
        Vector3 startPos = _init.CameraRig.GlobalPosition;

        Tween tween = _init.TweenHost.CreateTween();
        tween.SetTrans(Tween.TransitionType.Cubic);
        tween.SetEase(Tween.EaseType.InOut);
        tween.Parallel().TweenMethod(Callable.From<Vector3>((position) =>
        {
            _init.CameraRig.GlobalPosition = position;
            SyncMainCamera();
        }), startPos, endPos, _init.Config.ResetTweenDuration);
        tween.Parallel().TweenMethod(Callable.From<Vector3>((lookPos) =>
        {
            _init.CameraRig.LookAt(lookPos, Vector3.Up);
            SyncMainCamera();
        }), startLookPos, endLookPos, _init.Config.ResetTweenDuration);

        await _init.TweenHost.ToSignal(tween, Tween.SignalName.Finished);
        _init.CameraRig.LookAt(endLookPos, Vector3.Up);
        SyncMainCamera();
    }

    public void SetToStartImmediate()
    {
        if (!_isInitialized)
            return;

        StopClickCenterTween();
        Vector3 ballStartGlobal = _init.BallPositionProvider();
        AlignCameraYawToTarget(ballStartGlobal);
        ClearYawAnchor();
        _init.CameraRig.SetFollowNone();
        _init.CameraRig.SetLookAtNone();
        _init.CameraRig.GlobalPosition = GetOrbitPosition(ballStartGlobal);
        _init.CameraRig.LookAt(ballStartGlobal + _init.Config.CameraLookOffset, Vector3.Up);
        SyncMainCamera();
    }

    public void Freeze()
    {
        if (!_isInitialized)
            return;

        _init.CameraRig.SetFollowNone();
        _init.CameraRig.SetLookAtNone();
    }

    public void SetFollowEnabled(bool enabled)
    {
        if (!_isInitialized)
            return;

        if (enabled)
            StartCameraFollow();
        else
            DisableFollow();
    }

    public void OnRoundReset()
    {
        if (!_isInitialized)
            return;

        StopClickCenterTween();
        _isShotLaunching = false;
        _launchFollowDirection = Vector3.Right;
        _pendingFollowOffset = new Vector3(-_init.Config.FollowBack, _init.Config.FollowHeight, 0.0f);
        ClearYawAnchor();
    }

    public void CancelTransientTweens()
    {
        StopClickCenterTween();
    }

    private void StartCameraFollow()
    {
        Vector3 offset = _pendingFollowOffset;
        if (offset.LengthSquared() < 0.000001f)
            offset = ComputeFollowOffset();

        _init.CameraRig.SetFollowNone();
        _init.CameraRig.SetLookAtNone();
        // Start follow from current camera transform to avoid a visible snap/reset.
        _init.CameraRig.SetSimpleFollow(_init.BallNode, offset, damping: true);
        _init.CameraRig.SetSimpleLookAt(_init.BallNode);
        SyncMainCamera();
    }

    private void DisableFollow()
    {
        _init.CameraRig.SetFollowNone();
    }

    private Vector3 ComputeFollowOffset()
    {
        Vector3 dir = _init.BallVelocityProvider();
        if (dir.Length() < 0.5f)
            dir = _launchFollowDirection;
        if (dir.Length() < 0.5f)
            dir = _init.BallShotDirectionProvider();

        dir.Y = 0.0f;
        if (dir.LengthSquared() < 0.000001f)
            dir = Vector3.Right;

        dir = dir.Normalized();
        Vector3 back = -dir * _init.Config.FollowBack;
        Vector3 up = Vector3.Up * _init.Config.FollowHeight;
        return back + up;
    }

    private Vector3 GetLaunchFollowDirection(float shotHlaDeg, float worldYawOffsetDeg)
    {
        float worldHlaDeg = shotHlaDeg + worldYawOffsetDeg;
        float hlaRad = Mathf.DegToRad(worldHlaDeg);
        Vector3 dir = new Vector3(Mathf.Cos(hlaRad), 0.0f, Mathf.Sin(hlaRad));
        if (dir.LengthSquared() < 0.000001f)
            return Vector3.Right;

        return dir.Normalized();
    }

    private float GetCameraAimHla()
    {
        Vector3 forward = -_init.CameraRig.GlobalBasis.Z;
        Vector3 flatForward = new Vector3(forward.X, 0.0f, forward.Z);
        if (flatForward.LengthSquared() < 0.000001f)
            return -_cameraYaw;

        flatForward = flatForward.Normalized();
        return Mathf.RadToDeg(Mathf.Atan2(flatForward.Z, flatForward.X));
    }

    private void UpdatePlayerMarkerFromYaw()
    {
        if (_init.PlayerMarkerSelectionSetter == null)
            return;

        if (!TryGetYawIndicatorPoint(out Vector3 worldPoint))
            return;

        _init.PlayerMarkerSelectionSetter(worldPoint);
    }

    private void EnsureDefaultYawAnchor()
    {
        if (!_isInitialized || _hasYawAnchor)
            return;

        Vector3 anchorPoint;
        if (!TryResolveDefaultYawAnchor(out anchorPoint))
        {
            // Keep behavior available even if there is no default anchor source.
            anchorPoint = _init.BallPositionProvider() + (GetCurrentAimDirection() * _init.Config.YawIndicatorDistance);
        }

        SetYawAnchor(anchorPoint, resetBaseline: true);
    }

    private bool TryResolveDefaultYawAnchor(out Vector3 anchorPoint)
    {
        anchorPoint = Vector3.Zero;
        if (_init.DefaultYawAnchorProvider == null)
            return false;

        Vector3? point = _init.DefaultYawAnchorProvider();
        if (!point.HasValue)
            return false;

        anchorPoint = point.Value;
        return true;
    }

    private void SetYawAnchor(Vector3 worldPoint, bool resetBaseline)
    {
        Vector3 ballPosition = _init.BallPositionProvider();
        Vector3 anchorVector = worldPoint - ballPosition;
        anchorVector.Y = 0.0f;

        if (anchorVector.LengthSquared() < 0.000001f)
            anchorVector = GetCurrentAimDirection() * _init.Config.YawIndicatorDistance;

        _yawAnchorVectorFromBall = anchorVector;
        _hasYawAnchor = true;
        if (resetBaseline)
            _yawBaselineDeg = _cameraYaw;
    }

    private void ClearYawAnchor()
    {
        _hasYawAnchor = false;
        _yawAnchorVectorFromBall = Vector3.Zero;
        _yawBaselineDeg = _cameraYaw;
    }

    private bool TryGetYawIndicatorPoint(out Vector3 worldPoint)
    {
        worldPoint = Vector3.Zero;
        if (!_isInitialized)
            return false;

        if (!_hasYawAnchor)
            EnsureDefaultYawAnchor();

        if (!_hasYawAnchor)
            return false;

        Vector3 ballPosition = _init.BallPositionProvider();
        float yawDelta = Mathf.Wrap(_cameraYaw - _yawBaselineDeg, -180.0f, 180.0f);
        Vector3 rotatedVector = _yawAnchorVectorFromBall.Rotated(Vector3.Up, Mathf.DegToRad(yawDelta));
        worldPoint = ballPosition + rotatedVector;
        if (_init.GroundSnapper != null)
            worldPoint = _init.GroundSnapper(worldPoint);

        return true;
    }

    private void CenterCameraBehindBall()
    {
        Vector3 ballPosition = _init.BallPositionProvider();
        Vector3 endPosition = GetOrbitPosition(ballPosition);
        Vector3 endLookPosition = ballPosition + _init.Config.CameraLookOffset;
        Vector3 startPosition = _init.CameraRig.GlobalPosition;

        StopClickCenterTween();
        _init.CameraRig.SetFollowNone();
        _init.CameraRig.SetLookAtNone();

        if (startPosition.DistanceSquaredTo(endPosition) < 0.000001f)
        {
            _init.CameraRig.GlobalPosition = endPosition;
            _init.CameraRig.LookAt(endLookPosition, Vector3.Up);
            SyncMainCamera();
            return;
        }

        _clickLookTween = _init.TweenHost.CreateTween();
        _clickLookTween.SetTrans(Tween.TransitionType.Cubic);
        _clickLookTween.SetEase(Tween.EaseType.Out);
        _clickLookTween.TweenMethod(Callable.From<Vector3>((position) =>
        {
            _init.CameraRig.GlobalPosition = position;
            _init.CameraRig.LookAt(endLookPosition, Vector3.Up);
            SyncMainCamera();
        }), startPosition, endPosition, _init.Config.ClickLookTweenDuration);
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
        if (_init.InitialYawTargetProvider == null)
            return _cameraYaw;

        Vector3? target = _init.InitialYawTargetProvider();
        if (!target.HasValue)
            return _cameraYaw;

        return GetTargetAlignedYawDeg(ballPos, target.Value);
    }

    private float GetTargetAlignedYawDeg(Vector3 ballPos, Vector3 targetPoint)
    {
        Vector3 toTarget = targetPoint - ballPos;
        toTarget.Y = 0.0f;
        if (toTarget.LengthSquared() < 0.000001f)
            return _cameraYaw;

        float targetAimDeg = Mathf.RadToDeg(Mathf.Atan2(toTarget.Z, toTarget.X));
        return Mathf.Wrap(-targetAimDeg, -180.0f, 180.0f);
    }

    private Vector3 GetOrbitPosition(Vector3 center)
    {
        float rad = Mathf.DegToRad(_cameraYaw);
        return center + new Vector3(
            -Mathf.Cos(rad) * _init.Config.OrbitRadius,
            _init.Config.OrbitHeight,
            Mathf.Sin(rad) * _init.Config.OrbitRadius
        );
    }

    private void SyncMainCamera()
    {
        _init.SyncMainCamera?.Invoke();
    }
}
