using Godot.Collections;
using NUnit.Framework;

namespace OpenFairway.Tests
{
    [TestFixture]
    public class WedgeRegressionTests
    {
        private const float TargetCarryYd = 96.0f;
        private const float CarryToleranceYd = 1.5f;

        private PhysicsAdapter _adapter;

        [SetUp]
        public void Setup()
        {
            _adapter = new PhysicsAdapter();
        }

        [Test]
        [Category("RolloutPhysics")]
        public void WedgeShot_Regression_CarryMatchesTarget()
        {
            Dictionary shot = TestShotLoader.LoadTestShot("wedge_test_shot.json");
            Dictionary result = _adapter.SimulateShotFromJson(shot);

            Assert.That(result.ContainsKey("carry_yd"), Is.True, "Result missing carry_yd");
            Assert.That(result.ContainsKey("initial_spin_ratio"), Is.True, "Result missing initial_spin_ratio diagnostic");

            float carry = (float)result["carry_yd"];
            float spinRatio = (float)result["initial_spin_ratio"];

            TestContext.WriteLine("Wedge Shot Diagnostics:");
            TestContext.WriteLine($"  Carry: {carry:F1} yd (target {TargetCarryYd:F1})");
            TestContext.WriteLine($"  Initial Spin Ratio: {spinRatio:F3}");
            TestContext.WriteLine($"  Shared DT: {BallPhysics.SIMULATION_DT:F6} s ({BallPhysics.SIMULATION_HZ:F0} Hz)");

            Assert.That(carry, Is.EqualTo(TargetCarryYd).Within(CarryToleranceYd));
        }

        [Test]
        [Category("RolloutPhysics")]
        public void SharedIntegrator_UsesExpectedDefaultRate()
        {
            Assert.That(BallPhysics.SIMULATION_HZ, Is.EqualTo(120.0f).Within(0.001f));
            Assert.That(BallPhysics.SIMULATION_DT, Is.EqualTo(1.0f / 120.0f).Within(0.000001f));
        }

    }
}
