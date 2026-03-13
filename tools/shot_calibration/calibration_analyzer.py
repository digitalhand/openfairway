#!/usr/bin/env python
"""Diagnostic analyzer for shot calibration diffs.

Reads shot_diff_analysis.csv and produces a structured diagnostic report
with error classification, shot regime tagging, parameter suggestions,
and conflict detection.

Usage:
    python tools/shot_calibration/calibration_analyzer.py
    python tools/shot_calibration/calibration_analyzer.py --input path/to/shot_diff_analysis.csv
    python tools/shot_calibration/calibration_analyzer.py --output /tmp/diagnostic_report.txt
    python tools/shot_calibration/calibration_analyzer.py --json

By default, writes the report file (diagnostic_report.txt or .json) next to the input file.
"""

import argparse
import csv
import json
import os
import sys

SCRIPT_DIR = os.path.dirname(__file__)
DEFAULT_INPUT_PATH = os.path.normpath(
    os.path.join(SCRIPT_DIR, "..", "..", "assets", "data", "calibration", "shot_diff_analysis.csv")
)

# --- Thresholds ---

CARRY_PASS = 3.0
CARRY_MODERATE = 7.0
TOTAL_PASS = 5.0
TOTAL_MODERATE = 10.0
APEX_PASS = 5.0
APEX_MODERATE = 10.0

# --- Error pattern classification ---

ERROR_PATTERNS = {
    "ROLLOUT_TOO_LONG": "Carry close, total overshoots — rollout is too long",
    "ROLLOUT_TOO_SHORT": "Carry close, total undershoots — rollout is too short",
    "CARRY_TOO_LONG": "Physics carry exceeds reference",
    "CARRY_TOO_SHORT": "Physics carry under reference",
    "CARRY_AND_ROLLOUT_LONG": "Both carry and rollout overshoot",
    "CARRY_AND_ROLLOUT_SHORT": "Both carry and rollout undershoot",
}

# --- Shot regime definitions ---

REGIME_RULES = [
    ("low-launch", lambda r: r["vla_deg"] is not None and r["vla_deg"] < 10.0),
    ("high-spin-wedge", lambda r: r["total_spin_rpm"] is not None and r["total_spin_rpm"] > 5000),
    ("low-speed-chip", lambda r: r["speed_mph"] is not None and r["speed_mph"] < 60),
    ("driver-wood", lambda r: (
        r["speed_mph"] is not None and r["speed_mph"] > 110
        and r["vla_deg"] is not None and r["vla_deg"] < 18
    )),
    ("mid-iron", lambda r: (
        r["speed_mph"] is not None and 85 <= r["speed_mph"] <= 110
        and r["vla_deg"] is not None and 15 <= r["vla_deg"] <= 25
    )),
    ("high-loft-wedge", lambda r: (
        r["vla_deg"] is not None and r["vla_deg"] > 30
        and r["total_spin_rpm"] is not None and r["total_spin_rpm"] > 8000
    )),
]

# --- Parameter-to-shot mapping knowledge base ---
# Each entry: parameter name -> { profile, direction_hint, safe_range, step, affects_patterns, affected_regimes }

PARAM_KNOWLEDGE_BASE = {
    "Bounce.FlightTangentialRetentionBase": {
        "profile": "Bounce",
        "key": "FlightTangentialRetentionBase",
        "description": "First-bounce tangential velocity retention (0-1)",
        "direction_for_more_rollout": "increase",
        "safe_range": (0.40, 0.75),
        "step": 0.03,
        "affects_patterns": ["ROLLOUT_TOO_SHORT", "ROLLOUT_TOO_LONG"],
        "affected_regimes": ["low-launch", "driver-wood", "mid-iron"],
    },
    "Bounce.FlightSpinFactorMin": {
        "profile": "Bounce",
        "key": "FlightSpinFactorMin",
        "description": "Min tangential retention at high spin (0-1)",
        "direction_for_more_rollout": "increase",
        "safe_range": (0.25, 0.55),
        "step": 0.03,
        "affects_patterns": ["ROLLOUT_TOO_SHORT", "ROLLOUT_TOO_LONG"],
        "affected_regimes": ["high-spin-wedge", "high-loft-wedge"],
    },
    "Bounce.FlightSpinFactorDivisor": {
        "profile": "Bounce",
        "key": "FlightSpinFactorDivisor",
        "description": "Spin RPM divisor for tangential retention curve",
        "direction_for_more_rollout": "increase",
        "safe_range": (4000.0, 12000.0),
        "step": 500.0,
        "affects_patterns": ["ROLLOUT_TOO_SHORT", "ROLLOUT_TOO_LONG"],
        "affected_regimes": ["high-spin-wedge", "high-loft-wedge"],
    },
    "Bounce.CorBaseA": {
        "profile": "Bounce",
        "key": "CorBaseA",
        "description": "Base coefficient of restitution (vertical bounce)",
        "direction_for_more_rollout": "increase",
        "safe_range": (0.30, 0.60),
        "step": 0.02,
        "affects_patterns": ["ROLLOUT_TOO_SHORT", "ROLLOUT_TOO_LONG"],
        "affected_regimes": ["low-launch", "driver-wood"],
    },
    "Rollout.LowSpinMultiplierMax": {
        "profile": "Rollout",
        "key": "LowSpinMultiplierMax",
        "description": "Max friction multiplier for low-spin rollout",
        "direction_for_more_rollout": "decrease",
        "safe_range": (0.80, 1.50),
        "step": 0.05,
        "affects_patterns": ["ROLLOUT_TOO_LONG", "ROLLOUT_TOO_SHORT"],
        "affected_regimes": ["low-launch", "driver-wood"],
    },
    "Rollout.MidSpinMultiplierMax": {
        "profile": "Rollout",
        "key": "MidSpinMultiplierMax",
        "description": "Max friction multiplier for mid-spin rollout",
        "direction_for_more_rollout": "decrease",
        "safe_range": (1.50, 3.00),
        "step": 0.10,
        "affects_patterns": ["ROLLOUT_TOO_LONG", "ROLLOUT_TOO_SHORT"],
        "affected_regimes": ["mid-iron"],
    },
    "Rollout.HighSpinMultiplierMax": {
        "profile": "Rollout",
        "key": "HighSpinMultiplierMax",
        "description": "Max friction multiplier for high-spin rollout",
        "direction_for_more_rollout": "decrease",
        "safe_range": (1.50, 3.50),
        "step": 0.10,
        "affects_patterns": ["ROLLOUT_TOO_LONG", "ROLLOUT_TOO_SHORT"],
        "affected_regimes": ["high-spin-wedge", "high-loft-wedge"],
    },
    "Rollout.ChipVelocityScaleMin": {
        "profile": "Rollout",
        "key": "ChipVelocityScaleMin",
        "description": "Min velocity scale for chip-speed rollout",
        "direction_for_more_rollout": "increase",
        "safe_range": (0.40, 0.85),
        "step": 0.05,
        "affects_patterns": ["ROLLOUT_TOO_SHORT", "ROLLOUT_TOO_LONG"],
        "affected_regimes": ["low-speed-chip"],
    },
    "Rollout.ChipVelocityScaleMax": {
        "profile": "Rollout",
        "key": "ChipVelocityScaleMax",
        "description": "Max velocity scale for chip-speed rollout",
        "direction_for_more_rollout": "increase",
        "safe_range": (0.70, 1.00),
        "step": 0.03,
        "affects_patterns": ["ROLLOUT_TOO_SHORT", "ROLLOUT_TOO_LONG"],
        "affected_regimes": ["low-speed-chip"],
    },
    "Rollout.LowSpinThreshold": {
        "profile": "Rollout",
        "key": "LowSpinThreshold",
        "description": "RPM boundary between low and mid spin friction",
        "direction_for_more_rollout": "increase",
        "safe_range": (1000.0, 2500.0),
        "step": 100.0,
        "affects_patterns": ["ROLLOUT_TOO_LONG", "ROLLOUT_TOO_SHORT"],
        "affected_regimes": ["low-launch", "driver-wood"],
    },
    "Rollout.FrictionBlendSpeed": {
        "profile": "Rollout",
        "key": "FrictionBlendSpeed",
        "description": "Ball speed at which friction blending reaches full effect",
        "direction_for_more_rollout": "increase",
        "safe_range": (8.0, 25.0),
        "step": 1.0,
        "affects_patterns": ["ROLLOUT_TOO_LONG", "ROLLOUT_TOO_SHORT"],
        "affected_regimes": ["low-launch", "mid-iron"],
    },
    "Flight.ClMaxBase": {
        "profile": "Flight",
        "key": "ClMaxBase",
        "description": "Base max lift coefficient cap",
        "direction_for_more_carry": "increase",
        "safe_range": (0.22, 0.32),
        "step": 0.005,
        "affects_patterns": ["CARRY_TOO_SHORT", "CARRY_TOO_LONG"],
        "affected_regimes": ["mid-iron", "driver-wood"],
    },
    "Flight.CdMin": {
        "profile": "Flight",
        "key": "CdMin",
        "description": "Minimum drag coefficient floor",
        "direction_for_more_carry": "decrease",
        "safe_range": (0.18, 0.28),
        "step": 0.005,
        "affects_patterns": ["CARRY_TOO_SHORT", "CARRY_TOO_LONG"],
        "affected_regimes": ["driver-wood", "mid-iron"],
    },
    "Flight.HighLaunchDragBoostMax": {
        "profile": "Flight",
        "key": "HighLaunchDragBoostMax",
        "description": "Extra drag for high-launch, high-spin shots",
        "direction_for_more_carry": "decrease",
        "safe_range": (1.00, 1.30),
        "step": 0.02,
        "affects_patterns": ["CARRY_TOO_SHORT", "CARRY_TOO_LONG"],
        "affected_regimes": ["high-spin-wedge", "high-loft-wedge"],
    },
    "DragScaleMultiplier": {
        "profile": "Root",
        "key": "DragScaleMultiplier",
        "description": "Global drag scale multiplier",
        "direction_for_more_carry": "decrease",
        "safe_range": (0.85, 1.15),
        "step": 0.02,
        "affects_patterns": ["CARRY_TOO_SHORT", "CARRY_TOO_LONG"],
        "affected_regimes": ["driver-wood", "mid-iron", "high-spin-wedge"],
    },
    "LiftScaleMultiplier": {
        "profile": "Root",
        "key": "LiftScaleMultiplier",
        "description": "Global lift scale multiplier",
        "direction_for_more_carry": "increase",
        "safe_range": (0.85, 1.15),
        "step": 0.02,
        "affects_patterns": ["CARRY_TOO_SHORT", "CARRY_TOO_LONG"],
        "affected_regimes": ["driver-wood", "mid-iron"],
    },
    "KineticFrictionMultiplier": {
        "profile": "Root",
        "key": "KineticFrictionMultiplier",
        "description": "Global kinetic friction multiplier",
        "direction_for_more_rollout": "decrease",
        "safe_range": (0.70, 1.30),
        "step": 0.05,
        "affects_patterns": ["ROLLOUT_TOO_LONG", "ROLLOUT_TOO_SHORT"],
        "affected_regimes": ["low-launch", "driver-wood", "mid-iron"],
    },
    "RollingFrictionMultiplier": {
        "profile": "Root",
        "key": "RollingFrictionMultiplier",
        "description": "Global rolling friction multiplier",
        "direction_for_more_rollout": "decrease",
        "safe_range": (0.70, 1.30),
        "step": 0.05,
        "affects_patterns": ["ROLLOUT_TOO_LONG", "ROLLOUT_TOO_SHORT"],
        "affected_regimes": ["low-launch", "driver-wood"],
    },
    "Bounce.RolloutLowSpinRetention": {
        "profile": "Bounce",
        "key": "RolloutLowSpinRetention",
        "description": "Tangential retention on rollout bounces (low spin)",
        "direction_for_more_rollout": "increase",
        "safe_range": (0.70, 0.95),
        "step": 0.03,
        "affects_patterns": ["ROLLOUT_TOO_SHORT", "ROLLOUT_TOO_LONG"],
        "affected_regimes": ["low-launch", "driver-wood"],
    },
    "Bounce.RolloutHighSpinRetention": {
        "profile": "Bounce",
        "key": "RolloutHighSpinRetention",
        "description": "Tangential retention on rollout bounces (high spin)",
        "direction_for_more_rollout": "increase",
        "safe_range": (0.55, 0.85),
        "step": 0.03,
        "affects_patterns": ["ROLLOUT_TOO_SHORT", "ROLLOUT_TOO_LONG"],
        "affected_regimes": ["high-spin-wedge", "mid-iron"],
    },
}


def parse_float(value):
    if value is None:
        return None
    text = str(value).strip()
    if not text:
        return None
    try:
        return float(text)
    except ValueError:
        return None


def load_diff_csv(path):
    rows = []
    with open(path, "r", newline="") as f:
        reader = csv.DictReader(f)
        for row in reader:
            parsed = {
                "shot_name": row.get("shot_name", "").strip(),
                "speed_mph": parse_float(row.get("speed_mph")),
                "vla_deg": parse_float(row.get("vla_deg")),
                "hla_deg": parse_float(row.get("hla_deg")),
                "total_spin_rpm": parse_float(row.get("total_spin_rpm")),
                "spin_axis_deg": parse_float(row.get("spin_axis_deg")),
                "physics_carry_yd": parse_float(row.get("physics_carry_yd")),
                "flightscope_carry_yd": parse_float(row.get("flightscope_carry_yd")),
                "diff_carry_yd": parse_float(row.get("diff_carry_yd")),
                "physics_total_yd": parse_float(row.get("physics_total_yd")),
                "flightscope_total_yd": parse_float(row.get("flightscope_total_yd")),
                "diff_total_yd": parse_float(row.get("diff_total_yd")),
                "physics_apex_ft": parse_float(row.get("physics_apex_ft")),
                "flightscope_apex_ft": parse_float(row.get("flightscope_apex_ft")),
                "diff_apex_ft": parse_float(row.get("diff_apex_ft")),
                "rollout_physics_yd": parse_float(row.get("rollout_physics_yd")),
                "rollout_flightscope_yd": parse_float(row.get("rollout_flightscope_yd")),
                "diff_rollout_yd": parse_float(row.get("diff_rollout_yd")),
            }
            if parsed["shot_name"]:
                rows.append(parsed)
    return rows


def classify_status(row):
    carry_abs = abs(row["diff_carry_yd"]) if row["diff_carry_yd"] is not None else None
    total_abs = abs(row["diff_total_yd"]) if row["diff_total_yd"] is not None else None

    if total_abs is not None and total_abs > TOTAL_MODERATE:
        return "severe"
    if carry_abs is not None and carry_abs > CARRY_MODERATE:
        return "severe"
    if total_abs is not None and total_abs > TOTAL_PASS:
        return "moderate"
    if carry_abs is not None and carry_abs > CARRY_PASS:
        return "moderate"
    return "pass"


def classify_error_pattern(row):
    carry_diff = row["diff_carry_yd"]
    total_diff = row["diff_total_yd"]

    if carry_diff is None or total_diff is None:
        return None

    carry_close = abs(carry_diff) <= CARRY_PASS
    carry_long = carry_diff > CARRY_PASS
    carry_short = carry_diff < -CARRY_PASS
    total_long = total_diff > TOTAL_PASS
    total_short = total_diff < -TOTAL_PASS

    if carry_close and total_long:
        return "ROLLOUT_TOO_LONG"
    if carry_close and total_short:
        return "ROLLOUT_TOO_SHORT"
    if carry_long and total_long:
        return "CARRY_AND_ROLLOUT_LONG"
    if carry_short and total_short:
        return "CARRY_AND_ROLLOUT_SHORT"
    if carry_long:
        return "CARRY_TOO_LONG"
    if carry_short:
        return "CARRY_TOO_SHORT"
    return None


def tag_regimes(row):
    tags = []
    for name, rule in REGIME_RULES:
        try:
            if rule(row):
                tags.append(name)
        except (TypeError, KeyError):
            continue
    return tags


def compute_rollout_diff(row):
    """Compute rollout diff from carry and total if not already present."""
    if row["diff_rollout_yd"] is not None:
        return row["diff_rollout_yd"]
    p_carry = row["physics_carry_yd"]
    p_total = row["physics_total_yd"]
    f_carry = row["flightscope_carry_yd"]
    f_total = row["flightscope_total_yd"]
    if all(v is not None for v in [p_carry, p_total, f_carry, f_total]):
        p_rollout = p_total - p_carry
        f_rollout = f_total - f_carry
        return p_rollout - f_rollout
    return None


def suggest_parameters(pattern, regimes):
    suggestions = []
    for param_name, info in PARAM_KNOWLEDGE_BASE.items():
        if pattern not in info["affects_patterns"]:
            continue
        regime_overlap = set(regimes) & set(info["affected_regimes"])
        if not regime_overlap and regimes:
            continue

        direction_key = None
        if "direction_for_more_rollout" in info:
            direction_key = "direction_for_more_rollout"
        elif "direction_for_more_carry" in info:
            direction_key = "direction_for_more_carry"

        if direction_key is None:
            continue

        base_direction = info[direction_key]

        if pattern in ("ROLLOUT_TOO_LONG", "CARRY_TOO_LONG", "CARRY_AND_ROLLOUT_LONG"):
            needed = "decrease" if base_direction == "increase" else "increase"
        else:
            needed = base_direction

        suggestions.append({
            "parameter": param_name,
            "direction": needed,
            "step": info["step"],
            "safe_range": info["safe_range"],
            "description": info["description"],
            "regime_match": sorted(regime_overlap) if regime_overlap else ["general"],
        })
    return suggestions


def detect_conflicts(shot_diagnostics):
    """Find parameters where different failing shots need opposite adjustments."""
    param_directions = {}
    for diag in shot_diagnostics:
        if diag["status"] == "pass":
            continue
        for suggestion in diag.get("suggestions", []):
            param = suggestion["parameter"]
            direction = suggestion["direction"]
            shot = diag["shot_name"]
            param_directions.setdefault(param, []).append({
                "shot": shot,
                "direction": direction,
                "pattern": diag["error_pattern"],
            })

    conflicts = []
    for param, entries in param_directions.items():
        directions = set(e["direction"] for e in entries)
        if len(directions) > 1:
            conflicts.append({
                "parameter": param,
                "conflicting_shots": entries,
            })
    return conflicts


def analyze(rows):
    diagnostics = []
    for row in rows:
        row["rollout_diff_computed"] = compute_rollout_diff(row)
        status = classify_status(row)
        pattern = classify_error_pattern(row)
        regimes = tag_regimes(row)
        suggestions = suggest_parameters(pattern, regimes) if pattern else []

        diagnostics.append({
            "shot_name": row["shot_name"],
            "status": status,
            "error_pattern": pattern,
            "regimes": regimes,
            "diff_carry_yd": row["diff_carry_yd"],
            "diff_total_yd": row["diff_total_yd"],
            "diff_apex_ft": row["diff_apex_ft"],
            "diff_rollout_yd": row["rollout_diff_computed"],
            "suggestions": suggestions,
        })

    conflicts = detect_conflicts(diagnostics)

    summary = {"pass": 0, "moderate": 0, "severe": 0, "no_reference": 0}
    for d in diagnostics:
        if d["diff_total_yd"] is None:
            summary["no_reference"] += 1
        else:
            summary[d["status"]] += 1

    return {
        "summary": summary,
        "diagnostics": diagnostics,
        "conflicts": conflicts,
    }


def format_report(result):
    lines = []
    summary = result["summary"]
    lines.append("=" * 70)
    lines.append("CALIBRATION DIAGNOSTIC REPORT")
    lines.append("=" * 70)
    lines.append("")
    lines.append(f"  Pass:         {summary['pass']}")
    lines.append(f"  Moderate:     {summary['moderate']}")
    lines.append(f"  Severe:       {summary['severe']}")
    lines.append(f"  No reference: {summary['no_reference']}")
    lines.append("")

    severe = [d for d in result["diagnostics"] if d["status"] == "severe"]
    moderate = [d for d in result["diagnostics"] if d["status"] == "moderate"]

    if severe:
        lines.append("-" * 70)
        lines.append("SEVERE SHOTS (|total_diff| > 10 yd or |carry_diff| > 7 yd)")
        lines.append("-" * 70)
        for d in severe:
            lines.extend(_format_shot_diagnostic(d))

    if moderate:
        lines.append("-" * 70)
        lines.append("MODERATE SHOTS (5-10 yd total or 3-7 yd carry)")
        lines.append("-" * 70)
        for d in moderate:
            lines.extend(_format_shot_diagnostic(d))

    if result["conflicts"]:
        lines.append("")
        lines.append("=" * 70)
        lines.append("PARAMETER CONFLICTS")
        lines.append("=" * 70)
        for conflict in result["conflicts"]:
            param = conflict["parameter"]
            lines.append(f"\n  {param}:")
            for entry in conflict["conflicting_shots"]:
                lines.append(
                    f"    - {entry['shot']}: needs {entry['direction']} "
                    f"(pattern: {entry['pattern']})"
                )

    lines.append("")
    return "\n".join(lines)


def _format_shot_diagnostic(d):
    lines = []
    lines.append(f"\n  {d['shot_name']}:")
    lines.append(f"    Status:  {d['status'].upper()}")
    if d["error_pattern"]:
        lines.append(f"    Pattern: {d['error_pattern']} — {ERROR_PATTERNS.get(d['error_pattern'], '')}")
    lines.append(f"    Regimes: {', '.join(d['regimes']) if d['regimes'] else 'none'}")
    diffs = []
    if d["diff_carry_yd"] is not None:
        diffs.append(f"carry={d['diff_carry_yd']:+.1f}")
    if d["diff_total_yd"] is not None:
        diffs.append(f"total={d['diff_total_yd']:+.1f}")
    if d["diff_rollout_yd"] is not None:
        diffs.append(f"rollout={d['diff_rollout_yd']:+.1f}")
    if d["diff_apex_ft"] is not None:
        diffs.append(f"apex={d['diff_apex_ft']:+.1f}ft")
    lines.append(f"    Diffs:   {', '.join(diffs)}")

    if d["suggestions"]:
        lines.append("    Suggested knobs:")
        for s in d["suggestions"][:5]:
            lines.append(
                f"      -> {s['parameter']}: {s['direction']} by {s['step']} "
                f"(range {s['safe_range'][0]}-{s['safe_range'][1]})"
            )
    return lines


def parse_args():
    parser = argparse.ArgumentParser(description="Calibration diagnostic analyzer")
    parser.add_argument(
        "--input",
        default=DEFAULT_INPUT_PATH,
        help="Path to shot_diff_analysis.csv",
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Output as JSON instead of text report",
    )
    parser.add_argument(
        "--output",
        default=None,
        help="Path to write report file (default: diagnostic_report.txt/.json next to input)",
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

    result = analyze(rows)

    if args.json:
        output_text = json.dumps(result, indent=2)
    else:
        output_text = format_report(result)

    # Determine output path: explicit --output, or default next to input file
    if args.output:
        output_path = args.output
    else:
        input_dir = os.path.dirname(os.path.abspath(args.input))
        ext = ".json" if args.json else ".txt"
        output_path = os.path.join(input_dir, f"diagnostic_report{ext}")

    print(output_text)
    with open(output_path, "w") as f:
        f.write(output_text)
        if not output_text.endswith("\n"):
            f.write("\n")
    print(f"\nReport written to: {output_path}", file=sys.stderr)


if __name__ == "__main__":
    main()
