using System;
using Godot;

/// <summary>
/// Autoload service that performs threaded course loading and scene activation.
/// Proton assets are built after scene activation by the hole controller.
/// </summary>
public partial class CourseLoadService : Node
{
	public enum LoadState
	{
		Idle,
		Loading,
		Loaded,
		Activating,
		Failed
	}

	[Signal]
	public delegate void LoadStartedEventHandler(string scenePath);

	[Signal]
	public delegate void LoadProgressChangedEventHandler(float progress);

	[Signal]
	public delegate void LoadCompletedEventHandler(string scenePath);

	[Signal]
	public delegate void LoadFailedEventHandler(string scenePath, string reason);

	public LoadState State => _state;
	public float Progress => _progress;
	public string TargetScenePath => _targetScenePath;
	public string LastError => _lastError;
	public bool HasActiveRequest => _state is LoadState.Loading or LoadState.Loaded or LoadState.Activating;

	private string _targetScenePath = string.Empty;
	private string _lastError = string.Empty;
	private float _progress;
	private LoadState _state = LoadState.Idle;
	private PackedScene _loadedPackedScene;

	public Error StartLoad(string scenePath)
	{
		if (string.IsNullOrWhiteSpace(scenePath))
		{
			SetFailure(scenePath, "Course path is empty.");
			return Error.InvalidParameter;
		}

		string trimmedScenePath = scenePath.Trim();
		if (HasActiveRequest && string.Equals(_targetScenePath, trimmedScenePath, StringComparison.Ordinal))
			return Error.Ok;

		ResetState(clearError: true);
		_targetScenePath = trimmedScenePath;
		SetProgress(0.0f);

		Error requestError = ResourceLoader.LoadThreadedRequest(
			_targetScenePath,
			"PackedScene",
			false,
			ResourceLoader.CacheMode.Reuse
		);

		if (requestError != Error.Ok)
		{
			SetFailure(_targetScenePath, $"Threaded load request failed with {requestError}.");
			return requestError;
		}

		_state = LoadState.Loading;
		EmitSignal(SignalName.LoadStarted, _targetScenePath);
		return Error.Ok;
	}

	public void Poll()
	{
		if (_state != LoadState.Loading || string.IsNullOrWhiteSpace(_targetScenePath))
			return;

		var progress = new Godot.Collections.Array();
		ResourceLoader.ThreadLoadStatus status = ResourceLoader.LoadThreadedGetStatus(_targetScenePath, progress);
		UpdateThreadedLoadProgress(progress);

		switch (status)
		{
			case ResourceLoader.ThreadLoadStatus.InProgress:
				return;

			case ResourceLoader.ThreadLoadStatus.Loaded:
				Resource loaded = ResourceLoader.LoadThreadedGet(_targetScenePath);
				if (loaded is not PackedScene packedScene)
				{
					SetFailure(_targetScenePath, "Loaded resource is not a PackedScene.");
					return;
				}

				_loadedPackedScene = packedScene;
				SetProgress(1.0f);
				_state = LoadState.Loaded;
				EmitSignal(SignalName.LoadCompleted, _targetScenePath);
				return;

			case ResourceLoader.ThreadLoadStatus.Failed:
				SetFailure(_targetScenePath, "Threaded load failed.");
				return;

			case ResourceLoader.ThreadLoadStatus.InvalidResource:
				SetFailure(_targetScenePath, "Threaded load status returned InvalidResource.");
				return;
		}
	}

	public Error ActivateLoadedScene()
	{
		if (_state != LoadState.Loaded || _loadedPackedScene == null)
			return Error.InvalidData;

		SceneTree tree = GetTree();
		if (tree == null)
		{
			SetFailure(_targetScenePath, "SceneTree is unavailable during activation.");
			return Error.Failed;
		}

		_state = LoadState.Activating;
		Error changeError = tree.ChangeSceneToPacked(_loadedPackedScene);
		if (changeError != Error.Ok)
		{
			SetFailure(_targetScenePath, $"Failed to activate loaded scene: {changeError}.");
			return changeError;
		}

		ResetState(clearError: true);
		return Error.Ok;
	}

	public void CancelLoad(string reason = "Canceled.")
	{
		if (_state == LoadState.Idle)
			return;

		ResetState(clearError: false);
		_lastError = reason;
	}

	private void UpdateThreadedLoadProgress(Godot.Collections.Array progressArray)
	{
		if (progressArray == null || progressArray.Count == 0)
			return;

		float threadedProgress = Mathf.Clamp((float)progressArray[0], 0.0f, 1.0f);
		SetProgress(threadedProgress);
	}

	private void SetProgress(float nextProgress)
	{
		if (Mathf.Abs(nextProgress - _progress) < 0.001f)
			return;

		_progress = nextProgress;
		EmitSignal(SignalName.LoadProgressChanged, _progress);
	}

	private void SetFailure(string scenePath, string reason)
	{
		_loadedPackedScene = null;
		_state = LoadState.Failed;
		_lastError = reason;
		EmitSignal(SignalName.LoadFailed, scenePath, reason);
	}

	private void ResetState(bool clearError)
	{
		_targetScenePath = string.Empty;
		_loadedPackedScene = null;
		_progress = 0.0f;
		_state = LoadState.Idle;
		if (clearError)
			_lastError = string.Empty;
	}
}
