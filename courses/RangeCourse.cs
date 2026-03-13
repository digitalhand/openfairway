using Godot;

/// <summary>
/// Driving-range scene controller that reuses hole flow but enforces per-shot auto reset
/// and guarantees a large fairway practice area around the tee.
/// </summary>
public partial class RangeCourse : HoleSceneControllerBase
{
    private const float YardsToMeters = 1.0f / ShotSetup.YARDS_PER_METER;
    private Vector3 _teePoint = GolfBall.START_POSITION;

    [ExportGroup("Range Surface")]
    [Export] public NodePath SurfaceGridPath { get; set; } = new NodePath("SurfaceGrid");
    [Export] public NodePath BallNodePath { get; set; } = new NodePath("ShotTracker/Ball");
    [Export(PropertyHint.Range, "50,1000,1")] public float RangeLengthYards { get; set; } = 400.0f;
    [Export(PropertyHint.Range, "50,1000,1")] public float RangeWidthYards { get; set; } = 600.0f;
    [Export] public int FairwayItemId { get; set; } = 0;
    [Export] public int GridYLayer { get; set; } = 0;
    [Export] public bool OverwriteExistingCells { get; set; } = false;

    protected override bool ShouldAutoResetAfterRest()
    {
        return true;
    }

    protected override bool ShouldResetBallBeforeCameraTween()
    {
        return true;
    }

    protected override bool ShouldClearDisplaySessionOnAutoReset()
    {
        return false;
    }

    protected override bool ShouldPlayAmbientAudioOnStartup()
    {
        return false;
    }

    protected override bool ShouldShowCourseMeta()
    {
        return false;
    }

    protected override bool ShouldShowTargetElevation()
    {
        return false;
    }

    protected override bool ShouldShowRangeHudControls()
    {
        return true;
    }

    protected override int GetRangeTargetMinYards()
    {
        return 5;
    }

    protected override int GetRangeTargetMaxYards()
    {
        return 350;
    }

    protected override int GetDefaultRangeTargetYards()
    {
        return 100;
    }

    protected override bool ShouldShowTracerHistorySetting()
    {
        return true;
    }

    protected override bool ShouldUseTracerCountSetting()
    {
        return true;
    }

    protected override int GetFixedTracerCount()
    {
        return 2;
    }

    protected override bool ShouldClearTracersOnBallRest()
    {
        return false;
    }

    protected override bool ShouldClearTracersOnShotStart()
    {
        return false;
    }

    protected override bool ShouldClearTracersOnBallReset()
    {
        return false;
    }

    protected override Vector3? ResolveDistanceReferencePoint()
    {
        int selectedYards = GameplayUi != null ? GameplayUi.GetRangeTargetYardage() : GetDefaultRangeTargetYards();
        selectedYards = Mathf.Clamp(selectedYards, GetRangeTargetMinYards(), GetRangeTargetMaxYards());
        float meters = selectedYards * YardsToMeters;

        return _teePoint + new Vector3(meters, 0.0f, 0.0f);
    }

    protected override string ResolveShotRecordingClubTag()
    {
        if (GameplayUi == null)
            return RangeClubCatalog.ToFileTag(AppSettings.DefaultRangeDefaultClub);

        return GameplayUi.GetRangeSelectedClubFileTag();
    }

    protected override void OnHoleReadyAfterInit()
    {
        GolfBall ball = GetNodeOrNull<GolfBall>(BallNodePath);
        if (ball != null)
            _teePoint = ball.GlobalPosition;

        ExtendFairwaySurface();
    }

    private void ExtendFairwaySurface()
    {
        GridMap surfaceGrid = GetNodeOrNull<GridMap>(SurfaceGridPath);
        if (surfaceGrid == null)
        {
            GD.PushError($"{nameof(RangeCourse)}: SurfaceGridPath '{SurfaceGridPath}' does not resolve to a GridMap.");
            return;
        }

        MeshLibrary meshLibrary = surfaceGrid.MeshLibrary;
        if (meshLibrary == null)
        {
            GD.PushError($"{nameof(RangeCourse)}: SurfaceGrid has no MeshLibrary.");
            return;
        }

        if (!HasMeshItem(meshLibrary, FairwayItemId))
        {
            GD.PushError($"{nameof(RangeCourse)}: MeshLibrary is missing fairway item id {FairwayItemId}.");
            return;
        }

        GolfBall ball = GetNodeOrNull<GolfBall>(BallNodePath);
        Vector3 teePoint = ball != null ? ball.GlobalPosition : GolfBall.START_POSITION;

        float lengthMeters = Mathf.Max(1.0f, RangeLengthYards) * YardsToMeters;
        float halfWidthMeters = Mathf.Max(1.0f, RangeWidthYards) * YardsToMeters * 0.5f;

        Vector3 worldMin = new Vector3(teePoint.X, teePoint.Y, teePoint.Z - halfWidthMeters);
        Vector3 worldMax = new Vector3(teePoint.X + lengthMeters, teePoint.Y, teePoint.Z + halfWidthMeters);

        Vector3 localMin = surfaceGrid.ToLocal(worldMin);
        Vector3 localMax = surfaceGrid.ToLocal(worldMax);

        int minCellX = Mathf.Min(surfaceGrid.LocalToMap(localMin).X, surfaceGrid.LocalToMap(localMax).X);
        int maxCellX = Mathf.Max(surfaceGrid.LocalToMap(localMin).X, surfaceGrid.LocalToMap(localMax).X);
        int minCellZ = Mathf.Min(surfaceGrid.LocalToMap(localMin).Z, surfaceGrid.LocalToMap(localMax).Z);
        int maxCellZ = Mathf.Max(surfaceGrid.LocalToMap(localMin).Z, surfaceGrid.LocalToMap(localMax).Z);

        int appliedCells = 0;
        int skippedCells = 0;
        for (int x = minCellX; x <= maxCellX; x++)
        {
            for (int z = minCellZ; z <= maxCellZ; z++)
            {
                var cell = new Vector3I(x, GridYLayer, z);
                int existingItemId = surfaceGrid.GetCellItem(cell);
                if (!OverwriteExistingCells && existingItemId >= 0)
                {
                    skippedCells++;
                    continue;
                }

                surfaceGrid.SetCellItem(cell, FairwayItemId);
                appliedCells++;
            }
        }

        PhysicsLogger.Info(
            $"{nameof(RangeCourse)}: extended fairway to {RangeLengthYards:F0}yd x {RangeWidthYards:F0}yd " +
            $"({appliedCells} cells applied, {skippedCells} preserved, x:[{minCellX},{maxCellX}] z:[{minCellZ},{maxCellZ}], y={GridYLayer}, overwrite={OverwriteExistingCells})."
        );
    }

    private static bool HasMeshItem(MeshLibrary meshLibrary, int itemId)
    {
        foreach (int candidate in meshLibrary.GetItemList())
        {
            if (candidate == itemId)
                return true;
        }

        return false;
    }
}
