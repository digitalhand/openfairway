using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

public partial class RangeDispersionPlot : Control
{
    private const float FixedXAbsMaxYards = 150.0f;
    private const float FixedYMinYards = 0.0f;
    private const float FixedYMaxYards = 280.0f;
    private const int HorizontalDivisions = 10;
    private const int VerticalDivisions = 6;
    private const int MinShotsForEllipse = 3;
    private const int EllipseSegments = 72;
    private const float EllipseConfidenceScale95 = 2.4477468f; // sqrt(chi-square(0.95, dof=2))

    private static readonly Color PlotBackgroundColor = new Color(0.0627451f, 0.0862745f, 0.12549f, 0.95f);
    private static readonly Color PlotBorderColor = new Color(1.0f, 1.0f, 1.0f, 0.25f);
    private static readonly Color GridLineColor = new Color(1.0f, 1.0f, 1.0f, 0.08f);
    private static readonly Color ZeroLineColor = new Color(0.30f, 0.78f, 0.98f, 0.55f);
    private static readonly Color AxisLabelColor = new Color(0.94f, 0.98f, 1.0f, 0.9f);
    private static readonly Color LabelBackgroundColor = new Color(0.01f, 0.02f, 0.05f, 0.92f);
    private static readonly Color LabelBorderColor = new Color(1.0f, 1.0f, 1.0f, 0.18f);
    private static readonly Color EllipseShadowColor = new Color(0.0f, 0.0f, 0.0f, 0.38f);

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
    private bool _clubOverlayEnabled = true;
    private readonly HashSet<string> _visibleClubs = new(StringComparer.OrdinalIgnoreCase);
    private bool _hasClubVisibilityFilter;

    private sealed class ClubOverlay
    {
        public string ClubLabel { get; init; } = RangeClubCatalog.DefaultClubLabel;
        public Color Color { get; init; } = Colors.White;
        public Vector2 Center { get; init; } = Vector2.Zero;
        public Vector2 MajorAxis { get; init; } = Vector2.Right;
        public float MajorRadius { get; init; }
        public float MinorRadius { get; init; }
        public float TopY { get; init; }
        public string LabelText { get; init; } = string.Empty;
    }

    private readonly struct ClubAverages
    {
        public ClubAverages(float totalYards, float carryYards, float? hlaDeg, float? spinRpm)
        {
            TotalYards = totalYards;
            CarryYards = carryYards;
            HlaDeg = hlaDeg;
            SpinRpm = spinRpm;
        }

        public float TotalYards { get; }
        public float CarryYards { get; }
        public float? HlaDeg { get; }
        public float? SpinRpm { get; }
    }

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

    public void SetClubOverlayEnabled(bool enabled)
    {
        if (_clubOverlayEnabled == enabled)
            return;

        _clubOverlayEnabled = enabled;
        QueueRedraw();
    }

    public void SetVisibleClubs(IReadOnlyCollection<string> clubLabels)
    {
        _visibleClubs.Clear();
        _hasClubVisibilityFilter = clubLabels != null;

        if (clubLabels != null)
        {
            foreach (string clubLabel in clubLabels)
            {
                string normalized = RangeClubCatalog.NormalizeLabel(clubLabel);
                if (!string.IsNullOrWhiteSpace(normalized))
                    _visibleClubs.Add(normalized);
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

        if (!HasVisibleShots())
        {
            DrawCenteredLabel("All clubs hidden.", plotRect);
            return;
        }

        List<ClubOverlay> overlays = _clubOverlayEnabled
            ? BuildClubOverlays(plotRect)
            : new List<ClubOverlay>();

        foreach (ClubOverlay overlay in overlays)
            DrawClubEllipse(plotRect, overlay);

        foreach (RangeDispersionShot shot in _shots)
        {
            if (!IsClubVisible(shot.ClubLabel))
                continue;

            Vector2 point = MapShotToPlot(plotRect, shot, FixedXAbsMaxYards, FixedYMinYards, FixedYMaxYards);
            Color pointColor = ResolveClubColor(shot.ClubLabel);
            DrawCircle(point, 4.0f, pointColor);
            DrawArc(point, 4.0f, 0.0f, Mathf.Tau, 20, new Color(0, 0, 0, 0.45f), 1.0f, antialiased: true);
        }

        if (overlays.Count > 0)
            DrawOverlayLabels(plotRect, overlays);
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

    private List<ClubOverlay> BuildClubOverlays(Rect2 plotRect)
    {
        var groups = new Dictionary<string, List<RangeDispersionShot>>(StringComparer.OrdinalIgnoreCase);
        foreach (RangeDispersionShot shot in _shots)
        {
            if (shot == null)
                continue;

            string clubLabel = RangeClubCatalog.NormalizeLabel(shot.ClubLabel);
            if (!IsClubVisible(clubLabel))
                continue;

            if (!groups.TryGetValue(clubLabel, out List<RangeDispersionShot> clubShots))
            {
                clubShots = new List<RangeDispersionShot>();
                groups[clubLabel] = clubShots;
            }

            clubShots.Add(shot);
        }

        var overlays = new List<ClubOverlay>();
        foreach (string clubLabel in RangeClubCatalog.Labels)
        {
            if (!groups.TryGetValue(clubLabel, out List<RangeDispersionShot> clubShots))
                continue;

            if (clubShots.Count < MinShotsForEllipse)
                continue;

            var points = new List<Vector2>(clubShots.Count);
            foreach (RangeDispersionShot shot in clubShots)
                points.Add(MapShotToPlot(plotRect, shot, FixedXAbsMaxYards, FixedYMinYards, FixedYMaxYards));

            if (!TryBuildConfidenceEllipse(points, out Vector2 center, out Vector2 majorAxis, out float majorRadius, out float minorRadius))
                continue;

            ClubAverages averages = ComputeAverages(clubShots);
            overlays.Add(new ClubOverlay
            {
                ClubLabel = clubLabel,
                Color = ResolveClubColor(clubLabel),
                Center = center,
                MajorAxis = majorAxis,
                MajorRadius = majorRadius,
                MinorRadius = minorRadius,
                TopY = ComputeEllipseTopY(center, majorAxis, majorRadius, minorRadius),
                LabelText = BuildLabelText(clubLabel, averages)
            });
        }

        return overlays;
    }

    private void DrawClubEllipse(Rect2 plotRect, ClubOverlay overlay)
    {
        List<Vector2> points = BuildEllipsePolyline(overlay.Center, overlay.MajorAxis, overlay.MajorRadius, overlay.MinorRadius);
        if (points.Count < 2)
            return;

        Color stroke = new Color(overlay.Color.R, overlay.Color.G, overlay.Color.B, 0.82f);
        for (int i = 0; i < points.Count - 1; i++)
        {
            if (!TryClipLineToRect(plotRect, points[i], points[i + 1], out Vector2 clippedFrom, out Vector2 clippedTo))
                continue;

            DrawLine(clippedFrom, clippedTo, EllipseShadowColor, 3.6f, antialiased: true);
            DrawLine(clippedFrom, clippedTo, stroke, 2.4f, antialiased: true);
        }
    }

    private void DrawOverlayLabels(Rect2 plotRect, List<ClubOverlay> overlays)
    {
        Font font = GetThemeDefaultFont();
        int fontSize = Mathf.Max(9, GetThemeDefaultFontSize() - 4);
        if (font == null)
            return;

        overlays.Sort((left, right) => left.TopY.CompareTo(right.TopY));
        const float paddingX = 6.0f;
        const float paddingY = 4.0f;
        var placedRects = new List<Rect2>();

        foreach (ClubOverlay overlay in overlays)
        {
            if (string.IsNullOrWhiteSpace(overlay.LabelText))
                continue;

            Vector2 textSize = font.GetStringSize(overlay.LabelText, HorizontalAlignment.Left, -1.0f, fontSize);
            float rectWidth = textSize.X + (paddingX * 2.0f);
            float rectHeight = textSize.Y + (paddingY * 2.0f);

            Rect2 rect = new Rect2(
                overlay.Center.X - rectWidth * 0.5f,
                overlay.TopY - rectHeight - 8.0f,
                rectWidth,
                rectHeight);

            rect.Position = new Vector2(
                Mathf.Clamp(rect.Position.X, plotRect.Position.X + 2.0f, plotRect.End.X - rect.Size.X - 2.0f),
                Mathf.Clamp(rect.Position.Y, plotRect.Position.Y + 2.0f, plotRect.End.Y - rect.Size.Y - 2.0f));

            int safety = 0;
            while (IntersectsAny(rect, placedRects) && safety < 20)
            {
                float shiftedY = rect.Position.Y - (rect.Size.Y + 4.0f);
                rect.Position = new Vector2(rect.Position.X, Mathf.Max(plotRect.Position.Y + 2.0f, shiftedY));
                safety++;
            }

            placedRects.Add(rect);
            DrawRect(rect, LabelBackgroundColor, filled: true);
            DrawRect(rect, LabelBorderColor, filled: false, width: 1.0f);
            DrawString(
                font,
                new Vector2(rect.Position.X + paddingX, rect.Position.Y + paddingY + fontSize),
                overlay.LabelText,
                HorizontalAlignment.Left,
                -1.0f,
                fontSize,
                overlay.Color.Lerp(AxisLabelColor, 0.45f)
            );
        }
    }

    private static bool TryBuildConfidenceEllipse(
        List<Vector2> points,
        out Vector2 center,
        out Vector2 majorAxis,
        out float majorRadius,
        out float minorRadius)
    {
        center = Vector2.Zero;
        majorAxis = Vector2.Right;
        majorRadius = 0.0f;
        minorRadius = 0.0f;

        if (points == null || points.Count < MinShotsForEllipse)
            return false;

        float meanX = 0.0f;
        float meanY = 0.0f;
        foreach (Vector2 point in points)
        {
            meanX += point.X;
            meanY += point.Y;
        }

        float invCount = 1.0f / points.Count;
        meanX *= invCount;
        meanY *= invCount;
        center = new Vector2(meanX, meanY);

        float covarianceXX = 0.0f;
        float covarianceXY = 0.0f;
        float covarianceYY = 0.0f;
        foreach (Vector2 point in points)
        {
            float dx = point.X - meanX;
            float dy = point.Y - meanY;
            covarianceXX += dx * dx;
            covarianceXY += dx * dy;
            covarianceYY += dy * dy;
        }

        float denom = Mathf.Max(1.0f, points.Count - 1.0f);
        covarianceXX /= denom;
        covarianceXY /= denom;
        covarianceYY /= denom;

        float trace = covarianceXX + covarianceYY;
        float determinantTerm = (covarianceXX - covarianceYY) * (covarianceXX - covarianceYY) + 4.0f * covarianceXY * covarianceXY;
        float rootTerm = Mathf.Sqrt(Mathf.Max(0.0f, determinantTerm));
        float eigenValueMax = Mathf.Max((trace + rootTerm) * 0.5f, 0.0f);
        float eigenValueMin = Mathf.Max((trace - rootTerm) * 0.5f, 0.0f);
        if (eigenValueMax < 0.0001f)
            return false;

        if (Mathf.Abs(covarianceXY) > 0.0001f)
            majorAxis = new Vector2(eigenValueMax - covarianceYY, covarianceXY).Normalized();
        else
            majorAxis = covarianceXX >= covarianceYY ? Vector2.Right : Vector2.Down;

        if (majorAxis.LengthSquared() < 0.0001f)
            majorAxis = Vector2.Right;

        majorRadius = Mathf.Max(2.0f, EllipseConfidenceScale95 * Mathf.Sqrt(eigenValueMax));
        minorRadius = Mathf.Max(2.0f, EllipseConfidenceScale95 * Mathf.Sqrt(Mathf.Max(eigenValueMin, 0.0f)));
        return true;
    }

    private static List<Vector2> BuildEllipsePolyline(Vector2 center, Vector2 majorAxis, float majorRadius, float minorRadius)
    {
        Vector2 axisA = majorAxis.Normalized();
        Vector2 axisB = new Vector2(-axisA.Y, axisA.X);
        var points = new List<Vector2>(EllipseSegments + 1);

        for (int i = 0; i <= EllipseSegments; i++)
        {
            float t = (i / (float)EllipseSegments) * Mathf.Tau;
            float cos = Mathf.Cos(t);
            float sin = Mathf.Sin(t);
            Vector2 point = center + (axisA * (cos * majorRadius)) + (axisB * (sin * minorRadius));
            points.Add(point);
        }

        return points;
    }

    private static float ComputeEllipseTopY(Vector2 center, Vector2 majorAxis, float majorRadius, float minorRadius)
    {
        List<Vector2> ellipsePoints = BuildEllipsePolyline(center, majorAxis, majorRadius, minorRadius);
        if (ellipsePoints.Count == 0)
            return center.Y;

        float topY = ellipsePoints[0].Y;
        for (int i = 1; i < ellipsePoints.Count; i++)
        {
            if (ellipsePoints[i].Y < topY)
                topY = ellipsePoints[i].Y;
        }

        return topY;
    }

    private static bool IntersectsAny(Rect2 rect, List<Rect2> others)
    {
        foreach (Rect2 other in others)
        {
            if (rect.Intersects(other))
                return true;
        }

        return false;
    }

    private static ClubAverages ComputeAverages(IReadOnlyList<RangeDispersionShot> shots)
    {
        if (shots == null || shots.Count == 0)
            return new ClubAverages(0.0f, 0.0f, null, null);

        float totalDistance = 0.0f;
        float totalCarry = 0.0f;
        float sumHla = 0.0f;
        int hlaCount = 0;
        float sumSpin = 0.0f;
        int spinCount = 0;

        foreach (RangeDispersionShot shot in shots)
        {
            totalDistance += shot.DistanceYards;
            totalCarry += shot.CarryYards;

            if (shot.HlaDeg.HasValue && IsFinite(shot.HlaDeg.Value))
            {
                sumHla += shot.HlaDeg.Value;
                hlaCount++;
            }

            if (shot.TotalSpinRpm.HasValue && IsFinite(shot.TotalSpinRpm.Value))
            {
                sumSpin += shot.TotalSpinRpm.Value;
                spinCount++;
            }
        }

        float count = shots.Count;
        return new ClubAverages(
            totalDistance / count,
            totalCarry / count,
            hlaCount > 0 ? sumHla / hlaCount : null,
            spinCount > 0 ? sumSpin / spinCount : null
        );
    }

    private static string BuildLabelText(string clubLabel, ClubAverages averages)
    {
        string total = averages.TotalYards.ToString("F0", CultureInfo.InvariantCulture);
        string carry = averages.CarryYards.ToString("F0", CultureInfo.InvariantCulture);
        string hla = averages.HlaDeg.HasValue
            ? averages.HlaDeg.Value.ToString("F1", CultureInfo.InvariantCulture) + "°"
            : "N/A";
        string spin = averages.SpinRpm.HasValue
            ? averages.SpinRpm.Value.ToString("F0", CultureInfo.InvariantCulture)
            : "N/A";

        return $"{clubLabel}  TOT {total}y  CARRY {carry}y  HLA {hla}  SPIN {spin}";
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private bool HasVisibleShots()
    {
        foreach (RangeDispersionShot shot in _shots)
        {
            if (shot != null && IsClubVisible(shot.ClubLabel))
                return true;
        }

        return false;
    }

    private bool IsClubVisible(string clubLabel)
    {
        if (!_hasClubVisibilityFilter)
            return true;

        string normalized = RangeClubCatalog.NormalizeLabel(clubLabel);
        return _visibleClubs.Contains(normalized);
    }

    private static bool TryClipLineToRect(
        Rect2 rect,
        Vector2 from,
        Vector2 to,
        out Vector2 clippedFrom,
        out Vector2 clippedTo)
    {
        clippedFrom = from;
        clippedTo = to;

        float t0 = 0.0f;
        float t1 = 1.0f;
        Vector2 delta = to - from;

        if (!ClipTest(-delta.X, from.X - rect.Position.X, ref t0, ref t1))
            return false;
        if (!ClipTest(delta.X, rect.End.X - from.X, ref t0, ref t1))
            return false;
        if (!ClipTest(-delta.Y, from.Y - rect.Position.Y, ref t0, ref t1))
            return false;
        if (!ClipTest(delta.Y, rect.End.Y - from.Y, ref t0, ref t1))
            return false;

        clippedFrom = from + (delta * t0);
        clippedTo = from + (delta * t1);
        return true;
    }

    private static bool ClipTest(float p, float q, ref float t0, ref float t1)
    {
        const float epsilon = 0.0001f;
        if (Mathf.Abs(p) <= epsilon)
            return q >= 0.0f;

        float ratio = q / p;
        if (p < 0.0f)
        {
            if (ratio > t1)
                return false;

            if (ratio > t0)
                t0 = ratio;
        }
        else
        {
            if (ratio < t0)
                return false;

            if (ratio < t1)
                t1 = ratio;
        }

        return true;
    }
}
