using Godot;

public partial class GridCanvas : Control
{
    private bool _showGrid = false;
    private bool _editMode = true;
    private const float CELL_SIZE_X = 120f;
    private const float CELL_SIZE_Y = 93f;
    private const float GRID_SPACING_X = 10f;
    private const float GRID_SPACING_Y = 10f;
    private readonly Vector2 GRID_SIZE = new Vector2(CELL_SIZE_X + GRID_SPACING_X, CELL_SIZE_Y + GRID_SPACING_Y);
    private static readonly Vector2 GRID_ORIGIN = new Vector2(15f, 15f);

    public override void _Draw()
    {
        if (!_showGrid)
            return;

        Vector2 paddingCorrection = Vector2.Zero;
        Vector2 offset = GlobalPosition - GlobalPosition + paddingCorrection;
        Vector2 viewportSize = GetViewportRect().Size;
        Vector2 origin = Vector2.Zero;

        for (float x = 0; x < viewportSize.X; x += GRID_SIZE.X)
        {
            float gridX = x + offset.X + origin.X;
            DrawLine(new Vector2(gridX, 0), new Vector2(gridX, viewportSize.Y), Colors.Gray);
        }

        for (float y = 0; y < viewportSize.Y; y += GRID_SIZE.Y)
        {
            float gridY = y + offset.Y + origin.Y;
            DrawLine(new Vector2(0, gridY), new Vector2(viewportSize.X, gridY), Colors.Gray);
        }
    }

    public override void _Ready()
    {
        LoadLayout();
        GetNode<GlobalSettings>("/root/GlobalSettings").RangeSettings.RangeUnits.SettingChanged += SetUnits;

        // Connect DataPanel drag signals
        ConnectPanelSignals("Distance");
        ConnectPanelSignals("Carry");
        ConnectPanelSignals("Side");
        ConnectPanelSignals("Apex");
        ConnectPanelSignals("VLA");
        ConnectPanelSignals("HLA");
        ConnectPanelSignals("Speed");
        ConnectPanelSignals("BackSpin");
        ConnectPanelSignals("SideSpin");
        ConnectPanelSignals("TotalSpin");
        ConnectPanelSignals("SpinAxis");
    }

    private void ConnectPanelSignals(string panelName)
    {
        if (HasNode(panelName))
        {
            var panel = GetNode<DataPanel>(panelName);
            panel.DragStarted += OnPanelDragStarted;
            panel.DragEnded += OnPanelDragEnded;
        }
    }

    public void SnapToGrid(Control panel)
    {
        float globalSnapX = Mathf.Round((panel.GlobalPosition.X - GRID_ORIGIN.X) / GRID_SIZE.X) * GRID_SIZE.X + GRID_ORIGIN.X;
        float globalSnapY = Mathf.Round((panel.GlobalPosition.Y - GRID_ORIGIN.Y) / GRID_SIZE.Y) * GRID_SIZE.Y + GRID_ORIGIN.Y;
        panel.GlobalPosition = new Vector2(globalSnapX, globalSnapY);
    }

    public void ToggleEditMode()
    {
        _editMode = !_editMode;
        foreach (var panel in GetNode("VBoxContainer").GetChildren())
        {
            panel.Call("set_editable", _editMode);
        }
    }

    public void SaveLayout()
    {
        var config = new ConfigFile();
        foreach (Control panel in GetChildren())
        {
            config.SetValue("positions", panel.Name, panel.Position);
            config.SetValue("visibility", panel.Name, panel.Visible);
        }
        config.Save("user://layout.cfg");
    }

    public void LoadLayout()
    {
        var config = new ConfigFile();
        if (config.Load("user://layout.cfg") != Error.Ok)
        {
            config.Load("res://ui/default_layout.cfg");
        }

        foreach (Control panel in GetChildren())
        {
            if (config.HasSectionKey("positions", panel.Name))
            {
                panel.Position = (Vector2)config.GetValue("positions", panel.Name);
            }
            if (config.HasSectionKey("visibility", panel.Name))
            {
                panel.Visible = (bool)config.GetValue("visibility", panel.Name);
            }
        }
    }

    private void OnPanelDragStarted()
    {
        _showGrid = true;
        QueueRedraw();
    }

    private void OnPanelDragEnded(DataPanel panel)
    {
        _showGrid = false;
        QueueRedraw();
        SnapToGrid(panel);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            SaveLayout();
            GetTree().Quit();
        }
    }

    private void SetUnits(Variant value)
    {
        var units = (PhysicsEnums.Units)(int)value;
        if (units == PhysicsEnums.Units.Imperial)
        {
            GetNode<DataPanel>("Distance").SetUnits("yd");
            GetNode<DataPanel>("Carry").SetUnits("yd");
            GetNode<DataPanel>("Side").SetUnits("yd");
            GetNode<DataPanel>("Apex").SetUnits("ft");
        }
        else
        {
            GetNode<DataPanel>("Distance").SetUnits("m");
            GetNode<DataPanel>("Carry").SetUnits("m");
            GetNode<DataPanel>("Side").SetUnits("m");
            GetNode<DataPanel>("Apex").SetUnits("m");
        }
    }
}
