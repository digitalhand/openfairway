using Godot;

public static class ElevationPresenter
{
    public static readonly Color PositiveColor = new Color(0.683984f, 0.859376f, 0.307523f, 1.0f);
    public static readonly Color NegativeColor = new Color(0.92f, 0.33f, 0.33f, 1.0f);
    public static readonly Color NeutralColor = new Color(0.96f, 0.98f, 1.0f, 1.0f);

    public const string ArrowUp = "▲";
    public const string ArrowDown = "▼";
    public const string ArrowFlat = "→";

    public static ElevationVisual Build(int feet, bool includeSignInText)
    {
        if (feet > 0)
        {
            string text = includeSignInText ? $"+{feet}FT" : $"{feet}FT";
            return new ElevationVisual(ArrowUp, text, PositiveColor);
        }

        if (feet < 0)
        {
            string text = includeSignInText ? $"{feet}FT" : $"{Mathf.Abs(feet)}FT";
            return new ElevationVisual(ArrowDown, text, NegativeColor);
        }

        return new ElevationVisual(ArrowFlat, "0FT", NeutralColor);
    }
}

public readonly struct ElevationVisual
{
    public ElevationVisual(string arrow, string text, Color color)
    {
        Arrow = arrow;
        Text = text;
        Color = color;
    }

    public string Arrow { get; }
    public string Text { get; }
    public Color Color { get; }
}
