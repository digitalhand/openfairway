using Godot;

public partial class SettingsPanel : CanvasLayer
{
    [Signal]
    public delegate void ClosedEventHandler();

    private const string FallbackPlayerName = "JesseInCode";
    private const string FallbackResolutionPreset = "1728x972";

    private LineEdit _playerNameInput;
    private OptionButton _resolutionOption;
    private CheckBox _fullscreenCheck;
    private HSlider _cameraDistanceSlider;
    private SpinBox _cameraDistanceValue;
    private HSlider _cameraDelaySlider;
    private SpinBox _cameraDelayValue;
    private Button _closeButton;

    private GlobalSettings _globalSettings;
    private AppSettings _appSettings;
    private Setting _playerNameSetting;
    private Setting _resolutionSetting;
    private Setting _fullscreenSetting;
    private Setting _cameraDistanceSetting;
    private Setting _cameraDelaySetting;
    private bool _isSyncingControls;

    public override void _Ready()
    {
        _playerNameInput = GetNode<LineEdit>("Root/Panel/Margin/Content/Tabs/Player/PlayerNameInput");
        _resolutionOption = GetNode<OptionButton>("Root/Panel/Margin/Content/Tabs/Display/ResolutionOption");
        _fullscreenCheck = GetNode<CheckBox>("Root/Panel/Margin/Content/Tabs/Display/FullscreenCheck");
        _cameraDistanceSlider = GetNode<HSlider>("Root/Panel/Margin/Content/Tabs/Game/CameraDistanceRow/CameraDistanceSlider");
        _cameraDistanceValue = GetNode<SpinBox>("Root/Panel/Margin/Content/Tabs/Game/CameraDistanceRow/CameraDistanceValue");
        _cameraDelaySlider = GetNode<HSlider>("Root/Panel/Margin/Content/Tabs/Game/CameraDelayRow/CameraDelaySlider");
        _cameraDelayValue = GetNode<SpinBox>("Root/Panel/Margin/Content/Tabs/Game/CameraDelayRow/CameraDelayValue");
        _closeButton = GetNode<Button>("Root/Panel/Margin/Content/HeaderRow/CloseButton");

        _globalSettings = GetNodeOrNull<GlobalSettings>("/root/GlobalSettings");
        _appSettings = _globalSettings?.AppSettings;

        PopulateResolutionOptions();
        ConnectControlSignals();
        ConnectSettingSignals();
        RefreshControlsFromSettings();

        Visible = false;
    }

    public override void _ExitTree()
    {
        DisconnectControlSignals();
        DisconnectSettingSignals();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible)
            return;

        if (@event.IsActionPressed("ui_cancel"))
        {
            HidePanel();
            GetViewport()?.SetInputAsHandled();
        }
    }

    public void ShowPanel()
    {
        RefreshControlsFromSettings();
        Visible = true;
        _playerNameInput?.GrabFocus();
    }

    public void HidePanel()
    {
        if (!Visible)
            return;

        Visible = false;
        EmitSignal(SignalName.Closed);
    }

    private void PopulateResolutionOptions()
    {
        _resolutionOption.Clear();
        foreach (string preset in AppSettingsDisplayService.Presets)
            _resolutionOption.AddItem(preset);
    }

    private void ConnectControlSignals()
    {
        _closeButton.Pressed += OnClosePressed;
        _playerNameInput.TextSubmitted += OnPlayerNameTextSubmitted;
        _playerNameInput.FocusExited += OnPlayerNameFocusExited;
        _resolutionOption.ItemSelected += OnResolutionSelected;
        _fullscreenCheck.Toggled += OnFullscreenToggled;
        _cameraDistanceSlider.ValueChanged += OnCameraDistanceSliderChanged;
        _cameraDistanceValue.ValueChanged += OnCameraDistanceValueChanged;
        _cameraDelaySlider.ValueChanged += OnCameraDelaySliderChanged;
        _cameraDelayValue.ValueChanged += OnCameraDelayValueChanged;
    }

    private void DisconnectControlSignals()
    {
        if (_closeButton != null)
            _closeButton.Pressed -= OnClosePressed;
        if (_playerNameInput != null)
        {
            _playerNameInput.TextSubmitted -= OnPlayerNameTextSubmitted;
            _playerNameInput.FocusExited -= OnPlayerNameFocusExited;
        }
        if (_resolutionOption != null)
            _resolutionOption.ItemSelected -= OnResolutionSelected;
        if (_fullscreenCheck != null)
            _fullscreenCheck.Toggled -= OnFullscreenToggled;
        if (_cameraDistanceSlider != null)
            _cameraDistanceSlider.ValueChanged -= OnCameraDistanceSliderChanged;
        if (_cameraDistanceValue != null)
            _cameraDistanceValue.ValueChanged -= OnCameraDistanceValueChanged;
        if (_cameraDelaySlider != null)
            _cameraDelaySlider.ValueChanged -= OnCameraDelaySliderChanged;
        if (_cameraDelayValue != null)
            _cameraDelayValue.ValueChanged -= OnCameraDelayValueChanged;
    }

    private void ConnectSettingSignals()
    {
        if (_appSettings == null)
            return;

        _playerNameSetting = _appSettings.PlayerName;
        _resolutionSetting = _appSettings.DisplayResolutionPreset;
        _fullscreenSetting = _appSettings.DisplayFullscreen;
        _cameraDistanceSetting = _appSettings.CameraOrbitDistance;
        _cameraDelaySetting = _appSettings.CameraFollowDelaySeconds;

        _playerNameSetting.SettingChanged += OnAnySettingChanged;
        _resolutionSetting.SettingChanged += OnAnySettingChanged;
        _fullscreenSetting.SettingChanged += OnAnySettingChanged;
        _cameraDistanceSetting.SettingChanged += OnAnySettingChanged;
        _cameraDelaySetting.SettingChanged += OnAnySettingChanged;
    }

    private void DisconnectSettingSignals()
    {
        if (_playerNameSetting != null)
            _playerNameSetting.SettingChanged -= OnAnySettingChanged;
        if (_resolutionSetting != null)
            _resolutionSetting.SettingChanged -= OnAnySettingChanged;
        if (_fullscreenSetting != null)
            _fullscreenSetting.SettingChanged -= OnAnySettingChanged;
        if (_cameraDistanceSetting != null)
            _cameraDistanceSetting.SettingChanged -= OnAnySettingChanged;
        if (_cameraDelaySetting != null)
            _cameraDelaySetting.SettingChanged -= OnAnySettingChanged;
    }

    private void OnAnySettingChanged(Variant _value)
    {
        RefreshControlsFromSettings();
    }

    private void RefreshControlsFromSettings()
    {
        if (_appSettings == null)
            return;

        _isSyncingControls = true;

        _playerNameInput.Text = SanitizePlayerName(_appSettings.PlayerName.Value.ToString());

        string preset = _appSettings.DisplayResolutionPreset.Value.ToString();
        if (string.IsNullOrWhiteSpace(preset))
            preset = FallbackResolutionPreset;
        SelectOrAddResolutionPreset(preset);

        _fullscreenCheck.ButtonPressed = (bool)_appSettings.DisplayFullscreen.Value;

        float cameraDistance = (float)_appSettings.CameraOrbitDistance.Value;
        _cameraDistanceSlider.Value = cameraDistance;
        _cameraDistanceValue.Value = cameraDistance;

        float cameraDelay = (float)_appSettings.CameraFollowDelaySeconds.Value;
        _cameraDelaySlider.Value = cameraDelay;
        _cameraDelayValue.Value = cameraDelay;

        _isSyncingControls = false;
    }

    private void SelectOrAddResolutionPreset(string preset)
    {
        int selectedIndex = -1;
        int itemCount = _resolutionOption.ItemCount;
        for (int i = 0; i < itemCount; i++)
        {
            if (_resolutionOption.GetItemText(i) != preset)
                continue;

            selectedIndex = i;
            break;
        }

        if (selectedIndex < 0)
        {
            _resolutionOption.AddItem(preset);
            selectedIndex = _resolutionOption.ItemCount - 1;
        }

        _resolutionOption.Select(selectedIndex);
    }

    private void OnClosePressed()
    {
        HidePanel();
    }

    private void OnPlayerNameTextSubmitted(string text)
    {
        CommitPlayerName(text);
    }

    private void OnPlayerNameFocusExited()
    {
        CommitPlayerName(_playerNameInput.Text);
    }

    private void CommitPlayerName(string input)
    {
        if (_isSyncingControls || _appSettings == null)
            return;

        _appSettings.PlayerName.SetValue(SanitizePlayerName(input));
    }

    private void OnResolutionSelected(long index)
    {
        if (_isSyncingControls || _appSettings == null)
            return;

        string selectedPreset = _resolutionOption.GetItemText((int)index);
        _appSettings.DisplayResolutionPreset.SetValue(selectedPreset);
        ApplyDisplaySettings();
    }

    private void OnFullscreenToggled(bool pressed)
    {
        if (_isSyncingControls || _appSettings == null)
            return;

        _appSettings.DisplayFullscreen.SetValue(pressed);
        ApplyDisplaySettings();
    }

    private void OnCameraDistanceSliderChanged(double value)
    {
        if (_isSyncingControls || _appSettings == null)
            return;

        _appSettings.CameraOrbitDistance.SetValue((float)value);
    }

    private void OnCameraDistanceValueChanged(double value)
    {
        if (_isSyncingControls || _appSettings == null)
            return;

        _appSettings.CameraOrbitDistance.SetValue((float)value);
    }

    private void OnCameraDelaySliderChanged(double value)
    {
        if (_isSyncingControls || _appSettings == null)
            return;

        _appSettings.CameraFollowDelaySeconds.SetValue((float)value);
    }

    private void OnCameraDelayValueChanged(double value)
    {
        if (_isSyncingControls || _appSettings == null)
            return;

        _appSettings.CameraFollowDelaySeconds.SetValue((float)value);
    }

    private void ApplyDisplaySettings()
    {
        AppSettingsDisplayService.Apply(_appSettings, GetWindow());
        _globalSettings?.SaveAppSettings();
    }

    private static string SanitizePlayerName(string value)
    {
        string trimmed = string.IsNullOrWhiteSpace(value) ? FallbackPlayerName : value.Trim();
        return trimmed.Length > 24 ? trimmed.Substring(0, 24) : trimmed;
    }
}
