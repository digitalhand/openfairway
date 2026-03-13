using System.Collections.Generic;
using Godot;

public partial class RangeDispersionPlot : Control
{
    private const float FixedXAbsMaxYards = 150.0f;
    private const float FixedYMinYards = 0.0f;
    private const float FixedYMaxYards = 280.0f;
    private const int HorizontalDivisions = 10;
    private const int VerticalDivisions = 6;

    private static readonly Color PlotBackgroundColor = new Color(0.0627451f, 0.0862745f, 0.12549f, 0.95f);
    private static readonly Color PlotBorderColor = new Color(1.0f, 1.0f, 1.0f, 0.25f);
    private static readonly Color GridLineColor = new Color(1.0f, 1.0f, 1.0f, 0.08f);
    private static readonly Color ZeroLineColor = new Color(0.30f, 0.78f, 0.98f, 0.55f);
    private static readonly Color AxisLabelColor = new Color(0.94f, 0.98f, 1.0f, 0.9f);

    private static readonly Color[] ClubPalette =
    {
        new Color(0.23f, 0.80f, 0.34f, 0.92f), // Driver
        new Color(0.95f, 0.29f, 0.33f, 0.92f), // 3W
        new Color(0.98f, 0.71f, 0.24f, 0.92f), // 5W
        new Color(0.59f, 0.44f, 0.95f, 0.92f), // 4H
        new Color(0.20f, 0.67f, 0.93f, 0.92f), // 3I
        new Color(0.95f, 0.48f, 0.19f, 0.92f), // 4I
        new Color(0.93f, 0.29f, 0.70f, 0.92f), // 5I
        new Color(0.34f, 0.86f, 0.73f, 0.92f), // 6I
        new Color(0.78f, 0.86f, 0.30f, 0.92f), // 7I
        new Color(0.98f, 0.52f, 0.58f, 0.92f), // 8I
        new Color(0.54f, 0.72f, 0.98f, 0.92f), // 9I
        new Color(0.98f, 0.83f, 0.31f, 0.92f), // PW
        new Color(0.99f, 0.60f, 0.25f, 0.92f), // GW
        new Color(0.95f, 0.39f, 0.47f, 0.92f), // SW
        new Color(0.76f, 0.57f, 0.96f, 0.92f)  // LW
    };

    private readonly List<RangeDispersionShot> _shots = new();

    public void SetShots(IReadOnlyList<RangeDispersionShot> shots)
    {
        _shots.Clear();
        if (shots != null)
        {
            foreach (RangeDispersionShot shot in shots)
            {
                if (shot != null)
                    _shots.Add(shot);
            }
        }

        QueueRedraw();
    }

    public static Color ResolveClubColor(string clubLabel)
    {
        string normalized = RangeClubCatalog.NormalizeLabel(clubLabel);
        for (int i = 0; i < RangeClubCatalog.Labels.Count; i++)
        {
            if (RangeClubCatalog.Labels[i] == normalized)
                return ClubPalette[i % ClubPalette.Length];
        }

        return ClubPalette[0];
    }

    public override void _Draw()
    {
        Rect2 plotRect = BuildPlotRect();
        if (plotRect.Size.X <= 4.0f || plotRect.Size.Y <= 4.0f)
            return;

        DrawRect(plotRect, PlotBackgroundColor, filled: true);
        DrawRect(plotRect, PlotBorderColor, filled: false, width: 1.0f);

        DrawGrid(plotRect);
        DrawAxisLabels(plotRect, FixedXAbsMaxYards, FixedYMinYards, FixedYMaxYards);
        DrawZeroLine(plotRect, FixedXAbsMaxYards);

        if (_shots.Count == 0)
        {
            DrawCenteredLabel("No dispersion shots recorded yet.", plotRect);
            return;
        }

        foreach (RangeDispersionShot shot in _shots)
        {
            Vector2 point = MapShotToPlot(plotRect, shot, FixedXAbsMaxYards, FixedYMinYards, FixedYMaxYards);
            Color pointColor = ResolveClubColor(shot.ClubLabel);
            DrawCircle(point, 4.0f, pointColor);
            DrawArc(point, 4.0f, 0.0f, Mathf.Tau, 20, new Color(0, 0, 0, 0.45f), 1.0f, antialiased: true);
        }
    }

    private Rect2 BuildPlotRect()
    {
        const float leftPadding = 58.0f;
        const float topPadding = 14.0f;
        const float rightPadding = 20.0f;
        const float bottomPadding = 30.0f;
        return new Rect2(
            leftPadding,
            topPadding,
            Mathf.Max(1.0f, Size.X - leftPadding - rightPadding),
            Mathf.Max(1.0f, Size.Y - topPadding - bottomPadding)
        );
    }

    private void DrawGrid(Rect2 plotRect)
    {
        for (int i = 1; i < HorizontalDivisions; i++)
        {
            float t = i / (float)HorizontalDivisions;
            float y = Mathf.Lerp(plotRect.Position.Y, plotRect.End.Y, t);
            DrawLine(new Vector2(plotRect.Position.X, y), new Vector2(plotRect.End.X, y), GridLineColor, 1.0f);
        }

        for (int i = 1; i < VerticalDivisions; i++)
        {
            float t = i / (float)VerticalDivisions;
            float x = Mathf.Lerp(plotRect.Position.X, plotRect.End.X, t);
            DrawLine(new Vector2(x, plotRect.Position.Y), new Vector2(x, plotRect.End.Y), GridLineColor, 1.0f);
        }
    }

    private void DrawZeroLine(Rect2 plotRect, float xAbsMax)
    {
        if (xAbsMax <= 0.0f)
            return;

        float xZero = MapX(plotRect, 0.0f, xAbsMax);
        DrawLine(new Vector2(xZero, plotRect.Position.Y), new Vector2(xZero, plotRect.End.Y), ZeroLineColor, 1.5f);
    }

    private Vector2 MapShotToPlot(Rect2 plotRect, RangeDispersionShot shot, float xAbsMax, float yMin, float yMax)
    {
        float x = MapX(plotRect, shot.OfflineYards, xAbsMax);
        float y = MapY(plotRect, shot.DistanceYards, yMin, yMax);
        return new Vector2(x, y);
    }

    private static float MapX(Rect2 plotRect, float offlineYards, float xAbsMax)
    {
        float normalized = (offlineYards + xAbsMax) / (xAbsMax * 2.0f);
        return plotRect.Position.X + Mathf.Clamp(normalized, 0.0f, 1.0f) * plotRect.Size.X;
    }

    private static float MapY(Rect2 plotRect, float distanceYards, float yMin, float yMax)
    {
        float normalized = (distanceYards - yMin) / Mathf.Max(0.001f, yMax - yMin);
        return plotRect.End.Y - Mathf.Clamp(normalized, 0.0f, 1.0f) * plotRect.Size.Y;
    }

    private void DrawAxisLabels(Rect2 plotRect, float xAbsMax, float yMin, float yMax)
    {
        Font font = GetThemeDefaultFont();
        int fontSize = Mathf.Max(11, GetThemeDefaultFontSize() - 2);
        if (font == null)
            return;

        for (int i = 0; i <= HorizontalDivisions; i++)
        {
            float t = i / (float)HorizontalDivisions;
            float value = Mathf.Lerp(yMin, yMax, t);
            float y = MapY(plotRect, value, yMin, yMax);
            DrawString(
                font,
                new Vector2(plotRect.Position.X - 40.0f, y + fontSize * 0.35f),
                $"{value:F0}",
                HorizontalAlignment.Left,
                -1.0f,
                fontSize,
                AxisLabelColor
            );
        }

        for (int i = 0; i <= VerticalDivisions; i++)
        {
            float t = i / (float)VerticalDivisions;
            float value = Mathf.Lerp(-xAbsMax, xAbsMax, t);
            float x = Mathf.Lerp(plotRect.Position.X, plotRect.End.X, t);
            HorizontalAlignment alignment = i switch
            {
                0 => HorizontalAlignment.Left,
                VerticalDivisions => HorizontalAlignment.Right,
                _ => HorizontalAlignment.Center
            };

            DrawString(
                font,
                new Vector2(x, plotRect.End.Y + 14.0f),
                FormatOfflineLabel(value),
                alignment,
                -1.0f,
                fontSize,
                AxisLabelColor
            );
        }
    }

    private static string FormatOfflineLabel(float value)
    {
        if (Mathf.IsZeroApprox(value))
            return "0";

        return value < 0.0f
            ? $"{Mathf.Abs(value):F0}L"
            : $"{value:F0}R";
    }

    private void DrawCenteredLabel(string text, Rect2 rect)
    {
        Font font = GetThemeDefaultFont();
        int fontSize = Mathf.Max(13, GetThemeDefaultFontSize());
        if (font == null || string.IsNullOrWhiteSpace(text))
            return;

        Vector2 textSize = font.GetStringSize(text, HorizontalAlignment.Left, -1.0f, fontSize);
        Vector2 position = new Vector2(
            rect.GetCenter().X - textSize.X * 0.5f,
            rect.GetCenter().Y + textSize.Y * 0.35f
        );

        DrawString(font, position, text, HorizontalAlignment.Left, -1.0f, fontSize, AxisLabelColor);
    }
}
