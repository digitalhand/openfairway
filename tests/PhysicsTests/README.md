# Physics Tests

This directory contains unit tests for validating the OpenFairway physics engine.

## Test Categories

### 1. Formula Validation Tests (`Category=RolloutPhysics`) ✅ CI-Compatible

These tests validate the **CI-safe formula layer**: rollout friction, shared spin-drag behavior, low-launch lift recovery, and other pure math used by the physics engine. They run **without Godot runtime** and can be executed in CI/CD.

**✅ These tests run automatically in GitHub Actions CI**

**Run locally after any physics code changes:**

```bash
dotnet test --filter "Category=RolloutPhysics"
```

**What they validate:**
- Velocity scaling curve (0-35 m/s range)
- Spin multiplier curve (0-2750+ RPM range)
- Shared flight coefficient sampling for wood, wedge, and checked-spin regimes
- Specific shot scenarios (chip, bump, driver)
- Regression guards (old vs new formula behavior)

**Example output:**
```
Passed!  - Failed: 0, Passed: N, Skipped: 0, Total: N
```

`MidIronRegressionTests.cs`, `WedgeRegressionTests.cs`, and `GreenSpinBackRegressionTests.cs` are intentionally **not** in this category because they instantiate Godot-backed physics classes and will crash under plain CI `dotnet test`.

### 2. Headless Distance Benchmarks (`run_benchmarks.gd`) ✅ Local Benchmark Pass

Run the current benchmark shot set through `PhysicsAdapter` headlessly via Godot. This is the fastest local pass for spotting broad carry / apex / rollout shifts across the benchmark corpus.

```bash
godot --headless --script run_benchmarks.gd
```

The script prints whatever benchmark set is currently defined in `run_benchmarks.gd`, including driver, wood, wedge, chip, and specialty-shot fixtures. Capture that output as the local baseline before changing physics.

### 3. LM Carry-Window Runtime Regression (`Category=LmCarryWindow`)

This is the carry-focused runtime suite for the LM / GSP / FS comparison corpus. It requires a Godot-backed test environment.

Run locally after opening the project in Godot:

```bash
dotnet test --filter "Category=LmCarryWindow"
```

`LmCarryWindowRegressionTests.cs` checks carry against comparison windows and prints the shared flight diagnostics used during tuning:

- `initial_spin_ratio`
- `initial_re`
- `initial_launch_angle_deg`
- `initial_low_launch_lift_scale`
- `initial_spin_drag_multiplier`
- `initial_cd`
- `initial_cl`
- `peak_cl`

### 4. Runtime Regression Tests (`Category=PhysicsRuntime`)

These tests exercise `PhysicsAdapter`, `BallPhysics`, and other Godot-backed physics classes. They are useful local regression guards, but they require a Godot runtime-backed test environment and must stay out of the CI-only `RolloutPhysics` filter.

Run locally after opening the project in Godot:

```bash
dotnet test --filter "Category=PhysicsRuntime"
```

### 5. Distance Regression Tests (`Category=DistanceBenchmark`)

These tests in `DistanceBenchmarkTests.cs` also use `PhysicsAdapter` but are structured as NUnit tests. They are an alternative to `run_benchmarks.gd` and require Godot runtime via `dotnet test`.

Historical manual baselines are documented in `RolloutPhysicsTests.cs` under `ShotDistanceRegressionTests` (explicit/manual category only).

### 6. Core Physics Tests ⚠️ Requires Godot Runtime (Local Only)

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
dotnet test OpenFairway.sln
```

**Why?** These tests instantiate Godot classes like `BallPhysics`, `Aerodynamics`, `PhysicsAdapter` which require the Godot runtime.

## Workflow for Physics Changes

### 1. Capture Baseline

```bash
# Save current distances before making changes
godot --headless --script run_benchmarks.gd > baseline.txt
```

### 2. Make Physics Code Changes

Edit files in `addons/openfairway/physics/`:
- `BallPhysics.cs` - Friction multipliers, velocity scaling
- `Aerodynamics.cs` - Drag/lift coefficients
- `FlightAerodynamicsModel.cs` - Shared carry-flight coefficient sampling
- `Surface.cs` - Ground friction parameters

### 3. Run Formula Validation Tests

```bash
cd tests/PhysicsTests
dotnet test --filter "Category=RolloutPhysics"
```

If tests fail, the shared formula layer changed. Update the tests only after the new behavior is intentional and locally validated.

### 4. Run Headless Distance Benchmarks

```bash
godot --headless --script run_benchmarks.gd
```

Compare carry/total/rollout against the baseline from step 1.

### 5. Run Carry-Window Runtime Regression

```bash
dotnet test --filter "Category=LmCarryWindow"
```

Confirm that carry remains inside the expected comparison windows and inspect the printed flight diagnostics when tuning carry-sensitive shots.

### 6. (Optional) Validate In-Game

For visual validation (bounce behavior, tracer paths, spin effects), run shots in Godot editor and pipe console output through the parser:

```bash
python parse_shot_debug.py < debug_output.txt
```

### 7. Update Baselines

If distances changed intentionally:
1. Update any benchmark notes in this README if the workflow or diagnostic expectations changed
2. Update expected values in `DistanceBenchmarkTests.cs`, `FsCarryRegressionTests.cs`, or `LmCarryWindowRegressionTests.cs` as appropriate
3. If preserving historical manual notes, update `ShotDistanceRegressionTests` comments in `RolloutPhysicsTests.cs`
4. Include both code changes and test updates in the same commit

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

Re-run the Godot-backed benchmark and carry-window suites, update the relevant regression expectations in code, and refresh any workflow notes in this README if the validation process changed.
