# Physics Tests

This directory contains unit tests for validating the OpenFairway physics engine.

## Test Categories

### 1. Formula Validation Tests (`Category=RolloutPhysics`) ✅ CI-Compatible

These tests validate the **rollout physics formulas** (friction multipliers, velocity scaling) that control chip/bump/driver behavior. They run **without Godot runtime** and can be executed in CI/CD.

**✅ These tests run automatically in GitHub Actions CI**

**Run locally after any physics code changes:**

```bash
dotnet test --filter "Category=RolloutPhysics"
```

**What they validate:**
- Velocity scaling curve (0-35 m/s range)
- Spin multiplier curve (0-2750+ RPM range)
- Specific shot scenarios (chip, bump, driver)
- Regression guards (old vs new formula behavior)

**Example output:**
```
Passed!  - Failed: 0, Passed: 10, Skipped: 0, Total: 10
```

### 2. Headless Distance Benchmarks (`run_benchmarks.gd`) ✅ Primary Validation Tool

Run all 9 test shots through `PhysicsAdapter` headlessly via Godot. **This is the standard way to validate physics changes locally.**

```bash
godot --headless --script run_benchmarks.gd
```

Produces a table with carry/total/rollout for every shot. Compare output against these baselines:

**Baselines (as of 2026-02-11, via `run_benchmarks.gd`):**

| Shot | Speed | VLA | Spin | Carry (yd) | Total (yd) | Rollout (yd) |
|------|-------|-----|------|-----------|-----------|-------------|
| Drive | 150.0 mph | 12.50° | 2335 rpm | 250.8 | 267.3 | 16.5 |
| Wood Low | 114.5 mph | 6.95° | 1933 rpm | 122.5 | 196.1 | 73.6 |
| Wedge | 81.1 mph | 30.50° | 7851 rpm | 104.9 | 118.1 | 13.2 |
| Bump | 78.3 mph | 5.57° | 1850 rpm | 39.0 | 109.0 | 70.0 |
| Approach | 81.1 mph | 30.50° | 10490 rpm | 106.4 | 110.3 | 4.0 |
| Topped | 91.8 mph | 1.66° | 2195 rpm | 25.6 | 115.4 | 89.8 |
| Checked | 75.1 mph | 38.50° | 10701 rpm | 85.7 | 90.0 | 4.3 |
| Flop | 68.1 mph | 45.50° | 12551 rpm | 66.7 | 67.5 | 0.8 |
| Chip | 24.7 mph | 17.94° | 3204 rpm | 7.8 | 19.4 | 11.6 |

### 3. Distance Regression Tests (`Category=DistanceBenchmark`)

These tests in `DistanceBenchmarkTests.cs` also use `PhysicsAdapter` but are structured as NUnit tests. They are an alternative to `run_benchmarks.gd` but require Godot runtime via `dotnet test`.

### 3. Core Physics Tests ⚠️ Requires Godot Runtime (Local Only)

Other test files in this directory require Godot runtime and **cannot run in CI**:
- `AerodynamicsTests.cs` - Drag/lift coefficient calculations
- `SurfaceTests.cs` - Friction parameter validation
- `BallPhysicsTests.cs` - Force/torque calculations
- `EnumsTests.cs` - Enum value consistency
- `ApproachShotTests.cs` - Full shot simulation tests
- `DistanceBenchmarkTests.cs` - Headless distance benchmarks

**These tests must be run locally:**
```bash
# Open project in Godot first, then run:
dotnet test OpenShotGolf.sln
```

**Why?** These tests instantiate Godot classes like `BallPhysics`, `Aerodynamics`, `PhysicsAdapter` which require the Godot runtime.

## Workflow for Physics Changes

### 1. Capture Baseline

```bash
# Save current distances before making changes
godot --headless --script run_benchmarks.gd 2>&1 | grep -E "^(Shot|Drive|Wood|Wedge|Bump|Approach|Topped|Checked|Flop|Chip)" > baseline.txt
```

### 2. Make Physics Code Changes

Edit files in `addons/openfairway/physics/`:
- `BallPhysics.cs` - Friction multipliers, velocity scaling
- `Aerodynamics.cs` - Drag/lift coefficients
- `Surface.cs` - Ground friction parameters

### 3. Run Formula Validation Tests

```bash
cd tests/PhysicsTests
dotnet test --filter "Category=RolloutPhysics"
```

If tests fail, the formulas have changed. **Update the tests** to reflect the new expected values.

### 4. Run Headless Distance Benchmarks

```bash
godot --headless --script run_benchmarks.gd
```

Compare carry/total/rollout for all 9 shots against the baseline from step 1.

### 5. (Optional) Validate In-Game

For visual validation (bounce behavior, tracer paths, spin effects), run shots in Godot editor and pipe console output through the parser:

```bash
python parse_shot_debug.py < debug_output.txt
```

### 6. Update Baselines

If distances changed intentionally:
1. Update the baseline table in this README
2. Update expected values in `RolloutPhysicsTests.cs` and `ShotDistanceRegressionTests.cs`
3. Include both code changes and test updates in the same commit

## Key Physics Parameters (Validated)

### Velocity Scaling Curve
```csharp
if (ballSpeed < 20.0f)
    velocityScale = Lerp(0.60f, 0.87f, ballSpeed / 20.0f);  // Chip/pitch range
else if (ballSpeed < 35.0f)
    velocityScale = Lerp(0.87f, 1.0f, (ballSpeed - 20.0f) / 15.0f);  // Transition
else
    velocityScale = 1.0f;  // Full wedges/drivers
```

### Spin Multiplier Curve
```csharp
if (rpm < 1250)
    mult = 1.0 + (rpm/1250) * 0.30;  // Low spin: 1.0x to 1.30x
else if (rpm < 1750)
    mult = 1.30 + ((rpm-1250)/500) * 0.95;  // Bump/pitch: 1.30x to 2.25x (steep!)
else
    mult = 2.25 + Min((rpm-1750)/1000, 1.0) * 0.25;  // High spin: 2.25x to 2.50x
```

### Expected Multipliers by Shot Type
- **Chip** (2785 RPM, 2.44 m/s): ×1.95
- **Bump** (1365 RPM, 9.25 m/s): ×1.37
- **Driver** (1118 RPM, 16 m/s): ×1.20-1.30

## Troubleshooting

### "Property 'GodotProjectDir' is null or empty" Warning

This warning is expected when running tests outside Godot. It doesn't affect formula validation tests.

### Test Failures After Physics Changes

1. Check if the formula change was intentional
2. Verify distances in Godot match expectations
3. Update test expected values to reflect new validated behavior
4. Document the reason for the change in commit message

### Distance Regression Baselines Out of Date

Re-run all test shots in Godot and update the baselines in `ShotDistanceRegressionTests.cs` with the new validated values and date.
