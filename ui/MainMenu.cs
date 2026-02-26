using Godot;

public partial class MainMenu : Control
{
    private const string CoursesScenePath = "res://courses/airways_fresno/hole_1/hole_1.tscn";
    private const string VersionSettingPath = "application/config/version";
    private const string VersionFallback = "dev";

    private Button _settingsButton;
    private Button _exitButton;
    private Button _coursesButton;
    private SettingsPanel _settingsPanel;
    private Label _versionLabel;

    public override void _Ready()
    {
        _settingsButton = GetNode<Button>("TopBanner/LeftButtons/SettingsButton");
        _exitButton = GetNode<Button>("TopBanner/LeftButtons/ExitButton");
        _coursesButton = GetNode<Button>("TilesRow/CoursesTile/CoursesButton");
        _settingsPanel = GetNodeOrNull<SettingsPanel>("SettingsPanel");
        _versionLabel = GetNode<Label>("VersionLabel");

        _settingsButton.Pressed += OnSettingsPressed;
        _exitButton.Pressed += OnExitPressed;
        _coursesButton.Pressed += OnCoursesPressed;

        UpdateVersionLabel();
    }

    public override void _ExitTree()
    {
        if (_settingsButton != null)
            _settingsButton.Pressed -= OnSettingsPressed;
        if (_exitButton != null)
            _exitButton.Pressed -= OnExitPressed;
        if (_coursesButton != null)
            _coursesButton.Pressed -= OnCoursesPressed;
    }

    private void OnSettingsPressed()
    {
        _settingsPanel?.ShowPanel();
    }

    private void OnExitPressed()
    {
        GetTree().Quit();
    }

    private void OnCoursesPressed()
    {
        Error error = GetTree().ChangeSceneToFile(CoursesScenePath);
        if (error != Error.Ok)
            GD.PushError($"Failed to load courses scene '{CoursesScenePath}'. Error: {error}");
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
}
