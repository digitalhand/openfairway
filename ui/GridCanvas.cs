using System.Collections.Generic;
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

        Vector2 viewportSize = GetViewportRect().Size;
        Vector2 origin = GetGridOrigin();
        float startX = origin.X;
        float startY = origin.Y;

        while (startX > 0.0f)
            startX -= GRID_SIZE.X;

        while (startY > 0.0f)
            startY -= GRID_SIZE.Y;

        for (float x = startX; x < viewportSize.X; x += GRID_SIZE.X)
        {
            DrawLine(new Vector2(x, 0), new Vector2(x, viewportSize.Y), Colors.Gray);
        }

        for (float y = startY; y < viewportSize.Y; y += GRID_SIZE.Y)
        {
            DrawLine(new Vector2(0, y), new Vector2(viewportSize.X, y), Colors.Gray);
        }
    }

    private Setting _gameUnitsSetting;

    public override void _Ready()
    {
        LoadLayout();
        _gameUnitsSetting = GetNode<GlobalSettings>("/root/GlobalSettings").GameSettings.GameUnits;
        _gameUnitsSetting.SettingChanged += SetUnits;

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
        ApplyEditMode();
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
        Vector2 origin = GetGridOrigin(panel);
        float snapX = Mathf.Round((panel.Position.X - origin.X) / GRID_SIZE.X) * GRID_SIZE.X + origin.X;
        float snapY = Mathf.Round((panel.Position.Y - origin.Y) / GRID_SIZE.Y) * GRID_SIZE.Y + origin.Y;
        panel.Position = new Vector2(snapX, snapY);
    }

    public void ToggleEditMode()
    {
        _editMode = !_editMode;
        if (!_editMode)
            _showGrid = false;

        ApplyEditMode();
        QueueRedraw();
    }

    public void SaveLayout()
    {
        LayoutPersistenceService.Save("user://layout.cfg", GetPanels());
    }

    public void LoadLayout()
    {
        if (LayoutPersistenceService.TryLoad("user://layout.cfg", out ConfigFile userConfig))
        {
            LayoutPersistenceService.Apply(userConfig, GetPanels(), GetViewportRect().Size);
            bool layoutChanged = NormalizeVisiblePanelsToGrid();

            int layoutVersion = LayoutPersistenceService.GetLayoutVersion(userConfig);
            if (layoutVersion == 2)
                layoutChanged |= ShiftVisiblePanelsDownByOneGridStep();

            if (layoutChanged || layoutVersion != LayoutPersistenceService.CurrentLayoutVersion)
                SaveLayout();
            return;
        }

        ApplyFirstRunDefaultLayout();
        SaveLayout();
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
        if (!_editMode)
            return;

        _showGrid = true;
        QueueRedraw();
    }

    private void OnPanelDragEnded(DataPanel panel)
    {
        if (!_editMode)
            return;

        _showGrid = false;
        QueueRedraw();
        SnapToGrid(panel);
    }

    public override void _ExitTree()
    {
        if (_gameUnitsSetting != null)
            _gameUnitsSetting.SettingChanged -= SetUnits;
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

    private IEnumerable<Control> GetPanels()
    {
        foreach (Node node in GetChildren())
        {
            if (node is Control panel)
                yield return panel;
        }
    }

    private void ApplyEditMode()
    {
        foreach (Control panel in GetPanels())
        {
            if (panel is DataPanel dataPanel)
                dataPanel.SetEditable(_editMode);
        }
    }

    private bool NormalizeVisiblePanelsToGrid()
    {
        Vector2 origin = GetGridOrigin();
        bool changed = false;

        foreach (Control panel in GetPanels())
        {
            if (!panel.Visible)
                continue;

            float snapX = Mathf.Round((panel.Position.X - origin.X) / GRID_SIZE.X) * GRID_SIZE.X + origin.X;
            float snapY = Mathf.Round((panel.Position.Y - origin.Y) / GRID_SIZE.Y) * GRID_SIZE.Y + origin.Y;
            Vector2 snapped = new Vector2(snapX, snapY);
            if (panel.Position != snapped)
            {
                panel.Position = snapped;
                changed = true;
            }
        }

        return changed;
    }

    private bool ShiftVisiblePanelsDownByOneGridStep()
    {
        bool changed = false;
        float maxTop = Mathf.Max(10.0f, GetViewportRect().Size.Y - PANEL_HEIGHT);

        foreach (Control panel in GetPanels())
        {
            if (!panel.Visible)
                continue;

            Vector2 shifted = new Vector2(panel.Position.X, Mathf.Min(maxTop, panel.Position.Y + GRID_SIZE.Y));
            if (panel.Position != shifted)
            {
                panel.Position = shifted;
                changed = true;
            }
        }

        return changed;
    }

    private Vector2 GetGridOrigin(Control excludePanel = null)
    {
        foreach (Control panel in GetPanels())
        {
            if (!panel.Visible || panel == excludePanel)
                continue;

            return new Vector2(
                Mathf.PosMod(panel.Position.X, GRID_SIZE.X),
                Mathf.PosMod(panel.Position.Y, GRID_SIZE.Y)
            );
        }

        if (excludePanel != null)
        {
            return new Vector2(
                Mathf.PosMod(excludePanel.Position.X, GRID_SIZE.X),
                Mathf.PosMod(excludePanel.Position.Y, GRID_SIZE.Y)
            );
        }

        return Vector2.Zero;
    }
}
