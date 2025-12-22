using Godot;
using Godot.Collections;

/// <summary>
/// Formats ball/shot data for UI display with unit conversion.
/// </summary>
public static class ShotFormatter
{
    private const float METERS_TO_YARDS = 1.09361f;
    private const float METERS_TO_FEET = 3.28084f;
    private const float MPH_TO_MPS = 0.44704f;

    /// <summary>
    /// Format ball data for UI display.
    /// Converts units and calculates derived spin values as needed.
    /// </summary>
    /// <param name="rawBallData">Raw shot data from launch monitor or injector</param>
    /// <param name="shotTracker">Reference to ShotTracker for live measurements</param>
    /// <param name="units">Unit system to use for display</param>
    /// <param name="showDistance">Whether to update distance (false keeps previous value)</param>
    /// <param name="prevData">Previous display data (for preserving Distance when not updating)</param>
    public static Dictionary FormatBallDisplay(
        Dictionary rawBallData,
        Node shotTracker,
        PhysicsEnums.Units units,
        bool showDistance,
        Dictionary prevData = null)
    {
        if (prevData == null)
            prevData = new Dictionary();

        var ballData = new Dictionary();

        // Parse spin data
        var spin = ParseSpin(rawBallData);

        if (units == PhysicsEnums.Units.Imperial)
        {
            ballData = FormatImperial(rawBallData, shotTracker, showDistance, prevData, spin);
        }
        else
        {
            ballData = FormatMetric(rawBallData, shotTracker, showDistance, prevData, spin);
        }

        // Common fields (same in both unit systems)
        ballData["BackSpin"] = ((int)(float)spin["back"]).ToString();
        ballData["SideSpin"] = ((int)(float)spin["side"]).ToString();
        ballData["TotalSpin"] = ((int)(float)spin["total"]).ToString();
        ballData["SpinAxis"] = $"{(float)spin["axis"]:F1}";
        ballData["VLA"] = rawBallData.ContainsKey("VLA") ? rawBallData["VLA"] : 0.0f;
        ballData["HLA"] = rawBallData.ContainsKey("HLA") ? rawBallData["HLA"] : 0.0f;

        return ballData;
    }

    private static Dictionary FormatImperial(
        Dictionary rawData,
        Node tracker,
        bool showDistance,
        Dictionary prevData,
        Dictionary spin)
    {
        var data = new Dictionary();

        var shotTracker = (ShotTracker)tracker;

        // Distance
        if (showDistance)
        {
            data["Distance"] = $"{shotTracker.GetDistance() * METERS_TO_YARDS:F1}";
        }
        else
        {
            data["Distance"] = prevData.ContainsKey("Distance") ? prevData["Distance"] : "---";
        }

        // Carry
        float carryVal = shotTracker.Carry;
        if (carryVal <= 0 && rawData.ContainsKey("CarryDistance"))
        {
            carryVal = (float)rawData["CarryDistance"];
        }
        data["Carry"] = $"{carryVal * METERS_TO_YARDS:F1}";

        // Apex (convert meters to feet)
        data["Apex"] = $"{shotTracker.Apex * METERS_TO_FEET:F1}";

        // Side distance
        float sideDistance = shotTracker.GetSideDistance() * METERS_TO_YARDS;
        data["Offline"] = FormatSideDistance(sideDistance);

        // Speed (already in mph)
        data["Speed"] = $"{(rawData.ContainsKey("Speed") ? (float)rawData["Speed"] : 0.0f):F1}";

        return data;
    }

    private static Dictionary FormatMetric(
        Dictionary rawData,
        Node tracker,
        bool showDistance,
        Dictionary prevData,
        Dictionary spin)
    {
        var data = new Dictionary();
        var shotTracker = (ShotTracker)tracker;

        // Distance
        if (showDistance)
        {
            data["Distance"] = $"{shotTracker.GetDistance():F1}";
        }
        else
        {
            data["Distance"] = prevData.ContainsKey("Distance") ? prevData["Distance"] : "---";
        }

        // Carry
        float carryVal = shotTracker.Carry;
        if (carryVal <= 0 && rawData.ContainsKey("CarryDistance"))
        {
            carryVal = (float)rawData["CarryDistance"];
        }
        data["Carry"] = $"{carryVal:F1}";

        // Apex (meters)
        data["Apex"] = $"{shotTracker.Apex:F1}";

        // Side distance
        float sideDistance = shotTracker.GetSideDistance();
        data["Offline"] = FormatSideDistance(sideDistance);

        // Speed (convert mph to m/s)
        data["Speed"] = $"{(rawData.ContainsKey("Speed") ? (float)rawData["Speed"] : 0.0f) * MPH_TO_MPS:F1}";

        return data;
    }

    private static string FormatSideDistance(float distance)
    {
        string direction = distance >= 0 ? "R" : "L";
        return direction + $"{Mathf.Abs(distance):F1}";
    }

    private static Dictionary ParseSpin(Dictionary rawData)
    {
        bool hasBackspin = rawData.ContainsKey("BackSpin");
        bool hasSidespin = rawData.ContainsKey("SideSpin");
        bool hasTotal = rawData.ContainsKey("TotalSpin");
        bool hasAxis = rawData.ContainsKey("SpinAxis");

        float backspin = (float)(rawData.ContainsKey("BackSpin") ? rawData["BackSpin"] : 0.0f);
        float sidespin = (float)(rawData.ContainsKey("SideSpin") ? rawData["SideSpin"] : 0.0f);
        float totalSpin = (float)(rawData.ContainsKey("TotalSpin") ? rawData["TotalSpin"] : 0.0f);
        float spinAxis = (float)(rawData.ContainsKey("SpinAxis") ? rawData["SpinAxis"] : 0.0f);

        // Calculate missing values
        if (totalSpin == 0.0f && (hasBackspin || hasSidespin))
        {
            totalSpin = Mathf.Sqrt(backspin * backspin + sidespin * sidespin);
        }

        if (!hasAxis && (hasBackspin || hasSidespin))
        {
            spinAxis = Mathf.RadToDeg(Mathf.Atan2(sidespin, backspin));
        }

        if (hasTotal && hasAxis)
        {
            if (!hasBackspin)
            {
                backspin = totalSpin * Mathf.Cos(Mathf.DegToRad(spinAxis));
            }
            if (!hasSidespin)
            {
                sidespin = totalSpin * Mathf.Sin(Mathf.DegToRad(spinAxis));
            }
        }

        return new Dictionary
        {
            { "back", backspin },
            { "side", sidespin },
            { "total", totalSpin },
            { "axis", spinAxis }
        };
    }
}
