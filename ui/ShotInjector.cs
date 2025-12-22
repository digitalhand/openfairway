using Godot;
using Godot.Collections;

public partial class ShotInjector : VBoxContainer
{
    [Signal]
    public delegate void InjectEventHandler(Dictionary data);

    [Export] public string DefaultPayloadPath { get; set; } = TestShots.DefaultShot;

    private OptionButton _payloadOption;

    public override void _Ready()
    {
        _payloadOption = GetNode<OptionButton>("PayloadOption");

        // Connect button signals
        var button = GetNode<Button>("Button");
        button.Pressed += OnButtonPressed;

        _payloadOption.ItemSelected += OnPayloadOptionItemSelected;

        PopulatePayloads();
    }

    private void PopulatePayloads()
    {
        if (_payloadOption == null)
            return;

        _payloadOption.Clear();

        int selected = 0;
        int idx = 0;
        foreach (var kvp in TestShots.Shots)
        {
            _payloadOption.AddItem(kvp.Key);
            _payloadOption.SetItemMetadata(idx, kvp.Value);
            if (kvp.Value == DefaultPayloadPath)
            {
                selected = idx;
            }
            idx++;
        }
        _payloadOption.Select(selected);
    }

    private void OnButtonPressed()
    {
        // Collect data from boxes and send to be hit. If empty, fall back to default JSON payload.
        var data = new Dictionary();
        bool loaded = false;

        if (!string.IsNullOrEmpty(DefaultPayloadPath))
        {
            data = ShotLoader.LoadShotFromFile(DefaultPayloadPath);
            loaded = data.Count > 0;
        }

        // Override with UI entries when provided
        var speedText = GetNode<LineEdit>("SpeedText").Text.StripEdges();
        if (speedText != "")
            data["Speed"] = speedText.ToFloat();

        var spinAxisText = GetNode<LineEdit>("SpinAxisText").Text.StripEdges();
        if (spinAxisText != "")
            data["SpinAxis"] = spinAxisText.ToFloat();

        var totalSpinText = GetNode<LineEdit>("TotalSpinText").Text.StripEdges();
        if (totalSpinText != "")
            data["TotalSpin"] = totalSpinText.ToFloat();

        var hlaText = GetNode<LineEdit>("HLAText").Text.StripEdges();
        if (hlaText != "")
            data["HLA"] = hlaText.ToFloat();

        var vlaText = GetNode<LineEdit>("VLAText").Text.StripEdges();
        if (vlaText != "")
            data["VLA"] = vlaText.ToFloat();

        if (HasNode("BackSpinText"))
        {
            var backNode = GetNode<LineEdit>("BackSpinText");
            var backText = backNode.Text.StripEdges();
            if (backText != "")
                data["BackSpin"] = backText.ToFloat();
        }

        if (HasNode("SideSpinText"))
        {
            var sideNode = GetNode<LineEdit>("SideSpinText");
            var sideText = sideNode.Text.StripEdges();
            if (sideText != "")
                data["SideSpin"] = sideText.ToFloat();
        }

        if (data.Count == 0)
        {
            GD.Print("Shot injector: no data provided and default payload missing; using zeros");
        }

        if (loaded)
        {
            GD.Print($"Shot injector: loaded default payload from {DefaultPayloadPath}");
        }
        GD.Print($"Local shot injection payload: {Json.Stringify(data)}");

        EmitSignal(SignalName.Inject, data);
    }

    private void OnPayloadOptionItemSelected(long index)
    {
        var metadata = _payloadOption.GetItemMetadata((int)index);
        if (metadata.VariantType == Variant.Type.String)
        {
            DefaultPayloadPath = (string)metadata;
        }
    }
}
