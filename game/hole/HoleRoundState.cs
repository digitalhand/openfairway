using Godot;

public sealed class HoleRoundState
{
    private string _sceneId = string.Empty;
    private CourseCardInfo _courseCard = CourseCatalog.DefaultCourseCard;
    private int _strokeCount;

    public string SceneId => _sceneId;
    public CourseCardInfo CourseCard => _courseCard;
    public int StrokeCount => _strokeCount;
    public int Par => _courseCard.Par;

    public bool Initialize(string sceneId)
    {
        _sceneId = sceneId ?? string.Empty;

        bool hasCourseCard = CourseCatalog.TryGetCourseCard(_sceneId, out CourseCardInfo resolvedCard);
        _courseCard = hasCourseCard ? resolvedCard : CourseCatalog.DefaultCourseCard;
        _strokeCount = 0;
        return hasCourseCard;
    }

    public void SetStrokes(int strokes)
    {
        _strokeCount = Mathf.Max(0, strokes);
    }

    public void ResetStrokes()
    {
        _strokeCount = 0;
    }

    public void IncrementStroke()
    {
        _strokeCount++;
    }

    public bool TryGetScore(out ScoreResult score)
    {
        if (_strokeCount <= 0)
        {
            score = default;
            return false;
        }

        score = ScoreMapper.MapScore(_strokeCount, Par);
        return true;
    }

    public string GetRoundEndLabel()
    {
        if (!TryGetScore(out ScoreResult score))
            return "Par";

        return score.Label;
    }
}
