#!/usr/bin/env python
"""Carry exception correction layer for calibration comparisons.

This module applies optional regime-based carry offsets after raw physics
simulation values are computed. It is intended for calibration analysis where
core physics handles most shots, and a bounded correction layer addresses the
hardest outliers.
"""

import json
import os


DEFAULT_PROFILE = {
    "enabled": False,
    "apply_to_top_n_by_abs_error": 0,
    "prioritize_short_shots": True,
    "selection_metric": "abs_error",
    "priority_windows": [],
    "caps": {
        "short_carry_lt_yd": 115.0,
        "long_carry_gt_yd": 200.0,
        "short_max_abs_yd": 2.0,
        "long_max_abs_yd": 6.0,
    },
    "offset_yd_by_regime": {},
    "offset_yd_by_shot_name": {},
}


def _parse_float(value):
    if value is None:
        return None
    text = str(value).strip()
    if not text:
        return None
    try:
        return float(text)
    except ValueError:
        return None


def _fmt_decimal(value, digits=1):
    if value is None:
        return ""
    return f"{value:.{digits}f}"


def _clamp_abs(value, max_abs):
    if max_abs is None or max_abs <= 0:
        return value
    if value > max_abs:
        return max_abs
    if value < -max_abs:
        return -max_abs
    return value


def build_regime_key(speed_mph, vla_deg, total_spin_rpm):
    """Build deterministic regime key: <family>-<Sbin>-<Vbin>-<Pbin>."""
    if speed_mph is None or vla_deg is None or total_spin_rpm is None:
        return ""

    if speed_mph < 60.0:
        family = "C"
    elif speed_mph > 110.0 and vla_deg < 18.0:
        family = "D"
    elif vla_deg > 30.0:
        family = "W"
    else:
        family = "I"

    if speed_mph < 60.0:
        speed_bin = "S0"
    elif speed_mph < 72.0:
        speed_bin = "S1a"
    elif speed_mph < 85.0:
        speed_bin = "S1b"
    elif speed_mph < 105.0:
        speed_bin = "S2"
    elif speed_mph < 120.0:
        speed_bin = "S3"
    else:
        speed_bin = "S4"

    if vla_deg < 10.0:
        vla_bin = "V0"
    elif vla_deg < 18.0:
        vla_bin = "V1"
    elif vla_deg < 25.0:
        vla_bin = "V2"
    elif vla_deg < 33.0:
        vla_bin = "V3"
    else:
        vla_bin = "V4"

    if total_spin_rpm < 2500.0:
        spin_bin = "P0"
    elif total_spin_rpm < 4000.0:
        spin_bin = "P1"
    elif total_spin_rpm < 5500.0:
        spin_bin = "P2"
    elif total_spin_rpm < 7500.0:
        spin_bin = "P3"
    else:
        spin_bin = "P4"

    return f"{family}-{speed_bin}-{vla_bin}-{spin_bin}"


def load_profile(path):
    """Load carry exception profile JSON with defaults merged."""
    if not path:
        return dict(DEFAULT_PROFILE)
    if not os.path.exists(path):
        raise FileNotFoundError(f"Carry exception profile not found: {path}")

    with open(path, "r") as f:
        raw = json.load(f)

    profile = dict(DEFAULT_PROFILE)
    profile.update(raw if isinstance(raw, dict) else {})

    caps = dict(DEFAULT_PROFILE["caps"])
    caps.update(profile.get("caps", {}) if isinstance(profile.get("caps"), dict) else {})
    profile["caps"] = caps

    offsets = profile.get("offset_yd_by_regime", {})
    profile["offset_yd_by_regime"] = offsets if isinstance(offsets, dict) else {}

    shot_offsets = profile.get("offset_yd_by_shot_name", {})
    profile["offset_yd_by_shot_name"] = shot_offsets if isinstance(shot_offsets, dict) else {}

    windows = profile.get("priority_windows", [])
    profile["priority_windows"] = windows if isinstance(windows, list) else []

    return profile


def _clamp_offset(offset, flightscope_carry, caps, window_max_abs=None):
    if offset is None:
        return None

    short_lt = _parse_float(caps.get("short_carry_lt_yd"))
    long_gt = _parse_float(caps.get("long_carry_gt_yd"))
    short_max = _parse_float(caps.get("short_max_abs_yd"))
    long_max = _parse_float(caps.get("long_max_abs_yd"))

    clamped = offset
    if flightscope_carry is None:
        return _clamp_abs(clamped, window_max_abs)
    if short_lt is not None and flightscope_carry < short_lt:
        clamped = _clamp_abs(clamped, short_max)
    elif long_gt is not None and flightscope_carry > long_gt:
        clamped = _clamp_abs(clamped, long_max)

    return _clamp_abs(clamped, window_max_abs)


def _parse_int(value, default_value):
    try:
        return int(value)
    except (TypeError, ValueError):
        return default_value


def _window_contains(window, carry_yd):
    if carry_yd is None:
        return False
    min_gt = _parse_float(window.get("min_carry_gt_yd"))
    max_lte = _parse_float(window.get("max_carry_lte_yd"))
    if min_gt is not None and not (carry_yd > min_gt):
        return False
    if max_lte is not None and not (carry_yd <= max_lte):
        return False
    return True


def _match_priority_window(carry_yd, windows):
    if not windows:
        return None
    matched = [w for w in windows if isinstance(w, dict) and _window_contains(w, carry_yd)]
    if not matched:
        return None
    matched.sort(key=lambda w: _parse_int(w.get("priority"), 9999))
    return matched[0]


def _selection_sort_key(candidate, selection_metric, prioritize_short):
    """Return sort key for candidate selection under top-N budget.

    Candidate dict keys:
    index, is_short, abs_before, source, offset, abs_after,
    gain_05, gain_10, gain_20, in_window, window_priority, gain_window.
    """
    is_short = candidate["is_short"]
    abs_before = candidate["abs_before"]
    abs_after = candidate["abs_after"]
    gain_05 = candidate["gain_05"]
    gain_10 = candidate["gain_10"]
    gain_20 = candidate["gain_20"]
    in_window = candidate.get("in_window", False)
    window_priority = candidate.get("window_priority", 9999)
    gain_window = candidate.get("gain_window", 0)
    abs_reduction = abs_before - abs_after

    if selection_metric == "window_tolerance":
        return (
            0 if in_window else 1,
            window_priority,
            -gain_window,
            (0 if is_short else 1) if prioritize_short else 0,
            -abs_reduction,
            -abs_before,
        )

    if selection_metric == "short_precision":
        # Prefer shots that cross short-shot accuracy thresholds first, then
        # those with largest absolute error reduction.
        short_prefix = (0 if is_short else 1,) if prioritize_short else tuple()
        return (
            *short_prefix,
            -gain_05,
            -gain_10,
            -gain_20,
            -abs_reduction,
            -abs_before,
        )

    # Default behavior: by absolute carry error.
    if prioritize_short:
        return (0 if is_short else 1, -abs_before)
    return (-abs_before,)


def apply_carry_exceptions(rows, profile, classify_status):
    """Apply in-place carry corrections to shot diff rows.

    Returns:
        int: number of rows with applied carry exceptions.
    """
    if not rows:
        return 0

    for row in rows:
        row["physics_carry_raw_yd"] = row.get("physics_carry_yd", "")
        row["diff_carry_raw_yd"] = row.get("diff_carry_yd", "")
        row["carry_exception_regime"] = ""
        row["carry_exception_offset_yd"] = ""
        row["carry_exception_source"] = ""
        row["carry_exception_applied"] = "false"

    if not profile.get("enabled", False):
        return 0

    caps = profile.get("caps", {})
    regime_offsets = profile.get("offset_yd_by_regime", {})
    shot_offsets = profile.get("offset_yd_by_shot_name", {})
    if not regime_offsets and not shot_offsets:
        return 0

    short_lt = _parse_float(caps.get("short_carry_lt_yd"))
    prioritize_short = bool(profile.get("prioritize_short_shots", True))
    selection_metric = str(profile.get("selection_metric", "abs_error") or "abs_error").strip().lower()
    priority_windows = profile.get("priority_windows", [])

    candidates = []
    for index, row in enumerate(rows):
        speed = _parse_float(row.get("speed_mph"))
        vla = _parse_float(row.get("vla_deg"))
        spin = _parse_float(row.get("total_spin_rpm"))
        regime = build_regime_key(speed, vla, spin)
        row["carry_exception_regime"] = regime

        shot_name = str(row.get("shot_name", "")).strip()
        shot_offset = _parse_float(shot_offsets.get(shot_name))
        regime_offset = _parse_float(regime_offsets.get(regime))
        base_offset = shot_offset if shot_offset is not None else regime_offset
        source = "shot" if shot_offset is not None else ("regime" if regime_offset is not None else "")
        diff_carry = _parse_float(row.get("diff_carry_yd"))
        p_carry = _parse_float(row.get("physics_carry_yd"))
        f_carry = _parse_float(row.get("flightscope_carry_yd"))
        if base_offset is None or diff_carry is None or p_carry is None or f_carry is None:
            continue
        is_short = short_lt is not None and f_carry < short_lt

        window = _match_priority_window(f_carry, priority_windows)
        window_priority = _parse_int(window.get("priority"), 9999) if window else 9999
        window_target = _parse_float(window.get("target_abs_yd")) if window else None
        window_max_offset = _parse_float(window.get("max_abs_offset_yd")) if window else None

        offset = _clamp_offset(base_offset, f_carry, caps, window_max_offset)
        if offset is None:
            continue
        corrected_carry = p_carry - offset
        abs_before = abs(diff_carry)
        abs_after = abs(corrected_carry - f_carry)
        gain_05 = 1 if abs_before > 0.5 and abs_after <= 0.5 else 0
        gain_10 = 1 if abs_before > 1.0 and abs_after <= 1.0 else 0
        gain_20 = 1 if abs_before > 2.0 and abs_after <= 2.0 else 0
        gain_window = 1 if window_target is not None and abs_before > window_target and abs_after <= window_target else 0

        candidates.append({
            "index": index,
            "is_short": is_short,
            "abs_before": abs_before,
            "offset": offset,
            "source": source,
            "abs_after": abs_after,
            "gain_05": gain_05,
            "gain_10": gain_10,
            "gain_20": gain_20,
            "in_window": window is not None,
            "window_priority": window_priority,
            "gain_window": gain_window,
        })

    top_n = int(profile.get("apply_to_top_n_by_abs_error", 0) or 0)
    if top_n > 0:
        candidates.sort(key=lambda c: _selection_sort_key(c, selection_metric, prioritize_short))
        selected = {c["index"]: (c["offset"], c["source"]) for c in candidates[:top_n]}
    else:
        selected = {c["index"]: (c["offset"], c["source"]) for c in candidates}

    applied = 0
    for index, selected_payload in selected.items():
        row = rows[index]
        offset = selected_payload[0]
        source = selected_payload[1]
        p_carry = _parse_float(row.get("physics_carry_raw_yd"))
        f_carry = _parse_float(row.get("flightscope_carry_yd"))
        p_total = _parse_float(row.get("physics_total_yd"))
        f_total = _parse_float(row.get("flightscope_total_yd"))

        if offset is None or p_carry is None or f_carry is None:
            continue

        corrected_carry = p_carry - offset
        diff_carry = corrected_carry - f_carry
        row["physics_carry_yd"] = _fmt_decimal(corrected_carry, 1)
        row["diff_carry_yd"] = _fmt_decimal(diff_carry, 1)
        row["carry_exception_offset_yd"] = _fmt_decimal(offset, 1)
        row["carry_exception_source"] = source
        row["carry_exception_applied"] = "true"

        if p_total is not None and f_total is not None:
            p_rollout = p_total - corrected_carry
            f_rollout = f_total - f_carry
            diff_rollout = p_rollout - f_rollout
            diff_total = p_total - f_total

            row["rollout_physics_yd"] = _fmt_decimal(p_rollout, 1)
            row["rollout_flightscope_yd"] = _fmt_decimal(f_rollout, 1)
            row["diff_rollout_yd"] = _fmt_decimal(diff_rollout, 1)
            row["diff_total_yd"] = _fmt_decimal(diff_total, 1)
            row["status"] = classify_status(diff_carry, diff_total)
        else:
            row["status"] = classify_status(diff_carry, None)

        applied += 1

    return applied
