using Godot;

public partial class GridCanvas : Control
{
    private bool _showGrid = false;
    private bool _editMode = true;
    private const float PANEL_WIDTH = 84f;
    private const float PANEL_HEIGHT = 65f;
    private const float GRID_SPACING_X = 2f;
    private const float GRID_SPACING_Y = 2f;
    private const float DEFAULT_RIGHT_MARGIN = 20f;
    private const float DEFAULT_BOTTOM_MARGIN = 20f;
    private const float DEFAULT_TOP_MIN = 120f;
    private const float DEFAULT_TOP_RATIO = 0.36f;
    private const int DEFAULT_PANEL_COLUMNS = 2;
    private readonly Vector2 GRID_SIZE = new Vector2(CELL_SIZE_X + GRID_SPACING_X, CELL_SIZE_Y + GRID_SPACING_Y);
    private static readonly Vector2 GRID_ORIGIN = new Vector2(15f, 15f);
    private static readonly string[] DefaultPanelOrder =
    {
        "Distance",
        "Carry",
        "HLA",
        "VLA",
        "Speed",
        "Apex",
        "TotalSpin",
        "BackSpin",
        "SpinAxis",
        "SideSpin"
    };

    private const float CELL_SIZE_X = PANEL_WIDTH;
    private const float CELL_SIZE_Y = PANEL_HEIGHT;

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

    private Setting _rangeUnitsSetting;

    public override void _Ready()
    {
        LoadLayout();
        _rangeUnitsSetting = GetNode<GlobalSettings>("/root/GlobalSettings").RangeSettings.RangeUnits;
        _rangeUnitsSetting.SettingChanged += SetUnits;

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
        var userConfig = new ConfigFile();
        if (userConfig.Load("user://layout.cfg") == Error.Ok)
        {
            ApplyLayoutFromConfig(userConfig);
            return;
        }

        ApplyFirstRunDefaultLayout();
        SaveLayout();
    }

    private void ApplyLayoutFromConfig(ConfigFile config)
    {
        foreach (Control panel in GetChildren())
        {
            if (config.HasSectionKey("positions", panel.Name))
            {
                var posValue = config.GetValue("positions", panel.Name);
                if (posValue.VariantType == Variant.Type.Vector2)
                {
                    var pos = (Vector2)posValue;
                    var viewport = GetViewportRect().Size;
                    // Validate position is within reasonable bounds
                    pos.X = Mathf.Clamp(pos.X, -panel.Size.X, viewport.X);
                    pos.Y = Mathf.Clamp(pos.Y, -panel.Size.Y, viewport.Y);
                    panel.Position = pos;
                }
                else
                {
                    PhysicsLogger.Error($"GridCanvas: invalid position type for panel '{panel.Name}'");
                }
            }
            if (config.HasSectionKey("visibility", panel.Name))
            {
                var visValue = config.GetValue("visibility", panel.Name);
                if (visValue.VariantType == Variant.Type.Bool)
                {
                    panel.Visible = (bool)visValue;
                }
                else
                {
                    PhysicsLogger.Error($"GridCanvas: invalid visibility type for panel '{panel.Name}'");
                }
            }
        }
    }

    private void ApplyFirstRunDefaultLayout()
    {
        Vector2 viewport = GetViewportRect().Size;

        foreach (Control panel in GetChildren())
        {
            panel.Size = new Vector2(PANEL_WIDTH, PANEL_HEIGHT);
            panel.Visible = false;
        }

        float totalWidth = PANEL_WIDTH * DEFAULT_PANEL_COLUMNS + GRID_SPACING_X * (DEFAULT_PANEL_COLUMNS - 1);
        float startX = Mathf.Max(10.0f, viewport.X - DEFAULT_RIGHT_MARGIN - totalWidth);

        int rows = Mathf.CeilToInt((float)DefaultPanelOrder.Length / DEFAULT_PANEL_COLUMNS);
        float totalHeight = rows * PANEL_HEIGHT + GRID_SPACING_Y * (rows - 1);
        float maxTop = Mathf.Max(10.0f, viewport.Y - totalHeight - DEFAULT_BOTTOM_MARGIN);
        float minTop = Mathf.Min(DEFAULT_TOP_MIN, maxTop);
        float startY = Mathf.Clamp(viewport.Y * DEFAULT_TOP_RATIO, minTop, maxTop);

        for (int i = 0; i < DefaultPanelOrder.Length; i++)
        {
            string panelName = DefaultPanelOrder[i];
            if (!HasNode(panelName))
                continue;

            var panel = GetNode<Control>(panelName);
            int row = i / DEFAULT_PANEL_COLUMNS;
            int col = i % DEFAULT_PANEL_COLUMNS;
            panel.Position = new Vector2(
                startX + col * (PANEL_WIDTH + GRID_SPACING_X),
                startY + row * (PANEL_HEIGHT + GRID_SPACING_Y)
            );
            panel.Visible = true;
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

    public override void _ExitTree()
    {
        if (_rangeUnitsSetting != null)
            _rangeUnitsSetting.SettingChanged -= SetUnits;
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
