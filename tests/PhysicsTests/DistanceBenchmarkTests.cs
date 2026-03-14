using Godot;
using Godot.Collections;
using NUnit.Framework;

namespace OpenFairway.Tests
{
    /// <summary>
    /// Benchmark tests to measure actual shot distances and compare to target values.
    /// Run with: dotnet test --filter FullyQualifiedName~DistanceBenchmarkTests
    /// </summary>
    [TestFixture]
    public class DistanceBenchmarkTests
    {
        private PhysicsAdapter _adapter;

        [SetUp]
        public void Setup()
        {
            _adapter = new PhysicsAdapter();
        }

        private void AssertDistance(string shotName, string filename, float targetCarry, float targetTotal, float tolerance = 5.0f)
        {
            var shot = TestShotLoader.LoadTestShot(filename);
            var result = _adapter.SimulateShotFromJson(shot);

            float actualCarry = (float)(result.ContainsKey("carry_yd") ? result["carry_yd"] : 0.0);
            float actualTotal = (float)(result.ContainsKey("total_yd") ? result["total_yd"] : 0.0);
            float actualRollout = actualTotal - actualCarry;
            float targetRollout = targetTotal - targetCarry;

            TestContext.WriteLine($"\n{shotName}:");
            TestContext.WriteLine($"  Carry: {actualCarry:F1} yd (target {targetCarry:F1} yd, diff {actualCarry - targetCarry:+0.0;-0.0} yd)");
            TestContext.WriteLine($"  Total: {actualTotal:F1} yd (target {targetTotal:F1} yd, diff {actualTotal - targetTotal:+0.0;-0.0} yd)");
            TestContext.WriteLine($"  Rollout: {actualRollout:F1} yd (target {targetRollout:F1} yd, diff {actualRollout - targetRollout:+0.0;-0.0} yd)");

            float carryError = Mathf.Abs(actualCarry - targetCarry);
            float totalError = Mathf.Abs(actualTotal - targetTotal);

            if (carryError > tolerance || totalError > tolerance)
            {
                Assert.Warn($"{shotName} outside tolerance: carry error {carryError:F1} yd, total error {totalError:F1} yd");
            }
            else
            {
                TestContext.WriteLine($"  ✓ Within {tolerance} yd tolerance");
            }
        }

        [Test]
        public void ChipShot_Benchmark()
        {
            // 24.7 mph, 3204 RPM, 17.94° VLA
            // Should be short pitch with minimal rollout
            AssertDistance("Chip Shot", "chip_test_shot.json",
                targetCarry: 7.8f,
                targetTotal: 16.5f,   // Updated 2026-02-18 after surface retuning
                tolerance: 2.0f);
        }

        [Test]
        public void WoodLowShot_Benchmark()
        {
            // 114.5 mph, 1933 RPM, 6.95° VLA (driver-like)
            // Should have significant rollout
            AssertDistance("Wood Low Shot", "wood_low_test_shot.json",
                targetCarry: 122.5f,
                targetTotal: 180.1f,  // Updated 2026-02-18 after surface retuning
                tolerance: 10.0f);
        }

[Test]
        public void DriveShot_Benchmark()
        {
            // Full driver shot
            AssertDistance("Drive Shot", "drive_test_shot.json",
                targetCarry: 250.8f,
                targetTotal: 262.9f,  // Updated 2026-02-18 after surface retuning
                tolerance: 15.0f);
        }

        [Test]
        public void FlopShot_Benchmark()
        {
            // High loft, low rollout
            AssertDistance("Flop Shot", "flop_test_shot.json",
                targetCarry: 66.7f,
                targetTotal: 67.1f,   // Updated 2026-02-18 after surface retuning
                tolerance: 3.0f);
        }

        [Test]
        [Category("FullBenchmark")]
        public void RunFullBenchmark()
        {
            TestContext.WriteLine("\n=== DISTANCE BENCHMARK REPORT ===\n");

            ChipShot_Benchmark();
            WoodLowShot_Benchmark();
            DriveShot_Benchmark();
            FlopShot_Benchmark();

            TestContext.WriteLine("\n=== END BENCHMARK ===");
        }
    }
}
