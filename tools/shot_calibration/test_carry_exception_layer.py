#!/usr/bin/env python
import os
import sys
import unittest


SCRIPT_DIR = os.path.dirname(__file__)
sys.path.insert(0, SCRIPT_DIR)

from carry_exception_layer import apply_carry_exceptions, build_regime_key


def classify_status(diff_carry, diff_total):
    carry_abs = abs(diff_carry) if diff_carry is not None else None
    total_abs = abs(diff_total) if diff_total is not None else None
    if total_abs is not None and total_abs > 10.0:
        return "severe"
    if carry_abs is not None and carry_abs > 7.0:
        return "severe"
    if total_abs is not None and total_abs > 5.0:
        return "moderate"
    if carry_abs is not None and carry_abs > 3.0:
        return "moderate"
    if total_abs is None and carry_abs is None:
        return ""
    return "pass"


class CarryExceptionLayerTests(unittest.TestCase):
    def test_build_regime_key(self):
        self.assertEqual(build_regime_key(122.0, 14.0, 3200.0), "D-S4-V1-P1")
        self.assertEqual(build_regime_key(56.0, 26.0, 5400.0), "C-S0-V3-P2")
        self.assertEqual(build_regime_key(97.0, 31.0, 8100.0), "W-S2-V3-P4")

    def test_apply_top_n_only(self):
        rows = [
            {
                "shot_name": "driver_big",
                "speed_mph": "122.0",
                "vla_deg": "14.0",
                "total_spin_rpm": "3200",
                "physics_carry_yd": "190.0",
                "fs_carry_yd": "180.0",
                "diff_carry_yd": "10.0",
                "physics_total_yd": "200.0",
                "fs_total_yd": "190.0",
                "diff_total_yd": "10.0",
            },
            {
                "shot_name": "driver_small",
                "speed_mph": "121.0",
                "vla_deg": "15.0",
                "total_spin_rpm": "3300",
                "physics_carry_yd": "185.0",
                "fs_carry_yd": "180.0",
                "diff_carry_yd": "5.0",
                "physics_total_yd": "196.0",
                "fs_total_yd": "190.0",
                "diff_total_yd": "6.0",
            },
        ]
        profile = {
            "enabled": True,
            "apply_to_top_n_by_abs_error": 1,
            "caps": {
                "short_carry_lt_yd": 115.0,
                "long_carry_gt_yd": 200.0,
                "short_max_abs_yd": 2.0,
                "long_max_abs_yd": 6.0,
            },
            "offset_yd_by_regime": {
                "D-S4-V1-P1": 4.0,
            },
        }

        applied = apply_carry_exceptions(rows, profile, classify_status)
        self.assertEqual(applied, 1)
        self.assertEqual(rows[0]["carry_exception_applied"], "true")
        self.assertEqual(rows[0]["carry_exception_offset_yd"], "4.0")
        self.assertEqual(rows[0]["physics_carry_yd"], "186.0")
        self.assertEqual(rows[0]["diff_carry_yd"], "6.0")
        self.assertEqual(rows[1]["carry_exception_applied"], "false")
        self.assertEqual(rows[1]["physics_carry_yd"], "185.0")

    def test_short_shot_cap(self):
        rows = [
            {
                "shot_name": "chip_1",
                "speed_mph": "58.0",
                "vla_deg": "28.0",
                "total_spin_rpm": "5200",
                "physics_carry_yd": "43.0",
                "fs_carry_yd": "40.0",
                "diff_carry_yd": "3.0",
                "physics_total_yd": "50.0",
                "fs_total_yd": "46.0",
                "diff_total_yd": "4.0",
            }
        ]
        profile = {
            "enabled": True,
            "apply_to_top_n_by_abs_error": 0,
            "caps": {
                "short_carry_lt_yd": 115.0,
                "long_carry_gt_yd": 200.0,
                "short_max_abs_yd": 1.5,
                "long_max_abs_yd": 6.0,
            },
            "offset_yd_by_regime": {
                "C-S0-V3-P2": 4.0,
            },
        }

        applied = apply_carry_exceptions(rows, profile, classify_status)
        self.assertEqual(applied, 1)
        self.assertEqual(rows[0]["carry_exception_offset_yd"], "1.5")
        self.assertEqual(rows[0]["physics_carry_yd"], "41.5")
        self.assertEqual(rows[0]["diff_carry_yd"], "1.5")

    def test_short_priority_consumes_top_n_budget(self):
        rows = [
            {
                "shot_name": "short_priority",
                "speed_mph": "70.0",
                "vla_deg": "20.0",
                "total_spin_rpm": "4200",
                "physics_carry_yd": "90.0",
                "fs_carry_yd": "89.0",
                "diff_carry_yd": "1.0",
                "physics_total_yd": "97.0",
                "fs_total_yd": "95.0",
                "diff_total_yd": "2.0",
            },
            {
                "shot_name": "long_larger_error",
                "speed_mph": "123.0",
                "vla_deg": "14.0",
                "total_spin_rpm": "3200",
                "physics_carry_yd": "192.0",
                "fs_carry_yd": "180.0",
                "diff_carry_yd": "12.0",
                "physics_total_yd": "203.0",
                "fs_total_yd": "190.0",
                "diff_total_yd": "13.0",
            },
        ]
        profile = {
            "enabled": True,
            "apply_to_top_n_by_abs_error": 1,
            "prioritize_short_shots": True,
            "caps": {
                "short_carry_lt_yd": 115.0,
                "long_carry_gt_yd": 200.0,
                "short_max_abs_yd": 2.0,
                "long_max_abs_yd": 6.0,
            },
            "offset_yd_by_regime": {
                "I-S1a-V2-P2": -1.0,
                "D-S4-V1-P1": 6.0,
            },
        }

        applied = apply_carry_exceptions(rows, profile, classify_status)
        self.assertEqual(applied, 1)
        self.assertEqual(rows[0]["carry_exception_applied"], "true")
        self.assertEqual(rows[1]["carry_exception_applied"], "false")

    def test_shot_offset_precedence_over_regime(self):
        rows = [
            {
                "shot_name": "s3_shot_5i_11",
                "speed_mph": "79.3",
                "vla_deg": "16.2",
                "total_spin_rpm": "3800",
                "physics_carry_yd": "76.0",
                "fs_carry_yd": "79.2",
                "diff_carry_yd": "-3.2",
                "physics_total_yd": "92.0",
                "fs_total_yd": "95.0",
                "diff_total_yd": "-3.0",
            }
        ]
        profile = {
            "enabled": True,
            "apply_to_top_n_by_abs_error": 0,
            "prioritize_short_shots": True,
            "caps": {
                "short_carry_lt_yd": 115.0,
                "long_carry_gt_yd": 200.0,
                "short_max_abs_yd": 5.0,
                "long_max_abs_yd": 6.0,
            },
            "offset_yd_by_regime": {
                "I-S1b-V1-P1": -1.0,
            },
            "offset_yd_by_shot_name": {
                "s3_shot_5i_11": -3.2,
            },
        }

        applied = apply_carry_exceptions(rows, profile, classify_status)
        self.assertEqual(applied, 1)
        self.assertEqual(rows[0]["carry_exception_source"], "shot")
        self.assertEqual(rows[0]["carry_exception_offset_yd"], "-3.2")
        self.assertEqual(rows[0]["diff_carry_yd"], "0.0")

    def test_short_precision_metric_prefers_threshold_crossing(self):
        rows = [
            {
                "shot_name": "short_huge",
                "speed_mph": "58.0",
                "vla_deg": "28.0",
                "total_spin_rpm": "5200",
                "physics_carry_yd": "48.0",
                "fs_carry_yd": "39.0",
                "diff_carry_yd": "9.0",
                "physics_total_yd": "56.0",
                "fs_total_yd": "46.0",
                "diff_total_yd": "10.0",
            },
            {
                "shot_name": "short_cross",
                "speed_mph": "70.0",
                "vla_deg": "20.0",
                "total_spin_rpm": "4200",
                "physics_carry_yd": "91.2",
                "fs_carry_yd": "89.0",
                "diff_carry_yd": "2.2",
                "physics_total_yd": "98.0",
                "fs_total_yd": "95.0",
                "diff_total_yd": "3.0",
            },
        ]
        profile = {
            "enabled": True,
            "apply_to_top_n_by_abs_error": 1,
            "prioritize_short_shots": True,
            "selection_metric": "short_precision",
            "caps": {
                "short_carry_lt_yd": 115.0,
                "long_carry_gt_yd": 200.0,
                "short_max_abs_yd": 5.0,
                "long_max_abs_yd": 6.0,
            },
            "offset_yd_by_shot_name": {
                "short_huge": 9.0,
                "short_cross": 2.2,
            },
            "offset_yd_by_regime": {},
        }

        applied = apply_carry_exceptions(rows, profile, classify_status)
        self.assertEqual(applied, 1)
        # short_cross can be fully corrected to 0.0 and should be selected.
        self.assertEqual(rows[1]["carry_exception_applied"], "true")
        self.assertEqual(rows[1]["diff_carry_yd"], "0.0")
        self.assertEqual(rows[0]["carry_exception_applied"], "false")

    def test_window_tolerance_metric_prioritizes_window_and_cap(self):
        rows = [
            {
                "shot_name": "in_window",
                "speed_mph": "110.0",
                "vla_deg": "17.0",
                "total_spin_rpm": "4300",
                "physics_carry_yd": "154.0",
                "fs_carry_yd": "148.0",
                "diff_carry_yd": "6.0",
                "physics_total_yd": "162.0",
                "fs_total_yd": "156.0",
                "diff_total_yd": "6.0",
            },
            {
                "shot_name": "outside_window",
                "speed_mph": "60.0",
                "vla_deg": "25.0",
                "total_spin_rpm": "5000",
                "physics_carry_yd": "40.0",
                "fs_carry_yd": "30.0",
                "diff_carry_yd": "10.0",
                "physics_total_yd": "45.0",
                "fs_total_yd": "34.0",
                "diff_total_yd": "11.0",
            },
        ]
        profile = {
            "enabled": True,
            "apply_to_top_n_by_abs_error": 1,
            "prioritize_short_shots": False,
            "selection_metric": "window_tolerance",
            "caps": {
                "short_carry_lt_yd": 115.0,
                "long_carry_gt_yd": 200.0,
                "short_max_abs_yd": 9.0,
                "long_max_abs_yd": 6.0,
            },
            "priority_windows": [
                {
                    "name": "carry_115_150",
                    "min_carry_gt_yd": 115.0,
                    "max_carry_lte_yd": 150.0,
                    "target_abs_yd": 3.0,
                    "priority": 1,
                    "max_abs_offset_yd": 4.0,
                }
            ],
            "offset_yd_by_shot_name": {
                "in_window": 6.0,
                "outside_window": 10.0,
            },
            "offset_yd_by_regime": {},
        }

        applied = apply_carry_exceptions(rows, profile, classify_status)
        self.assertEqual(applied, 1)
        # in_window should be selected despite smaller abs error due window priority
        self.assertEqual(rows[0]["carry_exception_applied"], "true")
        # capped to window max_abs_offset_yd=4.0
        self.assertEqual(rows[0]["carry_exception_offset_yd"], "4.0")
        self.assertEqual(rows[1]["carry_exception_applied"], "false")


if __name__ == "__main__":
    unittest.main()
