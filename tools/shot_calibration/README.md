# Shot Calibration Tools

Compare OpenFairway physics output against FlightScope reference data, find where shots are off, and tune physics parameters to close the gap.

## Prerequisites

```bash
# Create and activate the venv (one-time setup)
python -m venv .venv
source .venv/bin/activate
pip install -r tools/shot_calibration/requirements.txt
```

Always activate the venv before running Python tools. GDScript tools (`export_physics_csv.gd`) need Godot 4.5+ and don't need the venv.

## Quick Start

### Run Everything (simulate + scrape + compare + diagnose)

```bash
# All shots (standard + all shot sessions combined)
python tools/shot_calibration/calibrate.py run

# One session only (outputs stay in that session's directory)
python tools/shot_calibration/calibrate.py run --session assets/data/shot_session_3
```

### Analyze (compare + diagnose + accuracy reports)

Use this when `physics.csv` and `flightscope.csv` already exist (e.g., you scraped FlightScope separately):

```bash
# All shots
python tools/shot_calibration/calibrate.py analyze

# One session
python tools/shot_calibration/calibrate.py analyze --session assets/data/shot_session_3

# Rebuild FlightScope CSV from reference JSON before comparing
python tools/shot_calibration/calibrate.py analyze --session assets/data/shot_session_3 --flightscope-export
```

The `analyze` command:
1. Compares `physics.csv` vs `flightscope.csv` → `shot_diff_analysis.csv`
2. Prints a diagnostic report
3. Writes accuracy reports to `assets/data/`:
   - `openfairway_accuracy_summary_<timestamp>.json` — carry + total + apex accuracy stats
   - `openfairway_critical_carry_<timestamp>.csv` — top 20 worst shots by carry error
   - `openfairway_critical_overall_<timestamp>.csv` — top 20 worst shots by max(carry, total) error
4. Saves an iteration snapshot to history

**Accuracy report field reference:**

| Field | What it means |
|-------|---------------|
| `avg_error` | Average error (positive = physics flies long, negative = short) |
| `avg_off` | Average how far off, ignoring direction |
| `typical_off` | Typical how far off (middle value, less affected by outliers) |
| `consistency` | How spread out errors are (lower = more consistent) |
| `worst_off` | Single worst shot error |
| `within_pct_yd` | Distribution of shots within N yards of reference (N = 1, 2, 3, 5, 7, 10, 15, 20) |
| `within_pct_ft` | Distribution of shots within N feet of reference (N = 1, 2, 3, 5, 7, 10, 13, 15, 20, 50) |

Fields end in `_yd` (yards) for carry/total or `_ft` (feet) for apex.

### Iteration History

```bash
# List all iterations
python tools/shot_calibration/calibrate.py history

# Compare two iterations side-by-side
python tools/shot_calibration/calibrate.py diff 1 3

# Show the latest iteration
python tools/shot_calibration/calibrate.py status
```

### Tuning Loop

```bash
# 1. Run calibration
python tools/shot_calibration/calibrate.py run

# 2. Auto-generate a profile with suggested tweaks
python tools/shot_calibration/generate_profile.py

# 3. Re-run (picks up calibration_profile.json automatically)
python tools/shot_calibration/calibrate.py run

# 4. Compare before/after
python tools/shot_calibration/calibrate.py diff 1 2
```

Profile overrides are JSON files loaded at runtime — no C# rebuild needed between iterations. See [Profile Override System](#profile-override-system).

## Directory Layout

```
assets/data/
├── *.json                          # Shot input files (from launch monitors)
├── SOT/
│   ├── flightscope_SoT.csv        # FlightScope reference CSV (standard shots)
│   └── flightscope_reference.json  # FlightScope reference data
├── openfairway_*_<timestamp>.*     # Accuracy reports (from analyze)
├── shot_session_N/                 # Session directories (from ShotRecordingService)
│   ├── shot_*.json                 # Recorded shot files
│   ├── physics.csv                 # Physics simulation output
│   ├── flightscope_reference.json  # FlightScope reference (from scraper)
│   ├── flightscope.csv             # FlightScope reference CSV
│   ├── shot_diff_analysis.csv      # Physics vs FlightScope diff
│   └── history/                    # Iteration history for this session
│       └── iteration_001.json
└── calibration/
    ├── physics.csv                 # Combined physics output (all shots)
    ├── flightscope.csv             # Combined FlightScope reference (all shots)
    ├── shot_diff_analysis.csv      # Combined diff (all shots)
    ├── calibration_profile.json    # Current profile override (optional)
    └── history/
        ├── iteration_001.json      # Iteration snapshots
        └── ...
```

## FlightScope Scraper

Scrapes [FlightScope Trajectory Optimizer](https://trajectory.flightscope.com/) to get reference carry/total/apex values. If interrupted, re-running the same command picks up where it left off.

```bash
# Scrape a session (visible browser recommended)
python tools/shot_calibration/flightscope_scraper.py --session assets/data/shot_session_3 --visible

# Scrape standard shots
python tools/shot_calibration/flightscope_scraper.py --visible

# Retry shots that failed last time
python tools/shot_calibration/flightscope_scraper.py --session assets/data/shot_session_3 --retry-failed

# Start over from scratch
python tools/shot_calibration/flightscope_scraper.py --session assets/data/shot_session_3 --visible --force
```

Requires Chrome or Brave in your `PATH`.

### reCAPTCHA Workaround

The scraper uses `undetected-chromedriver` with a browser profile saved at `~/.config/openfairway/scraper-profile`. If it still gets blocked by reCAPTCHA, attach to a real Chrome session instead:

**Terminal 1** — Launch Chrome with remote debugging:

```bash
google-chrome --incognito --remote-debugging-port=9222 --user-data-dir=~/.config/openfairway/scraper-profile
```

Open https://trajectory.flightscope.com/ manually the first time so reCAPTCHA sees a real user.

**Terminal 2** — Run the scraper against that browser:

```bash
python tools/shot_calibration/flightscope_scraper.py --session assets/data/shot_session_3 --debug-port 9222
```

The browser stays open when scraping finishes — you can scrape more sessions without restarting it. Use `brave-browser` instead of `google-chrome` if using Brave.

## Profile Override System

Tweak physics parameters via JSON without rebuilding C#. Only include the keys you want to change — everything else keeps its default.

```json
{
  "DragScaleMultiplier": 1.0,
  "LiftScaleMultiplier": 1.0,
  "Flight": {
    "ClMaxBase": 0.268,
    "CdMin": 0.22,
    "HighLaunchDragBoostMax": 1.18
  },
  "Bounce": {
    "FlightTangentialRetentionBase": 0.55,
    "CorBaseA": 0.45
  },
  "Rollout": {
    "LowSpinMultiplierMax": 1.15,
    "MidSpinMultiplierMax": 2.25,
    "HighSpinMultiplierMax": 2.50
  }
}
```

`generate_profile.py` builds a profile from the diagnostic report:

```bash
python tools/shot_calibration/generate_profile.py              # Generate from current diff
python tools/shot_calibration/generate_profile.py --dry-run    # Preview without writing
python tools/shot_calibration/generate_profile.py --base assets/data/calibration/calibration_profile.json  # Build on existing
```

### Available Parameters

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

Controls drag and lift during ball flight. Key parameters:

| Parameter | Default | Description |
|-----------|---------|-------------|
| `ClMaxBase` | 0.268 | Base max lift coefficient cap |
| `CdMin` | 0.22 | Minimum drag coefficient floor |
| `HighLaunchDragBoostMax` | 1.18 | Extra drag for high-launch/high-spin |
| `SpinDragMultiplierMax` | 1.20 | Max spin-induced drag multiplier |
| `LowLaunchLiftRecoveryMax` | 1.08 | Lift recovery for low-launch shots |

Full list: see `addons/openfairway/physics/FlightProfile.cs` (45+ parameters).

#### Bounce Profile

Controls how much energy and speed the ball keeps after hitting the ground.

| Parameter | Default | Description |
|-----------|---------|-------------|
| `FlightTangentialRetentionBase` | 0.55 | First-bounce forward speed retention |
| `FlightSpinFactorMin` | 0.40 | Min retention at high spin |
| `FlightSpinFactorDivisor` | 8000 | Spin RPM divisor for retention curve |
| `CorBaseA` | 0.45 | Base bounce energy (vertical) |
| `RolloutLowSpinRetention` | 0.85 | Rollout bounce retention (low spin) |
| `RolloutHighSpinRetention` | 0.70 | Rollout bounce retention (high spin) |

Full list: see `addons/openfairway/physics/BounceProfile.cs` (20+ parameters).

#### Rollout Profile

Controls friction and velocity during ground roll.

| Parameter | Default | Description |
|-----------|---------|-------------|
| `LowSpinMultiplierMax` | 1.15 | Max friction multiplier (low spin) |
| `MidSpinMultiplierMax` | 2.25 | Max friction multiplier (mid spin) |
| `HighSpinMultiplierMax` | 2.50 | Max friction multiplier (high spin) |
| `ChipVelocityScaleMin` | 0.60 | Min velocity scale for chips |
| `ChipVelocityScaleMax` | 0.87 | Max velocity scale for chips |
| `FrictionBlendSpeed` | 15.0 | Speed for full friction blending |

Full list: see `addons/openfairway/physics/RolloutProfile.cs` (11 parameters).

## Diagnostic Report

### Status Thresholds

| Metric | Pass | Moderate | Severe |
|--------|------|----------|--------|
| `carry_diff` | +/- 3 yd | 3-7 yd | > 7 yd |
| `total_diff` | +/- 5 yd | 5-10 yd | > 10 yd |
| `apex_diff` | +/- 5 ft | 5-10 ft | > 10 ft |

A shot's status is the worst of its carry and total grades.

### Error Patterns

| Pattern | Meaning |
|---------|---------|
| `ROLLOUT_TOO_LONG` | Carry is close, but total overshoots |
| `ROLLOUT_TOO_SHORT` | Carry is close, but total undershoots |
| `CARRY_TOO_LONG` | Physics carry exceeds reference |
| `CARRY_TOO_SHORT` | Physics carry falls short of reference |
| `CARRY_AND_ROLLOUT_LONG` | Both carry and rollout overshoot |
| `CARRY_AND_ROLLOUT_SHORT` | Both carry and rollout undershoot |

### Shot Regimes

Shots are tagged by their input characteristics to help suggest the right parameters to tweak:

| Regime | Criteria |
|--------|----------|
| `low-launch` | VLA < 10 degrees |
| `high-spin-wedge` | Total spin > 5000 RPM |
| `low-speed-chip` | Ball speed < 60 mph |
| `driver-wood` | Speed > 110 mph and VLA < 18 degrees |
| `mid-iron` | Speed 85-110 mph and VLA 15-25 degrees |
| `high-loft-wedge` | VLA > 30 degrees and spin > 8000 RPM |

### Conflicts

When different failing shots need opposite adjustments to the same parameter, it's flagged as a conflict. `generate_profile.py` skips conflicting parameters — they usually need a regime-specific fix in C# rather than a single-value tweak.

## Tool Reference

`calibrate.py` is the main entry point. These tools run under the hood but can also be used standalone (all support `--help` and most support `--session`):

| Tool | What it does | Needs |
|------|--------------|-------|
| `export_physics_csv.gd` | Simulate all shots, write CSV | Godot |
| `export_physics_json.gd` | Simulate all shots, write JSON | Godot |
| `physics_export_data.gd` | Shared helper for shot discovery | (not run directly) |
| `export_flightscope_csv.py` | Export FlightScope reference as CSV | Python |
| `compare_csv.py` | Diff physics vs FlightScope → `shot_diff_analysis.csv` | Python |
| `calibration_analyzer.py` | Generate diagnostic report from diff CSV | Python |
| `generate_profile.py` | Build profile override JSON from diagnostics | Python |
| `flightscope_scraper.py` | Scrape FlightScope trajectory optimizer | Python + Chrome/Brave |
| `flightscope_discover.py` | Debug helper for FlightScope page | Python + Chrome/Brave |

## Shot Data Format

Shot files use the BallData format from launch monitors (R10, Garmin, etc.):

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
