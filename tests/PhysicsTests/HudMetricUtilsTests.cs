using Godot;
using NUnit.Framework;

namespace OpenFairway.Tests
{
    [TestFixture]
    public class HudMetricUtilsTests
    {
        [Test]
        public void ElevationPresenter_Positive_SetsUpArrowAndSignedText()
        {
            ElevationVisual visual = ElevationPresenter.Build(7, includeSignInText: true);

            Assert.That(visual.Arrow, Is.EqualTo(ElevationPresenter.ArrowUp));
            Assert.That(visual.Text, Is.EqualTo("+7FT"));
            Assert.That(visual.Color, Is.EqualTo(ElevationPresenter.PositiveColor));
        }

        [Test]
        public void ElevationPresenter_Negative_UsesAbsoluteTextWhenUnsigned()
        {
            ElevationVisual visual = ElevationPresenter.Build(-6, includeSignInText: false);

            Assert.That(visual.Arrow, Is.EqualTo(ElevationPresenter.ArrowDown));
            Assert.That(visual.Text, Is.EqualTo("6FT"));
            Assert.That(visual.Color, Is.EqualTo(ElevationPresenter.NegativeColor));
        }

        [Test]
        public void ElevationPresenter_Zero_UsesNeutralArrowAndColor()
        {
            ElevationVisual visual = ElevationPresenter.Build(0, includeSignInText: true);

            Assert.That(visual.Arrow, Is.EqualTo(ElevationPresenter.ArrowFlat));
            Assert.That(visual.Text, Is.EqualTo("0FT"));
            Assert.That(visual.Color, Is.EqualTo(ElevationPresenter.NeutralColor));
        }

        [Test]
        public void MeasurementUtils_HorizontalDistanceYards_RoundsAsExpected()
        {
            int yards = MeasurementUtils.HorizontalDistanceYards(Vector3.Zero, new Vector3(10.0f, 0.0f, 0.0f));

            Assert.That(yards, Is.EqualTo(11));
        }

        [Test]
        public void MeasurementUtils_VerticalDeltaFeet_ComputesSignedDifference()
        {
            int feet = MeasurementUtils.VerticalDeltaFeet(new Vector3(0, 2, 0), new Vector3(0, 0, 0));

            Assert.That(feet, Is.EqualTo(-7));
        }
    }
}
