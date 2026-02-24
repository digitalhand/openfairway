using System;
using System.Collections.Generic;
using System.IO;

public static class CourseCatalog
{
    public const int DefaultPar = 3;
    public static readonly CourseCardInfo DefaultCourseCard = new("Airways", 1, DefaultPar, 203);

    private static readonly Dictionary<string, CourseCardInfo> CourseCardByKey = new(StringComparer.OrdinalIgnoreCase)
    {
        { "res://courses/Range/range.tscn", new CourseCardInfo("Airways", 1, 3, 203) },
        { "range", new CourseCardInfo("Airways", 1, 3, 203) }
    };

    public static bool TryGetPar(string sceneId, out int par)
    {
        par = DefaultPar;
        if (!TryGetCourseCard(sceneId, out CourseCardInfo courseCard))
            return false;

        par = courseCard.Par;
        return true;
    }

    public static bool TryGetCourseCard(string sceneId, out CourseCardInfo courseCard)
    {
        courseCard = DefaultCourseCard;
        if (string.IsNullOrWhiteSpace(sceneId))
            return false;

        if (CourseCardByKey.TryGetValue(sceneId, out CourseCardInfo directCard))
        {
            courseCard = NormalizeCourseCard(directCard);
            return true;
        }

        string normalizedKey = NormalizeCourseKey(sceneId);
        if (string.IsNullOrWhiteSpace(normalizedKey))
            return false;

        if (CourseCardByKey.TryGetValue(normalizedKey, out CourseCardInfo normalizedCard))
        {
            courseCard = NormalizeCourseCard(normalizedCard);
            return true;
        }

        return false;
    }

    public static string NormalizeCourseKey(string sceneId)
    {
        if (string.IsNullOrWhiteSpace(sceneId))
            return string.Empty;

        string trimmed = sceneId.Trim();
        if (trimmed.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
        {
            string withoutExt = Path.GetFileNameWithoutExtension(trimmed);
            return string.IsNullOrWhiteSpace(withoutExt) ? string.Empty : withoutExt;
        }

        return trimmed;
    }

    private static CourseCardInfo NormalizeCourseCard(CourseCardInfo card)
    {
        return new CourseCardInfo(
            card.CourseName,
            Math.Max(1, card.HoleNumber),
            Math.Max(1, card.Par),
            Math.Max(0, card.Yardage)
        );
    }
}

public readonly struct CourseCardInfo
{
    public CourseCardInfo(string courseName, int holeNumber, int par, int yardage)
    {
        CourseName = string.IsNullOrWhiteSpace(courseName) ? "Course" : courseName.Trim();
        HoleNumber = Math.Max(1, holeNumber);
        Par = Math.Max(1, par);
        Yardage = Math.Max(0, yardage);
    }

    public string CourseName { get; }
    public int HoleNumber { get; }
    public int Par { get; }
    public int Yardage { get; }
}
