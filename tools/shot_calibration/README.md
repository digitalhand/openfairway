# Shot Calibration Tools

Tools for comparing OpenFairway physics output against FlightScope reference data. Used to validate and tune the physics engine.

## Overview

The calibration workflow produces two CSVs — one from OpenFairway's physics simulation, one from FlightScope reference values — and compares them side-by-side to show carry, total distance, and apex deltas.

## Directory Layout

```
assets/data/
├── *.json                  # Shot input files (BallData from launch monitors)
├── SOT/
│   └── flightscope_reference.json   # Source-of-truth reference data
└── calibration/
    ├── flightscope.csv     # FlightScope reference CSV (from export tool)
    └── physics.csv         # Physics simulation CSV (from export tool)
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

- `Speed` — Ball speed in mph
- `VLA` — Vertical launch angle in degrees
- `HLA` — Horizontal launch angle in degrees
- `TotalSpin` — Total spin in RPM
- `SpinAxis` — Spin axis in degrees (negative = draw)
- `BackSpin` / `SideSpin` — Component spins in RPM

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

### `export_physics_csv.gd`

Runs every shot file through OpenFairway's `PhysicsAdapter` headless simulation and outputs a CSV with carry, total, apex, flight diagnostics, etc.

```bash
godot --headless --script tools/shot_calibration/export_physics_csv.gd > assets/data/calibration/physics.csv
```

Requires Godot runtime. Outputs columns: shot_name, filename, speed, VLA, HLA, spin, carry, total, rollout, apex, hang time, landing speed/angle, Re, spin ratio, Cd, Cl, peak Cl, carry-only.

### `export_flightscope_csv.py`

Exports FlightScope reference values as a matching CSV for comparison. Reads shot input fields from `assets/data/*.json` and merges reference carry/total/apex from `assets/data/SOT/flightscope_reference.json`.

```bash
python tools/shot_calibration/export_flightscope_csv.py > assets/data/calibration/flightscope.csv
python tools/shot_calibration/export_flightscope_csv.py --reference assets/data/SOT/flightscope_reference.json
```

No Godot runtime required.

### `compare_csv.py`

Side-by-side comparison table of physics vs FlightScope CSVs. Shows deltas for carry, total, and apex.

```bash
python tools/shot_calibration/compare_csv.py assets/data/calibration/physics.csv assets/data/calibration/flightscope.csv
```

### `flightscope_scraper.py`

Automated scraper for [FlightScope Trajectory Optimizer](https://trajectory.flightscope.com/). Uses Selenium to open the page, dismiss the weather popup, toggle wind OFF, fill in each shot's parameters (ball speed, VLA, HLA, spin, spin axis), click DISPLAY SHOT, and read carry/total/apex from the results table.

```bash
# Scrape all default shots (headless)
python tools/shot_calibration/flightscope_scraper.py

# Scrape all default shots with visible browser (for debugging)
python tools/shot_calibration/flightscope_scraper.py --visible

# Scrape specific shots only
python tools/shot_calibration/flightscope_scraper.py --shots driver1.json wood1.json

# Scrape specific shots with visible browser
python tools/shot_calibration/flightscope_scraper.py --shots driver1.json --visible

# Generate empty template for manual entry
python tools/shot_calibration/flightscope_scraper.py --template
```

Requires: `pip install selenium` (uses Chrome via ChromeDriver)

### `flightscope_discover.py`

Discovery/debugging script for the FlightScope page. Opens Chrome, dismisses the weather popup, toggles wind OFF, then dumps all interactive elements and takes a screenshot. Useful when page structure changes and selectors need updating.

```bash
# Discover page structure (visible browser)
python tools/shot_calibration/flightscope_discover.py

# Also fill in a test shot and capture results
python tools/shot_calibration/flightscope_discover.py --fill-test-shot

# Run headless
python tools/shot_calibration/flightscope_discover.py --headless
```

Requires: `pip install selenium`

## Workflow

1. **Export physics CSV** (requires Godot):
   ```bash
   godot --headless --script tools/shot_calibration/export_physics_csv.gd > assets/data/calibration/physics.csv
   ```

2. **Export FlightScope CSV**:
   ```bash
   python tools/shot_calibration/export_flightscope_csv.py > assets/data/calibration/flightscope.csv
   ```

3. **Compare**:
   ```bash
   python tools/shot_calibration/compare_csv.py assets/data/calibration/physics.csv assets/data/calibration/flightscope.csv
   ```

## Output

CSVs are written to `assets/data/calibration/`. Key columns:

| Column | Description |
|---|---|
| `carry_yd` | Carry distance in yards (first bounce) |
| `total_yd` | Total distance including rollout |
| `rollout_yd` | `total_yd - carry_yd` |
| `apex_ft` | Peak height in feet |
| `initial_cd` / `initial_cl` | Drag/lift coefficients at launch |
| `peak_cl` | Maximum lift coefficient during flight |
| `landing_angle_deg` | Descent angle at landing |
