#!/usr/bin/env python
"""Populate missing shots in fs_reference.json files.

For each shot_session_N directory, finds shot JSON files that lack entries
in fs_reference.json and adds placeholder entries with BallData populated
but carry/total/apex = 0 and _status = "pending".

Usage:
    python tools/shot_calibration/populate_missing_references.py
    python tools/shot_calibration/populate_missing_references.py --dry-run
"""

import glob
import json
import os
import sys

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.normpath(os.path.join(SCRIPT_DIR, "..", ".."))
DATA_DIR = os.path.join(PROJECT_ROOT, "assets", "data")


def discover_session_dirs():
    """Find all shot_session_* directories."""
    pattern = os.path.join(DATA_DIR, "shot_session_*")
    return sorted(glob.glob(pattern))


def load_shot_json(path):
    """Load a shot JSON file and return BallData fields."""
    with open(path, "r") as f:
        data = json.load(f)
    ball = data.get("BallData", data)
    return {
        "speed_mph": ball.get("Speed", 0.0),
        "vla_deg": ball.get("VLA", 0.0),
        "hla_deg": ball.get("HLA", 0.0),
        "total_spin_rpm": ball.get("TotalSpin", 0.0),
        "spin_axis_deg": ball.get("SpinAxis", 0.0),
    }


def build_placeholder_entry(shot_key, ball_data):
    """Build a placeholder fs_reference entry."""
    return {
        "filename": f"{shot_key}.json",
        "speed_mph": ball_data["speed_mph"],
        "vla_deg": ball_data["vla_deg"],
        "hla_deg": ball_data["hla_deg"],
        "total_spin_rpm": ball_data["total_spin_rpm"],
        "spin_axis_deg": ball_data["spin_axis_deg"],
        "carry_yd": 0,
        "roll_yd": 0,
        "total_yd": 0,
        "lateral_yd": 0,
        "time_s": 0,
        "apex_ft": 0,
        "_status": "pending",
        "_reason": "Awaiting FS trajectory lookup",
    }


def process_session(session_dir, dry_run=False):
    """Process a single session directory. Returns count of added entries."""
    ref_path = os.path.join(session_dir, "fs_reference.json")

    # Load existing reference (or empty dict)
    if os.path.exists(ref_path):
        with open(ref_path, "r") as f:
            ref_data = json.load(f)
    else:
        ref_data = {}

    # Find all shot JSON files
    shot_files = sorted(glob.glob(os.path.join(session_dir, "shot_*.json")))
    added = []

    for shot_path in shot_files:
        basename = os.path.basename(shot_path)
        shot_key = basename.replace(".json", "")

        if shot_key in ref_data:
            continue

        ball_data = load_shot_json(shot_path)
        ref_data[shot_key] = build_placeholder_entry(shot_key, ball_data)
        added.append(shot_key)

    if added and not dry_run:
        # Write back sorted by key
        sorted_ref = dict(sorted(ref_data.items()))
        with open(ref_path, "w") as f:
            json.dump(sorted_ref, f, indent=2)
            f.write("\n")

    return added


def main():
    dry_run = "--dry-run" in sys.argv

    session_dirs = discover_session_dirs()
    total_added = 0

    for sd in session_dirs:
        session_name = os.path.basename(sd)
        added = process_session(sd, dry_run=dry_run)
        if added:
            prefix = "[DRY RUN] " if dry_run else ""
            print(f"{prefix}{session_name}: added {len(added)} entries")
            for key in added:
                print(f"  + {key}")
            total_added += len(added)
        else:
            print(f"{session_name}: no missing entries")

    mode = " (dry run)" if dry_run else ""
    print(f"\nTotal: {total_added} entries added{mode}")


if __name__ == "__main__":
    main()
