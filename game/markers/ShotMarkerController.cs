using System;
using Godot;

public sealed class ShotMarkerInit
{
	public Func<Vector3> BallPositionProvider { get; set; }
	public Func<PhysicsEnums.BallState> BallStateProvider { get; set; }
	public Func<bool> IsShotLaunchingProvider { get; set; } = () => false;
	public Func<bool> IsGoalCountdownProvider { get; set; } = () => false;
	public Func<Vector3?> FlagReferencePointProvider { get; set; }
	public Func<Vector2, Vector3?> ClickWorldPointResolver { get; set; }
	public Action<MarkerSnapshot> OnMarkerSnapshotChanged { get; set; }
	public Func<Vector3, string> DistanceFormatter { get; set; }
	public Func<Vector3, int> ElevationFeetProvider { get; set; }
	public bool ClearPlayerSelectionOnShotLaunch { get; set; } = true;
}

public sealed class ShotMarkerController
{
	private ShotMarkerInit _init;
	private bool _isInitialized;
	private bool _hasPlayerSelection;
	private Vector3 _playerSelection = Vector3.Zero;
	private bool _suppressUntilRest;
	private bool _hasPublishedSnapshot;
	private MarkerSnapshot _lastSnapshot = MarkerSnapshot.Hidden;

	public void Initialize(ShotMarkerInit init)
	{
		if (init == null)
			throw new ArgumentNullException(nameof(init));

		if (init.BallPositionProvider == null)
			throw new ArgumentNullException(nameof(init.BallPositionProvider));

		if (init.BallStateProvider == null)
			throw new ArgumentNullException(nameof(init.BallStateProvider));

		_init = init;
		_isInitialized = true;
		_hasPlayerSelection = false;
		_playerSelection = Vector3.Zero;
		_suppressUntilRest = false;
		_hasPublishedSnapshot = false;
		PublishIfChanged();
	}

	public void Tick()
	{
		if (!_isInitialized)
			return;

		if (IsAtRestState())
			_suppressUntilRest = false;

		PublishIfChanged();
	}

	public void OnLeftClick(Vector2 screenPosition)
	{
		if (!_isInitialized || !CanShowPlayerMarker() || _init.ClickWorldPointResolver == null)
			return;

		Vector3? point = _init.ClickWorldPointResolver?.Invoke(screenPosition);
		if (!point.HasValue)
			return;

		SetPlayerSelection(point.Value);
	}

	public bool SetPlayerSelection(Vector3 worldPoint)
	{
		if (!_isInitialized || !CanShowPlayerMarker())
			return false;

		_playerSelection = worldPoint;
		_hasPlayerSelection = true;
		PublishIfChanged();
		return true;
	}

	public void OnShotLaunched()
	{
		if (!_isInitialized)
			return;

		if (_init.ClearPlayerSelectionOnShotLaunch)
		{
			_hasPlayerSelection = false;
			_playerSelection = Vector3.Zero;
		}

		_suppressUntilRest = true;
		PublishIfChanged();
	}

	public void OnBallRested()
	{
		if (!_isInitialized)
			return;

		if (IsAtRestState())
			_suppressUntilRest = false;

		PublishIfChanged();
	}

	public void OnRoundReset()
	{
		if (!_isInitialized)
			return;

		_hasPlayerSelection = false;
		_playerSelection = Vector3.Zero;
		_suppressUntilRest = false;
		PublishIfChanged();
	}

	private bool CanShowPlayerMarker()
	{
		return !_suppressUntilRest
			&& IsAtRestState();
	}

	private bool IsAtRestState()
	{
		if (!_isInitialized)
			return false;

		return _init.BallStateProvider() == PhysicsEnums.BallState.Rest
			&& !_init.IsShotLaunchingProvider()
			&& !_init.IsGoalCountdownProvider();
	}

	private MarkerSnapshot BuildSnapshot()
	{
		if (!_isInitialized)
			return MarkerSnapshot.Hidden;

		if (!IsAtRestState() || _suppressUntilRest)
			return MarkerSnapshot.Hidden;

		ShotMarkerData flag = BuildFlagMarkerData();
		ShotMarkerData player = BuildPlayerMarkerData();
		return new MarkerSnapshot(flag, player);
	}

	private ShotMarkerData BuildFlagMarkerData()
	{
		Vector3? point = _init.FlagReferencePointProvider?.Invoke();
		if (!point.HasValue)
			return ShotMarkerData.Hidden;

		Vector3 worldPoint = point.Value;
		return new ShotMarkerData(
			visible: true,
			worldPoint: worldPoint,
			distanceText: FormatDistance(worldPoint),
			elevationFeet: GetElevationFeet(worldPoint)
		);
	}

	private ShotMarkerData BuildPlayerMarkerData()
	{
		if (!_hasPlayerSelection)
			return ShotMarkerData.Hidden;

		return new ShotMarkerData(
			visible: true,
			worldPoint: _playerSelection,
			distanceText: FormatDistance(_playerSelection),
			elevationFeet: GetElevationFeet(_playerSelection)
		);
	}

	private string FormatDistance(Vector3 worldPoint)
	{
		if (_init.DistanceFormatter != null)
			return _init.DistanceFormatter(worldPoint);

		return MeasurementUtils.FormatHorizontalDistanceShortAware(
			_init.BallPositionProvider(),
			worldPoint,
			includeYardsSuffix: false
		);
	}

	private int GetElevationFeet(Vector3 worldPoint)
	{
		if (_init.ElevationFeetProvider != null)
			return _init.ElevationFeetProvider(worldPoint);

		return MeasurementUtils.VerticalDeltaFeet(_init.BallPositionProvider(), worldPoint);
	}

	private void PublishIfChanged()
	{
		MarkerSnapshot snapshot = BuildSnapshot();
		if (_hasPublishedSnapshot && snapshot == _lastSnapshot)
			return;

		_lastSnapshot = snapshot;
		_hasPublishedSnapshot = true;
		_init.OnMarkerSnapshotChanged?.Invoke(snapshot);
	}
}
