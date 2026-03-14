#!/usr/bin/env python
"""Compare physics CSV against FlightScope CSV and write a diff CSV.

Usage:
    python tools/shot_calibration/compare_csv.py assets/data/calibration/physics.csv assets/data/calibration/flightscope.csv
    python tools/shot_calibration/compare_csv.py assets/data/calibration/physics.csv assets/data/calibration/flightscope.csv --output /tmp/shot_diff_analysis.csv
    python tools/shot_calibration/compare_csv.py assets/data/calibration/physics.csv assets/data/calibration/flightscope.csv --carry-exceptions assets/data/calibration/carry_exception_profile.json
"""

import argparse
import csv
import os
import sys

from carry_exception_layer import apply_carry_exceptions, load_profile


SCRIPT_DIR = os.path.dirname(__file__)
DEFAULT_OUTPUT_PATH = os.path.normpath(
    os.path.join(SCRIPT_DIR, "..", "..", "assets", "data", "calibration", "shot_diff_analysis.csv")
)
DEFAULT_CARRY_EXCEPTION_PROFILE = os.path.normpath(
    os.path.join(SCRIPT_DIR, "..", "..", "assets", "data", "calibration", "carry_exception_profile.json")
)

CARRY_PASS = 3.0
CARRY_MODERATE = 7.0
TOTAL_PASS = 5.0
TOTAL_MODERATE = 10.0

OUTPUT_FIELDS = [
    "shot_name",
    "speed_mph",
    "vla_deg",
    "hla_deg",
    "total_spin_rpm",
    "spin_axis_deg",
    "physics_carry_yd",
    "flightscope_carry_yd",
    "diff_carry_yd",
    "physics_carry_raw_yd",
    "diff_carry_raw_yd",
    "physics_total_yd",
    "flightscope_total_yd",
    "diff_total_yd",
    "rollout_physics_yd",
    "rollout_flightscope_yd",
    "diff_rollout_yd",
    "physics_apex_ft",
    "flightscope_apex_ft",
    "diff_apex_ft",
    "carry_exception_regime",
    "carry_exception_offset_yd",
    "carry_exception_source",
    "carry_exception_applied",
    "status",
]


def load_csv(path):
    """Load CSV indexed by shot_name."""
    rows = {}
    with open(path, "r", newline="") as f:
        reader = csv.DictReader(f)
        for row in reader:
            shot_name = row.get("shot_name", "").strip()
            if shot_name:
                rows[shot_name] = row
    return rows


def parse_float(value):
    """Parse float from CSV field."""
    if value is None:
        return None
    text = str(value).strip()
    if not text:
        return None
    try:
        return float(text)
    except ValueError:
        return None


def parse_metric(value):
    """Parse distance/height metric, where 0 means missing reference."""
    parsed = parse_float(value)
    if parsed is None or parsed == 0.0:
        return None
    return parsed


def fmt_decimal(value, digits=1):
    """Format decimal as string for CSV output; blank if missing."""
    if value is None:
        return ""
    return f"{value:.{digits}f}"


def choose_input_value(primary, fallback):
    """Use primary input value when present; otherwise fallback."""
    first = parse_float(primary)
    if first is not None:
        return first
    return parse_float(fallback)


def classify_status(diff_carry, diff_total):
    """Classify shot status based on carry and total diffs."""
    carry_abs = abs(diff_carry) if diff_carry is not None else None
    total_abs = abs(diff_total) if diff_total is not None else None

    if total_abs is not None and total_abs > TOTAL_MODERATE:
        return "severe"
    if carry_abs is not None and carry_abs > CARRY_MODERATE:
        return "severe"
    if total_abs is not None and total_abs > TOTAL_PASS:
        return "moderate"
    if carry_abs is not None and carry_abs > CARRY_PASS:
        return "moderate"
    if total_abs is None and carry_abs is None:
        return ""
    return "pass"


def build_row(shot_name, physics_row, flightscope_row):
    speed = choose_input_value(
        physics_row.get("speed_mph"),
        flightscope_row.get("speed_mph"),
    )
    vla = choose_input_value(
        physics_row.get("vla_deg"),
        flightscope_row.get("vla_deg"),
    )
    hla = choose_input_value(
        physics_row.get("hla_deg"),
        flightscope_row.get("hla_deg"),
    )
    spin = choose_input_value(
        physics_row.get("total_spin_rpm"),
        flightscope_row.get("total_spin_rpm"),
    )
    spin_axis = choose_input_value(
        physics_row.get("spin_axis_deg"),
        flightscope_row.get("spin_axis_deg"),
    )

    p_carry = parse_metric(physics_row.get("carry_yd"))
    f_carry = parse_metric(flightscope_row.get("carry_yd"))
    p_total = parse_metric(physics_row.get("total_yd"))
    f_total = parse_metric(flightscope_row.get("total_yd"))
    p_apex = parse_metric(physics_row.get("apex_ft"))
    f_apex = parse_metric(flightscope_row.get("apex_ft"))

    diff_carry = p_carry - f_carry if p_carry is not None and f_carry is not None else None
    diff_total = p_total - f_total if p_total is not None and f_total is not None else None

    p_rollout = p_total - p_carry if p_total is not None and p_carry is not None else None
    f_rollout = f_total - f_carry if f_total is not None and f_carry is not None else None
    diff_rollout = p_rollout - f_rollout if p_rollout is not None and f_rollout is not None else None

    status = classify_status(diff_carry, diff_total)

    return {
        "shot_name": shot_name,
        "speed_mph": fmt_decimal(speed, 1),
        "vla_deg": fmt_decimal(vla, 1),
        "hla_deg": fmt_decimal(hla, 1),
        "total_spin_rpm": fmt_decimal(spin, 0),
        "spin_axis_deg": fmt_decimal(spin_axis, 1),
        "physics_carry_yd": fmt_decimal(p_carry, 1),
        "flightscope_carry_yd": fmt_decimal(f_carry, 1),
        "diff_carry_yd": fmt_decimal(diff_carry, 1),
        "physics_carry_raw_yd": fmt_decimal(p_carry, 1),
        "diff_carry_raw_yd": fmt_decimal(diff_carry, 1),
        "physics_total_yd": fmt_decimal(p_total, 1),
        "flightscope_total_yd": fmt_decimal(f_total, 1),
        "diff_total_yd": fmt_decimal(diff_total, 1),
        "rollout_physics_yd": fmt_decimal(p_rollout, 1),
        "rollout_flightscope_yd": fmt_decimal(f_rollout, 1),
        "diff_rollout_yd": fmt_decimal(diff_rollout, 1),
        "physics_apex_ft": fmt_decimal(p_apex, 1),
        "flightscope_apex_ft": fmt_decimal(f_apex, 1),
        "diff_apex_ft": fmt_decimal(p_apex - f_apex if p_apex is not None and f_apex is not None else None, 1),
        "carry_exception_regime": "",
        "carry_exception_offset_yd": "",
        "carry_exception_source": "",
        "carry_exception_applied": "false",
        "status": status,
    }


def write_output_csv(path, rows):
    """Write shot diff output rows to CSV."""
    output_dir = os.path.dirname(path)
    if output_dir:
        os.makedirs(output_dir, exist_ok=True)

    with open(path, "w", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=OUTPUT_FIELDS, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)


def parse_args():
    parser = argparse.ArgumentParser(description="Compare physics CSV against FlightScope CSV")
    parser.add_argument("physics_csv", help="Path to physics CSV input")
    parser.add_argument("flightscope_csv", help="Path to FlightScope CSV input")
    parser.add_argument(
        "--output",
        default=DEFAULT_OUTPUT_PATH,
        help="Output path for generated comparison CSV (default: assets/data/calibration/shot_diff_analysis.csv)",
    )
    parser.add_argument(
        "--carry-exceptions",
        default=None,
        help="Optional path to carry exception profile JSON",
    )
    parser.add_argument(
        "--no-carry-exceptions",
        action="store_true",
        help="Disable carry exception profile loading (even if default profile exists)",
    )
    return parser.parse_args()


def main():
    args = parse_args()
    physics = load_csv(args.physics_csv)
    flightscope = load_csv(args.flightscope_csv)

    all_shots = sorted(set(physics.keys()) | set(flightscope.keys()))
    rows = [build_row(shot, physics.get(shot, {}), flightscope.get(shot, {})) for shot in all_shots]

    carry_profile_path = None
    if not args.no_carry_exceptions:
        if args.carry_exceptions:
            carry_profile_path = os.path.normpath(args.carry_exceptions)
        elif os.path.exists(DEFAULT_CARRY_EXCEPTION_PROFILE):
            carry_profile_path = DEFAULT_CARRY_EXCEPTION_PROFILE

    applied = 0
    if carry_profile_path:
        try:
            profile = load_profile(carry_profile_path)
            applied = apply_carry_exceptions(rows, profile, classify_status)
            print(
                f"Carry exception layer: {applied} shot(s) adjusted using {carry_profile_path}",
                file=sys.stderr,
            )
        except Exception as exc:
            print(f"ERROR: Failed to apply carry exceptions: {exc}", file=sys.stderr)
            sys.exit(1)

    output_path = os.path.normpath(args.output)
    write_output_csv(output_path, rows)
    print(f"Wrote comparison CSV to {output_path} ({len(rows)} shots)", file=sys.stderr)


if __name__ == "__main__":
    main()
