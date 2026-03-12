# Shot Calibration Tools

Tools for comparing OpenFairway physics output against FlightScope reference data, diagnosing divergence, and iteratively tuning physics parameters.

## Table of Contents

- [Overview](#overview)
- [Directory Layout](#directory-layout)
- [Shot Data Source](#shot-data-source)
- [FlightScope Reference](#flightscope-reference)
- [Tools](#tools)
  - [export_physics_json.gd](#export_physics_jsongd)
  - [export_physics_csv.gd](#export_physics_csvgd)
  - [export_flightscope_csv.py](#export_flightscope_csvpy)
  - [compare_csv.py](#compare_csvpy)
  - [calibration_analyzer.py](#calibration_analyzerpy)
  - [generate_profile.py](#generate_profilepy)
  - [calibrate.py](#calibratepy)
  - [flightscope_scraper.py](#flightscope_scraperpy)
  - [flightscope_discover.py](#flightscope_discoverpy)
- [Profile Override System](#profile-override-system)
  - [Profile JSON Schema](#profile-json-schema)
  - [How Overrides Work](#how-overrides-work)
  - [Available Profile Parameters](#available-profile-parameters)
- [Calibration Workflow](#calibration-workflow)
  - [Quick Start (Automated)](#quick-start-automated)
  - [Manual Step-by-Step](#manual-step-by-step)
  - [Iterative Tuning Loop](#iterative-tuning-loop)
- [Diagnostic Report](#diagnostic-report)
  - [Status Thresholds](#status-thresholds)
  - [Error Patterns](#error-patterns)
  - [Shot Regimes](#shot-regimes)
  - [Conflict Detection](#conflict-detection)
- [Iteration History](#iteration-history)
- [Output Columns](#output-columns)

## Overview

The calibration system compares OpenFairway physics simulation output against FlightScope trajectory optimizer data (source of truth). The pipeline:

1. **Simulate** — Run all shot files through `PhysicsAdapter` headlessly (Godot)
2. **Compare** — Diff physics output against FlightScope reference values
3. **Diagnose** — Classify error patterns, tag shot regimes, suggest parameter adjustments
4. **Generate** — Produce a profile override JSON with conservative adjustments
5. **Repeat** — Re-simulate with the profile override, compare again

Profile overrides are loaded from JSON at runtime, eliminating the C# rebuild cycle. The orchestrator (`calibrate.py`) automates the full loop and tracks iteration history with regression guards.

## Directory Layout

```
assets/data/
├── *.json                          # Shot input files (BallData from launch monitors)
├── SOT/
│   └── flightscope_reference.json  # Source-of-truth reference data
└── calibration/
    ├── physics.json                # Physics simulation JSON export
    ├── physics.csv                 # Physics simulation CSV export
    ├── flightscope.csv             # FlightScope reference CSV
    ├── shot_diff_analysis.csv      # Physics vs FlightScope diff CSV
    ├── calibration_profile.json    # Current profile override (optional)
    └── history/
        ├── iteration_001.json      # Iteration snapshots
        ├── iteration_002.json
        └── ...
```

## Shot Data Source

Shot input files live in `assets/data/*.json` using the BallData format from launch monitors (R10, Garmin, etc.):

```json
{
  "BallData": {
    "Speed": 150.0,
    "VLA": 12.5,
    "HLA": -0.5,
    "TotalSpin": 2800.0,
    "SpinAxis": -3.0,
    "BackSpin": 2796.0,
    "SideSpin": -146.5
  }
}
```

| Field | Unit | Description |
|-------|------|-------------|
| `Speed` | mph | Ball speed |
| `VLA` | degrees | Vertical launch angle |
| `HLA` | degrees | Horizontal launch angle |
| `TotalSpin` | RPM | Total spin |
| `SpinAxis` | degrees | Spin axis (negative = draw) |
| `BackSpin` | RPM | Backspin component |
| `SideSpin` | RPM | Sidespin component |

## FlightScope Reference

`assets/data/SOT/flightscope_reference.json` contains known-good carry/total/apex values from FlightScope's trajectory optimizer, indexed by shot filename:

```json
{
  "driver1": {
    "filename": "driver1.json",
    "speed_mph": 150.0,
    "carry_yd": 245.0,
    "total_yd": 270.0,
    "apex_ft": 95.0
  }
}
```

Populate this file manually from [FlightScope Trajectory Optimizer](https://trajectory.flightscope.com/) or use the scraper tool.

## Tools

### `export_physics_json.gd`

Runs every shot file through OpenFairway's `PhysicsAdapter` headless simulation and outputs JSON keyed by shot name.

```bash
godot --headless --script tools/shot_calibration/export_physics_json.gd
godot --headless --script tools/shot_calibration/export_physics_json.gd -- --output=res://assets/data/calibration/physics.json
```

Requires Godot runtime. Writes `res://assets/data/calibration/physics.json` by default. If Godot reports a missing `PhysicsAdapter` method, rebuild the C# project first with `godot --headless --build-solutions --quit`.

### `export_physics_csv.gd`

Runs every shot file through the physics export path and emits a CSV. Supports profile overrides via `--profile`.

```bash
# Default export
godot --headless --script tools/shot_calibration/export_physics_csv.gd

# With custom output path
godot --headless --script tools/shot_calibration/export_physics_csv.gd -- --output=res://assets/data/calibration/physics.csv

# With profile override (no C# rebuild needed)
godot --headless --script tools/shot_calibration/export_physics_csv.gd -- --profile=assets/data/calibration/calibration_profile.json
```

Requires Godot runtime. Outputs columns: shot_name, filename, speed, VLA, HLA, spin, carry, total, rollout, apex, hang time, landing speed/angle, Re, spin ratio, Cd, Cl, peak Cl, carry-only.

### `export_flightscope_csv.py`

Exports FlightScope reference values as a matching CSV. Reads shot inputs from `assets/data/*.json` and merges reference carry/total/apex from `flightscope_reference.json`.

```bash
python tools/shot_calibration/export_flightscope_csv.py > assets/data/calibration/flightscope.csv
python tools/shot_calibration/export_flightscope_csv.py --reference assets/data/SOT/flightscope_reference.json
```

No Godot runtime required.

### `compare_csv.py`

Generates a comparison CSV with carry/total/rollout/apex deltas and per-shot status classification.

```bash
python tools/shot_calibration/compare_csv.py assets/data/calibration/physics.csv assets/data/calibration/flightscope.csv
python tools/shot_calibration/compare_csv.py assets/data/calibration/physics.csv assets/data/calibration/flightscope.csv --output /tmp/shot_diff_analysis.csv
```

Default output: `assets/data/calibration/shot_diff_analysis.csv`

Output includes `rollout_physics_yd`, `rollout_flightscope_yd`, `diff_rollout_yd`, and `status` (pass/moderate/severe) columns alongside carry/total/apex diffs.

### `calibration_analyzer.py`

Diagnostic analyzer that reads `shot_diff_analysis.csv` and produces a structured report with error classification, shot regime tagging, parameter suggestions, and conflict detection.

```bash
# Text report (default)
python tools/shot_calibration/calibration_analyzer.py

# Custom input path
python tools/shot_calibration/calibration_analyzer.py --input path/to/shot_diff_analysis.csv

# JSON output (for programmatic consumption)
python tools/shot_calibration/calibration_analyzer.py --json
```

The report includes:
- **Summary** — Count of pass/moderate/severe/no-reference shots
- **Per-shot diagnostics** — Error pattern, regime tags, diffs, suggested knobs
- **Parameter conflicts** — Flags when failing shots need opposite adjustments to the same parameter

See [Diagnostic Report](#diagnostic-report) for details on classification logic.

### `generate_profile.py`

Generates a calibration profile JSON from diagnostic analysis. Applies conservative step-size adjustments to non-conflicting parameters.

```bash
# Generate from current diff analysis
python tools/shot_calibration/generate_profile.py

# Incremental: adjust from an existing base profile
python tools/shot_calibration/generate_profile.py --base assets/data/calibration/calibration_profile.json

# Target specific shots only
python tools/shot_calibration/generate_profile.py --target bump_and_run,driver1

# Preview adjustments without writing
python tools/shot_calibration/generate_profile.py --dry-run

# Custom output path
python tools/shot_calibration/generate_profile.py --output /tmp/profile.json
```

Default output: `assets/data/calibration/calibration_profile.json`

The generator:
- Reads the diff CSV and runs the analyzer internally
- Applies one step-size adjustment per parameter in the direction needed
- Skips parameters with conflicting requirements (flags them for manual review)
- Clamps all values to safe ranges defined in the knowledge base

### `calibrate.py`

Orchestrator for the full calibration iteration loop. Automates simulate-compare-analyze and tracks history.

```bash
# Run a full iteration (Godot export + FlightScope CSV + compare + analyze)
python tools/shot_calibration/calibrate.py run

# Run with a specific profile override
python tools/shot_calibration/calibrate.py run --profile assets/data/calibration/calibration_profile.json

# Skip Godot export (reuse existing physics.csv)
python tools/shot_calibration/calibrate.py run --skip-godot

# Show last iteration summary
python tools/shot_calibration/calibrate.py status

# Show all iteration summaries
python tools/shot_calibration/calibrate.py history

# Compare two iterations side-by-side
python tools/shot_calibration/calibrate.py diff 1 3
```

The `run` subcommand:
1. Detects `calibration_profile.json` (or uses `--profile`)
2. Exports physics CSV via Godot headless (with profile override if present)
3. Exports FlightScope reference CSV
4. Runs `compare_csv.py` to generate the diff
5. Runs the diagnostic analyzer
6. Saves an iteration snapshot to `assets/data/calibration/history/`
7. Prints the diagnostic report with regression warnings

### `flightscope_scraper.py`

Automated scraper for [FlightScope Trajectory Optimizer](https://trajectory.flightscope.com/). Uses Selenium to fill shot parameters, submit, and read carry/total/apex results.

```bash
# Scrape all default shots (headless)
python tools/shot_calibration/flightscope_scraper.py

# Scrape specific shots with visible browser
python tools/shot_calibration/flightscope_scraper.py --shots driver1.json wood1.json --visible

# Generate empty template for manual entry
python tools/shot_calibration/flightscope_scraper.py --template
```

Requires: `pip install selenium` and `brave` in your `PATH`.

### `flightscope_discover.py`

Discovery/debugging script for the FlightScope page. Dumps interactive elements and captures screenshots.

```bash
python tools/shot_calibration/flightscope_discover.py
python tools/shot_calibration/flightscope_discover.py --fill-test-shot
python tools/shot_calibration/flightscope_discover.py --headless
```

Requires: `pip install selenium`

## Profile Override System

Profile overrides eliminate the C# edit/rebuild bottleneck. After the one-time C# build, all tuning iterations are Godot-only.

### Profile JSON Schema

Only keys you want to override need to be present. Unspecified keys keep their defaults.

```json
{
  "DragScaleMultiplier": 1.0,
  "LiftScaleMultiplier": 1.0,
  "KineticFrictionMultiplier": 1.0,
  "RollingFrictionMultiplier": 1.0,
  "GrassViscosityMultiplier": 1.0,
  "CriticalAngleOffsetRadians": 0.0,
  "SpinbackThetaBoostMultiplier": 1.0,
  "Flight": {
    "ClMaxBase": 0.268,
    "CdMin": 0.22,
    "HighLaunchDragBoostMax": 1.18
  },
  "Bounce": {
    "FlightTangentialRetentionBase": 0.55,
    "CorBaseA": 0.45,
    "RolloutLowSpinRetention": 0.85,
    "RolloutHighSpinRetention": 0.70
  },
  "Rollout": {
    "LowSpinMultiplierMax": 1.15,
    "MidSpinMultiplierMax": 2.25,
    "HighSpinMultiplierMax": 2.50,
    "ChipVelocityScaleMin": 0.60,
    "ChipVelocityScaleMax": 0.87
  }
}
```

A minimal override targeting only rollout:

```json
{
  "Rollout": {
    "LowSpinMultiplierMax": 1.25
  }
}
```

### How Overrides Work

1. `export_physics_csv.gd` accepts `--profile=<path>` on the command line
2. The JSON is read and passed to `PhysicsAdapter.LoadProfileFromJson()`
3. `BallPhysicsProfile.FromJson()` parses the JSON — only present keys override defaults
4. All subsequent `SimulateShotFromJson()` calls use the overridden profile
5. Sub-profiles (`Flight`, `Bounce`, `Rollout`) are independently partial-merged

### Available Profile Parameters

#### Root-Level Multipliers

| Parameter | Default | Description |
|-----------|---------|-------------|
| `DragScaleMultiplier` | 1.0 | Global drag scale |
| `LiftScaleMultiplier` | 1.0 | Global lift scale |
| `KineticFrictionMultiplier` | 1.0 | Ground kinetic friction scale |
| `RollingFrictionMultiplier` | 1.0 | Ground rolling friction scale |
| `GrassViscosityMultiplier` | 1.0 | Grass viscosity scale |
| `CriticalAngleOffsetRadians` | 0.0 | Bounce critical angle offset |
| `SpinbackThetaBoostMultiplier` | 1.0 | Spinback theta boost scale |

#### Flight Profile

Controls aerodynamic drag and lift during ball flight. Key tuning parameters:

| Parameter | Default | Description |
|-----------|---------|-------------|
| `ClMaxBase` | 0.268 | Base max lift coefficient cap |
| `CdMin` | 0.22 | Minimum drag coefficient floor |
| `HighLaunchDragBoostMax` | 1.18 | Extra drag for high-launch/high-spin |
| `SpinDragMultiplierMax` | 1.20 | Max spin-induced drag multiplier |
| `LowLaunchLiftRecoveryMax` | 1.08 | Lift recovery for low-launch shots |

Full list: see `addons/openfairway/physics/FlightProfile.cs` (45+ parameters).

#### Bounce Profile

Controls coefficient of restitution and tangential velocity retention at impact.

| Parameter | Default | Description |
|-----------|---------|-------------|
| `FlightTangentialRetentionBase` | 0.55 | First-bounce tangential retention |
| `FlightSpinFactorMin` | 0.40 | Min retention at high spin |
| `FlightSpinFactorDivisor` | 8000 | Spin RPM divisor for retention curve |
| `CorBaseA` | 0.45 | Base COR (vertical bounce energy) |
| `RolloutLowSpinRetention` | 0.85 | Rollout bounce retention (low spin) |
| `RolloutHighSpinRetention` | 0.70 | Rollout bounce retention (high spin) |

Full list: see `addons/openfairway/physics/BounceProfile.cs` (20+ parameters).

#### Rollout Profile

Controls friction and velocity scaling during ground rollout.

| Parameter | Default | Description |
|-----------|---------|-------------|
| `LowSpinMultiplierMax` | 1.15 | Max friction multiplier (low spin) |
| `MidSpinMultiplierMax` | 2.25 | Max friction multiplier (mid spin) |
| `HighSpinMultiplierMax` | 2.50 | Max friction multiplier (high spin) |
| `ChipVelocityScaleMin` | 0.60 | Min velocity scale for chips |
| `ChipVelocityScaleMax` | 0.87 | Max velocity scale for chips |
| `FrictionBlendSpeed` | 15.0 | Speed for full friction blending |

Full list: see `addons/openfairway/physics/RolloutProfile.cs` (11 parameters).

## Calibration Workflow

### Quick Start (Automated)

```bash
# 1. Run full calibration iteration (simulate + compare + diagnose)
python tools/shot_calibration/calibrate.py run

# 2. Review the diagnostic report printed to stdout
#    Iteration snapshot saved to assets/data/calibration/history/iteration_NNN.json

# 3. Generate a profile override from diagnostics
python tools/shot_calibration/generate_profile.py

# 4. Re-run with the generated profile
python tools/shot_calibration/calibrate.py run

# 5. Compare iterations
python tools/shot_calibration/calibrate.py diff 1 2
```

### Manual Step-by-Step

```bash
# 1. Export physics CSV (requires Godot)
godot --headless --script tools/shot_calibration/export_physics_csv.gd

# 2. Export FlightScope CSV
python tools/shot_calibration/export_flightscope_csv.py > assets/data/calibration/flightscope.csv

# 3. Compare
python tools/shot_calibration/compare_csv.py assets/data/calibration/physics.csv assets/data/calibration/flightscope.csv

# 4. Diagnose
python tools/shot_calibration/calibration_analyzer.py

# 5. Generate profile override
python tools/shot_calibration/generate_profile.py --dry-run
python tools/shot_calibration/generate_profile.py

# 6. Re-export with profile override
godot --headless --script tools/shot_calibration/export_physics_csv.gd -- --profile=assets/data/calibration/calibration_profile.json

# 7. Re-compare and re-diagnose
python tools/shot_calibration/compare_csv.py assets/data/calibration/physics.csv assets/data/calibration/flightscope.csv
python tools/shot_calibration/calibration_analyzer.py
```

### Iterative Tuning Loop

The typical workflow cycles through:

```
  Edit profile JSON  ──>  Godot export (--profile)  ──>  Compare  ──>  Diagnose
       ^                                                                  │
       └──────────────────────────────────────────────────────────────────┘
```

Each iteration is tracked in `assets/data/calibration/history/`. The orchestrator warns if previously-passing shots regress.

For parameters flagged as conflicting (e.g., `FlightTangentialRetentionBase` needed higher for `driver1` but lower for `wood_low_test_shot`), manual review is required. These usually indicate the physics model needs a regime-specific fix in the C# code rather than a single-value tweak.

## Diagnostic Report

### Status Thresholds

| Metric | Pass | Moderate | Severe |
|--------|------|----------|--------|
| `carry_diff` | +/- 3 yd | 3-7 yd | > 7 yd |
| `total_diff` | +/- 5 yd | 5-10 yd | > 10 yd |
| `apex_diff` | +/- 5 ft | 5-10 ft | > 10 ft |

A shot's status is the worst of its carry and total classifications.

### Error Patterns

| Pattern | Meaning |
|---------|---------|
| `ROLLOUT_TOO_LONG` | Carry close, total overshoots |
| `ROLLOUT_TOO_SHORT` | Carry close, total undershoots |
| `CARRY_TOO_LONG` | Physics carry exceeds reference |
| `CARRY_TOO_SHORT` | Physics carry under reference |
| `CARRY_AND_ROLLOUT_LONG` | Both carry and rollout overshoot |
| `CARRY_AND_ROLLOUT_SHORT` | Both carry and rollout undershoot |

### Shot Regimes

Shots are tagged by their input characteristics:

| Regime | Criteria |
|--------|----------|
| `low-launch` | VLA < 10 degrees |
| `high-spin-wedge` | Total spin > 5000 RPM |
| `low-speed-chip` | Ball speed < 60 mph |
| `driver-wood` | Speed > 110 mph and VLA < 18 degrees |
| `mid-iron` | Speed 85-110 mph and VLA 15-25 degrees |
| `high-loft-wedge` | VLA > 30 degrees and spin > 8000 RPM |

Regime tags help the analyzer suggest parameters that specifically affect that shot type.

### Conflict Detection

The analyzer flags parameters where different failing shots need opposite adjustments. For example, if `bump_test_shot` (ROLLOUT_TOO_LONG) needs `FlightTangentialRetentionBase` decreased while `driver1` (ROLLOUT_TOO_SHORT) needs it increased, the parameter is flagged as conflicting.

Conflicting parameters are:
- Skipped by `generate_profile.py` (not auto-adjusted)
- Listed in the diagnostic report for manual review
- Tracked in iteration history snapshots

## Iteration History

Each `calibrate.py run` saves a snapshot to `assets/data/calibration/history/iteration_NNN.json`:

```json
{
  "iteration": 1,
  "timestamp": "2026-03-11T14:30:00",
  "profile_overrides": { "Rollout": { "LowSpinMultiplierMax": 1.20 } },
  "summary": { "pass": 18, "moderate": 4, "severe": 4, "no_reference": 3 },
  "per_shot": {
    "drive_test_shot": {
      "diff_carry_yd": 1.0,
      "diff_total_yd": 0.8,
      "status": "pass",
      "error_pattern": null
    }
  },
  "regressions": [],
  "conflicts": ["Bounce.FlightTangentialRetentionBase"]
}
```

View history:

```bash
python tools/shot_calibration/calibrate.py history
```

```
   # Timestamp            Pass   Mod   Sev  Regr
-------------------------------------------------
   1 2026-03-11T14:30:00     4     9     9     0
   2 2026-03-11T15:00:00     7     8     7     0
   3 2026-03-11T15:30:00    10     7     5     1
```

## Output Columns

`shot_diff_analysis.csv` columns:

| Column | Description |
|--------|-------------|
| `shot_name` | Shot identifier (filename stem) |
| `speed_mph` | Ball speed in mph |
| `vla_deg` | Vertical launch angle |
| `hla_deg` | Horizontal launch angle |
| `total_spin_rpm` | Total spin in RPM |
| `spin_axis_deg` | Spin axis in degrees |
| `physics_carry_yd` | Physics carry distance (yards) |
| `flightscope_carry_yd` | FlightScope carry distance (yards) |
| `diff_carry_yd` | Carry delta (physics - flightscope) |
| `physics_total_yd` | Physics total distance (yards) |
| `flightscope_total_yd` | FlightScope total distance (yards) |
| `diff_total_yd` | Total delta (physics - flightscope) |
| `rollout_physics_yd` | Physics rollout (total - carry) |
| `rollout_flightscope_yd` | FlightScope rollout (total - carry) |
| `diff_rollout_yd` | Rollout delta (physics - flightscope) |
| `physics_apex_ft` | Physics peak height (feet) |
| `flightscope_apex_ft` | FlightScope peak height (feet) |
| `diff_apex_ft` | Apex delta (physics - flightscope) |
| `status` | pass / moderate / severe |
