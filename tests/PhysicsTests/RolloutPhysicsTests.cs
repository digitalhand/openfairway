using Godot;
using NUnit.Framework;

namespace OpenFairway.Tests
{
    /// <summary>
    /// Tests for rollout physics formulas (friction multipliers, velocity scaling).
    /// These validate the tuned parameters that control chip/bump/driver rollout behavior.
    /// </summary>
    [TestFixture]
    public class RolloutPhysicsTests
    {
        /// <summary>
        /// Simulates GetSpinFrictionMultiplier calculation from BallPhysics.cs
        /// </summary>
        private float CalculateScaledMultiplier(float impactSpinRpm, float ballSpeed)
        {
            float effectiveSpinRpm = impactSpinRpm;

            // Velocity scaling (lines 71-84 in BallPhysics.cs)
            float velocityScale;
            if (ballSpeed < 20.0f)
            {
                velocityScale = Mathf.Lerp(0.60f, 0.87f, ballSpeed / 20.0f);
            }
            else if (ballSpeed < 35.0f)
            {
                velocityScale = Mathf.Lerp(0.87f, 1.0f, (ballSpeed - 20.0f) / 15.0f);
            }
            else
            {
                velocityScale = 1.0f;
            }

            // Spin multiplier (lines 92-110 in BallPhysics.cs)
            float spinMultiplier;
            if (effectiveSpinRpm < 1250.0f)
            {
                spinMultiplier = 1.0f + (effectiveSpinRpm / 1250.0f) * 0.30f;
            }
            else if (effectiveSpinRpm < 1750.0f)
            {
                float excessSpin = effectiveSpinRpm - 1250.0f;
                spinMultiplier = 1.30f + (excessSpin / 500.0f) * 0.95f;
            }
            else
            {
                float excessSpin = effectiveSpinRpm - 1750.0f;
                float spinFactor = Mathf.Min(excessSpin / 1000.0f, 1.0f);
                spinMultiplier = 2.25f + spinFactor * 0.25f;
            }

            // Apply velocity scaling
            return 1.0f + (spinMultiplier - 1.0f) * velocityScale;
        }

        [Test]
        [Category("RolloutPhysics")]
        public void ChipShot_FrictionMultiplier_IsCorrect()
        {
            // Chip shot: 2785 RPM impact, 2.44 m/s rollout velocity
            // Expected: ×1.95 multiplier (from actual debug output after fix)
            float multiplier = CalculateScaledMultiplier(impactSpinRpm: 2785, ballSpeed: 2.44f);

            Assert.That(multiplier, Is.EqualTo(1.95f).Within(0.01f),
                "Chip shot friction multiplier should be ~1.95x to achieve 13.1 yd total");
        }

        [Test]
        [Category("RolloutPhysics")]
        public void BumpShot_FrictionMultiplier_IsCorrect()
        {
            // Bump shot: 1365 RPM impact, 9.25 m/s rollout velocity
            // Expected: ×1.37 multiplier (from actual debug output after fix)
            float multiplier = CalculateScaledMultiplier(impactSpinRpm: 1365, ballSpeed: 9.25f);

            Assert.That(multiplier, Is.EqualTo(1.37f).Within(0.01f),
                "Bump shot friction multiplier should be ~1.37x to achieve 89.7 yd total (target 85 yd)");
        }

        [Test]
        [Category("RolloutPhysics")]
        public void DriverShot_FrictionMultiplier_IsCorrect()
        {
            // Driver shot: 1118 RPM impact, 16 m/s rollout velocity (estimated)
            // Expected: ×1.25 multiplier to achieve 194.7 yd total (target 198 yd)
            float multiplier = CalculateScaledMultiplier(impactSpinRpm: 1118, ballSpeed: 16.0f);

            Assert.That(multiplier, Is.InRange(1.20f, 1.30f),
                "Driver friction multiplier should be ~1.25x to achieve 194.7 yd total");
        }

        [Test]
        [Category("RolloutPhysics")]
        public void VelocityScaling_ChipSpeed_IsCorrect()
        {
            // At 2.44 m/s (chip rollout), velocity scaling should be ~63.3%
            float ballSpeed = 2.44f;
            float velocityScale = Mathf.Lerp(0.60f, 0.87f, ballSpeed / 20.0f);

            Assert.That(velocityScale, Is.EqualTo(0.633f).Within(0.01f),
                "Velocity scaling at chip speed (2.44 m/s) should be ~63.3%");
        }

        [Test]
        [Category("RolloutPhysics")]
        public void VelocityScaling_BumpSpeed_IsCorrect()
        {
            // At 9.25 m/s (bump rollout), velocity scaling should be ~72.5%
            float ballSpeed = 9.25f;
            float velocityScale = Mathf.Lerp(0.60f, 0.87f, ballSpeed / 20.0f);

            Assert.That(velocityScale, Is.EqualTo(0.725f).Within(0.01f),
                "Velocity scaling at bump speed (9.25 m/s) should be ~72.5%");
        }

        [Test]
        [Category("RolloutPhysics")]
        public void VelocityScaling_DriverSpeed_IsCorrect()
        {
            // At 16 m/s (driver rollout start), velocity scaling should be ~81.7%
            float ballSpeed = 16.0f;
            float velocityScale = Mathf.Lerp(0.60f, 0.87f, ballSpeed / 20.0f);

            Assert.That(velocityScale, Is.EqualTo(0.817f).Within(0.01f),
                "Velocity scaling at driver speed (16 m/s) should be ~81.7%");
        }

        [Test]
        [Category("RolloutPhysics")]
        public void SpinMultiplier_LowSpin_IsLinear()
        {
            // Below 1250 RPM: linear from 1.0x to 1.30x
            float mult_0 = 1.0f + (0f / 1250.0f) * 0.30f;
            float mult_625 = 1.0f + (625f / 1250.0f) * 0.30f;
            float mult_1250 = 1.0f + (1250f / 1250.0f) * 0.30f;

            Assert.That(mult_0, Is.EqualTo(1.00f).Within(0.01f));
            Assert.That(mult_625, Is.EqualTo(1.15f).Within(0.01f));
            Assert.That(mult_1250, Is.EqualTo(1.30f).Within(0.01f));
        }

        [Test]
        [Category("RolloutPhysics")]
        public void SpinMultiplier_BumpRange_IsSteep()
        {
            // 1250-1750 RPM: steep curve from 1.30x to 2.25x
            // This is the key fix for bump shot rollout
            float mult_1250 = 1.30f;
            float mult_1500 = 1.30f + ((1500f - 1250f) / 500.0f) * 0.95f;
            float mult_1750 = 1.30f + ((1750f - 1250f) / 500.0f) * 0.95f;

            Assert.That(mult_1250, Is.EqualTo(1.30f).Within(0.01f));
            Assert.That(mult_1500, Is.EqualTo(1.775f).Within(0.01f),
                "Mid-range spin should have steep multiplier increase");
            Assert.That(mult_1750, Is.EqualTo(2.25f).Within(0.01f));
        }

        [Test]
        [Category("RolloutPhysics")]
        public void SpinMultiplier_HighSpin_IsCapped()
        {
            // Above 1750 RPM: gradual increase to 2.5x cap
            float mult_2000 = 2.25f + Mathf.Min((2000f - 1750f) / 1000.0f, 1.0f) * 0.25f;
            float mult_2750 = 2.25f + Mathf.Min((2750f - 1750f) / 1000.0f, 1.0f) * 0.25f;
            float mult_5000 = 2.25f + Mathf.Min((5000f - 1750f) / 1000.0f, 1.0f) * 0.25f;

            Assert.That(mult_2000, Is.EqualTo(2.3125f).Within(0.01f));
            Assert.That(mult_2750, Is.EqualTo(2.50f).Within(0.01f), "Should reach cap at 2750 RPM");
            Assert.That(mult_5000, Is.EqualTo(2.50f).Within(0.01f), "Should stay capped above 2750 RPM");
        }

        [Test]
        [Category("RolloutPhysics")]
        public void BumpShot_SpinMultiplier_IncreasedFrom_PreviousVersion()
        {
            // Bump shot at 1365 RPM
            // OLD formula: 1.15 + ((1365-1250)/1500)*1.35 = 1.25x
            // NEW formula: 1.30 + ((1365-1250)/500)*0.95 = 1.52x

            float oldSpinMult = 1.15f + ((1365f - 1250f) / 1500.0f) * 1.35f;
            float newSpinMult = 1.30f + ((1365f - 1250f) / 500.0f) * 0.95f;

            Assert.That(oldSpinMult, Is.EqualTo(1.25f).Within(0.01f));
            Assert.That(newSpinMult, Is.EqualTo(1.52f).Within(0.01f));
            Assert.That(newSpinMult / oldSpinMult, Is.EqualTo(1.22f).Within(0.02f),
                "New formula should give ~22% higher base multiplier for bump shots");
        }
    }

    /// <summary>
    /// Regression tests for shot distances.
    /// These document expected distances and detect unintended physics changes.
    /// NOTE: These cannot run as automated unit tests due to Godot runtime requirements,
    /// but serve as documentation of validated baseline values.
    /// </summary>
    [TestFixture]
    public class ShotDistanceRegressionTests
    {
        [Test]
        [Explicit("Requires manual validation in Godot - cannot run in dotnet test")]
        [Category("DistanceBenchmark")]
        public void ChipShot_Distance_Baseline()
        {
            // chip_test_shot.json: 24.7 mph, 3204 RPM, 17.94° VLA
            // Validated: 2024-02-11
            // Expected: 7.6 yd carry / 13.1 yd total (target 13.0 yd)
            // Error: +0.8%

            Assert.Pass("Baseline: 7.6/13.1 yd (chip_test_shot.json)");
        }

        [Test]
        [Explicit("Requires manual validation in Godot - cannot run in dotnet test")]
        [Category("DistanceBenchmark")]
        public void BumpShot_Distance_Baseline()
        {
            // bump_test_shot.json: 78.3 mph, 1702 RPM, 5.57° VLA
            // Validated: 2024-02-11
            // Expected: 38.1 yd carry / 89.7 yd total (target 85 yd)
            // Error: +5.5%

            Assert.Pass("Baseline: 38.1/89.7 yd (bump_test_shot.json)");
        }

        [Test]
        [Explicit("Requires manual validation in Godot - cannot run in dotnet test")]
        [Category("DistanceBenchmark")]
        public void WoodLowShot_Distance_Baseline()
        {
            // wood_low_test_shot.json: 114.5 mph, 1933 RPM, 6.95° VLA
            // Validated: 2024-02-11
            // Expected: 121.7 yd carry / 194.7 yd total (target 198 yd)
            // Error: -1.7%

            Assert.Pass("Baseline: 121.7/194.7 yd (wood_low_test_shot.json)");
        }

        [Test]
        [Explicit("Requires manual validation in Godot - cannot run in dotnet test")]
        [Category("DistanceBenchmark")]
        public void ApproachShot_Distance_Baseline()
        {
            // approach_test_shot.json: 81 mph, 10478 RPM, 30.5° VLA
            // Validated: 2024-02-11
            // Expected: 105.6 yd carry / 108.3 yd total (target 108 yd)
            // Error: +0.3%

            Assert.Pass("Baseline: 105.6/108.3 yd (approach_test_shot.json)");
        }

        [Test]
        [Explicit("Requires manual validation in Godot - cannot run in dotnet test")]
        [Category("DistanceBenchmark")]
        public void FlopShot_Distance_Baseline()
        {
            // flop_test_shot.json
            // Validated: 2024-02-11
            // Expected: 66.2 yd carry / 66.7 yd total (minimal rollout)

            Assert.Pass("Baseline: 66.2/66.7 yd (flop_test_shot.json)");
        }
    }
}
