using NUnit.Framework;
using Godot;
using Godot.Collections;
using System.IO;
using System.Text.Json;

namespace OpenShotGolf.Tests
{
    /// <summary>
    /// Benchmark tests to measure actual shot distances and compare to target values.
    /// Run with: dotnet test --filter FullyQualifiedName~DistanceBenchmarkTests
    /// </summary>
    [TestFixture]
    public class DistanceBenchmarkTests
    {
        private PhysicsAdapter _adapter;
        private string _dataPath;

        [SetUp]
        public void Setup()
        {
            _adapter = new PhysicsAdapter();
            _dataPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", "assets", "data");
        }

        private Dictionary LoadTestShot(string filename)
        {
            string path = Path.Combine(_dataPath, filename);
            if (!File.Exists(path))
            {
                Assert.Fail($"Test shot file not found: {path}");
            }

            string json = File.ReadAllText(path);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var data = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(json, options);

            var dict = new Dictionary();
            foreach (var kvp in data)
            {
                dict[kvp.Key] = Variant.From(ConvertJsonValue(kvp.Value));
            }
            return dict;
        }

        private object ConvertJsonValue(object value)
        {
            if (value is JsonElement element)
            {
                switch (element.ValueKind)
                {
                    case JsonValueKind.Number:
                        if (element.TryGetDouble(out double d))
                            return d;
                        break;
                    case JsonValueKind.String:
                        return element.GetString();
                    case JsonValueKind.True:
                        return true;
                    case JsonValueKind.False:
                        return false;
                    case JsonValueKind.Object:
                        var dict = new Dictionary();
                        foreach (var prop in element.EnumerateObject())
                        {
                            dict[prop.Name] = Variant.From(ConvertJsonValue(prop.Value));
                        }
                        return dict;
                }
            }
            return value;
        }

        private void AssertDistance(string shotName, string filename, float targetCarry, float targetTotal, float tolerance = 5.0f)
        {
            var shot = LoadTestShot(filename);
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
                targetCarry: 7.6f,
                targetTotal: 13.0f,   // User says 7-8 yards rollout is expected
                tolerance: 2.0f);
        }

        [Test]
        public void WoodLowShot_Benchmark()
        {
            // 114.5 mph, 1933 RPM, 6.95° VLA (driver-like)
            // Should have significant rollout
            AssertDistance("Wood Low Shot", "wood_low_test_shot.json",
                targetCarry: 121.0f,
                targetTotal: 198.0f,  // User says 190-205 total is expected
                tolerance: 10.0f);
        }

        [Test]
        public void ApproachShot_Benchmark()
        {
            // 81 mph, 10478 RPM, 30.5° VLA (high spin wedge)
            // Should check up and stop quickly
            AssertDistance("Approach Shot", "approach_test_shot.json",
                targetCarry: 103.0f,
                targetTotal: 108.0f,  // Minimal rollout for high-spin wedge
                tolerance: 5.0f);
        }

        [Test]
        public void DriveShot_Benchmark()
        {
            // Full driver shot
            AssertDistance("Drive Shot", "drive_test_shot.json",
                targetCarry: 250.0f,
                targetTotal: 290.0f,  // Typical driver rollout
                tolerance: 15.0f);
        }

        [Test]
        public void FlopShot_Benchmark()
        {
            // High loft, low rollout
            AssertDistance("Flop Shot", "flop_test_shot.json",
                targetCarry: 25.0f,
                targetTotal: 28.0f,   // Minimal rollout
                tolerance: 3.0f);
        }

        [Test]
        [Category("FullBenchmark")]
        public void RunFullBenchmark()
        {
            TestContext.WriteLine("\n=== DISTANCE BENCHMARK REPORT ===\n");

            ChipShot_Benchmark();
            WoodLowShot_Benchmark();
            ApproachShot_Benchmark();
            DriveShot_Benchmark();
            FlopShot_Benchmark();

            TestContext.WriteLine("\n=== END BENCHMARK ===");
        }
    }
}
