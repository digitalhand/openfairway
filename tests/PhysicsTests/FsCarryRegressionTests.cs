using System.Collections.Generic;
using Godot.Collections;
using NUnit.Framework;

namespace OpenFairway.Tests
{
    [TestFixture]
    public class FsCarryRegressionTests
    {
        private const float CarryToleranceYd = 3.0f;
        private const float ApexToleranceFt = 4.0f;

        private PhysicsAdapter _adapter;

        [SetUp]
        public void Setup()
        {
            _adapter = new PhysicsAdapter();
        }

        public static IEnumerable<TestCaseData> CarryCases()
        {
            yield return new TestCaseData("Flopped", "flop_test_shot.json", 61.8f, 76.9f, true)
                .SetName("Flopped_CarryMatchesFs");
            yield return new TestCaseData("Drive", "drive_test_shot.json", 244.1f, 0.0f, false)
                .SetName("Drive_CarryMatchesFs");
            yield return new TestCaseData("Wood Low", "wood_low_test_shot.json", 122.2f, 0.0f, false)
                .SetName("WoodLow_CarryMatchesFs");
            yield return new TestCaseData("Approach Mid Iron", "approach_mid_iron_test_shot.json", 125.8f, 69.3f, true)
                .SetName("ApproachMidIron_CarryMatchesFs");
            yield return new TestCaseData("Wedge", "wedge_test_shot.json", 70.6f, 31.2f, true)
                .SetName("Wedge_CarryMatchesFs");
            yield return new TestCaseData("Checked", "checked_test_shot.json", 77.9f, 79.7f, true)
                .SetName("Checked_CarryMatchesFs");
            yield return new TestCaseData("Topped", "topped_test_shot.json", 56.3f, 0.0f, false)
                .SetName("Topped_CarryMatchesFs");
        }

        [TestCaseSource(nameof(CarryCases))]
        [Category("FsCarry")]
        public void CarryMatchesFs(
            string shotName,
            string filename,
            float targetCarryYd,
            float targetApexFt,
            bool assertApex)
        {
            Dictionary shot = TestShotLoader.LoadTestShot(filename);
            Dictionary result = _adapter.SimulateShotFromJson(shot);

            Assert.That(result.ContainsKey("carry_yd"), Is.True, "Result missing carry_yd");
            Assert.That(result.ContainsKey("apex_ft"), Is.True, "Result missing apex_ft");
            Assert.That(result.ContainsKey("landing_speed_mps"), Is.True, "Result missing landing_speed_mps");
            Assert.That(result.ContainsKey("landing_angle_deg"), Is.True, "Result missing landing_angle_deg");
            Assert.That(result.ContainsKey("first_impact_time_s"), Is.True, "Result missing first_impact_time_s");

            float carry = (float)result["carry_yd"];
            float apex = (float)result["apex_ft"];
            float landingSpeedMps = (float)result["landing_speed_mps"];
            float landingAngleDeg = (float)result["landing_angle_deg"];
            float firstImpactTimeS = (float)result["first_impact_time_s"];
            float carryDiffYd = carry - targetCarryYd;

            TestContext.WriteLine($"{shotName} diagnostics:");
            TestContext.WriteLine($"  Carry: {carry:F1} yd (target {targetCarryYd:F1})");
            TestContext.WriteLine($"  Carry diff: {carryDiffYd:+0.0;-0.0} yd");
            TestContext.WriteLine($"  Apex: {apex:F1} ft");
            TestContext.WriteLine($"  Flight time: {firstImpactTimeS:F2} s");
            TestContext.WriteLine($"  Landing speed: {landingSpeedMps:F2} m/s");
            TestContext.WriteLine($"  Landing angle: {landingAngleDeg:F1} deg");

            Assert.That(carry, Is.EqualTo(targetCarryYd).Within(CarryToleranceYd));

            if (assertApex)
            {
                Assert.That(apex, Is.EqualTo(targetApexFt).Within(ApexToleranceFt));
            }
        }
    }
}
