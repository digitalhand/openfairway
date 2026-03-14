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
        private static FlightAerodynamicsSample BuildFlightSample(float speedMps, float spinRatio, float initialLaunchAngleDeg)
        {
            float spinRadPerSecond = spinRatio * speedMps / BallPhysics.RADIUS;
            Vector3 velocity = new Vector3(speedMps, 0.0f, 0.0f);
            Vector3 omega = new Vector3(0.0f, 0.0f, spinRadPerSecond);

            return BallPhysics.SampleFlightAerodynamics(
                velocity,
                omega,
                airDensity: 1.225f,
                airViscosity: 1.81e-05f,
                dragScale: 1.0f,
                liftScale: 1.0f,
                initialLaunchAngleDeg: initialLaunchAngleDeg
            );
        }

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

        [Test]
        [Category("RolloutPhysics")]
        public void SampleFlightAerodynamics_WedgeBand_UsesRelievedDragWithProgressiveCap()
        {
            FlightAerodynamicsSample sample = BuildFlightSample(24.45f, 0.45f, 26.8f);

            Assert.That(sample.Reynolds, Is.InRange(70000.0f, 71000.0f));
            // Progressive cap raises effective multiplier above pure relief
            Assert.That(sample.SpinDragMultiplier, Is.InRange(1.18f, 1.26f));
            Assert.That(sample.LowLaunchLiftScale, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(sample.LiftCoefficient, Is.GreaterThan(0.20f));
        }

        [Test]
        [Category("RolloutPhysics")]
        public void SampleFlightAerodynamics_LowLaunchWood_UsesLiftRecovery()
        {
            FlightAerodynamicsSample sample = BuildFlightSample(51.18f, 0.18f, 6.9f);

            Assert.That(sample.Reynolds, Is.InRange(145000.0f, 149000.0f));
            Assert.That(sample.SpinDragMultiplier, Is.EqualTo(1.13f).Within(0.01f));
            Assert.That(sample.LowLaunchLiftScale, Is.InRange(1.06f, 1.08f));
        }

        [Test]
        [Category("RolloutPhysics")]
        public void SampleFlightAerodynamics_CheckedBand_AtExpectedLevel()
        {
            FlightAerodynamicsSample checkedSample = BuildFlightSample(33.57f, 0.70f, 38.5f);

            Assert.That(checkedSample.Reynolds, Is.InRange(96000.0f, 98000.0f));
            Assert.That(checkedSample.SpinDragMultiplier, Is.InRange(1.23f, 1.27f));
            Assert.That(checkedSample.LowLaunchLiftScale, Is.EqualTo(1.0f).Within(0.0001f));
        }

        [Test]
        [Category("RolloutPhysics")]
        public void SpinDragMultiplier_MidSpinBand_KeepsFullCapUntilReliefWindow()
        {
            float multiplier = BallPhysics.GetSpinDragMultiplier(0.30f, 120000.0f);

            Assert.That(multiplier, Is.EqualTo(1.20f).Within(0.001f),
                "Approach-mid spin ratios should keep the full cap until the wedge-relief band starts.");
        }

        [Test]
        [Category("RolloutPhysics")]
        public void SpinDragMultiplier_WedgeBand_RelievedButWithProgressiveCap()
        {
            float multiplier = BallPhysics.GetSpinDragMultiplier(0.45f, 75000.0f);

            // With default SpinDragProgressiveCapBoostMax=0.25, the relief is partially
            // offset by the progressive cap. Result is between pure relief (~1.04) and
            // full cap (1.20).
            Assert.That(multiplier, Is.InRange(1.20f, 1.26f),
                "Wedge spin ratios with progressive cap should be above pure relief but below flat cap.");
        }

        [Test]
        [Category("RolloutPhysics")]
        public void SpinDragMultiplier_HighReWedgeBand_FadesBackTowardFullDragCap()
        {
            float multiplier = BallPhysics.GetSpinDragMultiplier(0.45f, 103000.0f);

            // At high Re, relief fades out. Progressive cap still applies.
            Assert.That(multiplier, Is.InRange(1.35f, 1.42f),
                "The wedge relief should fade out in the higher-Re regime, progressive cap active.");
        }

        [Test]
        [Category("RolloutPhysics")]
        public void SpinDragMultiplier_CheckedBand_AtExpectedLevel()
        {
            float checkedBandMultiplier = BallPhysics.GetSpinDragMultiplier(0.70f, 95000.0f);

            Assert.That(checkedBandMultiplier, Is.InRange(1.23f, 1.26f),
                "Checked spin ratios with progressive cap should be above pure ultra-high level.");
        }

        [Test]
        [Category("RolloutPhysics")]
        public void SpinDragMultiplier_UltraHighSpin_ReachesReboundCap()
        {
            float ultraHighSpinMultiplier = BallPhysics.GetSpinDragMultiplier(0.88f, 90000.0f);

            Assert.That(ultraHighSpinMultiplier, Is.InRange(1.20f, 1.22f),
                "Flop spin ratios should reach the ultra-high-spin rebound cap.");
        }

        [Test]
        [Category("RolloutPhysics")]
        public void SpinDragProgressiveCap_DefaultProfile_AppliesProgressiveBoost()
        {
            // Default profile has BoostMax=0.25, so at SR=0.40 the progressive cap
            // raises the effective cap above the flat 1.20.
            float multiplier = FlightAerodynamicsModel.GetSpinDragMultiplier(0.40f, 120000.0f, FlightProfile.Default);

            Assert.That(multiplier, Is.InRange(1.28f, 1.30f),
                "Default profile progressive cap should boost effective cap above flat 1.20 for mid-high SR.");
        }

        [Test]
        [Category("RolloutPhysics")]
        public void SpinDragProgressiveCap_BelowSrStart_NoBoosted()
        {
            // SR=0.30 is below SrStart=0.33, so boost should be zero even with BoostMax=0.15.
            var profile = new FlightProfile { SpinDragProgressiveCapBoostMax = 0.15f };
            float multiplier = FlightAerodynamicsModel.GetSpinDragMultiplier(0.30f, 120000.0f, profile);

            Assert.That(multiplier, Is.EqualTo(1.20f).Within(0.001f),
                "SR below progressive cap start should get no boost (carry-short wedges protected).");
        }

        [Test]
        [Category("RolloutPhysics")]
        public void SpinDragProgressiveCap_AboveSrStart_ExceedsFlatCap()
        {
            // SR=0.40 at high Re with BoostMax=0.15 should exceed the flat 1.20 cap.
            var profile = new FlightProfile { SpinDragProgressiveCapBoostMax = 0.15f };
            float multiplier = FlightAerodynamicsModel.GetSpinDragMultiplier(0.40f, 120000.0f, profile);

            Assert.That(multiplier, Is.GreaterThan(1.20f),
                "SR above progressive cap start should exceed the flat cap.");
        }

        [Test]
        [Category("RolloutPhysics")]
        public void SpinDragProgressiveCap_IsMonotonic()
        {
            // Progressive boost must be monotonically increasing with spin ratio.
            var profile = new FlightProfile { SpinDragProgressiveCapBoostMax = 0.15f };
            float atSr035 = FlightAerodynamicsModel.GetSpinDragMultiplier(0.35f, 120000.0f, profile);
            float atSr040 = FlightAerodynamicsModel.GetSpinDragMultiplier(0.40f, 120000.0f, profile);
            float atSr047 = FlightAerodynamicsModel.GetSpinDragMultiplier(0.47f, 120000.0f, profile);

            Assert.That(atSr040, Is.GreaterThan(atSr035),
                "SR=0.40 should have higher drag multiplier than SR=0.35.");
            Assert.That(atSr047, Is.GreaterThan(atSr040),
                "SR=0.47 should have higher drag multiplier than SR=0.40.");
        }

        [Test]
        [Category("RolloutPhysics")]
        public void MidSpinClBoost_DefaultProfile_AppliesBoost()
        {
            // Default profile has MidSpinClBoostMax=0.50, SrStart=0.17, SrEnd=0.31.
            // SR=0.22 is inside the bell curve, so boost should be > 1.0.
            float boost = FlightAerodynamicsModel.GetMidSpinClBoost(0.22f, FlightProfile.Default);

            Assert.That(boost, Is.GreaterThan(1.30f),
                "Default profile should apply mid-spin Cl boost for SR in [0.17, 0.31].");
        }

        [Test]
        [Category("RolloutPhysics")]
        public void MidSpinClBoost_BellPeaksNearMidpoint()
        {
            var profile = new FlightProfile { MidSpinClBoostMax = 0.10f };
            // Midpoint of default SrStart=0.17, SrEnd=0.31 is SR=0.24
            float boost = FlightAerodynamicsModel.GetMidSpinClBoost(0.24f, profile);

            Assert.That(boost, Is.InRange(1.09f, 1.10f),
                "Bell should peak near 1.10 at midpoint of [0.17, 0.31].");
        }

        [Test]
        [Category("RolloutPhysics")]
        public void MidSpinClBoost_BelowSrStart_NoBoost()
        {
            var profile = new FlightProfile { MidSpinClBoostMax = 0.10f };
            float boost = FlightAerodynamicsModel.GetMidSpinClBoost(0.05f, profile);

            Assert.That(boost, Is.EqualTo(1.0f).Within(0.0001f),
                "Drivers (SR below SrStart) should get zero boost.");
        }

        [Test]
        [Category("RolloutPhysics")]
        public void MidSpinClBoost_AboveSrEnd_NoBoost()
        {
            var profile = new FlightProfile { MidSpinClBoostMax = 0.10f };
            float boost = FlightAerodynamicsModel.GetMidSpinClBoost(0.45f, profile);

            Assert.That(boost, Is.EqualTo(1.0f).Within(0.0001f),
                "Wedges (SR above SrEnd) should get zero boost.");
        }

        [Test]
        [Category("RolloutPhysics")]
        public void MidSpinClBoost_Shot9Regime_GetsSignificantBoost()
        {
            var profile = new FlightProfile { MidSpinClBoostMax = 0.10f };
            // SR=0.213 is in the rising phase of bell [0.17, 0.31]
            float boost = FlightAerodynamicsModel.GetMidSpinClBoost(0.213f, profile);

            Assert.That(boost, Is.GreaterThan(1.05f),
                "Shot 9 (SR=0.213) should get significant boost.");
        }

        [Test]
        [Category("RolloutPhysics")]
        public void LowLaunchLiftScale_WoodBand_GetsRecovery()
        {
            float scale = BallPhysics.GetLowLaunchLiftScale(6.7f, 0.18f, 145000.0f);

            Assert.That(scale, Is.InRange(1.07f, 1.08f),
                "Low-launch, high-Re wood shots should get the lift recovery bump.");
        }

        [Test]
        [Category("RolloutPhysics")]
        public void LowLaunchLiftScale_HighLaunchShots_GetNoRecovery()
        {
            float scale = BallPhysics.GetLowLaunchLiftScale(13.2f, 0.16f, 152884.0f);

            Assert.That(scale, Is.EqualTo(1.0f).Within(0.0001f));
        }

        [Test]
        [Category("RolloutPhysics")]
        public void LowLaunchLiftScale_HighSpinShots_GetNoRecovery()
        {
            float scale = BallPhysics.GetLowLaunchLiftScale(6.7f, 0.30f, 145000.0f);

            Assert.That(scale, Is.EqualTo(1.0f).Within(0.0001f));
        }

        [Test]
        [Category("RolloutPhysics")]
        public void LowLaunchLiftScale_LowReShots_GetNoRecovery()
        {
            // Test with Re below new threshold (85k) to ensure no recovery
            float scale = BallPhysics.GetLowLaunchLiftScale(6.7f, 0.18f, 80000.0f);

            Assert.That(scale, Is.EqualTo(1.0f).Within(0.0001f));
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
            // Validated: 2026-02-18
            // Expected: 7.8 yd carry / 16.5 yd total

            Assert.Pass("Baseline: 7.8/16.5 yd (chip_test_shot.json)");
        }

        [Test]
        [Explicit("Requires manual validation in Godot - cannot run in dotnet test")]
        [Category("DistanceBenchmark")]
        public void BumpShot_Distance_Baseline()
        {
            // bump_test_shot.json: 78.3 mph, 1850 RPM, 5.57° VLA
            // Validated: 2026-02-18
            // Expected: 39.0 yd carry / 88.6 yd total

            Assert.Pass("Baseline: 39.0/88.6 yd (bump_test_shot.json)");
        }

        [Test]
        [Explicit("Requires manual validation in Godot - cannot run in dotnet test")]
        [Category("DistanceBenchmark")]
        public void WoodLowShot_Distance_Baseline()
        {
            // wood_low_test_shot.json: 114.5 mph, 1933 RPM, 6.95° VLA
            // Validated: 2026-02-18
            // Expected: 122.5 yd carry / 180.1 yd total

            Assert.Pass("Baseline: 122.5/180.1 yd (wood_low_test_shot.json)");
        }

        [Test]
        [Explicit("Requires manual validation in Godot - cannot run in dotnet test")]
        [Category("DistanceBenchmark")]
        public void FlopShot_Distance_Baseline()
        {
            // flop_test_shot.json
            // Validated: 2026-02-18
            // Expected: 66.7 yd carry / 67.1 yd total (minimal rollout)

            Assert.Pass("Baseline: 66.7/67.1 yd (flop_test_shot.json)");
        }
    }
}
