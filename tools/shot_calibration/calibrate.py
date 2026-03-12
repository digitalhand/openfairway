#!/usr/bin/env python
"""Orchestrator for the iterative physics calibration pipeline.

Main entry point for the tune-simulate-compare loop.

Usage:
    python tools/shot_calibration/calibrate.py run
    python tools/shot_calibration/calibrate.py run --profile assets/data/calibration/calibration_profile.json
    python tools/shot_calibration/calibrate.py status
    python tools/shot_calibration/calibrate.py history
    python tools/shot_calibration/calibrate.py diff 1 3
"""

import argparse
import csv
import datetime
import json
import os
import shutil
import subprocess
import sys

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.normpath(os.path.join(SCRIPT_DIR, "..", ".."))
CALIBRATION_DIR = os.path.join(PROJECT_ROOT, "assets", "data", "calibration")
HISTORY_DIR = os.path.join(CALIBRATION_DIR, "history")
PHYSICS_CSV = os.path.join(CALIBRATION_DIR, "physics.csv")
FLIGHTSCOPE_CSV = os.path.join(CALIBRATION_DIR, "flightscope.csv")
SOT_CSV = os.path.join(PROJECT_ROOT, "assets", "data", "SOT", "flightscope_SoT.csv")
DIFF_CSV = os.path.join(CALIBRATION_DIR, "shot_diff_analysis.csv")
DEFAULT_PROFILE = os.path.join(CALIBRATION_DIR, "calibration_profile.json")

sys.path.insert(0, SCRIPT_DIR)
from calibration_analyzer import load_diff_csv, analyze, format_report


def run_command(cmd, description, cwd=None):
    """Run a shell command, printing output."""
    print(f"\n--- {description} ---")
    print(f"  $ {' '.join(cmd)}")
    result = subprocess.run(cmd, cwd=cwd or PROJECT_ROOT, capture_output=True, text=True)
    if result.stdout:
        print(result.stdout)
    if result.stderr:
        print(result.stderr, file=sys.stderr)
    if result.returncode != 0:
        print(f"ERROR: Command failed with exit code {result.returncode}", file=sys.stderr)
        return False
    return True


def find_godot():
    """Find Godot executable."""
    for name in ["godot", "godot4", "Godot_v4.5-stable_linux.x86_64"]:
        result = subprocess.run(["which", name], capture_output=True, text=True)
        if result.returncode == 0:
            return result.stdout.strip()
    return "godot"


def get_next_iteration():
    """Get the next iteration number from history."""
    os.makedirs(HISTORY_DIR, exist_ok=True)
    existing = [
        f for f in os.listdir(HISTORY_DIR)
        if f.startswith("iteration_") and f.endswith(".json")
    ]
    if not existing:
        return 1
    numbers = []
    for f in existing:
        try:
            num = int(f.replace("iteration_", "").replace(".json", ""))
            numbers.append(num)
        except ValueError:
            continue
    return max(numbers) + 1 if numbers else 1


def load_iteration(n):
    """Load a specific iteration from history."""
    path = os.path.join(HISTORY_DIR, f"iteration_{n:03d}.json")
    if not os.path.exists(path):
        return None
    with open(path, "r") as f:
        return json.load(f)


def save_iteration(iteration_num, profile_overrides, analysis_result, prev_iteration=None):
    """Save an iteration snapshot to history."""
    os.makedirs(HISTORY_DIR, exist_ok=True)

    per_shot = {}
    for diag in analysis_result["diagnostics"]:
        per_shot[diag["shot_name"]] = {
            "diff_carry_yd": diag["diff_carry_yd"],
            "diff_total_yd": diag["diff_total_yd"],
            "diff_apex_ft": diag["diff_apex_ft"],
            "status": diag["status"],
            "error_pattern": diag["error_pattern"],
        }

    # Check for regressions against previous iteration
    regressions = []
    if prev_iteration:
        prev_shots = prev_iteration.get("per_shot", {})
        for shot_name, current in per_shot.items():
            prev = prev_shots.get(shot_name)
            if prev is None:
                continue
            if prev["status"] == "pass" and current["status"] != "pass":
                regressions.append({
                    "shot": shot_name,
                    "was": prev["status"],
                    "now": current["status"],
                    "prev_total_diff": prev.get("diff_total_yd"),
                    "curr_total_diff": current.get("diff_total_yd"),
                })

    snapshot = {
        "iteration": iteration_num,
        "timestamp": datetime.datetime.now().isoformat(),
        "profile_overrides": profile_overrides,
        "summary": analysis_result["summary"],
        "per_shot": per_shot,
        "regressions": regressions,
        "conflicts": [c["parameter"] for c in analysis_result["conflicts"]],
    }

    path = os.path.join(HISTORY_DIR, f"iteration_{iteration_num:03d}.json")
    with open(path, "w") as f:
        json.dump(snapshot, f, indent=2)
        f.write("\n")

    return snapshot


def cmd_run(args):
    """Run a full calibration iteration."""
    profile_path = args.profile
    if not profile_path and os.path.exists(DEFAULT_PROFILE):
        profile_path = DEFAULT_PROFILE
        print(f"Using default profile: {profile_path}")

    profile_overrides = {}
    if profile_path and os.path.exists(profile_path):
        with open(profile_path, "r") as f:
            profile_overrides = json.load(f)

    # Step 1: Export physics CSV (requires Godot)
    godot = find_godot()
    godot_cmd = [godot, "--headless", "--script", "tools/shot_calibration/export_physics_csv.gd", "--"]
    if profile_path:
        godot_cmd.append(f"--profile={profile_path}")
    godot_cmd.append(f"--output={PHYSICS_CSV}")

    if not args.skip_godot:
        if not run_command(godot_cmd, "Exporting physics CSV (Godot headless)"):
            print("ERROR: Godot export failed. Use --skip-godot to skip if physics CSV already exists.", file=sys.stderr)
            sys.exit(1)
    else:
        print("\n--- Skipping Godot export (--skip-godot) ---")
        if not os.path.exists(PHYSICS_CSV):
            print(f"ERROR: Physics CSV not found at {PHYSICS_CSV}", file=sys.stderr)
            sys.exit(1)

    # Step 2: FlightScope reference CSV
    os.makedirs(os.path.dirname(FLIGHTSCOPE_CSV), exist_ok=True)
    if args.export_flightscope:
        # Legacy path: run export_flightscope_csv.py against flightscope_reference.json
        flightscope_cmd = [
            sys.executable, os.path.join(SCRIPT_DIR, "export_flightscope_csv.py"),
        ]
        print(f"\n--- Exporting FlightScope CSV (--export-flightscope) ---")
        result = subprocess.run(
            flightscope_cmd, cwd=PROJECT_ROOT, capture_output=True, text=True
        )
        if result.returncode != 0:
            print(f"ERROR: FlightScope export failed: {result.stderr}", file=sys.stderr)
            sys.exit(1)
        with open(FLIGHTSCOPE_CSV, "w") as f:
            f.write(result.stdout)
        print(f"  Wrote {FLIGHTSCOPE_CSV}")
    else:
        # Default: copy the manually-maintained SoT CSV
        print(f"\n--- Loading FlightScope SoT CSV ---")
        if not os.path.exists(SOT_CSV):
            print(f"ERROR: SoT CSV not found at {SOT_CSV}", file=sys.stderr)
            print("  Use --export-flightscope to fall back to export_flightscope_csv.py", file=sys.stderr)
            sys.exit(1)
        shutil.copy2(SOT_CSV, FLIGHTSCOPE_CSV)
        print(f"  Copied {SOT_CSV} -> {FLIGHTSCOPE_CSV}")

        # Print reference coverage summary
        with open(SOT_CSV, "r") as f:
            reader = csv.DictReader(f)
            total_shots = 0
            shots_with_ref = 0
            for row in reader:
                total_shots += 1
                carry = float(row.get("carry_yd", 0) or 0)
                total = float(row.get("total_yd", 0) or 0)
                if carry > 0 or total > 0:
                    shots_with_ref += 1
            missing = total_shots - shots_with_ref
            print(f"  FlightScope SoT: {shots_with_ref} of {total_shots} shots have reference data ({missing} missing)")

    # Step 3: Compare CSVs
    compare_cmd = [
        sys.executable, os.path.join(SCRIPT_DIR, "compare_csv.py"),
        PHYSICS_CSV, FLIGHTSCOPE_CSV,
        "--output", DIFF_CSV,
    ]
    if not run_command(compare_cmd, "Comparing physics vs FlightScope"):
        sys.exit(1)

    # Step 4: Run diagnostic analyzer
    print("\n--- Running diagnostic analysis ---")
    rows = load_diff_csv(DIFF_CSV)
    if not rows:
        print("ERROR: No rows in diff CSV", file=sys.stderr)
        sys.exit(1)

    analysis_result = analyze(rows)

    # Step 5: Save iteration snapshot
    iteration_num = get_next_iteration()
    prev_iteration = load_iteration(iteration_num - 1) if iteration_num > 1 else None
    snapshot = save_iteration(iteration_num, profile_overrides, analysis_result, prev_iteration)

    # Step 6: Print report
    print(format_report(analysis_result))

    if snapshot["regressions"]:
        print("\n" + "!" * 70)
        print("REGRESSIONS DETECTED")
        print("!" * 70)
        for reg in snapshot["regressions"]:
            print(
                f"  {reg['shot']}: {reg['was']} -> {reg['now']} "
                f"(total_diff: {reg['prev_total_diff']} -> {reg['curr_total_diff']})"
            )

    print(f"\nIteration {iteration_num} saved to {HISTORY_DIR}/iteration_{iteration_num:03d}.json")
    summary = analysis_result["summary"]
    print(f"Summary: {summary['pass']} pass, {summary['moderate']} moderate, {summary['severe']} severe")


def cmd_status(args):
    """Show the last iteration summary."""
    iteration_num = get_next_iteration() - 1
    if iteration_num < 1:
        print("No iterations found. Run 'calibrate.py run' first.")
        return

    snapshot = load_iteration(iteration_num)
    if snapshot is None:
        print(f"Could not load iteration {iteration_num}")
        return

    print(f"Last iteration: #{snapshot['iteration']} ({snapshot['timestamp']})")
    s = snapshot["summary"]
    print(f"  Pass:     {s.get('pass', 0)}")
    print(f"  Moderate: {s.get('moderate', 0)}")
    print(f"  Severe:   {s.get('severe', 0)}")

    if snapshot.get("regressions"):
        print(f"\n  Regressions: {len(snapshot['regressions'])}")
        for reg in snapshot["regressions"]:
            print(f"    - {reg['shot']}: {reg['was']} -> {reg['now']}")

    if snapshot.get("conflicts"):
        print(f"\n  Conflicting params: {', '.join(snapshot['conflicts'])}")

    if snapshot.get("profile_overrides"):
        print(f"\n  Profile overrides: {json.dumps(snapshot['profile_overrides'], indent=4)}")


def cmd_history(args):
    """Show all iteration summaries."""
    os.makedirs(HISTORY_DIR, exist_ok=True)
    files = sorted(
        f for f in os.listdir(HISTORY_DIR)
        if f.startswith("iteration_") and f.endswith(".json")
    )

    if not files:
        print("No iterations found. Run 'calibrate.py run' first.")
        return

    print(f"{'#':>4} {'Timestamp':<20} {'Pass':>5} {'Mod':>5} {'Sev':>5} {'Regr':>5}")
    print("-" * 50)

    for fname in files:
        path = os.path.join(HISTORY_DIR, fname)
        with open(path, "r") as f:
            snap = json.load(f)
        s = snap["summary"]
        regr = len(snap.get("regressions", []))
        ts = snap.get("timestamp", "")[:19]
        print(f"{snap['iteration']:>4} {ts:<20} {s.get('pass', 0):>5} {s.get('moderate', 0):>5} {s.get('severe', 0):>5} {regr:>5}")


def cmd_diff(args):
    """Compare two iterations side by side."""
    a = load_iteration(args.iter_a)
    b = load_iteration(args.iter_b)

    if a is None:
        print(f"ERROR: Iteration {args.iter_a} not found", file=sys.stderr)
        sys.exit(1)
    if b is None:
        print(f"ERROR: Iteration {args.iter_b} not found", file=sys.stderr)
        sys.exit(1)

    print(f"Comparing iteration {args.iter_a} vs {args.iter_b}")
    print(f"  #{args.iter_a}: {a['timestamp']}")
    print(f"  #{args.iter_b}: {b['timestamp']}")

    sa = a["summary"]
    sb = b["summary"]
    print(f"\n{'Metric':<12} {'#' + str(args.iter_a):>8} {'#' + str(args.iter_b):>8} {'Delta':>8}")
    print("-" * 40)
    for key in ["pass", "moderate", "severe"]:
        va = sa.get(key, 0)
        vb = sb.get(key, 0)
        delta = vb - va
        sign = "+" if delta > 0 else ""
        print(f"{key:<12} {va:>8} {vb:>8} {sign}{delta:>7}")

    # Per-shot changes
    all_shots = sorted(set(a.get("per_shot", {}).keys()) | set(b.get("per_shot", {}).keys()))
    changed = []
    for shot in all_shots:
        sa_shot = a.get("per_shot", {}).get(shot, {})
        sb_shot = b.get("per_shot", {}).get(shot, {})
        status_a = sa_shot.get("status", "?")
        status_b = sb_shot.get("status", "?")
        total_a = sa_shot.get("diff_total_yd")
        total_b = sb_shot.get("diff_total_yd")

        if status_a != status_b or (total_a is not None and total_b is not None and abs(total_a - total_b) > 0.5):
            changed.append((shot, status_a, status_b, total_a, total_b))

    if changed:
        print(f"\nChanged shots:")
        print(f"  {'Shot':<30} {'Status A':>10} {'Status B':>10} {'Total A':>10} {'Total B':>10}")
        print("  " + "-" * 70)
        for shot, sa_s, sb_s, ta, tb in changed:
            ta_str = f"{ta:+.1f}" if ta is not None else "?"
            tb_str = f"{tb:+.1f}" if tb is not None else "?"
            print(f"  {shot:<30} {sa_s:>10} {sb_s:>10} {ta_str:>10} {tb_str:>10}")
    else:
        print("\nNo per-shot changes detected.")


def parse_args():
    parser = argparse.ArgumentParser(description="Iterative physics calibration orchestrator")
    subparsers = parser.add_subparsers(dest="command", help="Available commands")

    run_parser = subparsers.add_parser("run", help="Run a full calibration iteration")
    run_parser.add_argument("--profile", default=None, help="Path to profile override JSON")
    run_parser.add_argument("--skip-godot", action="store_true", help="Skip Godot export (use existing physics CSV)")
    run_parser.add_argument("--export-flightscope", action="store_true", help="Run export_flightscope_csv.py instead of using SoT CSV")

    subparsers.add_parser("status", help="Show last iteration summary")
    subparsers.add_parser("history", help="Show all iteration summaries")

    diff_parser = subparsers.add_parser("diff", help="Compare two iterations")
    diff_parser.add_argument("iter_a", type=int, help="First iteration number")
    diff_parser.add_argument("iter_b", type=int, help="Second iteration number")

    return parser.parse_args()


def main():
    args = parse_args()

    if args.command is None:
        print("Usage: calibrate.py {run|status|history|diff}", file=sys.stderr)
        sys.exit(1)

    commands = {
        "run": cmd_run,
        "status": cmd_status,
        "history": cmd_history,
        "diff": cmd_diff,
    }

    commands[args.command](args)


if __name__ == "__main__":
    main()
