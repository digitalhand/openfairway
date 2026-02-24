using System;

public static class ScoreMapper
{
    public static ScoreResult MapScore(int strokes, int par)
    {
        int normalizedStrokes = Math.Max(1, strokes);
        int normalizedPar = Math.Max(1, par);
        int relativeToPar = normalizedStrokes - normalizedPar;
        string label = GetScoreLabel(normalizedStrokes, relativeToPar);

        return new ScoreResult(normalizedStrokes, normalizedPar, relativeToPar, label);
    }

    private static string GetScoreLabel(int strokes, int relativeToPar)
    {
        if (strokes == 1)
            return "Hole in One";

        return relativeToPar switch
        {
            -1 => "Birdie",
            0 => "Par",
            1 => "Bogey",
            2 => "Double Bogey",
            3 => "Triple Bogey",
            -2 => "Eagle",
            -3 => "Albatross",
            <= -4 => $"{relativeToPar}",
            _ => $"+{relativeToPar}"
        };
    }
}

public readonly struct ScoreResult
{
    public ScoreResult(int strokes, int par, int relativeToPar, string label)
    {
        Strokes = strokes;
        Par = par;
        RelativeToPar = relativeToPar;
        Label = label ?? string.Empty;
    }

    public int Strokes { get; }
    public int Par { get; }
    public int RelativeToPar { get; }
    public string Label { get; }
}
