using Godot;
using Godot.Collections;
using NUnit.Framework;

namespace OpenFairway.Tests
{
    [TestFixture]
    public class GreenSpinBackRegressionTests
    {
        private PhysicsAdapter _adapter;

        [SetUp]
        public void Setup()
        {
            _adapter = new PhysicsAdapter();
        }

        [Test]
        [Category("PhysicsRuntime")]
        public void FlopShot_FlatGreen_MinimalRollout_WithImpactSpinback()
        {
            Dictionary shot = TestShotLoader.LoadTestShot("flop_test_shot.json");
            Dictionary result = _adapter.SimulateShotFromJson(shot, PhysicsEnums.SurfaceType.Green, Vector3.Up);

            Assert.That(result.ContainsKey("carry_yd"), Is.True, "Result missing carry_yd");
            Assert.That(result.ContainsKey("total_yd"), Is.True, "Result missing total_yd");
            Assert.That(result.ContainsKey("first_impact_spinback"), Is.True, "Result missing first-impact spinback diagnostic");
            Assert.That(result.ContainsKey("first_impact_tangent_out_mps"), Is.True, "Result missing first-impact tangent diagnostic");

            float carry = (float)result["carry_yd"];
            float total = (float)result["total_yd"];
            float rollout = total - carry;
            bool firstImpactSpinback = (bool)result["first_impact_spinback"];
            float tangentOut = (float)result["first_impact_tangent_out_mps"];

            TestContext.WriteLine("Flop shot on flat green:");
            TestContext.WriteLine($"  Carry: {carry:F1} yd");
            TestContext.WriteLine($"  Total: {total:F1} yd");
            TestContext.WriteLine($"  Rollout: {rollout:F2} yd");
            TestContext.WriteLine($"  First impact tangent out: {tangentOut:F3} m/s");
            TestContext.WriteLine($"  First impact spinback: {firstImpactSpinback}");

            Assert.That(rollout, Is.LessThanOrEqualTo(-0.5f), "High-spin flop on green should exhibit meaningful spinback rollout");
            Assert.That(firstImpactSpinback, Is.True, "Flat green flop should reverse tangential direction on first impact");
            Assert.That(tangentOut, Is.LessThan(0.0f), "Spinback should be reflected as negative tangential speed");
        }

        [Test]
        [Category("PhysicsRuntime")]
        public void FlopShot_GreenSlope_InfluencesPostImpactTravelDirectionally()
        {
            Dictionary shot = TestShotLoader.LoadTestShot("flop_test_shot.json");

            Dictionary flat = _adapter.SimulateShotFromJson(shot, PhysicsEnums.SurfaceType.Green, Vector3.Up);
            Vector3 downhillNormal = new Vector3(0.173648f, 0.984807f, 0.0f).Normalized();
            Vector3 uphillNormal = new Vector3(-0.173648f, 0.984807f, 0.0f).Normalized();
            Dictionary downhill = _adapter.SimulateShotFromJson(shot, PhysicsEnums.SurfaceType.Green, downhillNormal);
            Dictionary uphill = _adapter.SimulateShotFromJson(shot, PhysicsEnums.SurfaceType.Green, uphillNormal);

            float flatTotal = (float)flat["total_yd"];
            float downhillTotal = (float)downhill["total_yd"];
            float uphillTotal = (float)uphill["total_yd"];
            float flatCarry = (float)flat["carry_yd"];
            float downhillRollout = downhillTotal - (float)downhill["carry_yd"];
            float uphillRollout = uphillTotal - (float)uphill["carry_yd"];

            TestContext.WriteLine("Flop shot green slope comparison:");
            TestContext.WriteLine($"  Flat total: {flatTotal:F1} yd (carry {flatCarry:F1} yd)");
            TestContext.WriteLine($"  Downhill total: {downhillTotal:F1} yd, rollout: {downhillRollout:F2} yd");
            TestContext.WriteLine($"  Uphill total: {uphillTotal:F1} yd, rollout: {uphillRollout:F2} yd");

            Assert.That(downhillTotal, Is.GreaterThanOrEqualTo(flatTotal - 0.25f),
                "Downhill slope should not finish meaningfully shorter than flat");
            Assert.That(uphillTotal, Is.LessThanOrEqualTo(flatTotal + 0.25f),
                "Uphill slope should not finish meaningfully longer than flat");
            Assert.That(downhillRollout, Is.GreaterThanOrEqualTo(uphillRollout - 0.10f),
                "Downhill slope should provide at least as much post-impact travel as uphill");
        }

    }
}
