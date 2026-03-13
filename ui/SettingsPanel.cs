using System.Collections.Generic;
using Godot;

public partial class SettingsPanel : CanvasLayer
{
    [Signal]
    public delegate void ClosedEventHandler();
    [Signal]
    public delegate void MainMenuRequestedEventHandler();

    public enum SettingsTab
    {
        Player = 0,
        Display = 1,
        Game = 2,
        Panels = 3
    }

    private const string FallbackPlayerName = "JesseInCode";
    private const string FallbackResolutionPreset = "1728x972";
    private const float CameraDistanceMinUnits = 1.0f;
    private const float CameraDistanceMaxUnits = 8.0f;
    private const float FeetPerCameraDistanceUnit = 3.28084f;
    private const float CameraDistanceMinFeet = CameraDistanceMinUnits * FeetPerCameraDistanceUnit;
    private const float CameraDistanceMaxFeet = CameraDistanceMaxUnits * FeetPerCameraDistanceUnit;
    private const float PanelShadowPaddingX = 17.0f;
    private const float PanelShadowPaddingY = 18.0f;
    private const int TracerHistoryDefaultMin = 0;
    private const int TracerHistoryDefaultMax = 5;

    private Control _rootControl;
    private PanelContainer _panelShadow;
    private PanelContainer _panel;
    private LineEdit _playerNameInput;
    private PanelContainer _rangeDefaultClubCard;
    private OptionButton _rangeDefaultClubOption;
    private CheckBox _testShotsCheck;
    private OptionButton _resolutionOption;
    private CheckBox _fullscreenCheck;
    private HSlider _cameraDistanceSlider;
    private SpinBox _cameraDistanceValue;
    private Label _cameraDistanceHelper;
    private HSlider _cameraDelaySlider;
    private SpinBox _cameraDelayValue;
    private Label _cameraDelayHelper;
    private PanelContainer _tracerHistoryCard;
    private HSlider _tracerHistorySlider;
    private SpinBox _tracerHistoryValue;
    private Label _tracerHistoryHelper;
    private SpinBox _tcpPortValue;
    private CheckBox _shotRecordingCheck;
    private LineEdit _shotRecordingPathInput;
    private Button _shotRecordingBrowseButton;
    private Label _shotRecordingHelper;
    private FileDialog _shotRecordingFileDialog;
    private TabContainer _tabs;
    private GridContainer _panelsGrid;
    private Label _panelsEmptyLabel;
    private PanelContainer _panelCardTemplate;
    private Texture2D _panelToggleCheckedIcon;
    private Texture2D _panelToggleUncheckedIcon;
    private Button _mainMenuButton;
    private Button _saveButton;
    private Button _closeButton;

    private GlobalSettings _globalSettings;
    private GameSettings _gameSettings;
    private AppSettings _appSettings;
    private Setting _playerNameSetting;
    private Setting _testShotsSetting;
    private Setting _resolutionSetting;
    private Setting _fullscreenSetting;
    private Setting _cameraDistanceSetting;
    private Setting _cameraDelaySetting;
    private Setting _tcpPortSetting;
    private Setting _shotRecordingEnabledSetting;
    private Setting _shotRecordingPathSetting;
    private Setting _shotTracerCountSetting;
    private Setting _rangeDefaultClubSetting;
    private ShotRecordingService _shotRecordingService;
    private bool _isSyncingControls;
    private bool _isSyncingPanelsGrid;
    private GridCanvas _boundGridCanvas;
    private bool _showTracerHistorySetting;
    private bool _showRangeDefaultClubSetting;
    private readonly List<DataPanel> _boundHudPanels = new();
    private readonly Dictionary<string, CheckBox> _panelVisibilityByName = new();

    public override void _Ready()
    {
        _rootControl = GetNode<Control>("Root");
        _panelShadow = GetNode<PanelContainer>("Root/PanelShadow");
        _panel = GetNode<PanelContainer>("Root/Panel");
        _playerNameInput = GetNode<LineEdit>("Root/Panel/Margin/Content/Tabs/Player/PlayerCard/PlayerCardMargin/PlayerCardRow/PlayerNameInput");
        _rangeDefaultClubCard = GetNode<PanelContainer>("Root/Panel/Margin/Content/Tabs/Player/RangeDefaultClubCard");
        _rangeDefaultClubOption = GetNode<OptionButton>("Root/Panel/Margin/Content/Tabs/Player/RangeDefaultClubCard/RangeDefaultClubMargin/RangeDefaultClubRow/RangeDefaultClubOption");
        _testShotsCheck = GetNode<CheckBox>("Root/Panel/Margin/Content/Tabs/Player/PlayerTestShotsCard/PlayerTestShotsMargin/PlayerTestShotsRow/TestShotsCheck");
        _resolutionOption = GetNode<OptionButton>("Root/Panel/Margin/Content/Tabs/Display/DisplayResolutionCard/DisplayResolutionMargin/DisplayResolutionRow/ResolutionOption");
        _fullscreenCheck = GetNode<CheckBox>("Root/Panel/Margin/Content/Tabs/Display/DisplayResolutionCard/DisplayResolutionMargin/DisplayResolutionRow/FullscreenCheck");
        _cameraDistanceSlider = GetNode<HSlider>("Root/Panel/Margin/Content/Tabs/Game/CameraDistanceCard/CameraDistanceMargin/CameraDistanceContent/CameraDistanceRow/CameraDistanceSlider");
        _cameraDistanceValue = GetNode<SpinBox>("Root/Panel/Margin/Content/Tabs/Game/CameraDistanceCard/CameraDistanceMargin/CameraDistanceContent/CameraDistanceRow/CameraDistanceValue");
        _cameraDistanceHelper = GetNode<Label>("Root/Panel/Margin/Content/Tabs/Game/CameraDistanceCard/CameraDistanceMargin/CameraDistanceContent/CameraDistanceHelper");
        _cameraDelaySlider = GetNode<HSlider>("Root/Panel/Margin/Content/Tabs/Game/CameraDelayCard/CameraDelayMargin/CameraDelayContent/CameraDelayRow/CameraDelaySlider");
        _cameraDelayValue = GetNode<SpinBox>("Root/Panel/Margin/Content/Tabs/Game/CameraDelayCard/CameraDelayMargin/CameraDelayContent/CameraDelayRow/CameraDelayValue");
        _cameraDelayHelper = GetNode<Label>("Root/Panel/Margin/Content/Tabs/Game/CameraDelayCard/CameraDelayMargin/CameraDelayContent/CameraDelayHelper");
        _tracerHistoryCard = GetNode<PanelContainer>("Root/Panel/Margin/Content/Tabs/Game/TracerHistoryCard");
        _tracerHistorySlider = GetNode<HSlider>("Root/Panel/Margin/Content/Tabs/Game/TracerHistoryCard/TracerHistoryMargin/TracerHistoryContent/TracerHistoryRow/TracerHistorySlider");
        _tracerHistoryValue = GetNode<SpinBox>("Root/Panel/Margin/Content/Tabs/Game/TracerHistoryCard/TracerHistoryMargin/TracerHistoryContent/TracerHistoryRow/TracerHistoryValue");
        _tracerHistoryHelper = GetNode<Label>("Root/Panel/Margin/Content/Tabs/Game/TracerHistoryCard/TracerHistoryMargin/TracerHistoryContent/TracerHistoryHelper");
        _tcpPortValue = GetNode<SpinBox>("Root/Panel/Margin/Content/Tabs/Game/TcpPortCard/TcpPortMargin/TcpPortContent/TcpPortRow/TcpPortValue");
        _shotRecordingCheck = GetNode<CheckBox>("Root/Panel/Margin/Content/Tabs/Game/ShotRecordingCard/ShotRecordingMargin/ShotRecordingContent/ShotRecordingRow/ShotRecordingCheck");
        _shotRecordingPathInput = GetNode<LineEdit>("Root/Panel/Margin/Content/Tabs/Game/ShotRecordingCard/ShotRecordingMargin/ShotRecordingContent/ShotRecordingPathRow/ShotRecordingPathInput");
        _shotRecordingBrowseButton = GetNode<Button>("Root/Panel/Margin/Content/Tabs/Game/ShotRecordingCard/ShotRecordingMargin/ShotRecordingContent/ShotRecordingPathRow/ShotRecordingBrowseButton");
        _shotRecordingHelper = GetNode<Label>("Root/Panel/Margin/Content/Tabs/Game/ShotRecordingCard/ShotRecordingMargin/ShotRecordingContent/ShotRecordingHelper");
        _tabs = GetNode<TabContainer>("Root/Panel/Margin/Content/Tabs");
        _panelsGrid = GetNode<GridContainer>("Root/Panel/Margin/Content/Tabs/Panels/PanelsGridScroll/PanelsGrid");
        _panelsEmptyLabel = GetNode<Label>("Root/Panel/Margin/Content/Tabs/Panels/PanelsEmptyLabel");
        _panelCardTemplate = GetNode<PanelContainer>("Root/Panel/Margin/Content/Tabs/Panels/PanelCardTemplate");
        _mainMenuButton = GetNode<Button>("Root/Panel/Margin/Content/HeaderBanner/HeaderMargin/HeaderRow/MainMenuButton");
        _saveButton = GetNode<Button>("Root/Panel/Margin/Content/HeaderBanner/HeaderMargin/HeaderRow/SaveButton");
        _closeButton = GetNode<Button>("Root/Panel/Margin/Content/HeaderBanner/HeaderMargin/HeaderRow/CloseButton");

        _globalSettings = GetNodeOrNull<GlobalSettings>("/root/GlobalSettings");
        _appSettings = _globalSettings?.AppSettings;
        _gameSettings = _globalSettings?.GameSettings;
        _shotRecordingService = GetNodeOrNull<ShotRecordingService>("/root/ShotRecordingService");

        _shotRecordingFileDialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = "Select Shot Recording Directory"
        };
        AddChild(_shotRecordingFileDialog);

        ConfigureDistanceControls();
        ConfigureTracerHistoryControls();
        PopulateRangeDefaultClubOptions();
        CreatePanelToggleIcons();
        ApplyPanelToggleIcons(_testShotsCheck);
        ApplyPanelToggleIcons(_fullscreenCheck);
        ApplyPanelToggleIcons(_shotRecordingCheck);
        RebuildPanelsGrid();
        PopulateResolutionOptions();
        ConnectControlSignals();
        ConnectSettingSignals();
        RefreshControlsFromSettings();
        ApplyTracerHistoryVisibility();
        ApplyRangeDefaultClubVisibility();
        CallDeferred(nameof(SyncPanelShadowToPanel));

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
        ShowPanel(SettingsTab.Player);
    }

    public void ShowPanel(SettingsTab tab)
    {
        RefreshControlsFromSettings();
        SetActiveTab(tab);
        Visible = true;
        CallDeferred(nameof(SyncPanelShadowToPanel));

        if (tab == SettingsTab.Player)
            _playerNameInput?.GrabFocus();
    }

    public void BindHudPanels(GridCanvas gridCanvas, IEnumerable<DataPanel> panels)
    {
        _boundGridCanvas = gridCanvas;
        _boundHudPanels.Clear();

        if (panels != null)
        {
            foreach (DataPanel panel in panels)
            {
                if (panel == null)
                    continue;

                _boundHudPanels.Add(panel);
            }
        }

        RebuildPanelsGrid();
        SyncPanelsGridFromPanelState();
    }

    public void HidePanel()
    {
        if (!Visible)
            return;

        Visible = false;
        EmitSignal(SignalName.Closed);
    }

    public void SetMainMenuButtonVisible(bool visible)
    {
        if (_mainMenuButton != null)
            _mainMenuButton.Visible = visible;
    }

    public void SetTracerHistorySettingVisible(bool visible)
    {
        _showTracerHistorySetting = visible;
        ApplyTracerHistoryVisibility();
    }

    public void SetRangeDefaultClubSettingVisible(bool visible)
    {
        _showRangeDefaultClubSetting = visible;
        ApplyRangeDefaultClubVisibility();
    }

    private void PopulateResolutionOptions()
    {
        _resolutionOption.Clear();
        foreach (string preset in AppSettingsDisplayService.Presets)
            _resolutionOption.AddItem(preset);
    }

    private void ConfigureDistanceControls()
    {
        _cameraDistanceSlider.MinValue = CameraDistanceMinFeet;
        _cameraDistanceSlider.MaxValue = CameraDistanceMaxFeet;
        _cameraDistanceSlider.Step = 0.1f;

        _cameraDistanceValue.MinValue = CameraDistanceMinFeet;
        _cameraDistanceValue.MaxValue = CameraDistanceMaxFeet;
        _cameraDistanceValue.Step = 0.1f;
    }

    private void PopulateRangeDefaultClubOptions()
    {
        if (_rangeDefaultClubOption == null)
            return;

        _rangeDefaultClubOption.Clear();
        foreach (string label in RangeClubCatalog.Labels)
            _rangeDefaultClubOption.AddItem(label);
    }

    private void ConfigureTracerHistoryControls()
    {
        int min = TracerHistoryDefaultMin;
        int max = TracerHistoryDefaultMax;
        if (_gameSettings?.ShotTracerCount != null)
        {
            Setting setting = _gameSettings.ShotTracerCount;
            if (setting.MinValue.VariantType != Variant.Type.Nil)
                min = Mathf.RoundToInt((float)setting.MinValue);
            if (setting.MaxValue.VariantType != Variant.Type.Nil)
                max = Mathf.RoundToInt((float)setting.MaxValue);
        }

        _tracerHistorySlider.MinValue = min;
        _tracerHistorySlider.MaxValue = max;
        _tracerHistorySlider.Step = 1.0f;

        _tracerHistoryValue.MinValue = min;
        _tracerHistoryValue.MaxValue = max;
        _tracerHistoryValue.Step = 1.0f;
    }

    private void ApplyTracerHistoryVisibility()
    {
        if (_tracerHistoryCard != null)
            _tracerHistoryCard.Visible = _showTracerHistorySetting;

        CallDeferred(nameof(SyncPanelShadowToPanel));
    }

    private void ApplyRangeDefaultClubVisibility()
    {
        if (_rangeDefaultClubCard != null)
            _rangeDefaultClubCard.Visible = _showRangeDefaultClubSetting;

        CallDeferred(nameof(SyncPanelShadowToPanel));
    }

    private void ConnectControlSignals()
    {
        if (_panel != null)
            _panel.Resized += OnPanelLayoutChanged;
        if (_rootControl != null)
            _rootControl.Resized += OnPanelLayoutChanged;

        _mainMenuButton.Pressed += OnMainMenuPressed;
        _saveButton.Pressed += OnSavePressed;
        _closeButton.Pressed += OnClosePressed;
        _playerNameInput.TextSubmitted += OnPlayerNameTextSubmitted;
        _playerNameInput.FocusExited += OnPlayerNameFocusExited;
        _rangeDefaultClubOption.ItemSelected += OnRangeDefaultClubSelected;
        _testShotsCheck.Toggled += OnTestShotsToggled;
        _resolutionOption.ItemSelected += OnResolutionSelected;
        _fullscreenCheck.Toggled += OnFullscreenToggled;
        _cameraDistanceSlider.ValueChanged += OnCameraDistanceSliderChanged;
        _cameraDistanceValue.ValueChanged += OnCameraDistanceValueChanged;
        _cameraDelaySlider.ValueChanged += OnCameraDelaySliderChanged;
        _cameraDelayValue.ValueChanged += OnCameraDelayValueChanged;
        _tracerHistorySlider.ValueChanged += OnTracerHistorySliderChanged;
        _tracerHistoryValue.ValueChanged += OnTracerHistoryValueChanged;
        _tcpPortValue.ValueChanged += OnTcpPortValueChanged;
        _shotRecordingCheck.Toggled += OnShotRecordingToggled;
        _shotRecordingBrowseButton.Pressed += OnShotRecordingBrowsePressed;
        _shotRecordingFileDialog.DirSelected += OnShotRecordingDirSelected;
    }

    private void DisconnectControlSignals()
    {
        if (_panel != null)
            _panel.Resized -= OnPanelLayoutChanged;
        if (_rootControl != null)
            _rootControl.Resized -= OnPanelLayoutChanged;

        if (_mainMenuButton != null)
            _mainMenuButton.Pressed -= OnMainMenuPressed;
        if (_saveButton != null)
            _saveButton.Pressed -= OnSavePressed;
        if (_closeButton != null)
            _closeButton.Pressed -= OnClosePressed;
        if (_playerNameInput != null)
        {
            _playerNameInput.TextSubmitted -= OnPlayerNameTextSubmitted;
            _playerNameInput.FocusExited -= OnPlayerNameFocusExited;
        }
        if (_rangeDefaultClubOption != null)
            _rangeDefaultClubOption.ItemSelected -= OnRangeDefaultClubSelected;
        if (_testShotsCheck != null)
            _testShotsCheck.Toggled -= OnTestShotsToggled;
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
        if (_tracerHistorySlider != null)
            _tracerHistorySlider.ValueChanged -= OnTracerHistorySliderChanged;
        if (_tracerHistoryValue != null)
            _tracerHistoryValue.ValueChanged -= OnTracerHistoryValueChanged;
        if (_tcpPortValue != null)
            _tcpPortValue.ValueChanged -= OnTcpPortValueChanged;
        if (_shotRecordingCheck != null)
            _shotRecordingCheck.Toggled -= OnShotRecordingToggled;
        if (_shotRecordingBrowseButton != null)
            _shotRecordingBrowseButton.Pressed -= OnShotRecordingBrowsePressed;
        if (_shotRecordingFileDialog != null)
            _shotRecordingFileDialog.DirSelected -= OnShotRecordingDirSelected;
    }

    private void ConnectSettingSignals()
    {
        if (_appSettings != null)
        {
            _playerNameSetting = _appSettings.PlayerName;
            _testShotsSetting = _appSettings.TestShotsEnabled;
            _resolutionSetting = _appSettings.DisplayResolutionPreset;
            _fullscreenSetting = _appSettings.DisplayFullscreen;
            _cameraDistanceSetting = _appSettings.CameraOrbitDistance;
            _cameraDelaySetting = _appSettings.CameraFollowDelaySeconds;
            _tcpPortSetting = _appSettings.TcpPort;
            _shotRecordingEnabledSetting = _appSettings.ShotRecordingEnabled;
            _shotRecordingPathSetting = _appSettings.ShotRecordingPath;
            _rangeDefaultClubSetting = _appSettings.RangeDefaultClub;

            _playerNameSetting.SettingChanged += OnAnySettingChanged;
            _testShotsSetting.SettingChanged += OnAnySettingChanged;
            _resolutionSetting.SettingChanged += OnAnySettingChanged;
            _fullscreenSetting.SettingChanged += OnAnySettingChanged;
            _cameraDistanceSetting.SettingChanged += OnAnySettingChanged;
            _cameraDelaySetting.SettingChanged += OnAnySettingChanged;
            _tcpPortSetting.SettingChanged += OnAnySettingChanged;
            _shotRecordingEnabledSetting.SettingChanged += OnAnySettingChanged;
            _shotRecordingPathSetting.SettingChanged += OnAnySettingChanged;
            _rangeDefaultClubSetting.SettingChanged += OnAnySettingChanged;
        }

        _shotTracerCountSetting = _gameSettings?.ShotTracerCount;
        if (_shotTracerCountSetting != null)
            _shotTracerCountSetting.SettingChanged += OnAnySettingChanged;
    }

    private void DisconnectSettingSignals()
    {
        if (_playerNameSetting != null)
            _playerNameSetting.SettingChanged -= OnAnySettingChanged;
        if (_testShotsSetting != null)
            _testShotsSetting.SettingChanged -= OnAnySettingChanged;
        if (_resolutionSetting != null)
            _resolutionSetting.SettingChanged -= OnAnySettingChanged;
        if (_fullscreenSetting != null)
            _fullscreenSetting.SettingChanged -= OnAnySettingChanged;
        if (_cameraDistanceSetting != null)
            _cameraDistanceSetting.SettingChanged -= OnAnySettingChanged;
        if (_cameraDelaySetting != null)
            _cameraDelaySetting.SettingChanged -= OnAnySettingChanged;
        if (_tcpPortSetting != null)
            _tcpPortSetting.SettingChanged -= OnAnySettingChanged;
        if (_shotRecordingEnabledSetting != null)
            _shotRecordingEnabledSetting.SettingChanged -= OnAnySettingChanged;
        if (_shotRecordingPathSetting != null)
            _shotRecordingPathSetting.SettingChanged -= OnAnySettingChanged;
        if (_rangeDefaultClubSetting != null)
            _rangeDefaultClubSetting.SettingChanged -= OnAnySettingChanged;
        if (_shotTracerCountSetting != null)
            _shotTracerCountSetting.SettingChanged -= OnAnySettingChanged;
    }

    private void OnAnySettingChanged(Variant _value)
    {
        RefreshControlsFromSettings();
    }

    private void RefreshControlsFromSettings()
    {
        _isSyncingControls = true;

        if (_appSettings != null)
        {
            _playerNameInput.Text = SanitizePlayerName(_appSettings.PlayerName.Value.ToString());
            _testShotsCheck.ButtonPressed = (bool)_appSettings.TestShotsEnabled.Value;

            string preset = _appSettings.DisplayResolutionPreset.Value.ToString();
            if (string.IsNullOrWhiteSpace(preset))
                preset = FallbackResolutionPreset;
            SelectOrAddResolutionPreset(preset);

            _fullscreenCheck.ButtonPressed = (bool)_appSettings.DisplayFullscreen.Value;

            float cameraDistanceUnits = (float)_appSettings.CameraOrbitDistance.Value;
            float cameraDistanceFeet = UnitsToFeet(cameraDistanceUnits);
            int cameraDistanceDisplayFeet = Mathf.RoundToInt(cameraDistanceFeet);
            _cameraDistanceSlider.Value = cameraDistanceFeet;
            _cameraDistanceValue.Value = cameraDistanceFeet;
            _cameraDistanceHelper.Text = $"Distance from ball: {cameraDistanceDisplayFeet} ft";

            float cameraDelay = (float)_appSettings.CameraFollowDelaySeconds.Value;
            _cameraDelaySlider.Value = cameraDelay;
            _cameraDelayValue.Value = cameraDelay;
            _cameraDelayHelper.Text = $"Follow starts after {cameraDelay:0.00} seconds";

            int tcpPort = (int)_appSettings.TcpPort.Value;
            _tcpPortValue.Value = tcpPort;

            _shotRecordingCheck.ButtonPressed = (bool)_appSettings.ShotRecordingEnabled.Value;
            _shotRecordingPathInput.Text = _appSettings.ShotRecordingPath.Value.ToString();
            UpdateShotRecordingHelper();

            string defaultClub = RangeClubCatalog.NormalizeLabel(_appSettings.RangeDefaultClub.Value.ToString());
            int selectedClubIndex = 0;
            for (int i = 0; i < _rangeDefaultClubOption.ItemCount; i++)
            {
                if (_rangeDefaultClubOption.GetItemText(i) == defaultClub)
                {
                    selectedClubIndex = i;
                    break;
                }
            }
            _rangeDefaultClubOption.Select(selectedClubIndex);
        }

        if (_shotTracerCountSetting != null)
        {
            int tracerCount = Mathf.RoundToInt((float)_shotTracerCountSetting.Value);
            _tracerHistorySlider.Value = tracerCount;
            _tracerHistoryValue.Value = tracerCount;
            UpdateTracerHistoryHelper(tracerCount);
        }

        SyncPanelsGridFromPanelState();
        ApplyTracerHistoryVisibility();
        ApplyRangeDefaultClubVisibility();

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

    private void OnSavePressed()
    {
        _globalSettings?.SaveAppSettings();
        HidePanel();
    }

    private void OnMainMenuPressed()
    {
        _globalSettings?.SaveAppSettings();
        EmitSignal(SignalName.MainMenuRequested);
        HidePanel();
    }

    private void OnClosePressed()
    {
        HidePanel();
    }

    private void OnPanelLayoutChanged()
    {
        SyncPanelShadowToPanel();
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

    private void OnRangeDefaultClubSelected(long index)
    {
        if (_isSyncingControls || _appSettings == null || _rangeDefaultClubOption == null || _rangeDefaultClubOption.ItemCount == 0)
            return;

        int safeIndex = Mathf.Clamp((int)index, 0, _rangeDefaultClubOption.ItemCount - 1);
        string club = RangeClubCatalog.NormalizeLabel(_rangeDefaultClubOption.GetItemText(safeIndex));
        _appSettings.RangeDefaultClub.SetValue(club);
    }

    private void OnTestShotsToggled(bool enabled)
    {
        if (_isSyncingControls || _appSettings == null)
            return;

        _appSettings.TestShotsEnabled.SetValue(enabled);
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

        _appSettings.CameraOrbitDistance.SetValue(FeetToUnits((float)value));
    }

    private void OnCameraDistanceValueChanged(double value)
    {
        if (_isSyncingControls || _appSettings == null)
            return;

        _appSettings.CameraOrbitDistance.SetValue(FeetToUnits((float)value));
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

    private void OnTracerHistorySliderChanged(double value)
    {
        if (_isSyncingControls || _shotTracerCountSetting == null)
            return;

        _shotTracerCountSetting.SetValue(Mathf.RoundToInt((float)value));
    }

    private void OnTracerHistoryValueChanged(double value)
    {
        if (_isSyncingControls || _shotTracerCountSetting == null)
            return;

        _shotTracerCountSetting.SetValue(Mathf.RoundToInt((float)value));
    }

    private void OnTcpPortValueChanged(double value)
    {
        if (_isSyncingControls || _appSettings == null)
            return;

        _appSettings.TcpPort.SetValue(Mathf.RoundToInt((float)value));
    }

    private void OnShotRecordingToggled(bool enabled)
    {
        if (_isSyncingControls || _appSettings == null)
            return;

        _appSettings.ShotRecordingEnabled.SetValue(enabled);
    }

    private void OnShotRecordingBrowsePressed()
    {
        _shotRecordingFileDialog?.Popup();
    }

    private void OnShotRecordingDirSelected(string dir)
    {
        if (_isSyncingControls || _appSettings == null)
            return;

        _appSettings.ShotRecordingPath.SetValue(dir);
    }

    private void UpdateShotRecordingHelper()
    {
        if (_shotRecordingHelper == null)
            return;

        if (_shotRecordingService != null && _shotRecordingService.IsRecording)
            _shotRecordingHelper.Text = $"{_shotRecordingService.CurrentSessionName}: {_shotRecordingService.ShotCount} shots recorded";
        else
            _shotRecordingHelper.Text = "Not recording";
    }

    private void UpdateTracerHistoryHelper(int tracerCount)
    {
        if (_tracerHistoryHelper == null)
            return;

        if (tracerCount <= 0)
            _tracerHistoryHelper.Text = "No tracer history retained.";
        else if (tracerCount == 1)
            _tracerHistoryHelper.Text = "Retains the latest tracer in Range.";
        else
            _tracerHistoryHelper.Text = $"Retains the latest {tracerCount} tracers in Range.";
    }

    private void SetActiveTab(SettingsTab tab)
    {
        if (_tabs == null || _tabs.GetTabCount() == 0)
            return;

        int tabIndex = Mathf.Clamp((int)tab, 0, _tabs.GetTabCount() - 1);
        _tabs.CurrentTab = tabIndex;

        if ((SettingsTab)tabIndex == SettingsTab.Panels)
            SyncPanelsGridFromPanelState();
    }

    private void RebuildPanelsGrid()
    {
        if (_panelsGrid == null || _panelCardTemplate == null || _panelsEmptyLabel == null)
            return;

        foreach (Node child in _panelsGrid.GetChildren())
            child.QueueFree();

        _panelVisibilityByName.Clear();
        bool hasPanels = _boundHudPanels.Count > 0;
        _panelsEmptyLabel.Visible = !hasPanels;
        if (!hasPanels)
            return;

        foreach (DataPanel panel in _boundHudPanels)
        {
            PanelContainer card = _panelCardTemplate.Duplicate() as PanelContainer;
            if (card == null)
                continue;

            card.Name = $"{panel.Name}Card";
            card.Visible = true;
            card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

            Label panelNameLabel = card.GetNode<Label>("PanelCardMargin/PanelCardContent/PanelNameLabel");
            panelNameLabel.Text = string.IsNullOrWhiteSpace(panel.Label) ? panel.Name : panel.Label;

            CheckBox visibilityToggle = card.GetNode<CheckBox>("PanelCardMargin/PanelCardContent/PanelVisibilityCheck");
            visibilityToggle.Text = "Visible";
            ApplyPanelToggleIcons(visibilityToggle);
            string panelName = panel.Name;
            visibilityToggle.Toggled += pressed => OnPanelVisibilityToggled(panelName, pressed);

            _panelVisibilityByName[panelName] = visibilityToggle;
            _panelsGrid.AddChild(card);
        }
    }

    private void SyncPanelsGridFromPanelState()
    {
        if (_panelVisibilityByName.Count == 0)
            return;

        _isSyncingPanelsGrid = true;
        foreach (DataPanel panel in _boundHudPanels)
        {
            if (!_panelVisibilityByName.TryGetValue(panel.Name, out CheckBox visibilityToggle))
                continue;

            visibilityToggle.SetPressedNoSignal(panel.Visible);
        }
        _isSyncingPanelsGrid = false;
    }

    private void OnPanelVisibilityToggled(string panelName, bool visible)
    {
        if (_isSyncingPanelsGrid)
            return;

        DataPanel panel = FindBoundPanel(panelName);
        if (panel == null)
            return;

        panel.Visible = visible;
        _boundGridCanvas?.SaveLayout();
        SyncPanelsGridFromPanelState();
    }

    private DataPanel FindBoundPanel(string panelName)
    {
        foreach (DataPanel panel in _boundHudPanels)
        {
            if (panel.Name == panelName)
                return panel;
        }

        return null;
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

    private static float UnitsToFeet(float units)
    {
        return units * FeetPerCameraDistanceUnit;
    }

    private static float FeetToUnits(float feet)
    {
        return feet / FeetPerCameraDistanceUnit;
    }

    private void SyncPanelShadowToPanel()
    {
        if (_panel == null || _panelShadow == null)
            return;

        _panelShadow.Position = _panel.Position - new Vector2(PanelShadowPaddingX, PanelShadowPaddingY);
        _panelShadow.Size = _panel.Size + new Vector2(PanelShadowPaddingX * 2.0f, PanelShadowPaddingY * 2.0f);
    }

    private void CreatePanelToggleIcons()
    {
        _panelToggleCheckedIcon = BuildPanelToggleIcon(isChecked: true);
        _panelToggleUncheckedIcon = BuildPanelToggleIcon(isChecked: false);
    }

    private void ApplyPanelToggleIcons(CheckBox toggle)
    {
        if (toggle == null)
            return;

        if (_panelToggleCheckedIcon != null)
        {
            toggle.AddThemeIconOverride("checked", _panelToggleCheckedIcon);
            toggle.AddThemeIconOverride("checked_disabled", _panelToggleCheckedIcon);
            toggle.AddThemeIconOverride("radio_checked", _panelToggleCheckedIcon);
        }

        if (_panelToggleUncheckedIcon != null)
        {
            toggle.AddThemeIconOverride("unchecked", _panelToggleUncheckedIcon);
            toggle.AddThemeIconOverride("unchecked_disabled", _panelToggleUncheckedIcon);
            toggle.AddThemeIconOverride("radio_unchecked", _panelToggleUncheckedIcon);
        }
    }

    private static Texture2D BuildPanelToggleIcon(bool isChecked)
    {
        const int size = 16;
        Image image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);

        Color fill = new Color(0.070f, 0.149f, 0.243f, 1.0f);
        Color border = new Color(0.820f, 0.900f, 0.980f, 1.0f);
        Color check = new Color(0.929f, 0.969f, 1.0f, 1.0f);
        image.Fill(fill);

        for (int i = 0; i < size; i++)
        {
            image.SetPixel(i, 0, border);
            image.SetPixel(i, size - 1, border);
            image.SetPixel(0, i, border);
            image.SetPixel(size - 1, i, border);
        }

        if (isChecked)
        {
            for (int i = 0; i < 4; i++)
            {
                image.SetPixel(3 + i, 8 + i, check);
                image.SetPixel(4 + i, 8 + i, check);
            }

            for (int i = 0; i < 6; i++)
            {
                image.SetPixel(6 + i, 10 - i, check);
                image.SetPixel(6 + i, 9 - i, check);
            }
        }

        return ImageTexture.CreateFromImage(image);
    }
}
