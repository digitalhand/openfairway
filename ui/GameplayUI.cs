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

    public void SetMarkerCamera(Camera3D camera)
    {
        _markerHud?.SetCamera(camera);
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
