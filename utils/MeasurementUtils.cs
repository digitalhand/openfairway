using Godot;

public static class MeasurementUtils
{
    public const float MetersToYards = 1.09361f;
    public const float MetersToFeet = 3.28084f;

    public static int HorizontalDistanceYards(Vector3 from, Vector3 to)
    {
        Vector3 horizontal = to - from;
        horizontal.Y = 0.0f;
        return Mathf.RoundToInt(Mathf.Max(0.0f, horizontal.Length() * MetersToYards));
    }

    public static float HorizontalDistanceYardsFloat(Vector3 from, Vector3 to)
    {
        Vector3 horizontal = to - from;
        horizontal.Y = 0.0f;
        return Mathf.Max(0.0f, horizontal.Length() * MetersToYards);
    }

    public static int VerticalDeltaFeet(Vector3 from, Vector3 to)
    {
        return Mathf.RoundToInt((to.Y - from.Y) * MetersToFeet);
    }
}
