#!/usr/bin/env python
"""
Parse golf shot debug output and extract carry/total distances.
Usage: python parse_shot_debug.py < debug_output.txt
Or paste debug output and press Ctrl+D (Unix) or Ctrl+Z (Windows)
"""

import sys
import re
from typing import Dict, Optional

def parse_shot_debug(text: str) -> Dict[str, any]:
    """Parse shot debug output and extract key metrics."""
    result = {}

    # Extract shot parameters from === SHOT DEBUG ===
    speed_match = re.search(r'Speed:\s+([\d.]+)\s+mph', text)
    vla_match = re.search(r'VLA:\s+([\d.]+)°', text)
    spin_match = re.search(r'Spin:\s+([\d]+)\s+rpm', text)

    if speed_match:
        result['speed_mph'] = float(speed_match.group(1))
    if vla_match:
        result['vla_deg'] = float(vla_match.group(1))
    if spin_match:
        result['spin_rpm'] = int(spin_match.group(1))

    # Extract FIRST IMPACT for carry distance
    first_impact = re.search(r'FIRST IMPACT at pos: \(([\d.-]+), ([\d.-]+), ([\d.-]+)\), downrange: ([\d.]+) yds', text)
    if first_impact:
        result['carry_yd'] = float(first_impact.group(4))

    # Find the final position by looking for the last position before ball stops
    # Look for positions in rolling/slipping messages or final rest
    positions = re.findall(r'pos: \(([\d.-]+), ([\d.-]+), ([\d.-]+)\)', text)
    if positions:
        final_pos = positions[-1]
        x, y, z = float(final_pos[0]), float(final_pos[1]), float(final_pos[2])

        # Calculate distance (assuming shot goes in positive X direction)
        # Use Pythagoras: sqrt(x^2 + z^2) for horizontal distance
        import math
        result['total_yd'] = math.sqrt(x*x + z*z) * 1.09361  # meters to yards

    # If we can't find positions, try to extract from downrange messages
    if 'total_yd' not in result:
        # Look for any downrange measurements
        downrange_matches = re.findall(r'downrange: ([\d.]+) yds', text)
        if downrange_matches:
            result['total_yd'] = float(downrange_matches[-1])

    if 'carry_yd' in result and 'total_yd' in result:
        result['rollout_yd'] = result['total_yd'] - result['carry_yd']

    return result

def format_result(result: Dict, target_carry: Optional[float] = None, target_total: Optional[float] = None):
    """Format parsed result with optional target comparison."""
    if not result:
        print("ERROR: Could not parse shot data")
        return

    print("\n" + "="*60)
    print("SHOT METRICS")
    print("="*60)

    if 'speed_mph' in result:
        print(f"Ball Speed:  {result['speed_mph']:.1f} mph")
    if 'vla_deg' in result:
        print(f"Launch Angle: {result['vla_deg']:.1f}°")
    if 'spin_rpm' in result:
        print(f"Spin Rate:   {result['spin_rpm']} RPM")

    print()

    if 'carry_yd' in result:
        carry = result['carry_yd']
        if target_carry:
            diff = carry - target_carry
            pct = (diff / target_carry) * 100
            print(f"Carry:   {carry:6.1f} yd  (target {target_carry:.1f} yd,  {diff:+6.1f} yd  {pct:+5.1f}%)")
        else:
            print(f"Carry:   {carry:6.1f} yd")

    if 'total_yd' in result:
        total = result['total_yd']
        if target_total:
            diff = total - target_total
            pct = (diff / target_total) * 100
            print(f"Total:   {total:6.1f} yd  (target {target_total:.1f} yd,  {diff:+6.1f} yd  {pct:+5.1f}%)")
        else:
            print(f"Total:   {total:6.1f} yd")

    if 'rollout_yd' in result:
        rollout = result['rollout_yd']
        if target_carry and target_total:
            target_rollout = target_total - target_carry
            diff = rollout - target_rollout
            print(f"Rollout: {rollout:6.1f} yd  (target {target_rollout:.1f} yd,  {diff:+6.1f} yd)")
        else:
            print(f"Rollout: {rollout:6.1f} yd")

    print("="*60 + "\n")

def main():
    print("Paste shot debug output (press Ctrl+D when done, or Ctrl+Z on Windows):\n")

    # Read all input
    debug_text = sys.stdin.read()

    # Parse
    result = parse_shot_debug(debug_text)

    # Detect shot type based on parameters
    if result.get('speed_mph', 0) < 30:
        shot_type = "Chip Shot"
        target_carry, target_total = 7.6, 13.0
    elif result.get('speed_mph', 0) > 100:
        shot_type = "Driver/Wood"
        target_carry, target_total = 121.0, 198.0
    elif result.get('spin_rpm', 0) > 8000:
        shot_type = "Wedge"
        target_carry, target_total = 103.0, 108.0
    else:
        shot_type = "Unknown"
        target_carry, target_total = None, None

    print(f"\nShot Type: {shot_type}")
    format_result(result, target_carry, target_total)

if __name__ == "__main__":
    main()
