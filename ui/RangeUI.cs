using Godot;
using Godot.Collections;

public partial class RangeUI : MarginContainer
{
    [Signal]
    public delegate void HitShotEventHandler(Dictionary data);

    private string _selectedShotPath = TestShots.DefaultShot;
    private GridCanvas _gridCanvas;
    private Button _panelsMenu;
    private PopupMenu _panelsPopup;
    private OptionButton _shotTypeOption;
    private Button _hitShotButton;
    private bool _shotControlsVisible;
    private readonly System.Collections.Generic.Dictionary<int, string> _panelMenuIndexToName = new();
    private Setting _shotInjectorSetting;

    // Cached DataPanel references (m8)
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

        // Connect ShotInjector signal
        var shotInjector = GetNode<ShotInjector>("ShotInjector");
        shotInjector.Inject += OnShotInjectorInject;

        // Connect UI button signals
        _hitShotButton = GetNode<Button>("HBoxContainer/HitShotButton");
        _hitShotButton.Pressed += OnHitShotPressed;

        _shotTypeOption = GetNode<OptionButton>("HBoxContainer/ShotTypeOption");
        _shotTypeOption.ItemSelected += OnShotTypeSelected;

        // Cache DataPanel references
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
        SetShotControlsVisible(true);
    }

    public override void _ExitTree()
    {
        if (_shotInjectorSetting != null)
            _shotInjectorSetting.SettingChanged -= ToggleShotInjector;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            if (keyEvent.Keycode == Key.D)
            {
                SetShotControlsVisible(!_shotControlsVisible);
            }
        }
    }

    public void SetData(Dictionary data)
    {
        var units = (PhysicsEnums.Units)(int)GetNode<GlobalSettings>("/root/GlobalSettings").RangeSettings.RangeUnits.Value;
        string speedUnit = (units == PhysicsEnums.Units.Imperial) ? "mph" : "m/s";

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

    private string FormatAngle(Variant val)
    {
        if (val.VariantType == Variant.Type.Float || val.VariantType == Variant.Type.Int)
        {
            return $"{(float)val:F1}";
        }
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
        {
            _selectedShotPath = (string)metadata;
        }
    }

    private void OnHitShotPressed()
    {
        var data = ShotLoader.LoadShotFromFile(_selectedShotPath);

        if (data.Count == 0)
        {
            GD.Print($"Hit Shot: Failed to load shot data from {_selectedShotPath}");
            return;
        }

        GD.Print($"Hit Shot: Loaded from {_selectedShotPath}");
        EmitSignal(SignalName.HitShot, data);
    }

    private void SetupPanelsMenu()
    {
        _panelsMenu = GetNode<Button>("HBoxContainer/PanelsMenu");
        _panelsPopup = GetNode<PopupMenu>("HBoxContainer/PanelsMenu/PanelsPopup");
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

    private void OnPanelsMenuIdPressed(long id)
    {
        int index = (int)id;
        if (!_panelMenuIndexToName.TryGetValue(index, out var panelName))
        {
            return;
        }

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
