using Godot;
using Godot.Collections;

public partial class CourseHud : Control
{
    [Signal]
    public delegate void HitShotEventHandler(Dictionary data);

    private static readonly Color ControlsThemeColor = new Color(0.0431373f, 0.180392f, 0.309804f, 0.8f);
    private static readonly Color ControlsFontColor = new Color(0.96f, 0.98f, 1.0f, 1.0f);
    private const string MainMenuScenePath = "res://ui/main_menu.tscn";
    private const string DefaultPlayerName = "JohnDoe";
    private const string DefaultCourseName = "Airways";
    private const int DefaultHoleNumber = 1;
    private const int DefaultPar = 3;
    private const int DefaultYardage = 203;
    private const int DefaultRangeTargetYards = 100;
    private const int DefaultRangeTargetMinYards = 5;
    private const int DefaultRangeTargetMaxYards = 350;

    private string _selectedShotPath = TestShots.DefaultShot;
    private GridCanvas _gridCanvas;
    private Button _settingsMenu;
    private SettingsPanel _settingsPanel;
    private ShotInjector _shotInjector;
    private OptionButton _shotTypeOption;
    private Button _hitShotButton;
    private Label _courseNameLabel;
    private Label _holeNumberLabel;
    private Label _parHeaderLabel;
    private Label _yardageHeaderLabel;
    private Control _courseMetaBar;
    private Control _courseMetaSpacer;
    private Label _playerNameLabel;
    private Control _rangeControlsBar;
    private HSlider _rangeTargetSlider;
    private SpinBox _rangeTargetStepper;
    private OptionButton _rangeClubOption;
    private Label _shotLabel;
    private Label _targetLabel;
    private Label _targetYardageLabel;
    private Label _targetElevationLabel;
    private Label _roundEndScoreOverlay;
    private Tween _roundEndScoreTween;
    private bool _shotControlsVisible;
    private bool _isLeavingScene;
    private readonly System.Collections.Generic.List<DataPanel> _hudPanels = new();
    private Setting _shotInjectorSetting;
    private Setting _testShotsEnabledSetting;
    private Setting _playerNameSetting;
    private Setting _rangeDefaultClubSetting;
    private bool _isRangeHudControlsVisible;
    private bool _isSyncingRangeControls;
    private int _rangeTargetMinYards = DefaultRangeTargetMinYards;
    private int _rangeTargetMaxYards = DefaultRangeTargetMaxYards;
    private int _rangeTargetYards = DefaultRangeTargetYards;

    private DataPanel _panelDistance;
    private DataPanel _panelCarry;
    private DataPanel _panelSide;
    private DataPanel _panelApex;
    private DataPanel _panelSpeed;
    private DataPanel _panelBackSpin;
    private DataPanel _panelSideSpin;
    private DataPanel _panelTotalSpin;
    private DataPanel _panelSpinAxis;
    private DataPanel _panelVLA;
    private DataPanel _panelHLA;

    public override void _Ready()
    {
        _isLeavingScene = false;

        _gridCanvas = GetNode<GridCanvas>("GridCanvas");
        _settingsPanel = GetNodeOrNull<SettingsPanel>("SettingsPanel");
        GlobalSettings globalSettings = GetNode<GlobalSettings>("/root/GlobalSettings");
        _shotInjectorSetting = globalSettings.GameSettings.ShotInjectorEnabled;
        _testShotsEnabledSetting = globalSettings.AppSettings?.TestShotsEnabled;
        _playerNameSetting = globalSettings.AppSettings?.PlayerName;
        _rangeDefaultClubSetting = globalSettings.AppSettings?.RangeDefaultClub;
        _shotInjectorSetting.SettingChanged += OnShotInjectorSettingChanged;
        if (_testShotsEnabledSetting != null)
            _testShotsEnabledSetting.SettingChanged += OnTestShotsEnabledSettingChanged;
        if (_playerNameSetting != null)
            _playerNameSetting.SettingChanged += OnPlayerNameSettingChanged;
        if (_rangeDefaultClubSetting != null)
            _rangeDefaultClubSetting.SettingChanged += OnRangeDefaultClubSettingChanged;

        _shotInjector = GetNode<ShotInjector>("ShotInjector");
        _shotInjector.Inject += OnShotInjectorInject;

        _hitShotButton = GetNode<Button>("OverlayLayer/CourseHeaderControlsRow/HitShotButton");
        _hitShotButton.Pressed += OnHitShotPressed;

        _shotTypeOption = GetNode<OptionButton>("OverlayLayer/CourseHeaderControlsRow/ShotTypeOption");
        _shotTypeOption.ItemSelected += OnShotTypeSelected;
        _courseNameLabel = GetNode<Label>("OverlayLayer/CourseHeaderCard/InfoBlock/CourseNameBar/CourseNameLabel");
        _holeNumberLabel = GetNode<Label>("OverlayLayer/CourseHeaderCard/HoleBox/HoleNumberLabel");
        _parHeaderLabel = GetNode<Label>("OverlayLayer/CourseHeaderCard/InfoBlock/CourseMetaBar/MetaHBox/ParLabel");
        _yardageHeaderLabel = GetNode<Label>("OverlayLayer/CourseHeaderCard/InfoBlock/CourseMetaBar/MetaHBox/YardageLabel");
        _courseMetaBar = GetNodeOrNull<Control>("OverlayLayer/CourseHeaderCard/InfoBlock/CourseMetaBar");
        _courseMetaSpacer = GetNodeOrNull<Control>("OverlayLayer/CourseHeaderCard/InfoBlock/CourseMetaBar/MetaHBox/MetaSpacer");
        _playerNameLabel = GetNode<Label>("OverlayLayer/PlayerShotCard/TopBar/PlayerNameLabel");
        _rangeControlsBar = GetNode<Control>("OverlayLayer/PlayerShotCard/RangeControlsBar");
        _rangeTargetSlider = GetNode<HSlider>("OverlayLayer/PlayerShotCard/RangeControlsBar/RangeControlsRow/TargetSlider");
        _rangeTargetStepper = GetNode<SpinBox>("OverlayLayer/PlayerShotCard/RangeControlsBar/RangeControlsRow/TargetStepper");
        _rangeClubOption = GetNode<OptionButton>("OverlayLayer/PlayerShotCard/RangeControlsBar/RangeControlsRow/ClubOption");
        _shotLabel = GetNode<Label>("OverlayLayer/PlayerShotCard/BottomBar/BottomRow/ShotLabel");
        _targetLabel = GetNode<Label>("OverlayLayer/PlayerShotCard/BottomBar/BottomRow/TargetLabel");
        _targetYardageLabel = GetNode<Label>("OverlayLayer/PlayerShotCard/BottomBar/BottomRow/YardageLabel");
        _targetElevationLabel = GetNode<Label>("OverlayLayer/PlayerShotCard/BottomBar/BottomRow/DeltaLabel");
        _roundEndScoreOverlay = GetNode<Label>("OverlayLayer/RoundEndScoreOverlay");

        SetPlayerName(_playerNameSetting != null ? _playerNameSetting.Value.ToString() : DefaultPlayerName);
        SetCourseHeader(DefaultCourseName, DefaultHoleNumber, DefaultPar, DefaultYardage);
        SetStrokeCount(0);
        SetScoreUnknown();
        SetTargetYardageUnknown();
        SetTargetElevationUnknown();
        HideRoundEndScore();

        _panelDistance = GetNode<DataPanel>("GridCanvas/Distance");
        _panelCarry = GetNode<DataPanel>("GridCanvas/Carry");
        _panelSide = GetNode<DataPanel>("GridCanvas/Side");
        _panelApex = GetNode<DataPanel>("GridCanvas/Apex");
        _panelSpeed = GetNode<DataPanel>("GridCanvas/Speed");
        _panelBackSpin = GetNode<DataPanel>("GridCanvas/BackSpin");
        _panelSideSpin = GetNode<DataPanel>("GridCanvas/SideSpin");
        _panelTotalSpin = GetNode<DataPanel>("GridCanvas/TotalSpin");
        _panelSpinAxis = GetNode<DataPanel>("GridCanvas/SpinAxis");
        _panelVLA = GetNode<DataPanel>("GridCanvas/VLA");
        _panelHLA = GetNode<DataPanel>("GridCanvas/HLA");
        InitializeHudPanels();
        ConnectPanelContextSignals();
        _settingsPanel?.BindHudPanels(_gridCanvas, _hudPanels);
        _settingsPanel?.SetMainMenuButtonVisible(true);
        if (_settingsPanel != null)
            _settingsPanel.MainMenuRequested += OnSettingsPanelMainMenuRequested;

        PopulateShotTypes();
        PopulateRangeClubOptions();
        ConfigureRangeHudControls(
            DefaultRangeTargetMinYards,
            DefaultRangeTargetMaxYards,
            DefaultRangeTargetYards,
            _rangeDefaultClubSetting != null ? _rangeDefaultClubSetting.Value.ToString() : AppSettings.DefaultRangeDefaultClub
        );
        ConnectRangeControlSignals();
        SetupSettingsMenu();
        ApplyDropdownThemes();
        SetRangeHudControlsVisible(false);
        SetShotControlsVisible(true);
    }

    public override void _ExitTree()
    {
        if (_shotInjectorSetting != null)
            _shotInjectorSetting.SettingChanged -= OnShotInjectorSettingChanged;
        if (_testShotsEnabledSetting != null)
            _testShotsEnabledSetting.SettingChanged -= OnTestShotsEnabledSettingChanged;
        if (_playerNameSetting != null)
            _playerNameSetting.SettingChanged -= OnPlayerNameSettingChanged;
        if (_rangeDefaultClubSetting != null)
            _rangeDefaultClubSetting.SettingChanged -= OnRangeDefaultClubSettingChanged;
        if (_shotInjector != null)
            _shotInjector.Inject -= OnShotInjectorInject;
        if (_shotTypeOption != null)
            _shotTypeOption.ItemSelected -= OnShotTypeSelected;
        if (_hitShotButton != null)
            _hitShotButton.Pressed -= OnHitShotPressed;
        DisconnectRangeControlSignals();
        if (_settingsMenu != null)
            _settingsMenu.Pressed -= OnSettingsMenuPressed;
        if (_settingsPanel != null)
            _settingsPanel.MainMenuRequested -= OnSettingsPanelMainMenuRequested;
        DisconnectPanelContextSignals();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
            return;

        if (keyEvent.Keycode == Key.P)
        {
            SetShotControlsVisible(!_shotControlsVisible);
            return;
        }

        if (keyEvent.Keycode == Key.F)
            ToggleFullscreen();
    }

    public void SetData(Dictionary data)
    {
        var globalSettings = GetNodeOrNull<GlobalSettings>("/root/GlobalSettings");
        if (globalSettings?.GameSettings == null)
            return;

        var units = (PhysicsEnums.Units)(int)globalSettings.GameSettings.GameUnits.Value;
        string speedUnit = units == PhysicsEnums.Units.Imperial ? "mph" : "m/s";

        _panelDistance.SetData(data["Distance"].ToString());
        _panelCarry.SetData(data["Carry"].ToString());
        _panelSide.SetData(data["Offline"].ToString());
        _panelApex.SetData(data["Apex"].ToString());
        _panelSpeed.SetUnits(speedUnit);
        _panelSpeed.SetData(data["Speed"].ToString());
        _panelBackSpin.SetUnits("rpm");
        _panelBackSpin.SetData(data["BackSpin"].ToString());
        _panelSideSpin.SetUnits("rpm");
        _panelSideSpin.SetData(data["SideSpin"].ToString());
        _panelTotalSpin.SetUnits("rpm");
        _panelTotalSpin.SetData(data["TotalSpin"].ToString());
        _panelSpinAxis.SetUnits("deg");
        _panelSpinAxis.SetData(data["SpinAxis"].ToString());
        _panelVLA.SetData(FormatAngle(data.ContainsKey("VLA") ? data["VLA"] : 0.0f));
        _panelHLA.SetData(FormatAngle(data.ContainsKey("HLA") ? data["HLA"] : 0.0f));
    }

    public void SetTotalDistance(string text)
    {
        GetNode<Label>("OverlayLayer/TotalDistanceOverlay").Text = text;
        GetNode("OverlayLayer/TotalDistanceOverlay").Set("visible", true);
    }

    public void ClearTotalDistance()
    {
        GetNode("OverlayLayer/TotalDistanceOverlay").Set("visible", false);
        GetNode<Label>("OverlayLayer/TotalDistanceOverlay").Text = "Total Distance --";
    }

    public void SetStrokeCount(int strokes)
    {
        if (_shotLabel == null)
            return;

        _shotLabel.Text = $"Shot {Mathf.Max(0, strokes) + 1}";
    }

    public void SetFinalStrokeCount(int strokes)
    {
        if (_shotLabel == null)
            return;

        _shotLabel.Text = $"Shot {Mathf.Max(1, strokes)}";
    }

    public void SetTargetYardage(float yards)
    {
        if (_targetYardageLabel == null)
            return;

        _targetYardageLabel.Text = $"{Mathf.Max(0.0f, yards):F1} YDS";
    }

    public void SetTargetDistanceText(string text)
    {
        if (_targetYardageLabel == null)
            return;

        _targetYardageLabel.Text = string.IsNullOrWhiteSpace(text) ? "--.- YDS" : text.Trim().ToUpperInvariant();
    }

    public void SetTargetYardageUnknown()
    {
        if (_targetYardageLabel == null)
            return;

        _targetYardageLabel.Text = "--.- YDS";
    }

    public void SetTargetElevationFeet(int feet)
    {
        if (_targetElevationLabel == null)
            return;

        ElevationVisual visual = ElevationPresenter.Build(feet, includeSignInText: false);
        _targetElevationLabel.Text = $"{visual.Arrow} {visual.Text}";
        _targetElevationLabel.AddThemeColorOverride("font_color", visual.Color);
    }

    public void SetTargetElevationUnknown()
    {
        if (_targetElevationLabel == null)
            return;

        ElevationVisual visual = ElevationPresenter.Build(0, includeSignInText: false);
        _targetElevationLabel.Text = $"{visual.Arrow} {visual.Text}";
        _targetElevationLabel.AddThemeColorOverride("font_color", visual.Color);
    }

    public void SetTargetElevationVisible(bool visible)
    {
        if (_targetElevationLabel != null)
            _targetElevationLabel.Visible = visible;
    }

    public void SetRangeHudControlsVisible(bool visible)
    {
        _isRangeHudControlsVisible = visible;
        if (_rangeControlsBar != null)
            _rangeControlsBar.Visible = visible;
        if (_targetLabel != null)
            _targetLabel.Visible = visible;
    }

    public void ConfigureRangeHudControls(int minYards, int maxYards, int defaultYards, string defaultClub)
    {
        _rangeTargetMinYards = Mathf.Max(0, minYards);
        _rangeTargetMaxYards = Mathf.Max(_rangeTargetMinYards, maxYards);
        _rangeTargetYards = Mathf.Clamp(defaultYards, _rangeTargetMinYards, _rangeTargetMaxYards);

        _isSyncingRangeControls = true;
        ConfigureTargetSlider();
        ConfigureTargetStepper();
        if (_rangeTargetSlider != null)
            _rangeTargetSlider.Value = _rangeTargetYards;
        if (_rangeTargetStepper != null)
            _rangeTargetStepper.Value = _rangeTargetYards;
        SetSelectedRangeClub(defaultClub);
        _isSyncingRangeControls = false;
    }

    public int GetRangeTargetYardage()
    {
        return _rangeTargetYards;
    }

    public string GetRangeSelectedClubFileTag()
    {
        return RangeClubCatalog.ToFileTag(GetRangeSelectedClubLabel());
    }

    public void SetScoreLabel(string label)
    {
        // Score logic is still computed in gameplay code, but hidden in this HUD revision.
    }

    public void SetScoreUnknown()
    {
        // Score logic is still computed in gameplay code, but hidden in this HUD revision.
    }

    public void ShowRoundEndScore(string label)
    {
        if (_roundEndScoreOverlay == null)
            return;

        string safeLabel = string.IsNullOrWhiteSpace(label) ? "PAR" : label.Trim();
        _roundEndScoreOverlay.Text = safeLabel.ToUpperInvariant();
        _roundEndScoreTween?.Kill();

        _roundEndScoreOverlay.PivotOffset = _roundEndScoreOverlay.Size * 0.5f;
        _roundEndScoreOverlay.Scale = new Vector2(0.82f, 0.82f);
        _roundEndScoreOverlay.Modulate = new Color(1, 1, 1, 0);
        _roundEndScoreOverlay.Visible = true;

        _roundEndScoreTween = CreateTween();
        _roundEndScoreTween.SetTrans(Tween.TransitionType.Cubic);
        _roundEndScoreTween.SetEase(Tween.EaseType.Out);
        _roundEndScoreTween.Parallel().TweenProperty(_roundEndScoreOverlay, "scale", Vector2.One, 0.28f);
        _roundEndScoreTween.Parallel().TweenProperty(_roundEndScoreOverlay, "modulate:a", 1.0f, 0.2f);
    }

    public void HideRoundEndScore()
    {
        if (_roundEndScoreOverlay == null)
            return;

        _roundEndScoreTween?.Kill();
        _roundEndScoreTween = null;
        _roundEndScoreOverlay.Visible = false;
        _roundEndScoreOverlay.Text = string.Empty;
        _roundEndScoreOverlay.Scale = Vector2.One;
        _roundEndScoreOverlay.Modulate = Colors.White;
    }

    public void SetCourseHeader(string courseName, int holeNumber, int par, int yardage)
    {
        if (_courseNameLabel != null)
            _courseNameLabel.Text = string.IsNullOrWhiteSpace(courseName) ? DefaultCourseName : courseName.Trim();

        if (_holeNumberLabel != null)
            _holeNumberLabel.Text = Mathf.Max(1, holeNumber).ToString();

        if (_parHeaderLabel != null)
            _parHeaderLabel.Text = $"PAR {Mathf.Max(1, par)}";

        SetCourseHeaderYardage(yardage);
    }

    public void SetPlayerName(string playerName)
    {
        if (_playerNameLabel == null)
            return;

        string safeName = string.IsNullOrWhiteSpace(playerName) ? DefaultPlayerName : playerName.Trim();
        _playerNameLabel.Text = safeName;
    }

    public void SetCourseHeaderYardage(int yardage)
    {
        if (_yardageHeaderLabel == null)
            return;

        _yardageHeaderLabel.Text = $"{Mathf.Max(0, yardage)} YDS";
    }

    public void SetCourseMetaVisible(bool visible)
    {
        if (_courseMetaBar != null)
            _courseMetaBar.Visible = true;

        if (_parHeaderLabel != null)
            _parHeaderLabel.Visible = visible;
        if (_yardageHeaderLabel != null)
            _yardageHeaderLabel.Visible = visible;
        if (_courseMetaSpacer != null)
            _courseMetaSpacer.Visible = visible;

        if (_courseMetaBar != null)
            _courseMetaBar.MouseFilter = Control.MouseFilterEnum.Ignore;
    }

    public void SetTracerHistorySettingVisible(bool visible)
    {
        _settingsPanel?.SetTracerHistorySettingVisible(visible);
    }

    public void SetRangeDefaultClubSettingVisible(bool visible)
    {
        _settingsPanel?.SetRangeDefaultClubSettingVisible(visible);
    }

    private string FormatAngle(Variant val)
    {
        if (val.VariantType == Variant.Type.Float || val.VariantType == Variant.Type.Int)
            return $"{(float)val:F1}";

        return "0.0";
    }

    private void OnShotInjectorInject(Dictionary data)
    {
        if (!IsTestShotsEnabled())
            return;

        EmitSignal(SignalName.HitShot, data);
    }

    private void OnShotInjectorSettingChanged(Variant _value)
    {
        ApplyTestShotVisibility();
    }

    private void OnTestShotsEnabledSettingChanged(Variant _value)
    {
        ApplyTestShotVisibility();
    }

    private bool IsTestShotsEnabled()
    {
        if (_testShotsEnabledSetting == null)
            return AppSettings.DefaultTestShotsEnabled;

        return (bool)_testShotsEnabledSetting.Value;
    }

    private void ApplyTestShotVisibility()
    {
        bool testShotsEnabled = IsTestShotsEnabled();
        bool showTopControls = testShotsEnabled && _shotControlsVisible;

        if (_shotTypeOption != null)
            _shotTypeOption.Visible = showTopControls;
        if (_hitShotButton != null)
            _hitShotButton.Visible = showTopControls;

        if (_shotInjector != null)
        {
            bool showShotInjector = testShotsEnabled
                && _shotInjectorSetting != null
                && (bool)_shotInjectorSetting.Value;
            _shotInjector.Visible = showShotInjector;
        }
    }

    private void PopulateRangeClubOptions()
    {
        if (_rangeClubOption == null)
            return;

        _rangeClubOption.Clear();
        int index = 0;
        foreach (string label in RangeClubCatalog.Labels)
        {
            _rangeClubOption.AddItem(label);
            _rangeClubOption.SetItemMetadata(index, label);
            index++;
        }
    }

    private void ConfigureTargetStepper()
    {
        if (_rangeTargetStepper == null)
            return;

        _rangeTargetStepper.MinValue = _rangeTargetMinYards;
        _rangeTargetStepper.MaxValue = _rangeTargetMaxYards;
        _rangeTargetStepper.Step = 1.0f;
        _rangeTargetStepper.Rounded = true;
        _rangeTargetStepper.Value = _rangeTargetYards;
    }

    private void ConfigureTargetSlider()
    {
        if (_rangeTargetSlider == null)
            return;

        _rangeTargetSlider.MinValue = _rangeTargetMinYards;
        _rangeTargetSlider.MaxValue = _rangeTargetMaxYards;
        _rangeTargetSlider.Step = 1.0f;
        _rangeTargetSlider.Value = _rangeTargetYards;
    }

    private void ConnectRangeControlSignals()
    {
        if (_rangeTargetSlider != null)
            _rangeTargetSlider.ValueChanged += OnRangeTargetSliderChanged;
        if (_rangeTargetStepper != null)
            _rangeTargetStepper.ValueChanged += OnRangeTargetStepperChanged;
        if (_rangeClubOption != null)
            _rangeClubOption.ItemSelected += OnRangeClubSelected;
    }

    private void DisconnectRangeControlSignals()
    {
        if (_rangeTargetSlider != null)
            _rangeTargetSlider.ValueChanged -= OnRangeTargetSliderChanged;
        if (_rangeTargetStepper != null)
            _rangeTargetStepper.ValueChanged -= OnRangeTargetStepperChanged;
        if (_rangeClubOption != null)
            _rangeClubOption.ItemSelected -= OnRangeClubSelected;
    }

    private void OnRangeTargetSliderChanged(double value)
    {
        if (_isSyncingRangeControls)
            return;

        _rangeTargetYards = Mathf.Clamp(Mathf.RoundToInt((float)value), _rangeTargetMinYards, _rangeTargetMaxYards);
        _isSyncingRangeControls = true;
        if (_rangeTargetStepper != null)
            _rangeTargetStepper.Value = _rangeTargetYards;
        if (_rangeTargetSlider != null)
            _rangeTargetSlider.Value = _rangeTargetYards;
        _isSyncingRangeControls = false;
    }

    private void OnRangeTargetStepperChanged(double value)
    {
        if (_isSyncingRangeControls)
            return;

        _rangeTargetYards = Mathf.Clamp(Mathf.RoundToInt((float)value), _rangeTargetMinYards, _rangeTargetMaxYards);
        _isSyncingRangeControls = true;
        if (_rangeTargetSlider != null)
            _rangeTargetSlider.Value = _rangeTargetYards;
        if (_rangeTargetStepper != null)
            _rangeTargetStepper.Value = _rangeTargetYards;
        _isSyncingRangeControls = false;
    }

    private void OnRangeClubSelected(long _index)
    {
        // Selection is pulled on demand by gameplay/recording flow.
    }

    private void OnRangeDefaultClubSettingChanged(Variant value)
    {
        if (!_isRangeHudControlsVisible)
            return;

        SetSelectedRangeClub(value.ToString());
    }

    private void SetSelectedRangeClub(string clubLabel)
    {
        if (_rangeClubOption == null)
            return;

        string normalized = RangeClubCatalog.NormalizeLabel(clubLabel);
        int selectedIndex = -1;
        for (int i = 0; i < _rangeClubOption.ItemCount; i++)
        {
            if (_rangeClubOption.GetItemText(i) == normalized)
            {
                selectedIndex = i;
                break;
            }
        }

        if (selectedIndex < 0 && _rangeClubOption.ItemCount > 0)
            selectedIndex = 0;

        if (selectedIndex >= 0)
            _rangeClubOption.Select(selectedIndex);
    }

    private string GetRangeSelectedClubLabel()
    {
        if (_rangeClubOption == null || _rangeClubOption.ItemCount == 0)
            return RangeClubCatalog.DefaultClubLabel;

        int selected = _rangeClubOption.Selected;
        if (selected < 0 || selected >= _rangeClubOption.ItemCount)
            selected = 0;

        return RangeClubCatalog.NormalizeLabel(_rangeClubOption.GetItemText(selected));
    }

    private void ToggleFullscreen()
    {
        Window window = GetWindow();
        if (window == null)
            return;

        window.Mode = window.Mode == Window.ModeEnum.Fullscreen
            ? Window.ModeEnum.Windowed
            : Window.ModeEnum.Fullscreen;
    }

    private void PopulateShotTypes()
    {
        _shotTypeOption.Clear();
        int idx = 0;
        foreach (var kvp in TestShots.Shots)
        {
            _shotTypeOption.AddItem(kvp.Key);
            _shotTypeOption.SetItemMetadata(idx, kvp.Value);
            idx++;
        }

        _shotTypeOption.Select(0);
    }

    private void OnShotTypeSelected(long index)
    {
        var metadata = _shotTypeOption.GetItemMetadata((int)index);
        if (metadata.VariantType == Variant.Type.String)
            _selectedShotPath = (string)metadata;
    }

    private void OnHitShotPressed()
    {
        if (!IsTestShotsEnabled())
            return;

        var data = ShotLoader.LoadShotFromFile(_selectedShotPath);

        if (data.Count == 0)
        {
            PhysicsLogger.Info($"Hit Shot: Failed to load shot data from {_selectedShotPath}");
            return;
        }

        PhysicsLogger.Info($"Hit Shot: Loaded from {_selectedShotPath}");
        EmitSignal(SignalName.HitShot, data);
    }

    private void InitializeHudPanels()
    {
        _hudPanels.Clear();
        _hudPanels.Add(_panelDistance);
        _hudPanels.Add(_panelCarry);
        _hudPanels.Add(_panelSide);
        _hudPanels.Add(_panelApex);
        _hudPanels.Add(_panelSpeed);
        _hudPanels.Add(_panelBackSpin);
        _hudPanels.Add(_panelSideSpin);
        _hudPanels.Add(_panelTotalSpin);
        _hudPanels.Add(_panelSpinAxis);
        _hudPanels.Add(_panelVLA);
        _hudPanels.Add(_panelHLA);
    }

    private void ConnectPanelContextSignals()
    {
        foreach (DataPanel panel in _hudPanels)
            panel.PanelContextRequested += OnPanelContextRequested;
    }

    private void DisconnectPanelContextSignals()
    {
        foreach (DataPanel panel in _hudPanels)
            panel.PanelContextRequested -= OnPanelContextRequested;
    }

    private void OnPanelContextRequested(DataPanel _panel)
    {
        _settingsPanel?.ShowPanel(SettingsPanel.SettingsTab.Panels);
    }

    private void ApplyDropdownThemes()
    {
        ApplyPopupTheme(_shotTypeOption?.GetPopup());
        ApplyPopupTheme(_rangeClubOption?.GetPopup());
    }

    private void ApplyPopupTheme(PopupMenu popup)
    {
        if (popup == null)
            return;

        popup.AddThemeStyleboxOverride("panel", CreatePopupPanelStyle());
        popup.AddThemeStyleboxOverride("hover", CreatePopupHoverStyle());
        popup.AddThemeColorOverride("font_color", ControlsFontColor);
        popup.AddThemeColorOverride("font_hover_color", ControlsFontColor);
        popup.AddThemeColorOverride("font_separator_color", ControlsFontColor);
        popup.AddThemeColorOverride("font_disabled_color", new Color(ControlsFontColor.R, ControlsFontColor.G, ControlsFontColor.B, 0.6f));
    }

    private static StyleBoxFlat CreatePopupPanelStyle()
    {
        return new StyleBoxFlat
        {
            BgColor = ControlsThemeColor,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            BorderColor = new Color(1, 1, 1, 0.25f),
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomRight = 6,
            CornerRadiusBottomLeft = 6
        };
    }

    private static StyleBoxFlat CreatePopupHoverStyle()
    {
        return new StyleBoxFlat
        {
            BgColor = ControlsThemeColor,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            BorderColor = new Color(1, 1, 1, 0.5f),
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomRight = 4,
            CornerRadiusBottomLeft = 4
        };
    }

    private void SetupSettingsMenu()
    {
        _settingsMenu = GetNode<Button>("OverlayLayer/CourseHeaderCard/SettingsBox/SettingsMenu");
        _settingsMenu.Pressed += OnSettingsMenuPressed;
    }

    private void OnSettingsMenuPressed()
    {
        if (_isLeavingScene)
            return;

        _settingsPanel?.ShowPanel(SettingsPanel.SettingsTab.Player);
    }

    private void OnSettingsPanelMainMenuRequested()
    {
        if (_isLeavingScene)
            return;

        _isLeavingScene = true;
        if (_settingsMenu != null)
            _settingsMenu.Disabled = true;

        _settingsPanel?.HidePanel();

        Error error = GetTree().ChangeSceneToFile(MainMenuScenePath);
        if (error != Error.Ok)
        {
            _isLeavingScene = false;
            if (_settingsMenu != null)
                _settingsMenu.Disabled = false;
            GD.PushError($"Settings menu: failed to load main menu scene '{MainMenuScenePath}'. Error: {error}");
        }
    }

    private void OnPlayerNameSettingChanged(Variant value)
    {
        SetPlayerName(value.ToString());
    }

    private void SetShotControlsVisible(bool visible)
    {
        _shotControlsVisible = visible;
        ApplyTestShotVisibility();
    }
}
