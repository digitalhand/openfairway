using Godot;

/// <summary>
/// Reusable goal zone marker queried when the ball settles.
/// </summary>
public partial class CourseGoalZone : Area3D
{
    public override void _Ready()
    {
        AddToGroup("course_goal_zone");
    }

    public bool IsBallOnZone(GolfBall ball)
    {
        return ball != null && OverlapsBody(ball);
    }
}
