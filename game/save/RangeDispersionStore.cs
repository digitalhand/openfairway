using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Godot;
using Godot.Collections;

public sealed class RangeDispersionShot
{
    public RangeDispersionShot()
    {
    }

    public RangeDispersionShot(
        string clubLabel,
        float distanceYards,
        float carryYards,
        float offlineYards,
        float? hlaDeg = null,
        float? totalSpinRpm = null)
    {
        ClubLabel = RangeClubCatalog.NormalizeLabel(clubLabel);
        ClubTag = RangeClubCatalog.ToFileTag(ClubLabel);
        DistanceYards = distanceYards;
        CarryYards = carryYards;
        OfflineYards = offlineYards;
        HlaDeg = hlaDeg;
        TotalSpinRpm = totalSpinRpm;
        RecordedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    }

    public string ClubLabel { get; set; } = RangeClubCatalog.DefaultClubLabel;
    public string ClubTag { get; set; } = RangeClubCatalog.ToFileTag(RangeClubCatalog.DefaultClubLabel);
    public float DistanceYards { get; set; }
    public float CarryYards { get; set; }
    public float OfflineYards { get; set; }
    public float? HlaDeg { get; set; }
    public float? TotalSpinRpm { get; set; }
    public string RecordedUtc { get; set; } = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
}

public sealed class RangeDispersionSession
{
    public string FileName { get; set; } = string.Empty;
    public string CreatedUtc { get; set; } = string.Empty;
    public string UpdatedUtc { get; set; } = string.Empty;
    public List<RangeDispersionShot> Shots { get; } = new();
}

public static class RangeDispersionStore
{
    public const string SaveDirectoryPath = "user://range_dispersion";

    private const int SaveVersion = 2;
    private const int LegacySaveVersion = 1;
    private const string FilePrefix = "range_dispersion_";
    private const string FileSuffix = ".json";
    private const string TimestampFormat = "yyyyMMdd_HHmmssfff";

    public static List<RangeDispersionSession> LoadAllSessions()
    {
        var sessions = new List<RangeDispersionSession>();
        EnsureSaveDirectory();

        string absoluteDir = ProjectSettings.GlobalizePath(SaveDirectoryPath);
        if (!Directory.Exists(absoluteDir))
            return sessions;

        foreach (string absoluteFilePath in Directory.GetFiles(absoluteDir, $"{FilePrefix}*{FileSuffix}"))
        {
            string fileName = Path.GetFileName(absoluteFilePath);
            if (!IsValidSessionFileName(fileName))
                continue;

            RangeDispersionSession session = LoadSession(fileName);
            if (session != null)
                sessions.Add(session);
        }

        sessions.Sort((a, b) => string.CompareOrdinal(b.FileName, a.FileName));
        return sessions;
    }

    public static RangeDispersionSession CreateNewSession(bool persist = true)
    {
        EnsureSaveDirectory();

        string timestamp = DateTime.UtcNow.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        string fileName = $"{FilePrefix}{timestamp}{FileSuffix}";
        int suffix = 1;
        while (File.Exists(ToAbsoluteFilePath(fileName)))
        {
            fileName = $"{FilePrefix}{timestamp}_{suffix}{FileSuffix}";
            suffix++;
        }

        var session = new RangeDispersionSession
        {
            FileName = fileName,
            CreatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            UpdatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };

        if (persist)
            SaveSession(session);
        return session;
    }

    public static RangeDispersionSession LoadSession(string fileName)
    {
        string safeFileName = NormalizeFileName(fileName);
        if (string.IsNullOrEmpty(safeFileName))
            return null;

        string absolutePath = ToAbsoluteFilePath(safeFileName);
        if (!File.Exists(absolutePath))
            return null;

        try
        {
            string text = File.ReadAllText(absolutePath);
            var json = new Json();
            if (json.Parse(text) != Error.Ok || json.Data.VariantType != Variant.Type.Dictionary)
            {
                PhysicsLogger.Error($"RangeDispersionStore: invalid JSON in {safeFileName}.");
                return null;
            }

            var root = (Dictionary)json.Data;
            if (!root.ContainsKey("version"))
                return null;

            int version = VariantToInt(root["version"], 0);
            if (version < LegacySaveVersion || version > SaveVersion)
            {
                PhysicsLogger.Info($"RangeDispersionStore: unsupported save version {version} in {safeFileName}.");
                return null;
            }

            var session = new RangeDispersionSession
            {
                FileName = safeFileName,
                CreatedUtc = root.ContainsKey("created_utc")
                    ? root["created_utc"].ToString()
                    : GuessCreatedUtcFromFileName(safeFileName),
                UpdatedUtc = root.ContainsKey("updated_utc")
                    ? root["updated_utc"].ToString()
                    : string.Empty
            };

            if (root.ContainsKey("shots") && root["shots"].VariantType == Variant.Type.Array)
            {
                var shotsArray = (Godot.Collections.Array)root["shots"];
                foreach (Variant variant in shotsArray)
                {
                    if (variant.VariantType != Variant.Type.Dictionary)
                        continue;

                    var shotDict = (Dictionary)variant;
                    if (!TryParseShot(shotDict, out RangeDispersionShot shot))
                        continue;

                    session.Shots.Add(shot);
                }
            }

            return session;
        }
        catch (Exception ex)
        {
            PhysicsLogger.Error($"RangeDispersionStore: failed loading {safeFileName}: {ex.Message}");
            return null;
        }
    }

    public static bool AppendShot(string fileName, RangeDispersionShot shot, out RangeDispersionSession updatedSession)
    {
        updatedSession = null;
        if (shot == null)
            return false;

        string safeFileName = NormalizeFileName(fileName);
        if (string.IsNullOrEmpty(safeFileName))
            return false;

        RangeDispersionSession session = LoadSession(safeFileName);
        if (session == null)
        {
            string nowUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            session = new RangeDispersionSession
            {
                FileName = safeFileName,
                CreatedUtc = GuessCreatedUtcFromFileName(safeFileName),
                UpdatedUtc = nowUtc
            };

            if (string.IsNullOrWhiteSpace(session.CreatedUtc))
                session.CreatedUtc = nowUtc;
        }

        shot.ClubLabel = RangeClubCatalog.NormalizeLabel(shot.ClubLabel);
        shot.ClubTag = RangeClubCatalog.ToFileTag(shot.ClubLabel);
        shot.RecordedUtc = string.IsNullOrWhiteSpace(shot.RecordedUtc)
            ? DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            : shot.RecordedUtc;

        session.Shots.Add(shot);
        session.UpdatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        SaveSession(session);

        updatedSession = session;
        return true;
    }

    public static bool DeleteSession(string fileName)
    {
        string safeFileName = NormalizeFileName(fileName);
        if (string.IsNullOrEmpty(safeFileName))
            return false;

        string absolutePath = ToAbsoluteFilePath(safeFileName);
        if (!File.Exists(absolutePath))
            return false;

        try
        {
            File.Delete(absolutePath);
            return true;
        }
        catch (Exception ex)
        {
            PhysicsLogger.Error($"RangeDispersionStore: failed deleting {safeFileName}: {ex.Message}");
            return false;
        }
    }

    public static string BuildSessionLabel(RangeDispersionSession session)
    {
        if (session == null)
            return "Unknown";

        DateTime timestamp;
        if (!TryParseUtc(session.CreatedUtc, out timestamp))
            TryParseUtc(GuessCreatedUtcFromFileName(session.FileName), out timestamp);

        string timeLabel = timestamp != default
            ? timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : session.FileName;

        return $"{timeLabel} ({session.Shots.Count} shots)";
    }

    private static void SaveSession(RangeDispersionSession session)
    {
        if (session == null)
            return;

        string safeFileName = NormalizeFileName(session.FileName);
        if (string.IsNullOrEmpty(safeFileName))
            return;

        EnsureSaveDirectory();
        session.FileName = safeFileName;
        if (string.IsNullOrWhiteSpace(session.CreatedUtc))
            session.CreatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(session.UpdatedUtc))
            session.UpdatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        var shotsArray = new Godot.Collections.Array();
        foreach (RangeDispersionShot shot in session.Shots)
        {
            var shotDict = new Dictionary
            {
                { "club_label", RangeClubCatalog.NormalizeLabel(shot.ClubLabel) },
                { "club_tag", RangeClubCatalog.ToFileTag(shot.ClubLabel) },
                { "distance_yards", shot.DistanceYards },
                { "carry_yards", shot.CarryYards },
                { "offline_yards", shot.OfflineYards },
                { "recorded_utc", string.IsNullOrWhiteSpace(shot.RecordedUtc)
                    ? DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                    : shot.RecordedUtc }
            };

            if (shot.HlaDeg.HasValue && IsFinite(shot.HlaDeg.Value))
                shotDict["hla_deg"] = shot.HlaDeg.Value;
            if (shot.TotalSpinRpm.HasValue && IsFinite(shot.TotalSpinRpm.Value))
                shotDict["total_spin_rpm"] = shot.TotalSpinRpm.Value;

            shotsArray.Add(shotDict);
        }

        var root = new Dictionary
        {
            { "version", SaveVersion },
            { "created_utc", session.CreatedUtc },
            { "updated_utc", session.UpdatedUtc },
            { "shots", shotsArray }
        };

        string absolutePath = ToAbsoluteFilePath(safeFileName);
        try
        {
            File.WriteAllText(absolutePath, Json.Stringify(root, "\t"));
        }
        catch (Exception ex)
        {
            PhysicsLogger.Error($"RangeDispersionStore: failed writing {safeFileName}: {ex.Message}");
        }
    }

    private static bool TryParseShot(Dictionary shotDict, out RangeDispersionShot shot)
    {
        shot = null;
        if (shotDict == null)
            return false;

        string clubLabel = shotDict.ContainsKey("club_label")
            ? RangeClubCatalog.NormalizeLabel(shotDict["club_label"].ToString())
            : RangeClubCatalog.DefaultClubLabel;

        float distanceYards = shotDict.ContainsKey("distance_yards")
            ? VariantToFloat(shotDict["distance_yards"], 0.0f)
            : 0.0f;
        float carryYards = shotDict.ContainsKey("carry_yards")
            ? VariantToFloat(shotDict["carry_yards"], 0.0f)
            : 0.0f;
        float offlineYards = shotDict.ContainsKey("offline_yards")
            ? VariantToFloat(shotDict["offline_yards"], 0.0f)
            : 0.0f;
        float? hlaDeg = TryVariantToFloat(shotDict, "hla_deg");
        float? totalSpinRpm = TryVariantToFloat(shotDict, "total_spin_rpm");
        string recordedUtc = shotDict.ContainsKey("recorded_utc")
            ? shotDict["recorded_utc"].ToString()
            : string.Empty;

        shot = new RangeDispersionShot(clubLabel, distanceYards, carryYards, offlineYards, hlaDeg, totalSpinRpm)
        {
            RecordedUtc = string.IsNullOrWhiteSpace(recordedUtc)
                ? DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                : recordedUtc
        };

        return true;
    }

    private static void EnsureSaveDirectory()
    {
        string absolutePath = ProjectSettings.GlobalizePath(SaveDirectoryPath);
        if (!Directory.Exists(absolutePath))
            Directory.CreateDirectory(absolutePath);
    }

    private static string ToAbsoluteFilePath(string fileName)
    {
        return Path.Combine(ProjectSettings.GlobalizePath(SaveDirectoryPath), fileName);
    }

    private static string NormalizeFileName(string fileName)
    {
        if (!IsValidSessionFileName(fileName))
            return string.Empty;

        return Path.GetFileName(fileName);
    }

    private static bool IsValidSessionFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        string onlyFileName = Path.GetFileName(fileName);
        return onlyFileName.StartsWith(FilePrefix, StringComparison.Ordinal)
            && onlyFileName.EndsWith(FileSuffix, StringComparison.OrdinalIgnoreCase);
    }

    private static string GuessCreatedUtcFromFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        string trimmed = fileName;
        if (trimmed.StartsWith(FilePrefix, StringComparison.Ordinal))
            trimmed = trimmed.Substring(FilePrefix.Length);
        if (trimmed.EndsWith(FileSuffix, StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed.Substring(0, trimmed.Length - FileSuffix.Length);

        // Supports optional numeric suffixes added for collision handling.
        string[] parts = trimmed.Split('_');
        if (parts.Length >= 2)
        {
            string timestampToken = $"{parts[0]}_{parts[1]}";
            if (DateTime.TryParseExact(
                    timestampToken,
                    TimestampFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTime parsed))
            {
                return parsed.ToString("O", CultureInfo.InvariantCulture);
            }
        }

        return DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    }

    private static bool TryParseUtc(string utcText, out DateTime timestamp)
    {
        return DateTime.TryParse(
            utcText,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out timestamp
        );
    }

    private static int VariantToInt(Variant value, int fallback)
    {
        return value.VariantType switch
        {
            Variant.Type.Int => (int)value,
            Variant.Type.Float => Mathf.RoundToInt((float)value),
            Variant.Type.String => int.TryParse((string)value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback,
            _ => fallback
        };
    }

    private static float VariantToFloat(Variant value, float fallback)
    {
        return value.VariantType switch
        {
            Variant.Type.Float => (float)value,
            Variant.Type.Int => (int)value,
            Variant.Type.String => float.TryParse((string)value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) ? parsed : fallback,
            _ => fallback
        };
    }

    private static float? TryVariantToFloat(Dictionary dictionary, string key)
    {
        if (dictionary == null || string.IsNullOrWhiteSpace(key) || !dictionary.ContainsKey(key))
            return null;

        float parsed = VariantToFloat(dictionary[key], float.NaN);
        return IsFinite(parsed) ? parsed : null;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
