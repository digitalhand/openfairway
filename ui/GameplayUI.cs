using Godot;
using Godot.Collections;

public partial class GameplayUI : MarginContainer
{
    [Signal]
    public delegate void HitShotEventHandler(Dictionary data);

    private CourseHud _courseHud;
    private MarkerHUD _markerHud;

    public override void _Ready()
    {
        _courseHud = GetNodeOrNull<CourseHud>("CourseHud");
        _markerHud = GetNodeOrNull<MarkerHUD>("OverlayLayer/MarkerHUD");

        if (_courseHud != null)
            _courseHud.HitShot += OnCourseHudHitShot;

        _markerHud?.HideAll();
    }

    public override void _ExitTree()
    {
        if (_courseHud != null)
            _courseHud.HitShot -= OnCourseHudHitShot;
    }

    public void SetData(Dictionary data)
    {
        _courseHud?.SetData(data);
    }

    public void SetTotalDistance(string text)
    {
        _courseHud?.SetTotalDistance(text);
    }

    public void ClearTotalDistance()
    {
        _courseHud?.ClearTotalDistance();
    }

    public void SetStrokeCount(int strokes)
    {
        _courseHud?.SetStrokeCount(strokes);
    }

    public void SetFinalStrokeCount(int strokes)
    {
        _courseHud?.SetFinalStrokeCount(strokes);
    }

    public void SetTargetYardage(float yards)
    {
        _courseHud?.SetTargetYardage(yards);
    }

    public void SetTargetDistanceText(string text)
    {
        _courseHud?.SetTargetDistanceText(text);
    }

    public void SetTargetYardageUnknown()
    {
        _courseHud?.SetTargetYardageUnknown();
    }

    public void SetTargetElevationFeet(int feet)
    {
        _courseHud?.SetTargetElevationFeet(feet);
    }

    public void SetTargetElevationUnknown()
    {
        _courseHud?.SetTargetElevationUnknown();
    }

    public void SetTargetElevationVisible(bool visible)
    {
        _courseHud?.SetTargetElevationVisible(visible);
    }

    public void SetScoreLabel(string label)
    {
        _courseHud?.SetScoreLabel(label);
    }

    public void SetScoreUnknown()
    {
        _courseHud?.SetScoreUnknown();
    }

    public void ShowRoundEndScore(string label)
    {
        _courseHud?.ShowRoundEndScore(label);
    }

    public void HideRoundEndScore()
    {
        _courseHud?.HideRoundEndScore();
    }

    public void SetCourseHeader(string courseName, int holeNumber, int par, int yardage)
    {
        _courseHud?.SetCourseHeader(courseName, holeNumber, par, yardage);
    }

    public void SetCourseHeaderYardage(int yardage)
    {
        _courseHud?.SetCourseHeaderYardage(yardage);
    }

    public void SetCourseMetaVisible(bool visible)
    {
        _courseHud?.SetCourseMetaVisible(visible);
    }

    public void SetTracerHistorySettingVisible(bool visible)
    {
        _courseHud?.SetTracerHistorySettingVisible(visible);
    }

    public void SetRangeDefaultClubSettingVisible(bool visible)
    {
        _courseHud?.SetRangeDefaultClubSettingVisible(visible);
    }

    public void SetRangeHudControlsVisible(bool visible)
    {
        _courseHud?.SetRangeHudControlsVisible(visible);
    }

    public void ConfigureRangeHudControls(int minYards, int maxYards, int defaultYards, string defaultClub)
    {
        _courseHud?.ConfigureRangeHudControls(minYards, maxYards, defaultYards, defaultClub);
    }

    public int GetRangeTargetYardage()
    {
        return _courseHud?.GetRangeTargetYardage() ?? 0;
    }

    public string GetRangeSelectedClubFileTag()
    {
        return _courseHud?.GetRangeSelectedClubFileTag() ?? string.Empty;
    }

    public string GetRangeSelectedClubLabel()
    {
        return _courseHud?.GetRangeSelectedClubLabel() ?? RangeClubCatalog.DefaultClubLabel;
    }

    public void RecordRangeDispersionShot(string clubLabel, float distanceYards, float carryYards, float offlineYards)
    {
        _courseHud?.RecordRangeDispersionShot(clubLabel, distanceYards, carryYards, offlineYards);
    }

    public void SetMarkerCamera(Camera3D camera)
    {
        _markerHud?.SetCamera(camera);
    }

    public void SetMarkerElevationVisible(bool visible)
    {
        _markerHud?.SetElevationVisible(visible);
    }

    public void ApplyMarkerSnapshot(MarkerSnapshot snapshot)
    {
        _markerHud?.ApplySnapshot(snapshot);
    }

    public void ShowFlagMarker(Vector3 worldPoint, string distanceText, int elevationFeet)
    {
        _markerHud?.ShowFlagMarker(worldPoint, distanceText, elevationFeet);
    }

    public void HideFlagMarker()
    {
        _markerHud?.HideFlagMarker();
    }

    public void ShowPlayerMarker(Vector3 worldPoint, string distanceText, int elevationFeet)
    {
        _markerHud?.ShowPlayerMarker(worldPoint, distanceText, elevationFeet);
    }

    public void HidePlayerMarker()
    {
        _markerHud?.HidePlayerMarker();
    }

    private void OnCourseHudHitShot(Dictionary data)
    {
        EmitSignal(SignalName.HitShot, data);
    }
}
