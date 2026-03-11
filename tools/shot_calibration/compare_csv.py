#!/usr/bin/env python
"""Compare physics CSV against FlightScope CSV side-by-side.

Usage:
    python tools/shot_calibration/compare_csv.py assets/data/calibration/physics.csv assets/data/calibration/flightscope.csv
"""

import csv
import sys


def load_csv(path):
    """Load CSV indexed by shot_name."""
    rows = {}
    with open(path, "r") as f:
        reader = csv.DictReader(f)
        for row in reader:
            rows[row["shot_name"]] = row
    return rows


def fmt(val, width=8):
    """Format a numeric value or dash if zero/missing."""
    try:
        v = float(val)
        if v == 0.0:
            return "-".rjust(width)
        return f"{v:.1f}".rjust(width)
    except (ValueError, TypeError):
        return "-".rjust(width)


def delta(a, b):
    """Compute delta string between two values."""
    try:
        va, vb = float(a), float(b)
        if va == 0.0 or vb == 0.0:
            return "-".rjust(8)
        return f"{va - vb:+.1f}".rjust(8)
    except (ValueError, TypeError):
        return "-".rjust(8)


def main():
    if len(sys.argv) != 3:
        print(f"Usage: python {sys.argv[0]} <physics.csv> <flightscope.csv>")
        sys.exit(1)

    physics = load_csv(sys.argv[1])
    flightscope = load_csv(sys.argv[2])

    all_shots = sorted(set(list(physics.keys()) + list(flightscope.keys())))

    # Header
    header = (
        f"{'shot_name':<25} | "
        f"{'spd':>5} | "
        f"{'vla':>5} | "
        f"{'spin':>6} | "
        f"{'p_carry':>8} | {'fs_carry':>8} | {'d_carry':>8} | "
        f"{'p_total':>8} | {'fs_total':>8} | {'d_total':>8} | "
        f"{'p_apex':>8} | {'fs_apex':>8} | {'d_apex':>8}"
    )
    print(header)
    print("-" * len(header))

    for shot in all_shots:
        p = physics.get(shot, {})
        f = flightscope.get(shot, {})

        speed = p.get("speed_mph", f.get("speed_mph", ""))
        vla = p.get("vla_deg", f.get("vla_deg", ""))
        spin = p.get("total_spin_rpm", f.get("total_spin_rpm", ""))

        p_carry = p.get("carry_yd", "0")
        f_carry = f.get("carry_yd", "0")
        p_total = p.get("total_yd", "0")
        f_total = f.get("total_yd", "0")
        p_apex = p.get("apex_ft", "0")
        f_apex = f.get("apex_ft", "0")

        try:
            spd_str = f"{float(speed):>5.1f}" if speed else "    -"
        except ValueError:
            spd_str = "    -"
        try:
            vla_str = f"{float(vla):>5.1f}" if vla else "    -"
        except ValueError:
            vla_str = "    -"
        try:
            spin_str = f"{float(spin):>6.0f}" if spin else "     -"
        except ValueError:
            spin_str = "     -"

        print(
            f"{shot:<25} | "
            f"{spd_str} | "
            f"{vla_str} | "
            f"{spin_str} | "
            f"{fmt(p_carry)} | {fmt(f_carry)} | {delta(p_carry, f_carry)} | "
            f"{fmt(p_total)} | {fmt(f_total)} | {delta(p_total, f_total)} | "
            f"{fmt(p_apex)} | {fmt(f_apex)} | {delta(p_apex, f_apex)}"
        )


if __name__ == "__main__":
    main()
