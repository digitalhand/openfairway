using Godot;

/// <summary>
/// Data panel UI component for displaying labeled values with units.
/// Supports drag-and-drop repositioning.
/// </summary>
public partial class DataPanel : PanelContainer
{
    [Signal]
    public delegate void DragStartedEventHandler();

    [Signal]
    public delegate void DragEndedEventHandler(DataPanel panel);

    [Signal]
    public delegate void PanelContextRequestedEventHandler(DataPanel panel);

    [Export] public string Label { get; set; } = "Label";
    [Export] public string Data { get; set; } = "---";
    [Export] public string Units { get; set; } = "units";

    private bool _dragging = false;
    private Vector2 _dragOffset = Vector2.Zero;
    private bool _editable = true;

    public override void _Ready()
    {
        SetLabel(Label);
        SetData(Data);
        SetUnits(Units);
    }

    public void SetLabel(string l)
    {
        Label = l;
        GetNode<Godot.Label>("VBoxContainer/Label").Text = l;
    }

    public void SetData(string value)
    {
        Data = value;
        GetNode<Godot.Label>("VBoxContainer/Data").Text = value;
    }

    public void SetUnits(string u)
    {
        Units = u;
        GetNode<Godot.Label>("VBoxContainer/Units").Text = u;
    }

    public void SetEditable(bool editable)
    {
        _editable = editable;
        if (!editable)
            _dragging = false;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (!_editable)
        {
            if (@event is InputEventMouseButton readOnlyMouseButton
                && readOnlyMouseButton.ButtonIndex == MouseButton.Right
                && readOnlyMouseButton.Pressed)
            {
                EmitSignal(SignalName.PanelContextRequested, this);
                AcceptEvent();
            }
            return;
        }

        if (@event is InputEventMouseButton contextMouseButton
            && contextMouseButton.ButtonIndex == MouseButton.Right
            && contextMouseButton.Pressed)
        {
            EmitSignal(SignalName.PanelContextRequested, this);
            AcceptEvent();
            return;
        }

        if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
        {
            if (mouseButton.Pressed)
            {
                EmitSignal(SignalName.DragStarted);
                _dragging = true;
                _dragOffset = GetGlobalMousePosition() - GlobalPosition;
            }
            else
            {
                EmitSignal(SignalName.DragEnded, this);
                _dragging = false;
            }
        }
        else if (@event is InputEventMouseMotion && _dragging)
        {
            GlobalPosition = GetGlobalMousePosition() - _dragOffset;
        }
    }
}
