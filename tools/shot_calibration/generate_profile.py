#!/usr/bin/env python
"""Generate a calibration profile JSON from diagnostic analysis.

Reads shot_diff_analysis.csv, runs the diagnostic analyzer, and produces
a conservative profile override JSON with suggested parameter adjustments.

Usage:
    python tools/shot_calibration/generate_profile.py
    python tools/shot_calibration/generate_profile.py --base assets/data/calibration/calibration_profile.json
    python tools/shot_calibration/generate_profile.py --target bump_and_run,driver1
    python tools/shot_calibration/generate_profile.py --output /tmp/profile.json
"""

import argparse
import json
import os
import sys

SCRIPT_DIR = os.path.dirname(__file__)
DEFAULT_INPUT_PATH = os.path.normpath(
    os.path.join(SCRIPT_DIR, "..", "..", "assets", "data", "calibration", "shot_diff_analysis.csv")
)
DEFAULT_OUTPUT_PATH = os.path.normpath(
    os.path.join(SCRIPT_DIR, "..", "..", "assets", "data", "calibration", "calibration_profile.json")
)

# Import the analyzer from the same directory
sys.path.insert(0, SCRIPT_DIR)
from calibration_analyzer import load_diff_csv, analyze, PARAM_KNOWLEDGE_BASE


def load_base_profile(path):
    if not path or not os.path.exists(path):
        return {}
    with open(path, "r") as f:
        return json.load(f)


def get_current_value(base_profile, param_name):
    """Get the current value from base profile, or None if not set."""
    info = PARAM_KNOWLEDGE_BASE.get(param_name)
    if info is None:
        return None

    profile_section = info["profile"]
    key = info["key"]

    if profile_section == "Root":
        return base_profile.get(key)
    else:
        section = base_profile.get(profile_section, {})
        return section.get(key)


def set_profile_value(profile, param_name, value):
    """Set a value in the profile dict using the knowledge base structure."""
    info = PARAM_KNOWLEDGE_BASE.get(param_name)
    if info is None:
        return

    profile_section = info["profile"]
    key = info["key"]

    if profile_section == "Root":
        profile[key] = value
    else:
        if profile_section not in profile:
            profile[profile_section] = {}
        profile[profile_section][key] = value


def compute_adjustments(analysis_result, base_profile, target_shots=None):
    """Compute conservative parameter adjustments from diagnostic analysis."""
    conflicting_params = set()
    for conflict in analysis_result["conflicts"]:
        conflicting_params.add(conflict["parameter"])

    # Collect all suggestions from failing shots, grouped by parameter
    param_votes = {}
    for diag in analysis_result["diagnostics"]:
        if diag["status"] == "pass":
            continue
        if target_shots and diag["shot_name"] not in target_shots:
            continue

        for suggestion in diag.get("suggestions", []):
            param = suggestion["parameter"]
            if param in conflicting_params:
                continue
            param_votes.setdefault(param, []).append(suggestion)

    adjustments = {}
    skipped_conflicts = []

    for param, votes in param_votes.items():
        if param in conflicting_params:
            skipped_conflicts.append(param)
            continue

        # All votes should agree on direction (conflicts already filtered)
        directions = set(v["direction"] for v in votes)
        if len(directions) > 1:
            skipped_conflicts.append(param)
            continue

        direction = directions.pop()
        info = PARAM_KNOWLEDGE_BASE[param]
        step = info["step"]
        safe_min, safe_max = info["safe_range"]

        current = get_current_value(base_profile, param)
        if current is None:
            # Use the default from a fresh profile instance — approximate with midpoint
            current = (safe_min + safe_max) / 2.0

        if direction == "increase":
            new_value = min(current + step, safe_max)
        else:
            new_value = max(current - step, safe_min)

        # Round to avoid floating point noise
        new_value = round(new_value, 6)

        if abs(new_value - current) > 1e-8:
            adjustments[param] = {
                "direction": direction,
                "old_value": current,
                "new_value": new_value,
                "num_shots": len(votes),
            }

    return adjustments, skipped_conflicts


def build_profile(adjustments, base_profile):
    """Build the output profile JSON from adjustments merged onto base."""
    profile = json.loads(json.dumps(base_profile))  # deep copy

    for param_name, adj in adjustments.items():
        set_profile_value(profile, param_name, adj["new_value"])

    return profile


def parse_args():
    parser = argparse.ArgumentParser(description="Generate calibration profile from diagnostics")
    parser.add_argument(
        "--input",
        default=DEFAULT_INPUT_PATH,
        help="Path to shot_diff_analysis.csv",
    )
    parser.add_argument(
        "--base",
        default=None,
        help="Path to base profile JSON for incremental adjustments",
    )
    parser.add_argument(
        "--target",
        default=None,
        help="Comma-separated list of shot names to target (default: all failing)",
    )
    parser.add_argument(
        "--output",
        default=DEFAULT_OUTPUT_PATH,
        help="Output path for generated profile JSON",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Print adjustments without writing profile",
    )
    return parser.parse_args()


def main():
    args = parse_args()

    if not os.path.exists(args.input):
        print(f"ERROR: Input file not found: {args.input}", file=sys.stderr)
        sys.exit(1)

    rows = load_diff_csv(args.input)
    if not rows:
        print("ERROR: No data rows found in input CSV", file=sys.stderr)
        sys.exit(1)

    base_profile = load_base_profile(args.base) if args.base else {}
    target_shots = set(args.target.split(",")) if args.target else None

    analysis_result = analyze(rows)
    adjustments, skipped = compute_adjustments(analysis_result, base_profile, target_shots)

    # Report
    print(f"Analyzed {len(rows)} shots", file=sys.stderr)
    print(f"Adjustments: {len(adjustments)}, Skipped conflicts: {len(skipped)}", file=sys.stderr)

    if adjustments:
        print("\nProposed adjustments:", file=sys.stderr)
        for param, adj in sorted(adjustments.items()):
            print(
                f"  {param}: {adj['old_value']:.4f} -> {adj['new_value']:.4f} "
                f"({adj['direction']}, {adj['num_shots']} shots)",
                file=sys.stderr,
            )

    if skipped:
        print(f"\nSkipped due to conflicts: {', '.join(sorted(skipped))}", file=sys.stderr)

    if not adjustments:
        print("\nNo adjustments to make.", file=sys.stderr)
        return

    if args.dry_run:
        print("\nDry run — no profile written.", file=sys.stderr)
        return

    profile = build_profile(adjustments, base_profile)

    output_dir = os.path.dirname(args.output)
    if output_dir:
        os.makedirs(output_dir, exist_ok=True)

    with open(args.output, "w") as f:
        json.dump(profile, f, indent=2)
        f.write("\n")

    print(f"\nWrote profile to {args.output}", file=sys.stderr)


if __name__ == "__main__":
    main()
