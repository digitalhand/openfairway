#!/usr/bin/env python
"""Export FS reference CSV from shot JSON files.

Auto-discovers all *.json shot files in assets/data/.
Merges in known reference values from fs_reference.json if available.

Usage:
    python tools/shot_calibration/export_fs_csv.py
    python tools/shot_calibration/export_fs_csv.py --reference assets/data/SOT/fs_reference.json
    python tools/shot_calibration/export_fs_csv.py > assets/data/calibration/fs.csv
"""

import argparse
import json
import os
import sys

SKIP_FILES = set()
DATA_DIR = os.path.join(os.path.dirname(__file__), "..", "..", "assets", "data")

HEADER = "shot_name,filename,speed_mph,vla_deg,hla_deg,total_spin_rpm,spin_axis_deg,backspin_rpm,sidespin_rpm,carry_yd,total_yd,rollout_yd,apex_ft"


def load_reference(path):
    """Load fs_reference.json and index by filename."""
    if not path or not os.path.exists(path):
        return {}
    with open(path, "r") as f:
        data = json.load(f)
    by_filename = {}
    for key, entry in data.items():
        fname = entry.get("filename", "")
        if fname:
            by_filename[fname] = entry
    return by_filename


def discover_shots(data_dir):
    """Find all shot JSON files, sorted by name."""
    files = []
    for fname in os.listdir(data_dir):
        if fname.endswith(".json") and fname not in SKIP_FILES:
            files.append(fname)
    files.sort()
    return files


def load_shot(data_dir, fname):
    """Load a shot JSON and extract BallData fields."""
    path = os.path.join(data_dir, fname)
    with open(path, "r") as f:
        data = json.load(f)

    ball = data.get("BallData", data)
    if "Speed" not in ball:
        return None

    return {
        "speed_mph": ball.get("Speed", 0.0),
        "vla_deg": ball.get("VLA", 0.0),
        "hla_deg": ball.get("HLA", 0.0),
        "total_spin_rpm": ball.get("TotalSpin", 0.0),
        "spin_axis_deg": ball.get("SpinAxis", 0.0),
        "backspin_rpm": ball.get("BackSpin", 0.0),
        "sidespin_rpm": ball.get("SideSpin", 0.0),
    }


def main():
    parser = argparse.ArgumentParser(description="Export FS reference CSV")
    parser.add_argument(
        "--reference",
        default=None,
        help="Path to fs_reference.json (default: assets/data/SOT/fs_reference.json)",
    )
    parser.add_argument(
        "--data-dir",
        default=None,
        help="Path to shot data directory (default: assets/data/)",
    )
    parser.add_argument(
        "--session",
        default=None,
        help="Session directory path (overrides --data-dir and --reference defaults)",
    )
    args = parser.parse_args()

    # Resolve session mode: --session sets defaults for --data-dir and --reference
    if args.session:
        session_dir = os.path.normpath(args.session)
        data_dir = os.path.normpath(args.data_dir) if args.data_dir else session_dir
        reference = args.reference if args.reference else os.path.join(session_dir, "fs_reference.json")
    else:
        data_dir = os.path.normpath(args.data_dir) if args.data_dir else os.path.normpath(DATA_DIR)
        reference = args.reference if args.reference else os.path.join(DATA_DIR, "SOT", "fs_reference.json")
    ref = load_reference(reference)
    files = discover_shots(data_dir)

    print(HEADER)

    for fname in files:
        shot = load_shot(data_dir, fname)
        if shot is None:
            print(f"# WARN: skipping non-shot file {fname}", file=sys.stderr)
            continue

        shot_name = os.path.splitext(fname)[0]

        # Merge reference values if available
        ref_entry = ref.get(fname, {})
        carry = ref_entry.get("carry_yd", 0.0)
        total = ref_entry.get("total_yd", 0.0)
        rollout = total - carry if total > 0 and carry > 0 else 0.0
        apex = ref_entry.get("apex_ft", 0.0)

        print(
            f"{shot_name},{fname},"
            f"{shot['speed_mph']:.2f},{shot['vla_deg']:.2f},{shot['hla_deg']:.2f},"
            f"{shot['total_spin_rpm']:.1f},{shot['spin_axis_deg']:.2f},"
            f"{shot['backspin_rpm']:.1f},{shot['sidespin_rpm']:.1f},"
            f"{carry:.1f},{total:.1f},{rollout:.1f},{apex:.1f}"
        )


if __name__ == "__main__":
    main()
