using Godot;
using Godot.Collections;

public partial class CourseHud : Control
{
    [Signal]
    public delegate void HitShotEventHandler(Dictionary data);

    private static readonly Color ControlsThemeColor = new Color(0.0431373f, 0.180392f, 0.309804f, 0.8f);
    private static readonly Color ControlsFontColor = new Color(0.96f, 0.98f, 1.0f, 1.0f);
    private const string DefaultPlayerName = "JesseInCode";
    private const string DefaultCourseName = "Airways";
    private const int DefaultHoleNumber = 1;
    private const int DefaultPar = 3;
    private const int DefaultYardage = 203;

    private string _selectedShotPath = TestShots.DefaultShot;
    private GridCanvas _gridCanvas;
    private Button _panelsMenu;
    private PopupMenu _panelsPopup;
    private OptionButton _shotTypeOption;
    private Button _hitShotButton;
    private Label _courseNameLabel;
    private Label _holeNumberLabel;
    private Label _parHeaderLabel;
    private Label _yardageHeaderLabel;
    private Label _playerNameLabel;
    private Label _shotLabel;
    private Label _targetYardageLabel;
    private Label _targetElevationLabel;
    private Label _roundEndScoreOverlay;
    private Tween _roundEndScoreTween;
    private bool _shotControlsVisible;
    private readonly System.Collections.Generic.Dictionary<int, string> _panelMenuIndexToName = new();
    private Setting _shotInjectorSetting;

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
        _gridCanvas = GetNode<GridCanvas>("GridCanvas");
        _shotInjectorSetting = GetNode<GlobalSettings>("/root/GlobalSettings").RangeSettings.ShotInjectorEnabled;
        _shotInjectorSetting.SettingChanged += ToggleShotInjector;

        var shotInjector = GetNode<ShotInjector>("ShotInjector");
        shotInjector.Inject += OnShotInjectorInject;

        _hitShotButton = GetNode<Button>("OverlayLayer/CourseHeaderControlsRow/HitShotButton");
        _hitShotButton.Pressed += OnHitShotPressed;

        _shotTypeOption = GetNode<OptionButton>("OverlayLayer/CourseHeaderControlsRow/ShotTypeOption");
        _shotTypeOption.ItemSelected += OnShotTypeSelected;
        _courseNameLabel = GetNode<Label>("OverlayLayer/CourseHeaderCard/InfoBlock/CourseNameBar/CourseNameLabel");
        _holeNumberLabel = GetNode<Label>("OverlayLayer/CourseHeaderCard/HoleBox/HoleNumberLabel");
        _parHeaderLabel = GetNode<Label>("OverlayLayer/CourseHeaderCard/InfoBlock/CourseMetaBar/MetaHBox/ParLabel");
        _yardageHeaderLabel = GetNode<Label>("OverlayLayer/CourseHeaderCard/InfoBlock/CourseMetaBar/MetaHBox/YardageLabel");
        _playerNameLabel = GetNode<Label>("OverlayLayer/PlayerShotCard/TopBar/PlayerNameLabel");
        _shotLabel = GetNode<Label>("OverlayLayer/PlayerShotCard/BottomBar/BottomRow/ShotLabel");
        _targetYardageLabel = GetNode<Label>("OverlayLayer/PlayerShotCard/BottomBar/BottomRow/YardageLabel");
        _targetElevationLabel = GetNode<Label>("OverlayLayer/PlayerShotCard/BottomBar/BottomRow/DeltaLabel");
        _roundEndScoreOverlay = GetNode<Label>("OverlayLayer/RoundEndScoreOverlay");

        _playerNameLabel.Text = DefaultPlayerName;
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

        PopulateShotTypes();
        SetupPanelsMenu();
        ApplyDropdownThemes();
        SetShotControlsVisible(true);
    }

    public override void _ExitTree()
    {
        if (_shotInjectorSetting != null)
            _shotInjectorSetting.SettingChanged -= ToggleShotInjector;
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
        var units = (PhysicsEnums.Units)(int)GetNode<GlobalSettings>("/root/GlobalSettings").RangeSettings.RangeUnits.Value;
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

    public void SetCourseHeaderYardage(int yardage)
    {
        if (_yardageHeaderLabel == null)
            return;

        _yardageHeaderLabel.Text = $"{Mathf.Max(0, yardage)} YDS";
    }

    private string FormatAngle(Variant val)
    {
        if (val.VariantType == Variant.Type.Float || val.VariantType == Variant.Type.Int)
            return $"{(float)val:F1}";

        return "0.0";
    }

    private void OnShotInjectorInject(Dictionary data)
    {
        EmitSignal(SignalName.HitShot, data);
    }

    private void ToggleShotInjector(Variant value)
    {
        GetNode("ShotInjector").Set("visible", value);
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
        var data = ShotLoader.LoadShotFromFile(_selectedShotPath);

        if (data.Count == 0)
        {
            PhysicsLogger.Info($"Hit Shot: Failed to load shot data from {_selectedShotPath}");
            return;
        }

        PhysicsLogger.Info($"Hit Shot: Loaded from {_selectedShotPath}");
        EmitSignal(SignalName.HitShot, data);
    }

    private void SetupPanelsMenu()
    {
        _panelsMenu = GetNode<Button>("OverlayLayer/CourseHeaderControlsRow/PanelsMenu");
        _panelsPopup = GetNode<PopupMenu>("OverlayLayer/CourseHeaderControlsRow/PanelsMenu/PanelsPopup");
        _panelsPopup.Clear();
        _panelMenuIndexToName.Clear();

        int index = 0;
        foreach (var child in _gridCanvas.GetChildren())
        {
            if (child is DataPanel panel)
            {
                string label = string.IsNullOrWhiteSpace(panel.Label) ? panel.Name : panel.Label;
                _panelsPopup.AddCheckItem(label, index);
                _panelsPopup.SetItemChecked(index, panel.Visible);
                _panelMenuIndexToName[index] = panel.Name;
                index++;
            }
        }

        _panelsPopup.IdPressed += OnPanelsMenuIdPressed;
        _panelsMenu.Pressed += OnPanelsMenuPressed;
    }

    private void ApplyDropdownThemes()
    {
        ApplyPopupTheme(_shotTypeOption.GetPopup());
        ApplyPopupTheme(_panelsPopup);
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

    private void OnPanelsMenuIdPressed(long id)
    {
        int index = (int)id;
        if (!_panelMenuIndexToName.TryGetValue(index, out var panelName))
            return;

        var panel = _gridCanvas.GetNode<DataPanel>(panelName);
        bool newVisible = !panel.Visible;
        panel.Visible = newVisible;
        _panelsPopup.SetItemChecked(index, newVisible);
        _gridCanvas.SaveLayout();
    }

    private void OnPanelsMenuPressed()
    {
        var popupPos = _panelsMenu.GlobalPosition + new Vector2(0, _panelsMenu.Size.Y);
        _panelsPopup.Position = new Vector2I((int)popupPos.X, (int)popupPos.Y);
        _panelsPopup.Popup();
    }

    private void SetShotControlsVisible(bool visible)
    {
        _shotControlsVisible = visible;
        _shotTypeOption.Visible = visible;
        _hitShotButton.Visible = visible;
    }
}
