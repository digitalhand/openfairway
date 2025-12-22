using Godot;
using Godot.Collections;

public partial class RangeUI : MarginContainer
{
    [Signal]
    public delegate void HitShotEventHandler(Dictionary data);

    private string _selectedShotPath = TestShots.DefaultShot;

    public override void _Ready()
    {
        GetNode<GlobalSettings>("/root/GlobalSettings").RangeSettings.ShotInjectorEnabled.SettingChanged += ToggleShotInjector;

        // Connect ShotInjector signal
        var shotInjector = GetNode<ShotInjector>("ShotInjector");
        shotInjector.Inject += OnShotInjectorInject;

        // Connect UI button signals
        var hitShotButton = GetNode<Button>("HBoxContainer/HitShotButton");
        hitShotButton.Pressed += OnHitShotPressed;

        var shotTypeOption = GetNode<OptionButton>("HBoxContainer/ShotTypeOption");
        shotTypeOption.ItemSelected += OnShotTypeSelected;

        PopulateShotTypes();
    }

    public void SetData(Dictionary data)
    {
        var units = (PhysicsEnums.Units)(int)GetNode<GlobalSettings>("/root/GlobalSettings").RangeSettings.RangeUnits.Value;
        string speedUnit = (units == PhysicsEnums.Units.Imperial) ? "mph" : "m/s";

        GetNode<DataPanel>("GridCanvas/Distance").SetData(data["Distance"].ToString());
        GetNode<DataPanel>("GridCanvas/Carry").SetData(data["Carry"].ToString());
        GetNode<DataPanel>("GridCanvas/Side").SetData(data["Offline"].ToString());
        GetNode<DataPanel>("GridCanvas/Apex").SetData(data["Apex"].ToString());
        GetNode<DataPanel>("GridCanvas/Speed").SetUnits(speedUnit);
        GetNode<DataPanel>("GridCanvas/Speed").SetData(data["Speed"].ToString());
        GetNode<DataPanel>("GridCanvas/BackSpin").SetUnits("rpm");
        GetNode<DataPanel>("GridCanvas/BackSpin").SetData(data["BackSpin"].ToString());
        GetNode<DataPanel>("GridCanvas/SideSpin").SetUnits("rpm");
        GetNode<DataPanel>("GridCanvas/SideSpin").SetData(data["SideSpin"].ToString());
        GetNode<DataPanel>("GridCanvas/TotalSpin").SetUnits("rpm");
        GetNode<DataPanel>("GridCanvas/TotalSpin").SetData(data["TotalSpin"].ToString());
        GetNode<DataPanel>("GridCanvas/SpinAxis").SetUnits("deg");
        GetNode<DataPanel>("GridCanvas/SpinAxis").SetData(data["SpinAxis"].ToString());
        GetNode<DataPanel>("GridCanvas/VLA").SetData(FormatAngle(data.ContainsKey("VLA") ? data["VLA"] : 0.0f));
        GetNode<DataPanel>("GridCanvas/HLA").SetData(FormatAngle(data.ContainsKey("HLA") ? data["HLA"] : 0.0f));
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
        var optionButton = GetNode<OptionButton>("HBoxContainer/ShotTypeOption");
        optionButton.Clear();
        int idx = 0;
        foreach (var kvp in TestShots.Shots)
        {
            optionButton.AddItem(kvp.Key);
            optionButton.SetItemMetadata(idx, kvp.Value);
            idx++;
        }
        optionButton.Select(0);
    }

    private void OnShotTypeSelected(long index)
    {
        var optionButton = GetNode<OptionButton>("HBoxContainer/ShotTypeOption");
        var metadata = optionButton.GetItemMetadata((int)index);
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
}
