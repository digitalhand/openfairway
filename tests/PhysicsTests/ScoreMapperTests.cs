using NUnit.Framework;

namespace OpenFairway.Tests
{
    [TestFixture]
    public class ScoreMapperTests
    {
        [TestCase(1, 3, "Hole in One", -2)]
        [TestCase(2, 3, "Birdie", -1)]
        [TestCase(3, 3, "Par", 0)]
        [TestCase(4, 3, "Bogey", 1)]
        [TestCase(5, 3, "Double Bogey", 2)]
        [TestCase(6, 3, "Triple Bogey", 3)]
        [TestCase(7, 3, "+4", 4)]
        [TestCase(8, 3, "+5", 5)]
        public void MapScore_Par3_ReturnsExpectedLabel(int strokes, int par, string expectedLabel, int expectedRelative)
        {
            ScoreResult result = ScoreMapper.MapScore(strokes, par);

            Assert.That(result.Label, Is.EqualTo(expectedLabel));
            Assert.That(result.RelativeToPar, Is.EqualTo(expectedRelative));
            Assert.That(result.Strokes, Is.EqualTo(strokes));
            Assert.That(result.Par, Is.EqualTo(par));
        }

        [TestCase(2, 4, "Eagle", -2)]
        [TestCase(1, 5, "Hole in One", -4)]
        [TestCase(1, 4, "Hole in One", -3)]
        [TestCase(1, 6, "Hole in One", -5)]
        [TestCase(3, 6, "Albatross", -3)]
        [TestCase(4, 8, "-4", -4)]
        public void MapScore_UnderParScoring_ReturnsStandardOrFallbackLabels(int strokes, int par, string expectedLabel, int expectedRelative)
        {
            ScoreResult result = ScoreMapper.MapScore(strokes, par);

            Assert.That(result.Label, Is.EqualTo(expectedLabel));
            Assert.That(result.RelativeToPar, Is.EqualTo(expectedRelative));
        }

        [Test]
        public void CourseCatalog_RangeScene_UsesPar3()
        {
            bool found = CourseCatalog.TryGetPar("res://courses/Range/range.tscn", out int par);

            Assert.That(found, Is.True);
            Assert.That(par, Is.EqualTo(3));
        }

        [Test]
        public void CourseCatalog_RangeScene_UsesExpectedCardMetadata()
        {
            bool found = CourseCatalog.TryGetCourseCard("res://courses/Range/range.tscn", out CourseCardInfo courseCard);

            Assert.That(found, Is.True);
            Assert.That(courseCard.CourseName, Is.EqualTo("Airways"));
            Assert.That(courseCard.HoleNumber, Is.EqualTo(1));
            Assert.That(courseCard.Par, Is.EqualTo(3));
            Assert.That(courseCard.Yardage, Is.EqualTo(150));
        }
    }
}
