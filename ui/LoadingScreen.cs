using Godot;

public partial class LoadingScreen : Control
{
	private const string CourseLoadServicePath = "/root/CourseLoadService";
	private const string MainMenuScenePath = "res://ui/main_menu.tscn";
	private const string DefaultCourseScenePath = "res://courses/airways_fresno/hole_1/hole_1.tscn";

	private ProgressBar _progressBar;
	private Label _statusLabel;
	private Label _percentLabel;
	private Button _backToMenuButton;
	private CourseLoadService _courseLoadService;
	private bool _activateQueued;

	public override void _Ready()
	{
		_progressBar = GetNodeOrNull<ProgressBar>("Center/VBox/ProgressBar");
		_statusLabel = GetNodeOrNull<Label>("Center/VBox/StatusLabel");
		_percentLabel = GetNodeOrNull<Label>("Center/VBox/PercentLabel");
		_backToMenuButton = GetNodeOrNull<Button>("Center/VBox/BackToMenuButton");

		if (_backToMenuButton != null)
		{
			_backToMenuButton.Pressed += OnBackToMenuPressed;
			_backToMenuButton.Visible = false;
		}

		SetStatus("Loading course...");
		SetProgress(0.0f);

		_courseLoadService = GetNodeOrNull<CourseLoadService>(CourseLoadServicePath);
		if (_courseLoadService == null)
		{
			ShowFailure("Loading service was not found.");
			return;
		}

		if (!_courseLoadService.HasActiveRequest)
		{
			Error requestError = _courseLoadService.StartLoad(DefaultCourseScenePath);
			if (requestError != Error.Ok)
			{
				ShowFailure($"Unable to start loading: {requestError}");
				return;
			}
		}
	}

	public override void _ExitTree()
	{
		if (_backToMenuButton != null)
			_backToMenuButton.Pressed -= OnBackToMenuPressed;
	}

	public override void _Process(double delta)
	{
		if (_courseLoadService == null)
			return;

		_courseLoadService.Poll();
		SetProgress(_courseLoadService.Progress);

		switch (_courseLoadService.State)
		{
			case CourseLoadService.LoadState.Loading:
				SetStatus("Loading course assets...");
				break;

			case CourseLoadService.LoadState.Loaded:
				QueueSceneActivation();
				break;

			case CourseLoadService.LoadState.Activating:
				SetStatus("Opening course...");
				break;

			case CourseLoadService.LoadState.Failed:
				ShowFailure(_courseLoadService.LastError);
				break;
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
			return;

		if (keyEvent.Keycode == Key.Escape)
			ReturnToMainMenu();
	}

	private void QueueSceneActivation()
	{
		if (_activateQueued)
			return;

		_activateQueued = true;
		SetStatus("Finalizing scene...");
		CallDeferred(nameof(ActivateSceneDeferred));
	}

	private void ActivateSceneDeferred()
	{
		if (_courseLoadService == null)
		{
			ShowFailure("Loading service was not found.");
			return;
		}

		Error activateError = _courseLoadService.ActivateLoadedScene();
		if (activateError == Error.Ok)
			return;

		_activateQueued = false;
		ShowFailure($"Failed to activate scene: {activateError}");
	}

	private void OnBackToMenuPressed()
	{
		ReturnToMainMenu();
	}

	private void ReturnToMainMenu()
	{
		_courseLoadService?.CancelLoad("Canceled by user.");
		Error error = GetTree().ChangeSceneToFile(MainMenuScenePath);
		if (error != Error.Ok)
			GD.PushError($"Loading screen: failed to open main menu '{MainMenuScenePath}'. Error: {error}");
	}

	private void ShowFailure(string reason)
	{
		string message = string.IsNullOrWhiteSpace(reason)
			? "Loading failed. Press ESC to return to the main menu."
			: $"Loading failed: {reason}\nPress ESC to return to the main menu.";

		SetStatus(message);
		if (_backToMenuButton != null)
			_backToMenuButton.Visible = true;
	}

	private void SetStatus(string text)
	{
		if (_statusLabel == null)
			return;

		_statusLabel.Text = string.IsNullOrWhiteSpace(text) ? "Loading..." : text;
	}

	private void SetProgress(float progress)
	{
		float clamped = Mathf.Clamp(progress, 0.0f, 1.0f);
		if (_progressBar != null)
			_progressBar.Value = clamped * 100.0f;
		if (_percentLabel != null)
			_percentLabel.Text = $"{Mathf.RoundToInt(clamped * 100.0f)}%";
	}
}
