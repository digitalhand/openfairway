using Godot;

public partial class MarkerHUD : Control
{
    private Control _flagMarker;
    private Label _flagMarkerDistanceLabel;
    private Label _flagMarkerArrowLabel;
    private Label _flagMarkerElevationLabel;
    private Control _flagMarkerElevationRow;
    private Control _flagMarkerDivider;
    private bool _flagMarkerActive;
    private Vector3 _flagMarkerPosition = Vector3.Zero;

    private Control _playerMarker;
    private Label _playerMarkerDistanceLabel;
    private Label _playerMarkerArrowLabel;
    private Label _playerMarkerElevationLabel;
    private Control _playerMarkerElevationRow;
    private Control _playerMarkerDivider;
    private bool _playerMarkerActive;
    private Vector3 _playerMarkerPosition = Vector3.Zero;

    private Camera3D _markerCamera;

    public override void _Ready()
    {
        _flagMarker = GetNode<Control>("FlagMarker");
        _flagMarkerDistanceLabel = GetNode<Label>("FlagMarker/Card/MarkerContent/DistanceLabel");
        _flagMarkerArrowLabel = GetNode<Label>("FlagMarker/Card/MarkerContent/ElevationRow/ArrowLabel");
        _flagMarkerElevationLabel = GetNode<Label>("FlagMarker/Card/MarkerContent/ElevationRow/ElevationLabel");
        _flagMarkerElevationRow = GetNode<Control>("FlagMarker/Card/MarkerContent/ElevationRow");
        _flagMarkerDivider = GetNode<Control>("FlagMarker/Card/MarkerContent/Divider");
        _playerMarker = GetNode<Control>("PlayerMarker");
        _playerMarkerDistanceLabel = GetNode<Label>("PlayerMarker/Card/MarkerContent/DistanceLabel");
        _playerMarkerArrowLabel = GetNode<Label>("PlayerMarker/Card/MarkerContent/ElevationRow/ArrowLabel");
        _playerMarkerElevationLabel = GetNode<Label>("PlayerMarker/Card/MarkerContent/ElevationRow/ElevationLabel");
        _playerMarkerElevationRow = GetNode<Control>("PlayerMarker/Card/MarkerContent/ElevationRow");
        _playerMarkerDivider = GetNode<Control>("PlayerMarker/Card/MarkerContent/Divider");
        HideAll();
    }

    public override void _Process(double delta)
    {
        UpdateMarkerProjection(_flagMarker, _flagMarkerActive, _flagMarkerPosition);
        UpdateMarkerProjection(_playerMarker, _playerMarkerActive, _playerMarkerPosition);
    }

    public void SetCamera(Camera3D camera)
    {
        _markerCamera = camera;
        UpdateMarkerProjection(_flagMarker, _flagMarkerActive, _flagMarkerPosition);
        UpdateMarkerProjection(_playerMarker, _playerMarkerActive, _playerMarkerPosition);
    }

    public void SetElevationVisible(bool visible)
    {
        if (_flagMarkerElevationRow != null)
            _flagMarkerElevationRow.Visible = visible;
        if (_flagMarkerDivider != null)
            _flagMarkerDivider.Visible = visible;
        if (_playerMarkerElevationRow != null)
            _playerMarkerElevationRow.Visible = visible;
        if (_playerMarkerDivider != null)
            _playerMarkerDivider.Visible = visible;
    }

    public void ApplySnapshot(MarkerSnapshot snapshot)
    {
        if (snapshot.Flag.Visible)
        {
            ShowFlagMarker(snapshot.Flag.WorldPoint, snapshot.Flag.DistanceText, snapshot.Flag.ElevationFeet);
        }
        else
        {
            HideFlagMarker();
        }

        if (snapshot.Player.Visible)
        {
            ShowPlayerMarker(snapshot.Player.WorldPoint, snapshot.Player.DistanceText, snapshot.Player.ElevationFeet);
        }
        else
        {
            HidePlayerMarker();
        }
    }

    public void HideAll()
    {
        HideFlagMarker();
        HidePlayerMarker();
    }

    public void ShowFlagMarker(Vector3 worldPoint, string distanceText, int elevationFeet)
    {
        ShowMarker(
            _flagMarker,
            _flagMarkerDistanceLabel,
            _flagMarkerArrowLabel,
            _flagMarkerElevationLabel,
            worldPoint,
            distanceText,
            elevationFeet,
            ref _flagMarkerActive,
            ref _flagMarkerPosition
        );
    }

    public void HideFlagMarker()
    {
        HideMarker(_flagMarker, ref _flagMarkerActive);
    }

    public void ShowPlayerMarker(Vector3 worldPoint, string distanceText, int elevationFeet)
    {
        ShowMarker(
            _playerMarker,
            _playerMarkerDistanceLabel,
            _playerMarkerArrowLabel,
            _playerMarkerElevationLabel,
            worldPoint,
            distanceText,
            elevationFeet,
            ref _playerMarkerActive,
            ref _playerMarkerPosition
        );
    }

    public void HidePlayerMarker()
    {
        HideMarker(_playerMarker, ref _playerMarkerActive);
    }

    private void ShowMarker(
        Control markerRoot,
        Label distanceLabel,
        Label arrowLabel,
        Label elevationLabel,
        Vector3 worldPoint,
        string distanceText,
        int elevationFeet,
        ref bool isActive,
        ref Vector3 markerPosition)
    {
        if (markerRoot == null)
            return;

        markerPosition = worldPoint;
        isActive = true;
        distanceLabel.Text = string.IsNullOrWhiteSpace(distanceText) ? "---" : distanceText;

        ElevationVisual visual = ElevationPresenter.Build(elevationFeet, includeSignInText: true);
        if (arrowLabel != null)
        {
            arrowLabel.Text = visual.Arrow;
            arrowLabel.AddThemeColorOverride("font_color", visual.Color);
        }

        elevationLabel.Text = visual.Text;
        elevationLabel.AddThemeColorOverride("font_color", visual.Color);

        markerRoot.Visible = true;
        UpdateMarkerProjection(markerRoot, isActive, markerPosition);
    }

    private void HideMarker(Control markerRoot, ref bool isActive)
    {
        isActive = false;
        if (markerRoot != null)
            markerRoot.Visible = false;
    }

    private void UpdateMarkerProjection(Control markerRoot, bool isActive, Vector3 markerPosition)
    {
        if (markerRoot == null)
            return;

        if (!isActive || _markerCamera == null)
        {
            markerRoot.Visible = false;
            return;
        }

        if (_markerCamera.IsPositionBehind(markerPosition))
        {
            markerRoot.Visible = false;
            return;
        }

        Vector2 screenPosition = _markerCamera.UnprojectPosition(markerPosition);
        Vector2 markerSize = markerRoot.Size;
        if (markerSize.X <= 0.0f || markerSize.Y <= 0.0f)
            markerSize = markerRoot.GetCombinedMinimumSize();

        markerRoot.Position = screenPosition - new Vector2(markerSize.X * 0.5f, markerSize.Y);
        markerRoot.Visible = true;
    }
}
