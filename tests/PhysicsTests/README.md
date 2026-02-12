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

### 2. Distance Regression Tests (`Category=DistanceBenchmark`)

These tests **document validated baseline distances** for each shot type. They are marked `[Explicit]` because they require manual validation in Godot.

**Purpose:** Document expected distances to detect unintended physics changes

**Validated baselines (as of 2024-02-11):**
- **Chip** (chip_test_shot.json): 7.6/13.1 yd (target 13.0, error +0.8%)
- **Bump** (bump_test_shot.json): 38.1/89.7 yd (target 85, error +5.5%)
- **Wood Low** (wood_low_test_shot.json): 121.7/194.7 yd (target 198, error -1.7%)
- **Approach** (approach_test_shot.json): 105.6/108.3 yd (target 108, error +0.3%)
- **Flop** (flop_test_shot.json): 66.2/66.7 yd (minimal rollout)

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

### 1. Before Making Changes

Run all tests to establish baseline:
```bash
dotnet test OpenShotGolf.sln
```

### 2. Make Physics Code Changes

Edit files in `addons/openfairway/physics/`:
- `BallPhysics.cs` - Friction multipliers, velocity scaling
- `Aerodynamics.cs` - Drag/lift coefficients
- `Surface.cs` - Ground friction parameters

### 3. Run Formula Validation Tests

```bash
dotnet test --filter "Category=RolloutPhysics"
```

If tests fail, the formulas have changed. **Update the tests** to reflect the new expected values.

### 4. Validate in Godot

Run the test shots in Godot (F5) and verify distances:
1. Load each test shot via ShotInjector UI
2. Compare carry/total distances to regression baselines
3. Check debug console for friction multipliers (should match formula tests)

### 5. Update Regression Baselines

If distances changed significantly, update the documentation in `ShotDistanceRegressionTests.cs` with new validated values.

### 6. Commit Changes

Include both code changes and test updates in the same commit.

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
