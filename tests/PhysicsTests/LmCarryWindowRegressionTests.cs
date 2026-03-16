using System.Collections.Generic;
using Godot;
using Godot.Collections;
using NUnit.Framework;

namespace OpenFairway.Tests
{
    [TestFixture]
    public class LmCarryWindowRegressionTests
    {
        private const float WindowToleranceYd = 2.0f;
        private const float TightWedgeToleranceYd = 1.0f;
        private const float NonWedgeDriftToleranceYd = 2.0f;

        private PhysicsAdapter _adapter;

        [SetUp]
        public void Setup()
        {
            _adapter = new PhysicsAdapter();
        }

        public static IEnumerable<TestCaseData> CarryWindowCases()
        {
            yield return CreateCase("Driver 1", "driver1.json", 158.0f, 168.0f);
            yield return CreateCase("Driver 2", "driver2.json", 186.2f, 186.7f);
            yield return CreateCase("Driver 3", "driver3.json", 172.8f, 174.1f);
            yield return CreateCase("Driver 4", "driver4.json", 174.9f, 175.6f);
            yield return CreateCase("5 Iron", "5iron.json", 138.1f, 139.0f);
            yield return CreateCase("Wood 1", "wood1.json", 165.5f, 170.1f);
            yield return CreateCase("Wood 2", "wood2.json", 175.6f, 179.1f);
            yield return CreateCase("Wedge 1", "wedge_test_shot.json", 70.6f, 72.0f);
            yield return CreateCase("Wedge 2", "wedge_test_shot2.json", 52.2f, 52.4f);
            yield return CreateCase("Wood Low", "wood_low_test_shot.json", 122.2f, 122.2f);
            yield return CreateCase("Approach Mid", "approach_mid_iron_test_shot.json", 125.8f, 125.8f);
            yield return CreateCase("Checked", "checked_test_shot.json", 77.9f, 77.9f);
            yield return CreateCase("Flop", "flop_test_shot.json", 61.8f, 61.8f);
            yield return CreateCase("P Wedge 1", "p_wedge_shot_1.json", 104.6f, 105.1f);
            yield return CreateCase("Wedge Shot 1", "wedge_shot_1.json", 42.7f, 42.9f);
            yield return CreateCase("Wedge Shot 2", "wedge_shot_2.json", 49.0f, 49.7f);
        }

        [TestCaseSource(nameof(CarryWindowCases))]
        [Category("PhysicsRuntime")]
        [Category("LmCarryWindow")]
        public void CarryFallsInsideComparisonWindow(
            string shotName,
            string filename,
            float comparisonCarryA,
            float comparisonCarryB)
        {
            Dictionary shot = TestShotLoader.LoadTestShot(filename);
            Dictionary result = _adapter.SimulateShotFromJson(shot);

            Assert.That(result.ContainsKey("carry_yd"), Is.True, "Result missing carry_yd");
            Assert.That(result.ContainsKey("initial_spin_ratio"), Is.True, "Result missing initial_spin_ratio");
            Assert.That(result.ContainsKey("initial_re"), Is.True, "Result missing initial_re");
            Assert.That(result.ContainsKey("initial_launch_angle_deg"), Is.True, "Result missing initial_launch_angle_deg");
            Assert.That(result.ContainsKey("initial_low_launch_lift_scale"), Is.True, "Result missing initial_low_launch_lift_scale");
            Assert.That(result.ContainsKey("initial_spin_drag_multiplier"), Is.True, "Result missing initial_spin_drag_multiplier");
            Assert.That(result.ContainsKey("initial_backspin_rpm"), Is.True, "Result missing initial_backspin_rpm");
            Assert.That(result.ContainsKey("initial_sidespin_rpm"), Is.True, "Result missing initial_sidespin_rpm");
            Assert.That(result.ContainsKey("initial_cd"), Is.True, "Result missing initial_cd");
            Assert.That(result.ContainsKey("initial_cl"), Is.True, "Result missing initial_cl");
            Assert.That(result.ContainsKey("peak_cl"), Is.True, "Result missing peak_cl");

            float carry = (float)result["carry_yd"];
            float windowMin = Mathf.Min(comparisonCarryA, comparisonCarryB) - WindowToleranceYd;
            float windowMax = Mathf.Max(comparisonCarryA, comparisonCarryB) + WindowToleranceYd;

            TestContext.WriteLine($"{shotName} diagnostics:");
            TestContext.WriteLine($"  Carry: {carry:F1} yd (window {windowMin:F1}-{windowMax:F1})");
            TestContext.WriteLine($"  Initial Launch Angle: {(float)result["initial_launch_angle_deg"]:F1} deg");
            TestContext.WriteLine($"  Initial Spin Ratio: {(float)result["initial_spin_ratio"]:F3}");
            TestContext.WriteLine($"  Initial Re: {(float)result["initial_re"]:F0}");
            TestContext.WriteLine($"  Low-Launch Lift Scale: {(float)result["initial_low_launch_lift_scale"]:F3}");
            TestContext.WriteLine($"  Initial Spin-Drag Multiplier: {(float)result["initial_spin_drag_multiplier"]:F3}");
            TestContext.WriteLine($"  BackSpin: {(float)result["initial_backspin_rpm"]:F0} rpm");
            TestContext.WriteLine($"  SideSpin: {(float)result["initial_sidespin_rpm"]:F0} rpm");
            TestContext.WriteLine($"  Initial Cd: {(float)result["initial_cd"]:F3}");
            TestContext.WriteLine($"  Initial Cl: {(float)result["initial_cl"]:F3}");
            TestContext.WriteLine($"  Peak Cl: {(float)result["peak_cl"]:F3}");

            Assert.That(carry, Is.InRange(windowMin, windowMax));
        }

        public static IEnumerable<TestCaseData> TightWedgeCarryCases()
        {
            yield return new TestCaseData("P Wedge 1", "p_wedge_shot_1.json", 104.6f)
                .SetName("PWedge1_CarryMatchesFsWithinOneYard");
            yield return new TestCaseData("Wedge Shot 1", "wedge_shot_1.json", 42.7f)
                .SetName("WedgeShot1_CarryMatchesFsWithinOneYard");
            yield return new TestCaseData("Wedge Shot 2", "wedge_shot_2.json", 49.0f)
                .SetName("WedgeShot2_CarryMatchesFsWithinOneYard");
        }

        [TestCaseSource(nameof(TightWedgeCarryCases))]
        [Category("PhysicsRuntime")]
        [Category("WedgeTightening")]
        public void TightWedgeCarryMatchesFs(
            string shotName,
            string filename,
            float targetCarryYd)
        {
            Dictionary shot = TestShotLoader.LoadTestShot(filename);
            Dictionary result = _adapter.SimulateShotFromJson(shot);

            Assert.That(result.ContainsKey("carry_yd"), Is.True, "Result missing carry_yd");
            float carry = (float)result["carry_yd"];

            TestContext.WriteLine($"{shotName} strict wedge diagnostics:");
            TestContext.WriteLine($"  Carry: {carry:F1} yd (target {targetCarryYd:F1} ±{TightWedgeToleranceYd:F1})");

            Assert.That(carry, Is.EqualTo(targetCarryYd).Within(TightWedgeToleranceYd));
        }

        public static IEnumerable<TestCaseData> NonWedgeGuardrailCases()
        {
            yield return CreateGuardrailCase("Drive", "drive_test_shot.json", 244.1f);
            yield return CreateGuardrailCase("Wood Low", "wood_low_test_shot.json", 122.2f);
            yield return CreateGuardrailCase("Approach Mid", "approach_mid_iron_test_shot.json", 125.8f);
            yield return CreateGuardrailCase("Checked", "checked_test_shot.json", 77.9f);
            yield return CreateGuardrailCase("Flop", "flop_test_shot.json", 61.8f);
        }

        [TestCaseSource(nameof(NonWedgeGuardrailCases))]
        [Category("PhysicsRuntime")]
        [Category("CarryStability")]
        public void NonWedgeCarryRemainsInsideDriftBudget(
            string shotName,
            string filename,
            float baselineCarryYd)
        {
            Dictionary shot = TestShotLoader.LoadTestShot(filename);
            Dictionary result = _adapter.SimulateShotFromJson(shot);

            Assert.That(result.ContainsKey("carry_yd"), Is.True, "Result missing carry_yd");
            float carry = (float)result["carry_yd"];
            float diff = carry - baselineCarryYd;

            TestContext.WriteLine($"{shotName} guardrail diagnostics:");
            TestContext.WriteLine($"  Carry: {carry:F1} yd (baseline {baselineCarryYd:F1}, diff {diff:+0.0;-0.0} yd)");

            Assert.That(carry, Is.EqualTo(baselineCarryYd).Within(NonWedgeDriftToleranceYd));
        }

        private static TestCaseData CreateCase(string shotName, string filename, float comparisonCarryA, float comparisonCarryB)
        {
            return new TestCaseData(shotName, filename, comparisonCarryA, comparisonCarryB)
                .SetName($"{shotName.Replace(" ", string.Empty)}_CarryWithinComparisonWindow");
        }

        private static TestCaseData CreateGuardrailCase(string shotName, string filename, float baselineCarryYd)
        {
            return new TestCaseData(shotName, filename, baselineCarryYd)
                .SetName($"{shotName.Replace(" ", string.Empty)}_CarryWithinTwoYardDriftBudget");
        }
    }
}
