#!/usr/bin/env python
"""Orchestrator for the iterative physics calibration pipeline.

Main entry point for the tune-simulate-compare loop.

Usage:
    python tools/shot_calibration/calibrate.py run
    python tools/shot_calibration/calibrate.py run --profile assets/data/calibration/calibration_profile.json
    python tools/shot_calibration/calibrate.py run --carry-exceptions assets/data/calibration/carry_exception_profile.json
    python tools/shot_calibration/calibrate.py analyze
    python tools/shot_calibration/calibrate.py analyze --session assets/data/shot_session_3
    python tools/shot_calibration/calibrate.py analyze --carry-exceptions assets/data/calibration/carry_exception_profile.json
    python tools/shot_calibration/calibrate.py status
    python tools/shot_calibration/calibrate.py history
    python tools/shot_calibration/calibrate.py diff 1 3
"""

import argparse
import csv
import datetime
import json
import math
import os
import shutil
import subprocess
import sys

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.normpath(os.path.join(SCRIPT_DIR, "..", ".."))
DATA_DIR = os.path.join(PROJECT_ROOT, "assets", "data")
CALIBRATION_DIR = os.path.join(DATA_DIR, "calibration")
HISTORY_DIR = os.path.join(CALIBRATION_DIR, "history")
PHYSICS_CSV = os.path.join(CALIBRATION_DIR, "physics.csv")
FLIGHTSCOPE_CSV = os.path.join(CALIBRATION_DIR, "flightscope.csv")
SOT_CSV = os.path.join(DATA_DIR, "SOT", "flightscope_SoT.csv")
DIFF_CSV = os.path.join(CALIBRATION_DIR, "shot_diff_analysis.csv")
DEFAULT_PROFILE = os.path.join(CALIBRATION_DIR, "calibration_profile.json")

sys.path.insert(0, SCRIPT_DIR)
from calibration_analyzer import load_diff_csv, analyze, format_report


def discover_session_dirs():
    """Scan assets/data/ for shot_session_* directories."""
    sessions = []
    for entry in sorted(os.listdir(DATA_DIR)):
        if entry.startswith("shot_session_") and os.path.isdir(os.path.join(DATA_DIR, entry)):
            sessions.append(os.path.join(DATA_DIR, entry))
    return sessions


def session_prefix(session_dir):
    """Extract prefix like 's2' from 'shot_session_2'."""
    basename = os.path.basename(session_dir)
    num = basename.replace("shot_session_", "")
    return f"s{num}"


def build_dirs_spec(session_dirs):
    """Build --dirs spec string for Godot: 'res://assets/data|,res://assets/data/shot_session_2|s2,...'"""
    parts = ["res://assets/data|"]
    for sd in session_dirs:
        rel = os.path.relpath(sd, PROJECT_ROOT).replace(os.sep, "/")
        prefix = session_prefix(sd)
        parts.append(f"res://{rel}|{prefix}")
    return ",".join(parts)


def load_session_reference(session_dir):
    """Load flightscope_reference.json from a session directory. Returns dict keyed by shot key."""
    ref_path = os.path.join(session_dir, "flightscope_reference.json")
    if not os.path.exists(ref_path):
        return {}
    with open(ref_path, "r") as f:
        return json.load(f)


def build_merged_flightscope_csv(sot_csv, session_dirs, output_path):
    """Merge SoT CSV rows with session flightscope_reference.json entries into a combined CSV.

    Session shots get prefixed names (e.g., s2_shot_10). BackSpin/SideSpin are read from
    the shot JSON files since the reference JSON doesn't contain them.
    """
    rows = []

    # Read standard SoT rows
    if os.path.exists(sot_csv):
        with open(sot_csv, "r") as f:
            reader = csv.DictReader(f)
            for row in reader:
                rows.append(row)

    # Process each session's reference data
    for sd in session_dirs:
        ref_data = load_session_reference(sd)
        prefix = session_prefix(sd)

        for shot_key, entry in sorted(ref_data.items()):
            fname = entry.get("filename", f"{shot_key}.json")
            shot_path = os.path.join(sd, fname)

            # Read backspin/sidespin from the shot JSON
            backspin = 0.0
            sidespin = 0.0
            if os.path.exists(shot_path):
                with open(shot_path, "r") as f:
                    shot_data = json.load(f)
                ball = shot_data.get("BallData", shot_data)
                backspin = ball.get("BackSpin", 0.0)
                sidespin = ball.get("SideSpin", 0.0)

            carry = entry.get("carry_yd", 0.0)
            total = entry.get("total_yd", 0.0)
            rollout = total - carry if total > 0 and carry > 0 else 0.0

            rows.append({
                "shot_name": f"{prefix}_{shot_key}",
                "filename": fname,
                "speed_mph": f"{entry.get('speed_mph', 0.0):.2f}",
                "vla_deg": f"{entry.get('vla_deg', 0.0):.2f}",
                "hla_deg": f"{entry.get('hla_deg', 0.0):.2f}",
                "total_spin_rpm": f"{entry.get('total_spin_rpm', 0.0):.1f}",
                "spin_axis_deg": f"{entry.get('spin_axis_deg', 0.0):.2f}",
                "backspin_rpm": f"{backspin:.1f}",
                "sidespin_rpm": f"{sidespin:.1f}",
                "carry_yd": f"{carry:.1f}",
                "total_yd": f"{total:.1f}",
                "rollout_yd": f"{rollout:.1f}",
                "apex_ft": f"{entry.get('apex_ft', 0.0):.1f}",
            })

    # Write combined CSV
    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    fieldnames = ["shot_name", "filename", "speed_mph", "vla_deg", "hla_deg",
                  "total_spin_rpm", "spin_axis_deg", "backspin_rpm", "sidespin_rpm",
                  "carry_yd", "total_yd", "rollout_yd", "apex_ft"]
    with open(output_path, "w", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames, extrasaction="ignore")
        writer.writeheader()
        for row in rows:
            writer.writerow(row)

    return rows


def filter_physics_csv(physics_csv, reference_shot_names):
    """Remove rows from physics CSV whose shot_name is not in the reference set."""
    if not os.path.exists(physics_csv):
        return
    with open(physics_csv, "r") as f:
        reader = csv.DictReader(f)
        fieldnames = reader.fieldnames
        rows = [row for row in reader if row["shot_name"] in reference_shot_names]

    with open(physics_csv, "w", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)


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


def resolve_carry_exception_profile(args):
    """Resolve optional carry-exception profile path from CLI args only."""
    if getattr(args, "no_carry_exceptions", False):
        return None

    explicit = getattr(args, "carry_exceptions", None)
    if explicit:
        return os.path.normpath(os.path.join(PROJECT_ROOT, explicit)) if not os.path.isabs(explicit) else os.path.normpath(explicit)

    return None


def find_godot():
    """Find Godot executable."""
    for name in ["godot", "godot4", "Godot_v4.5-stable_linux.x86_64"]:
        result = subprocess.run(["which", name], capture_output=True, text=True)
        if result.returncode == 0:
            return result.stdout.strip()
    return "godot"


def _get_next_iteration(history_dir):
    """Get the next iteration number from history."""
    os.makedirs(history_dir, exist_ok=True)
    existing = [
        f for f in os.listdir(history_dir)
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


def get_next_iteration():
    return _get_next_iteration(HISTORY_DIR)


def _load_iteration(history_dir, n):
    """Load a specific iteration from history."""
    path = os.path.join(history_dir, f"iteration_{n:03d}.json")
    if not os.path.exists(path):
        return None
    with open(path, "r") as f:
        return json.load(f)


def load_iteration(n):
    return _load_iteration(HISTORY_DIR, n)


def _save_iteration(history_dir, iteration_num, profile_overrides, analysis_result, prev_iteration=None):
    """Save an iteration snapshot to history."""
    os.makedirs(history_dir, exist_ok=True)

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

    path = os.path.join(history_dir, f"iteration_{iteration_num:03d}.json")
    with open(path, "w") as f:
        json.dump(snapshot, f, indent=2)
        f.write("\n")

    return snapshot


def save_iteration(iteration_num, profile_overrides, analysis_result, prev_iteration=None):
    return _save_iteration(HISTORY_DIR, iteration_num, profile_overrides, analysis_result, prev_iteration)


def cmd_run(args):
    """Run a full calibration iteration."""
    # Override all path constants when --session is provided
    session_dir = None
    physics_csv = PHYSICS_CSV
    flightscope_csv = FLIGHTSCOPE_CSV
    diff_csv = DIFF_CSV
    history_dir = HISTORY_DIR

    if args.session:
        session_dir = os.path.normpath(os.path.join(PROJECT_ROOT, args.session)) if not os.path.isabs(args.session) else os.path.normpath(args.session)
        physics_csv = os.path.join(session_dir, "physics.csv")
        flightscope_csv = os.path.join(session_dir, "flightscope.csv")
        diff_csv = os.path.join(session_dir, "shot_diff_analysis.csv")
        history_dir = os.path.join(session_dir, "history")
        print(f"Session: {session_dir}")

    profile_path = args.profile
    if not profile_path and os.path.exists(DEFAULT_PROFILE):
        profile_path = DEFAULT_PROFILE
        print(f"Using default profile: {profile_path}")

    profile_overrides = {}
    if profile_path and os.path.exists(profile_path):
        with open(profile_path, "r") as f:
            profile_overrides = json.load(f)

    carry_exception_path = resolve_carry_exception_profile(args)
    if carry_exception_path:
        print(f"Using carry exception profile: {carry_exception_path}")

    # Discover session directories (default: include all sessions)
    session_dirs = []
    if not session_dir and not args.no_sessions:
        session_dirs = discover_session_dirs()
        if session_dirs:
            prefixes = [session_prefix(sd) for sd in session_dirs]
            print(f"Including {len(session_dirs)} session(s): {', '.join(prefixes)}")

    # Step 1: Export physics CSV (requires Godot)
    godot = find_godot()
    godot_cmd = [godot, "--headless", "--script", "tools/shot_calibration/export_physics_csv.gd", "--"]
    if profile_path:
        godot_cmd.append(f"--profile={profile_path}")
    if session_dir:
        godot_cmd.append(f"--session={session_dir}")
    elif session_dirs:
        godot_cmd.append(f"--dirs={build_dirs_spec(session_dirs)}")
    godot_cmd.append(f"--output={physics_csv}")

    if not args.skip_godot:
        if not run_command(godot_cmd, "Exporting physics CSV (Godot headless)"):
            print("ERROR: Godot export failed. Use --skip-godot to skip if physics CSV already exists.", file=sys.stderr)
            sys.exit(1)
    else:
        print("\n--- Skipping Godot export (--skip-godot) ---")
        if not os.path.exists(physics_csv):
            print(f"ERROR: Physics CSV not found at {physics_csv}", file=sys.stderr)
            sys.exit(1)

    # Step 2: FlightScope reference CSV
    os.makedirs(os.path.dirname(flightscope_csv), exist_ok=True)
    if session_dir:
        # Scrape FlightScope for session shots, then export CSV
        print(f"\n--- Generating FlightScope reference for session ---")
        scraper_cmd = [
            sys.executable, os.path.join(SCRIPT_DIR, "flightscope_scraper.py"),
            "--session", session_dir,
        ]
        if not run_command(scraper_cmd, "Scraping FlightScope for session shots"):
            print("WARNING: FlightScope scraper failed. Attempting export from existing reference.", file=sys.stderr)

        export_cmd = [
            sys.executable, os.path.join(SCRIPT_DIR, "export_flightscope_csv.py"),
            "--session", session_dir,
        ]
        result = subprocess.run(
            export_cmd, cwd=PROJECT_ROOT, capture_output=True, text=True
        )
        if result.returncode != 0:
            print(f"ERROR: FlightScope CSV export failed: {result.stderr}", file=sys.stderr)
            sys.exit(1)
        with open(flightscope_csv, "w") as f:
            f.write(result.stdout)
        print(f"  Wrote {flightscope_csv}")
    elif args.export_flightscope:
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
        with open(flightscope_csv, "w") as f:
            f.write(result.stdout)
        print(f"  Wrote {flightscope_csv}")
    else:
        # Default: SoT CSV + session references merged
        print(f"\n--- Loading FlightScope reference data ---")
        if not os.path.exists(SOT_CSV):
            print(f"ERROR: SoT CSV not found at {SOT_CSV}", file=sys.stderr)
            print("  Use --export-flightscope to fall back to export_flightscope_csv.py", file=sys.stderr)
            sys.exit(1)

        if session_dirs:
            merged_rows = build_merged_flightscope_csv(SOT_CSV, session_dirs, flightscope_csv)
            # Count standard vs session shots
            sot_count = 0
            session_count = 0
            for row in merged_rows:
                carry = float(row.get("carry_yd", 0) or 0)
                total = float(row.get("total_yd", 0) or 0)
                if carry > 0 or total > 0:
                    if "_" in row["shot_name"] and row["shot_name"].split("_")[0].startswith("s"):
                        session_count += 1
                    else:
                        sot_count += 1
            print(f"  Merged FlightScope CSV: {sot_count} standard + {session_count} session shots")
            print(f"  Wrote {flightscope_csv}")
        else:
            shutil.copy2(SOT_CSV, flightscope_csv)
            print(f"  Copied {SOT_CSV} -> {flightscope_csv}")

        # Print reference coverage summary
        with open(flightscope_csv, "r") as f:
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
            print(f"  FlightScope reference: {shots_with_ref} of {total_shots} shots have reference data ({missing} missing)")

    # Step 2b: Filter physics CSV to only include shots with FlightScope reference data
    if session_dirs and not session_dir:
        with open(flightscope_csv, "r") as f:
            reader = csv.DictReader(f)
            ref_names = {row["shot_name"] for row in reader}
        filter_physics_csv(physics_csv, ref_names)
        print(f"  Filtered physics CSV to {len(ref_names)} referenced shots")

    # Step 3: Compare CSVs
    compare_cmd = [
        sys.executable, os.path.join(SCRIPT_DIR, "compare_csv.py"),
        physics_csv, flightscope_csv,
        "--output", diff_csv,
    ]
    if carry_exception_path:
        compare_cmd.extend(["--carry-exceptions", carry_exception_path])
    elif args.no_carry_exceptions:
        compare_cmd.append("--no-carry-exceptions")
    if not run_command(compare_cmd, "Comparing physics vs FlightScope"):
        sys.exit(1)

    # Step 4: Run diagnostic analyzer
    print("\n--- Running diagnostic analysis ---")
    rows = load_diff_csv(diff_csv)
    if not rows:
        print("ERROR: No rows in diff CSV", file=sys.stderr)
        sys.exit(1)

    analysis_result = analyze(rows)

    # Step 5: Save iteration snapshot (use session-local history dir)
    os.makedirs(history_dir, exist_ok=True)
    iteration_num = _get_next_iteration(history_dir)
    prev_iteration = _load_iteration(history_dir, iteration_num - 1) if iteration_num > 1 else None
    snapshot = _save_iteration(history_dir, iteration_num, profile_overrides, analysis_result, prev_iteration)

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

    print(f"\nIteration {iteration_num} saved to {history_dir}/iteration_{iteration_num:03d}.json")
    summary = analysis_result["summary"]
    print(f"Summary: {summary['pass']} pass, {summary['moderate']} moderate, {summary['severe']} severe")


def _compute_accuracy_stats(diffs, thresholds):
    """Compute accuracy statistics from a list of diff values.

    Args:
        diffs: List of numeric diff values (physics - reference).
        thresholds: List of threshold values for within-X percentages.

    Returns:
        Dict with avg_error, avg_off, typical_off, consistency, worst_off,
        and within_pct (dict mapping threshold to percentage).
    """
    if not diffs:
        return {}

    n = len(diffs)
    abs_diffs = [abs(d) for d in diffs]
    mean_error = sum(diffs) / n
    mean_abs = sum(abs_diffs) / n
    sorted_abs = sorted(abs_diffs)
    if n % 2 == 1:
        median_abs = sorted_abs[n // 2]
    else:
        median_abs = (sorted_abs[n // 2 - 1] + sorted_abs[n // 2]) / 2
    variance = sum((d - mean_error) ** 2 for d in diffs) / n
    std_dev = math.sqrt(variance)
    max_abs = max(abs_diffs)

    within_pct = {}
    for threshold in thresholds:
        count = sum(1 for ad in abs_diffs if ad <= threshold)
        within_pct[str(threshold)] = round(count / n * 100, 1)

    return {
        "avg_error": round(mean_error, 1),
        "avg_off": round(mean_abs, 1),
        "typical_off": round(median_abs, 1),
        "consistency": round(std_dev, 1),
        "worst_off": round(max_abs, 1),
        "within_pct": within_pct,
    }


def _safe_float(value):
    try:
        return float(value)
    except (TypeError, ValueError):
        return None


def _get_numeric(row, primary_key, fallback_key=None):
    value = _safe_float(row.get(primary_key))
    if value is not None or fallback_key is None:
        return value
    return _safe_float(row.get(fallback_key))


def _percentage(count, total):
    if total <= 0:
        return 0.0
    return round(count / total * 100, 1)


def _window_target_abs_yd(carry_yd):
    if carry_yd is None:
        return 3.0
    if carry_yd < 115.0:
        return 1.0
    if carry_yd <= 150.0:
        return 3.0
    if carry_yd <= 180.0:
        return 6.0
    if carry_yd <= 200.0:
        return 10.0
    return 15.0


def _resolve_critical_baseline(args, output_dir):
    explicit = getattr(args, "critical_baseline", None)
    if explicit:
        return os.path.normpath(os.path.join(PROJECT_ROOT, explicit)) if not os.path.isabs(explicit) else os.path.normpath(explicit)

    candidates = sorted(
        os.path.join(output_dir, name)
        for name in os.listdir(output_dir)
        if name.startswith("openfairway_critical_carry_") and name.endswith(".csv")
    )
    return candidates[-1] if candidates else None


def _summarize_baseline_comparison(ref_rows, baseline_path):
    if not baseline_path or not os.path.exists(baseline_path):
        return None

    current_by_name = {row["shot_name"]: row for row in ref_rows if row.get("shot_name")}
    with open(baseline_path, "r") as f:
        baseline_rows = [row for row in csv.DictReader(f) if row.get("shot_name")]

    improved = 0
    regressed = 0
    unchanged = 0
    within_target_now = 0
    missing = []
    remaining = []

    for baseline_row in baseline_rows:
        shot_name = baseline_row["shot_name"]
        current_row = current_by_name.get(shot_name)
        if current_row is None:
            missing.append(shot_name)
            continue

        prev_abs = abs(_get_numeric(baseline_row, "diff_carry_raw_yd", "diff_carry_yd") or 0.0)
        curr_abs = abs(_get_numeric(current_row, "diff_carry_raw_yd", "diff_carry_yd") or 0.0)
        carry_yd = _get_numeric(current_row, "flightscope_carry_yd")
        if carry_yd is None:
            carry_yd = _get_numeric(baseline_row, "flightscope_carry_yd")
        target_abs = _window_target_abs_yd(carry_yd)

        if curr_abs < prev_abs - 0.05:
            improved += 1
        elif curr_abs > prev_abs + 0.05:
            regressed += 1
        else:
            unchanged += 1

        if curr_abs <= target_abs:
            within_target_now += 1

        remaining.append({
            "shot_name": shot_name,
            "current_abs_yd": round(curr_abs, 1),
            "baseline_abs_yd": round(prev_abs, 1),
            "target_abs_yd": round(target_abs, 1),
            "carry_window": current_row.get("carry_window") or baseline_row.get("carry_window") or "",
        })

    remaining.sort(key=lambda row: row["current_abs_yd"], reverse=True)

    tracked = len(baseline_rows) - len(missing)
    return {
        "path": baseline_path,
        "shots": len(baseline_rows),
        "tracked_shots": tracked,
        "missing_shots": missing,
        "improved": improved,
        "regressed": regressed,
        "unchanged": unchanged,
        "within_target_now": within_target_now,
        "within_target_now_pct": _percentage(within_target_now, tracked),
        "top_remaining_outliers": remaining[:10],
    }


def _generate_accuracy_reports(diff_csv, output_dir, top_n=20, critical_baseline=None):
    """Read shot_diff_analysis.csv and generate accuracy report files.

    Returns tuple of (list_of_paths_written, full_summary_dict).
    """
    with open(diff_csv, "r") as f:
        reader = csv.DictReader(f)
        all_rows = list(reader)

    # Filter to shots with reference data (non-zero flightscope carry or total)
    ref_rows = [
        r for r in all_rows
        if float(r.get("flightscope_carry_yd", 0) or 0) > 0
        or float(r.get("flightscope_total_yd", 0) or 0) > 0
    ]

    carry_diffs = [_get_numeric(r, "diff_carry_yd") for r in ref_rows]
    carry_diffs = [d for d in carry_diffs if d is not None]
    carry_raw_diffs = [_get_numeric(r, "diff_carry_raw_yd", "diff_carry_yd") for r in ref_rows]
    carry_raw_diffs = [d for d in carry_raw_diffs if d is not None]
    total_diffs = [float(r["diff_total_yd"]) for r in ref_rows if r.get("diff_total_yd")]
    apex_diffs = [float(r["diff_apex_ft"]) for r in ref_rows if r.get("diff_apex_ft")]

    short_rows = [
        r for r in ref_rows
        if _safe_float(r.get("flightscope_carry_yd")) is not None
        and _safe_float(r.get("flightscope_carry_yd")) < 115.0
    ]
    mid_115_150_rows = [
        r for r in ref_rows
        if _safe_float(r.get("flightscope_carry_yd")) is not None
        and _safe_float(r.get("flightscope_carry_yd")) > 115.0
        and _safe_float(r.get("flightscope_carry_yd")) <= 150.0
    ]
    mid_150_180_rows = [
        r for r in ref_rows
        if _safe_float(r.get("flightscope_carry_yd")) is not None
        and _safe_float(r.get("flightscope_carry_yd")) > 150.0
        and _safe_float(r.get("flightscope_carry_yd")) <= 180.0
    ]
    long_rows = [
        r for r in ref_rows
        if _safe_float(r.get("flightscope_carry_yd")) is not None
        and _safe_float(r.get("flightscope_carry_yd")) > 200.0
    ]

    timestamp = datetime.datetime.now().strftime("%Y%m%d_%H%M")
    os.makedirs(output_dir, exist_ok=True)
    written = []

    # --- 1. Full accuracy summary (carry + total + apex) ---
    _YD_THRESHOLDS = [1, 2, 3, 5, 7, 10, 15, 20]
    _FT_THRESHOLDS = [1, 2, 3, 5, 7, 10, 13, 15, 20, 50]

    carry_stats = _compute_accuracy_stats(carry_diffs, _YD_THRESHOLDS)
    carry_accuracy = {f"{k}_yd": v for k, v in carry_stats.items() if k != "within_pct"}
    carry_accuracy["within_pct_yd"] = carry_stats.get("within_pct", {})

    total_stats = _compute_accuracy_stats(total_diffs, _YD_THRESHOLDS)
    total_accuracy = {f"{k}_yd": v for k, v in total_stats.items() if k != "within_pct"}
    total_accuracy["within_pct_yd"] = total_stats.get("within_pct", {})

    apex_stats = _compute_accuracy_stats(apex_diffs, _FT_THRESHOLDS)
    apex_accuracy = {f"{k}_ft": v for k, v in apex_stats.items() if k != "within_pct"}
    apex_accuracy["within_pct_ft"] = apex_stats.get("within_pct", {})

    short_carry_diffs = [_get_numeric(r, "diff_carry_yd") for r in short_rows]
    short_carry_diffs = [d for d in short_carry_diffs if d is not None]
    short_stats = _compute_accuracy_stats(short_carry_diffs, [0.5, 1, 2, 3])
    short_accuracy = {f"{k}_yd": v for k, v in short_stats.items() if k != "within_pct"}
    short_accuracy["within_pct_yd"] = short_stats.get("within_pct", {})

    mid_115_150_diffs = [_get_numeric(r, "diff_carry_yd") for r in mid_115_150_rows]
    mid_115_150_diffs = [d for d in mid_115_150_diffs if d is not None]
    mid_115_150_stats = _compute_accuracy_stats(mid_115_150_diffs, [1, 2, 3, 5])
    mid_115_150_accuracy = {f"{k}_yd": v for k, v in mid_115_150_stats.items() if k != "within_pct"}
    mid_115_150_accuracy["within_pct_yd"] = mid_115_150_stats.get("within_pct", {})

    mid_150_180_diffs = [_get_numeric(r, "diff_carry_yd") for r in mid_150_180_rows]
    mid_150_180_diffs = [d for d in mid_150_180_diffs if d is not None]
    mid_150_180_stats = _compute_accuracy_stats(mid_150_180_diffs, [3, 5, 6, 7, 10])
    mid_150_180_accuracy = {f"{k}_yd": v for k, v in mid_150_180_stats.items() if k != "within_pct"}
    mid_150_180_accuracy["within_pct_yd"] = mid_150_180_stats.get("within_pct", {})

    long_carry_diffs = [_get_numeric(r, "diff_carry_yd") for r in long_rows]
    long_carry_diffs = [d for d in long_carry_diffs if d is not None]
    long_stats = _compute_accuracy_stats(long_carry_diffs, [3, 5, 7, 10, 15])
    long_accuracy = {f"{k}_yd": v for k, v in long_stats.items() if k != "within_pct"}
    long_accuracy["within_pct_yd"] = long_stats.get("within_pct", {})

    short_raw_diffs = [_get_numeric(r, "diff_carry_raw_yd", "diff_carry_yd") for r in short_rows]
    short_raw_diffs = [d for d in short_raw_diffs if d is not None]
    short_raw_stats = _compute_accuracy_stats(short_raw_diffs, [0.5, 1, 2, 3])
    short_physics_accuracy = {f"{k}_yd": v for k, v in short_raw_stats.items() if k != "within_pct"}
    short_physics_accuracy["within_pct_yd"] = short_raw_stats.get("within_pct", {})

    mid_115_150_raw_diffs = [_get_numeric(r, "diff_carry_raw_yd", "diff_carry_yd") for r in mid_115_150_rows]
    mid_115_150_raw_diffs = [d for d in mid_115_150_raw_diffs if d is not None]
    mid_115_150_raw_stats = _compute_accuracy_stats(mid_115_150_raw_diffs, [1, 2, 3, 5])
    mid_115_150_physics = {f"{k}_yd": v for k, v in mid_115_150_raw_stats.items() if k != "within_pct"}
    mid_115_150_physics["within_pct_yd"] = mid_115_150_raw_stats.get("within_pct", {})

    mid_150_180_raw_diffs = [_get_numeric(r, "diff_carry_raw_yd", "diff_carry_yd") for r in mid_150_180_rows]
    mid_150_180_raw_diffs = [d for d in mid_150_180_raw_diffs if d is not None]
    mid_150_180_raw_stats = _compute_accuracy_stats(mid_150_180_raw_diffs, [3, 5, 6, 7, 10])
    mid_150_180_physics = {f"{k}_yd": v for k, v in mid_150_180_raw_stats.items() if k != "within_pct"}
    mid_150_180_physics["within_pct_yd"] = mid_150_180_raw_stats.get("within_pct", {})

    long_raw_diffs = [_get_numeric(r, "diff_carry_raw_yd", "diff_carry_yd") for r in long_rows]
    long_raw_diffs = [d for d in long_raw_diffs if d is not None]
    long_raw_stats = _compute_accuracy_stats(long_raw_diffs, [3, 5, 7, 10, 15])
    long_physics_accuracy = {f"{k}_yd": v for k, v in long_raw_stats.items() if k != "within_pct"}
    long_physics_accuracy["within_pct_yd"] = long_raw_stats.get("within_pct", {})

    physics_within_3 = sum(1 for d in carry_raw_diffs if abs(d) <= 3.0)
    residual_candidates = []
    short_residual = 0
    for row in ref_rows:
        raw_diff = _get_numeric(row, "diff_carry_raw_yd", "diff_carry_yd")
        carry_yd = _safe_float(row.get("flightscope_carry_yd"))
        if raw_diff is None:
            continue
        target_abs = _window_target_abs_yd(carry_yd)
        if abs(raw_diff) > target_abs:
            residual_candidates.append(row)
            if carry_yd is not None and carry_yd < 115.0:
                short_residual += 1

    full_summary = {
        "timestamp": datetime.datetime.now().strftime("%Y-%m-%dT%H:%M"),
        "total_shots": len(all_rows),
        "shots_with_reference": len(ref_rows),
        "priority_gates": {
            "short_carry_lt_115yd": {
                "shots": len(short_rows),
                "carry_accuracy": short_accuracy,
            },
            "carry_115_150yd": {
                "shots": len(mid_115_150_rows),
                "carry_accuracy": mid_115_150_accuracy,
            },
            "carry_150_180yd": {
                "shots": len(mid_150_180_rows),
                "carry_accuracy": mid_150_180_accuracy,
            },
            "long_carry_gt_200yd": {
                "shots": len(long_rows),
                "carry_accuracy": long_accuracy,
            },
        },
        "carry_accuracy": carry_accuracy,
        "total_accuracy": total_accuracy,
        "apex_accuracy": apex_accuracy,
        "physics_only_priority_gates": {
            "short_carry_lt_115yd": {
                "shots": len(short_rows),
                "carry_accuracy": short_physics_accuracy,
            },
            "carry_115_150yd": {
                "shots": len(mid_115_150_rows),
                "carry_accuracy": mid_115_150_physics,
            },
            "carry_150_180yd": {
                "shots": len(mid_150_180_rows),
                "carry_accuracy": mid_150_180_physics,
            },
            "long_carry_gt_200yd": {
                "shots": len(long_rows),
                "carry_accuracy": long_physics_accuracy,
            },
        },
        "objectives": {
            "physics_only": {
                "shots": len(carry_raw_diffs),
                "within_3yd_pct": _percentage(physics_within_3, len(carry_raw_diffs)),
                "residual_outliers_gt_window_target": len(residual_candidates),
            },
            "short_shot_priority": {
                "shots": len(short_rows),
                "goal_within_1yd_pct": 80.0,
                "actual_within_1yd_pct": short_raw_stats.get("within_pct", {}).get("1", 0.0),
                "actual_within_0.5yd_pct": short_raw_stats.get("within_pct", {}).get("0.5", 0.0),
                "actual_within_3yd_pct": short_raw_stats.get("within_pct", {}).get("3", 0.0),
            },
            "residual_regime_candidates": {
                "count": len(residual_candidates),
                "short_count": short_residual,
                "by_window": {
                    "<115": sum(1 for row in residual_candidates if _safe_float(row.get("flightscope_carry_yd")) is not None and _safe_float(row.get("flightscope_carry_yd")) < 115.0),
                    "115-150": sum(1 for row in residual_candidates if _safe_float(row.get("flightscope_carry_yd")) is not None and 115.0 < _safe_float(row.get("flightscope_carry_yd")) <= 150.0),
                    "150-180": sum(1 for row in residual_candidates if _safe_float(row.get("flightscope_carry_yd")) is not None and 150.0 < _safe_float(row.get("flightscope_carry_yd")) <= 180.0),
                    "180-200": sum(1 for row in residual_candidates if _safe_float(row.get("flightscope_carry_yd")) is not None and 180.0 < _safe_float(row.get("flightscope_carry_yd")) <= 200.0),
                    ">200": sum(1 for row in residual_candidates if _safe_float(row.get("flightscope_carry_yd")) is not None and _safe_float(row.get("flightscope_carry_yd")) > 200.0),
                },
            },
        },
    }
    baseline_summary = _summarize_baseline_comparison(ref_rows, critical_baseline)
    if baseline_summary is not None:
        full_summary["critical_baseline"] = baseline_summary

    full_path = os.path.join(output_dir, f"openfairway_accuracy_summary_{timestamp}.json")
    with open(full_path, "w") as f:
        json.dump(full_summary, f, indent=2)
        f.write("\n")
    written.append(full_path)

    # --- 2. Critical carry CSV (top N by |raw diff_carry_yd|) ---
    sorted_by_carry = sorted(
        ref_rows,
        key=lambda r: abs(_get_numeric(r, "diff_carry_raw_yd", "diff_carry_yd") or 0.0),
        reverse=True,
    )[:top_n]
    carry_csv_path = os.path.join(output_dir, f"openfairway_critical_carry_{timestamp}.csv")
    if sorted_by_carry:
        fieldnames = list(all_rows[0].keys())
        with open(carry_csv_path, "w", newline="") as f:
            writer = csv.DictWriter(f, fieldnames=fieldnames)
            writer.writeheader()
            writer.writerows(sorted_by_carry)
    written.append(carry_csv_path)

    # --- 3. Critical overall CSV (top 20 by max(|diff_carry|, |diff_total|)) ---
    sorted_by_overall = sorted(
        ref_rows,
        key=lambda r: max(
            abs(float(r.get("diff_carry_yd", 0) or 0)),
            abs(float(r.get("diff_total_yd", 0) or 0)),
        ),
        reverse=True,
    )[:top_n]
    overall_csv_path = os.path.join(output_dir, f"openfairway_critical_overall_{timestamp}.csv")
    if sorted_by_overall:
        fieldnames = list(all_rows[0].keys())
        with open(overall_csv_path, "w", newline="") as f:
            writer = csv.DictWriter(f, fieldnames=fieldnames)
            writer.writeheader()
            writer.writerows(sorted_by_overall)
    written.append(overall_csv_path)

    return written, full_summary


def cmd_analyze(args):
    """Post-scrape analysis: compare, diagnose, generate accuracy reports, save iteration."""
    # Resolve paths based on --session flag
    session_dir = None
    physics_csv = PHYSICS_CSV
    flightscope_csv = FLIGHTSCOPE_CSV
    diff_csv = DIFF_CSV
    history_dir = HISTORY_DIR
    report_output_dir = DATA_DIR

    if args.session:
        session_dir = (
            os.path.normpath(os.path.join(PROJECT_ROOT, args.session))
            if not os.path.isabs(args.session)
            else os.path.normpath(args.session)
        )
        physics_csv = os.path.join(session_dir, "physics.csv")
        flightscope_csv = os.path.join(session_dir, "flightscope.csv")
        diff_csv = os.path.join(session_dir, "shot_diff_analysis.csv")
        history_dir = os.path.join(session_dir, "history")
        print(f"Session: {session_dir}")

    carry_exception_path = resolve_carry_exception_profile(args)
    if carry_exception_path:
        print(f"Using carry exception profile: {carry_exception_path}")

    # Discover session directories (default: include all sessions)
    session_dirs = []
    if not session_dir and not args.no_sessions:
        session_dirs = discover_session_dirs()
        if session_dirs:
            prefixes = [session_prefix(sd) for sd in session_dirs]
            print(f"Including {len(session_dirs)} session(s): {', '.join(prefixes)}")

    # Validate physics.csv exists
    if not os.path.exists(physics_csv):
        print(f"ERROR: Physics CSV not found at {physics_csv}", file=sys.stderr)
        print("  Run 'calibrate.py run' or Godot export first.", file=sys.stderr)
        sys.exit(1)

    # Step 1: FlightScope CSV (optional re-export, or validate existence)
    if args.flightscope_export:
        if session_dir:
            export_cmd = [
                sys.executable, os.path.join(SCRIPT_DIR, "export_flightscope_csv.py"),
                "--session", session_dir,
            ]
            result = subprocess.run(
                export_cmd, cwd=PROJECT_ROOT, capture_output=True, text=True
            )
            if result.returncode != 0:
                print(f"ERROR: FlightScope CSV export failed: {result.stderr}", file=sys.stderr)
                sys.exit(1)
            os.makedirs(os.path.dirname(flightscope_csv), exist_ok=True)
            with open(flightscope_csv, "w") as f:
                f.write(result.stdout)
            print(f"  Exported FlightScope CSV -> {flightscope_csv}")
        elif session_dirs:
            merged_rows = build_merged_flightscope_csv(SOT_CSV, session_dirs, flightscope_csv)
            print(f"  Merged FlightScope CSV: {len(merged_rows)} rows -> {flightscope_csv}")
        else:
            shutil.copy2(SOT_CSV, flightscope_csv)
            print(f"  Copied {SOT_CSV} -> {flightscope_csv}")
    else:
        if not os.path.exists(flightscope_csv):
            print(f"ERROR: FlightScope CSV not found at {flightscope_csv}", file=sys.stderr)
            print("  Use --flightscope-export to generate it, or run scraper first.", file=sys.stderr)
            sys.exit(1)

    # Filter physics CSV to only include shots with FlightScope reference
    if session_dirs and not session_dir:
        with open(flightscope_csv, "r") as f:
            reader = csv.DictReader(f)
            ref_names = {row["shot_name"] for row in reader}
        filter_physics_csv(physics_csv, ref_names)
        print(f"  Filtered physics CSV to {len(ref_names)} referenced shots")

    # Step 2: Compare physics vs FlightScope -> shot_diff_analysis.csv
    compare_cmd = [
        sys.executable, os.path.join(SCRIPT_DIR, "compare_csv.py"),
        physics_csv, flightscope_csv,
        "--output", diff_csv,
    ]
    if carry_exception_path:
        compare_cmd.extend(["--carry-exceptions", carry_exception_path])
    elif args.no_carry_exceptions:
        compare_cmd.append("--no-carry-exceptions")
    if not run_command(compare_cmd, "Comparing physics vs FlightScope"):
        sys.exit(1)

    # Step 3: Run diagnostic analyzer
    print("\n--- Running diagnostic analysis ---")
    rows = load_diff_csv(diff_csv)
    if not rows:
        print("ERROR: No rows in diff CSV", file=sys.stderr)
        sys.exit(1)

    analysis_result = analyze(rows)
    print(format_report(analysis_result))

    # Step 4: Generate accuracy reports
    print("\n--- Generating accuracy reports ---")
    critical_baseline = _resolve_critical_baseline(args, report_output_dir)
    if critical_baseline:
        print(f"  Critical baseline: {critical_baseline}")
    report_paths, report_summary = _generate_accuracy_reports(
        diff_csv,
        report_output_dir,
        top_n=args.show,
        critical_baseline=critical_baseline,
    )
    for p in report_paths:
        print(f"  {p}")

    objectives = report_summary.get("objectives", {})
    physics_only = objectives.get("physics_only", {})
    short_priority = objectives.get("short_shot_priority", {})
    print(
        "  Physics-only within ±3 yd: "
        f"{physics_only.get('within_3yd_pct', 0.0):.1f}%"
    )
    print(
        "  Short shots <115 yd within ±1 yd: "
        f"{short_priority.get('actual_within_1yd_pct', 0.0):.1f}%"
    )
    print(
        "  Short shots <115 yd within ±0.5 yd: "
        f"{short_priority.get('actual_within_0.5yd_pct', 0.0):.1f}%"
    )

    baseline_summary = report_summary.get("critical_baseline")
    if baseline_summary:
        print(
            "  Baseline critical shots improved/regressed: "
            f"{baseline_summary.get('improved', 0)}/{baseline_summary.get('regressed', 0)}"
        )

    # Step 5: Save iteration snapshot
    os.makedirs(history_dir, exist_ok=True)
    iteration_num = _get_next_iteration(history_dir)
    prev_iteration = _load_iteration(history_dir, iteration_num - 1) if iteration_num > 1 else None
    snapshot = _save_iteration(history_dir, iteration_num, {}, analysis_result, prev_iteration)

    if snapshot["regressions"]:
        print("\n" + "!" * 70)
        print("REGRESSIONS DETECTED")
        print("!" * 70)
        for reg in snapshot["regressions"]:
            print(
                f"  {reg['shot']}: {reg['was']} -> {reg['now']} "
                f"(total_diff: {reg['prev_total_diff']} -> {reg['curr_total_diff']})"
            )

    print(f"\nIteration {iteration_num} saved to {history_dir}/iteration_{iteration_num:03d}.json")
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
    run_parser.add_argument("--session", default=None, help="Session directory path (all outputs go into session dir)")
    run_parser.add_argument("--no-sessions", action="store_true", help="Exclude session directories (standard shots only)")
    run_parser.add_argument("--carry-exceptions", default=None, help="Path to carry exception profile JSON (explicit opt-in; default disabled)")
    run_parser.add_argument("--no-carry-exceptions", action="store_true", help="Disable carry exception profile")

    analyze_parser = subparsers.add_parser("analyze", help="Post-scrape analysis: compare, diagnose, generate accuracy reports")
    analyze_parser.add_argument("--session", default=None, help="Session directory path")
    analyze_parser.add_argument("--no-sessions", action="store_true", help="Exclude session directories (standard shots only)")
    analyze_parser.add_argument("--flightscope-export", action="store_true", help="Re-export FlightScope CSV before comparing")
    analyze_parser.add_argument("--show", type=int, default=20, help="Number of worst shots to include in critical CSVs (default: 20)")
    analyze_parser.add_argument("--critical-baseline", default=None, help="Optional prior critical-carry CSV to compare against (defaults to latest existing report in assets/data)")
    analyze_parser.add_argument("--carry-exceptions", default=None, help="Path to carry exception profile JSON (explicit opt-in; default disabled)")
    analyze_parser.add_argument("--no-carry-exceptions", action="store_true", help="Disable carry exception profile")

    subparsers.add_parser("status", help="Show last iteration summary")
    subparsers.add_parser("history", help="Show all iteration summaries")

    diff_parser = subparsers.add_parser("diff", help="Compare two iterations")
    diff_parser.add_argument("iter_a", type=int, help="First iteration number")
    diff_parser.add_argument("iter_b", type=int, help="Second iteration number")

    return parser.parse_args()


def main():
    args = parse_args()

    if args.command is None:
        print("Usage: calibrate.py {run|analyze|status|history|diff}", file=sys.stderr)
        sys.exit(1)

    commands = {
        "run": cmd_run,
        "analyze": cmd_analyze,
        "status": cmd_status,
        "history": cmd_history,
        "diff": cmd_diff,
    }

    commands[args.command](args)


if __name__ == "__main__":
    main()
