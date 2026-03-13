using Godot;

public partial class MainMenu : Control
{
    private const string LoadingScenePath = "res://ui/loading_screen.tscn";
    private const string CoursesScenePath = "res://courses/airways_fresno/hole_1/hole_1.tscn";
    private const string RangeScenePath = "res://courses/range.tscn";
    private const string TcpServerServicePath = "/root/TcpServerService";
    private const string VersionSettingPath = "application/config/version";
    private const string VersionFallback = "dev";

    private Button _settingsButton;
    private Button _exitButton;
    private Button _rangeButton;
    private Button _coursesButton;
    private SettingsPanel _settingsPanel;
    private TcpServer _tcpServer;
    private Control _launchMonitorStatus;
    private Label _launchMonitorLabel;
    private Label _versionLabel;

    public override void _Ready()
    {
        _settingsButton = GetNode<Button>("TopBanner/LeftButtons/SettingsButton");
        _exitButton = GetNode<Button>("TopBanner/LeftButtons/ExitButton");
        _rangeButton = GetNode<Button>("TilesRow/RangeTile/RangeButton");
        _coursesButton = GetNode<Button>("TilesRow/CoursesTile/CoursesButton");
        _settingsPanel = GetNodeOrNull<SettingsPanel>("SettingsPanel");
        _launchMonitorStatus = GetNode<Control>("TopBanner/LaunchMonitorStatus");
        _launchMonitorLabel = GetNode<Label>("TopBanner/LaunchMonitorStatus/LaunchMonitorLabel");
        _versionLabel = GetNode<Label>("VersionLabel");
        _settingsPanel?.SetMainMenuButtonVisible(false);
        _tcpServer = GetNodeOrNull<TcpServer>(TcpServerServicePath);

        _settingsButton.Pressed += OnSettingsPressed;
        _exitButton.Pressed += OnExitPressed;
        _rangeButton.Pressed += OnRangePressed;
        _coursesButton.Pressed += OnCoursesPressed;
        if (_tcpServer != null)
            _tcpServer.ConnectionStatusChanged += OnTcpConnectionStatusChanged;

        UpdateVersionLabel();
        if (_tcpServer != null)
            UpdateLaunchMonitorStatus(_tcpServer.HasIdentifiedDevice, _tcpServer.ConnectedDeviceId);
        else
            UpdateLaunchMonitorStatus(false, string.Empty);
    }

    public override void _ExitTree()
    {
        if (_settingsButton != null)
            _settingsButton.Pressed -= OnSettingsPressed;
        if (_exitButton != null)
            _exitButton.Pressed -= OnExitPressed;
        if (_rangeButton != null)
            _rangeButton.Pressed -= OnRangePressed;
        if (_coursesButton != null)
            _coursesButton.Pressed -= OnCoursesPressed;
        if (_tcpServer != null)
            _tcpServer.ConnectionStatusChanged -= OnTcpConnectionStatusChanged;
    }

    private void OnSettingsPressed()
    {
        _settingsPanel?.ShowPanel();
    }

    private void OnExitPressed()
    {
        GetTree().Quit();
    }

    private void OnRangePressed()
    {
        StartSceneLoad(_rangeButton, RangeScenePath);
    }

    private void OnCoursesPressed()
    {
        StartSceneLoad(_coursesButton, CoursesScenePath);
    }

    private void StartSceneLoad(Button sourceButton, string scenePath)
    {
        if (sourceButton == null)
            return;

        sourceButton.Disabled = true;

        CourseLoadService courseLoadService = GetNodeOrNull<CourseLoadService>("/root/CourseLoadService");
        if (courseLoadService == null)
        {
            sourceButton.Disabled = false;
            GD.PushError("Course load service autoload is missing.");
            return;
        }

        Error requestError = courseLoadService.StartLoad(scenePath);
        if (requestError != Error.Ok)
        {
            sourceButton.Disabled = false;
            GD.PushError($"Failed to start loading scene '{scenePath}'. Error: {requestError}");
            return;
        }

        Error transitionError = GetTree().ChangeSceneToFile(LoadingScenePath);
        if (transitionError != Error.Ok)
        {
            courseLoadService.CancelLoad("Failed to open loading screen.");
            sourceButton.Disabled = false;
            GD.PushError($"Failed to open loading scene '{LoadingScenePath}'. Error: {transitionError}");
        }
    }

    private void UpdateVersionLabel()
    {
        string versionText = VersionFallback;
        if (ProjectSettings.HasSetting(VersionSettingPath))
        {
            string configuredVersion = $"{ProjectSettings.GetSetting(VersionSettingPath)}".Trim();
            if (!string.IsNullOrWhiteSpace(configuredVersion))
                versionText = configuredVersion;
        }

        _versionLabel.Text = $"Version {versionText}";
    }

    private void OnTcpConnectionStatusChanged(bool connected, string deviceId)
    {
        UpdateLaunchMonitorStatus(connected, deviceId);
    }

    private void UpdateLaunchMonitorStatus(bool connected, string deviceId)
    {
        if (_launchMonitorStatus == null || _launchMonitorLabel == null)
            return;

        string safeDeviceId = string.IsNullOrWhiteSpace(deviceId) ? string.Empty : deviceId.Trim();
        bool showStatus = connected && !string.IsNullOrWhiteSpace(safeDeviceId);
        _launchMonitorStatus.Visible = showStatus;
        if (showStatus)
            _launchMonitorLabel.Text = safeDeviceId;
    }
}
