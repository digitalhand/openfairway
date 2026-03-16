#!/usr/bin/env python
"""Compare physics CSV against reference CSV and write a diff CSV.

Usage:
    python tools/shot_calibration/compare_csv.py assets/data/calibration/physics.csv assets/data/calibration/fs.csv
    python tools/shot_calibration/compare_csv.py assets/data/calibration/physics.csv assets/data/calibration/fs.csv --output /tmp/shot_diff_analysis.csv
    python tools/shot_calibration/compare_csv.py assets/data/calibration/physics.csv assets/data/calibration/fs.csv --carry-exceptions assets/data/calibration/carry_exception_profile.json
"""

import argparse
import csv
import os
import sys

from carry_exception_layer import apply_carry_exceptions, build_regime_key, load_profile


SCRIPT_DIR = os.path.dirname(__file__)
DEFAULT_OUTPUT_PATH = os.path.normpath(
    os.path.join(SCRIPT_DIR, "..", "..", "assets", "data", "calibration", "shot_diff_analysis.csv")
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
    "launch_regime_key",
    "carry_window",
    "physics_carry_yd",
    "fs_carry_yd",
    "diff_carry_yd",
    "physics_carry_raw_yd",
    "diff_carry_raw_yd",
    "physics_total_yd",
    "fs_total_yd",
    "diff_total_yd",
    "rollout_physics_yd",
    "rollout_fs_yd",
    "diff_rollout_yd",
    "physics_apex_ft",
    "fs_apex_ft",
    "diff_apex_ft",
    "gsp_carry_yd",
    "gsp_total_yd",
    "gsp_apex_ft",
    "diff_gsp_carry_yd",
    "diff_gsp_total_yd",
    "diff_gsp_apex_ft",
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


def classify_carry_window(carry_yd):
    """Bucket carry distance into the same priority windows used by analysis."""
    if carry_yd is None:
        return ""
    if carry_yd < 115.0:
        return "<115"
    if carry_yd <= 150.0:
        return "115-150"
    if carry_yd <= 180.0:
        return "150-180"
    if carry_yd <= 200.0:
        return "180-200"
    return ">200"


def build_row(shot_name, physics_row, ref_row, gsp_row=None):
    speed = choose_input_value(
        physics_row.get("speed_mph"),
        ref_row.get("speed_mph"),
    )
    vla = choose_input_value(
        physics_row.get("vla_deg"),
        ref_row.get("vla_deg"),
    )
    hla = choose_input_value(
        physics_row.get("hla_deg"),
        ref_row.get("hla_deg"),
    )
    spin = choose_input_value(
        physics_row.get("total_spin_rpm"),
        ref_row.get("total_spin_rpm"),
    )
    spin_axis = choose_input_value(
        physics_row.get("spin_axis_deg"),
        ref_row.get("spin_axis_deg"),
    )
    regime_key = build_regime_key(speed, vla, spin)

    p_carry = parse_metric(physics_row.get("carry_yd"))
    f_carry = parse_metric(ref_row.get("carry_yd"))
    p_total = parse_metric(physics_row.get("total_yd"))
    f_total = parse_metric(ref_row.get("total_yd"))
    p_apex = parse_metric(physics_row.get("apex_ft"))
    f_apex = parse_metric(ref_row.get("apex_ft"))

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
        "launch_regime_key": regime_key,
        "carry_window": classify_carry_window(f_carry),
        "physics_carry_yd": fmt_decimal(p_carry, 1),
        "fs_carry_yd": fmt_decimal(f_carry, 1),
        "diff_carry_yd": fmt_decimal(diff_carry, 1),
        "physics_carry_raw_yd": fmt_decimal(p_carry, 1),
        "diff_carry_raw_yd": fmt_decimal(diff_carry, 1),
        "physics_total_yd": fmt_decimal(p_total, 1),
        "fs_total_yd": fmt_decimal(f_total, 1),
        "diff_total_yd": fmt_decimal(diff_total, 1),
        "rollout_physics_yd": fmt_decimal(p_rollout, 1),
        "rollout_fs_yd": fmt_decimal(f_rollout, 1),
        "diff_rollout_yd": fmt_decimal(diff_rollout, 1),
        "physics_apex_ft": fmt_decimal(p_apex, 1),
        "fs_apex_ft": fmt_decimal(f_apex, 1),
        "diff_apex_ft": fmt_decimal(p_apex - f_apex if p_apex is not None and f_apex is not None else None, 1),
        "gsp_carry_yd": "",
        "gsp_total_yd": "",
        "gsp_apex_ft": "",
        "diff_gsp_carry_yd": "",
        "diff_gsp_total_yd": "",
        "diff_gsp_apex_ft": "",
        "carry_exception_regime": "",
        "carry_exception_offset_yd": "",
        "carry_exception_source": "",
        "carry_exception_applied": "false",
        "status": status,
    }

    if gsp_row is not None:
        g_carry = parse_metric(gsp_row.get("carry_yd"))
        g_total = parse_metric(gsp_row.get("total_yd"))
        g_apex = parse_metric(gsp_row.get("apex_ft"))
        row["gsp_carry_yd"] = fmt_decimal(g_carry, 1)
        row["gsp_total_yd"] = fmt_decimal(g_total, 1)
        row["gsp_apex_ft"] = fmt_decimal(g_apex, 1)
        row["diff_gsp_carry_yd"] = fmt_decimal(p_carry - g_carry if p_carry is not None and g_carry is not None else None, 1)
        row["diff_gsp_total_yd"] = fmt_decimal(p_total - g_total if p_total is not None and g_total is not None else None, 1)
        row["diff_gsp_apex_ft"] = fmt_decimal(p_apex - g_apex if p_apex is not None and g_apex is not None else None, 1)

    return row


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
    parser = argparse.ArgumentParser(description="Compare physics CSV against reference CSV")
    parser.add_argument("physics_csv", help="Path to physics CSV input")
    parser.add_argument("reference_csv", help="Path to reference CSV input")
    parser.add_argument(
        "--output",
        default=DEFAULT_OUTPUT_PATH,
        help="Output path for generated comparison CSV (default: assets/data/calibration/shot_diff_analysis.csv)",
    )
    parser.add_argument(
        "--carry-exceptions",
        default=None,
        help="Optional path to carry exception profile JSON (explicit opt-in; default is disabled)",
    )
    parser.add_argument(
        "--no-carry-exceptions",
        action="store_true",
        help="Disable carry exception profile loading",
    )
    parser.add_argument(
        "--gsp-csv",
        default=None,
        help="Optional path to GSP reference CSV (carry, total, apex only)",
    )
    return parser.parse_args()


def main():
    args = parse_args()
    physics = load_csv(args.physics_csv)
    reference = load_csv(args.reference_csv)
    gsp = load_csv(args.gsp_csv) if args.gsp_csv else {}

    all_shots = sorted(set(physics.keys()) | set(reference.keys()))
    rows = [
        build_row(
            shot,
            physics.get(shot, {}),
            reference.get(shot, {}),
            gsp_row=gsp.get(shot) if gsp else None,
        )
        for shot in all_shots
    ]

    carry_profile_path = None if args.no_carry_exceptions else (os.path.normpath(args.carry_exceptions) if args.carry_exceptions else None)

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
