#!/usr/bin/env python
"""
FlightScope Trajectory Optimizer scraper.

Reads shot data from assets/data/*.json, enters each shot into
https://trajectory.flightscope.com/, and captures the carry/total/apex results.

Outputs: assets/data/SOT/flightscope_reference.json

Requirements:
    pip install selenium

Usage:
    python tools/shot_calibration/flightscope_scraper.py
    python tools/shot_calibration/flightscope_scraper.py --shots driver1.json wood1.json
    python tools/shot_calibration/flightscope_scraper.py --visible
"""

import argparse
import json
import math
import os
import re
import sys
import time
from pathlib import Path

from selenium import webdriver
from selenium.webdriver.chrome.options import Options
from selenium.webdriver.common.by import By
from selenium.webdriver.common.keys import Keys
from selenium.webdriver.support.ui import WebDriverWait
from selenium.webdriver.support import expected_conditions as EC

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
DATA_DIR = REPO_ROOT / "assets" / "data"
OUTPUT_FILE = REPO_ROOT / "assets" / "data" / "SOT" / "flightscope_reference.json"
URL = "https://trajectory.flightscope.com/"

# Map of shot name -> filename for the standard regression set.
DEFAULT_SHOTS = {
    "driver1": "driver1.json",
    "driver2": "driver2.json",
    "driver3": "driver3.json",
    "driver4": "driver4.json",
    "5iron": "5iron.json",
    "wood1": "wood1.json",
    "wood2": "wood2.json",
    "wedge1": "wedge_test_shot.json",
    "wedge2": "wedge_test_shot2.json",
    "wood_low": "wood_low_test_shot.json",
    "approach_mid": "approach_mid_iron_test_shot.json",
    "checked": "checked_test_shot.json",
    "flop": "flop_test_shot.json",
    "p_wedge_1": "p_wedge_shot_1.json",
    "wedge_shot_1": "wedge_shot_1.json",
    "wedge_shot_2": "wedge_shot_2.json",
}


def load_shot_data(filename: str) -> dict:
    """Load a shot JSON file and extract ball data fields."""
    path = DATA_DIR / filename
    if not path.exists():
        print(f"  WARNING: {path} not found, skipping")
        return None

    with open(path) as f:
        data = json.load(f)

    # Handle BallData wrapper
    ball = data.get("BallData", data)

    speed = ball.get("Speed", 0)
    vla = ball.get("VLA", 0)
    hla = ball.get("HLA", 0)
    total_spin = ball.get("TotalSpin", 0)
    spin_axis = ball.get("SpinAxis", 0)

    # Compute backspin/sidespin from total spin + axis if not provided
    backspin = ball.get("BackSpin")
    sidespin = ball.get("SideSpin")
    if backspin is None or sidespin is None:
        axis_rad = math.radians(spin_axis)
        backspin = total_spin * math.cos(axis_rad)
        sidespin = total_spin * math.sin(axis_rad)

    return {
        "speed_mph": speed,
        "vla_deg": vla,
        "hla_deg": hla,
        "total_spin_rpm": total_spin,
        "backspin_rpm": backspin,
        "sidespin_rpm": sidespin,
        "spin_axis_deg": spin_axis,
        "filename": filename,
    }


def _log(msg):
    print(f"  [scraper] {msg}")


def _dismiss_weather_popup(driver, wait):
    """Dismiss the 'Weather Condition Setup' popup by clicking SAVE."""
    _log("Dismissing weather popup...")
    try:
        save_btn = wait.until(
            EC.element_to_be_clickable((By.XPATH,
                "//button[contains(translate(., 'save', 'SAVE'), 'SAVE')]"))
        )
        save_btn.click()
        time.sleep(1)
        _log("Weather popup dismissed.")
    except Exception:
        _log("No weather popup found, continuing...")
        # Try alternative modal close buttons
        try:
            for btn in driver.find_elements(By.CSS_SELECTOR,
                    ".v-dialog button, .modal button, [class*='dialog'] button"):
                txt = btn.text.strip().upper()
                if txt in ("SAVE", "OK", "CLOSE"):
                    btn.click()
                    time.sleep(1)
                    break
        except Exception:
            pass


def _toggle_wind_off(driver):
    """Toggle wind OFF if it's currently ON."""
    _log("Toggling wind OFF...")
    try:
        off_buttons = driver.find_elements(By.XPATH,
            "//*[contains(translate(., 'off', 'OFF'), 'OFF')]"
        )
        # Prefer a button-like element with exactly "OFF" text
        for btn in off_buttons:
            if btn.is_displayed() and btn.text.strip().upper() == "OFF":
                btn.click()
                time.sleep(0.5)
                _log("Wind toggled OFF.")
                return
        # Fallback: any displayed OFF element
        for btn in off_buttons:
            if btn.is_displayed() and btn.tag_name in ("button", "div", "span", "a"):
                btn.click()
                time.sleep(0.5)
                _log("Wind toggled OFF (fallback).")
                return
        _log("Could not find Wind OFF toggle.")
    except Exception as e:
        _log(f"Error toggling wind: {e}")


def _fill_field_by_label(driver, label_fragment, value):
    """Find an input field by nearby label text and fill it with value."""
    label_els = driver.find_elements(By.XPATH,
        f"//*[contains(text(), '{label_fragment}')]"
    )
    for label_el in label_els:
        if not label_el.is_displayed():
            continue
        # Search parent and grandparent for an input
        for ancestor_xpath in ["./..","./../.."]:
            try:
                ancestor = label_el.find_element(By.XPATH, ancestor_xpath)
                inputs = ancestor.find_elements(By.TAG_NAME, "input")
                for inp in inputs:
                    if inp.is_displayed():
                        inp.click()
                        inp.send_keys(Keys.CONTROL + "a")
                        inp.send_keys(str(value))
                        return True
            except Exception:
                continue
    return False


def _set_direction_dropdown(driver, label_fragment, direction):
    """Set a Left/Right dropdown near a label to the given direction."""
    try:
        label_els = driver.find_elements(By.XPATH,
            f"//*[contains(text(), '{label_fragment}')]"
        )
        for label_el in label_els:
            if not label_el.is_displayed():
                continue
            # Walk up to find a select or clickable direction element
            for depth in ["./..","./../.."]:
                try:
                    ancestor = label_el.find_element(By.XPATH, depth)
                    # Native select
                    selects = ancestor.find_elements(By.TAG_NAME, "select")
                    for sel in selects:
                        if sel.is_displayed():
                            from selenium.webdriver.support.ui import Select
                            Select(sel).select_by_visible_text(direction)
                            return True
                    # Vuetify-style: clickable with direction text
                    els = ancestor.find_elements(By.XPATH,
                        f".//*[contains(text(), '{direction}')]"
                    )
                    for el in els:
                        if el.is_displayed():
                            el.click()
                            return True
                except Exception:
                    continue
    except Exception:
        pass
    return False


def _fill_shot_form(driver, shot_data):
    """Fill all form fields for a single shot."""
    # VLA
    if not _fill_field_by_label(driver, "Launch V", str(round(shot_data["vla_deg"], 1))):
        _log("WARNING: Could not fill Launch V field")

    # Ball speed
    if not _fill_field_by_label(driver, "Ball", str(round(shot_data["speed_mph"], 1))):
        _log("WARNING: Could not fill Ball speed field")

    # HLA (absolute value + direction)
    hla = shot_data["hla_deg"]
    if not _fill_field_by_label(driver, "Launch H", str(round(abs(hla), 1))):
        _log("WARNING: Could not fill Launch H field")
    if hla < 0:
        _set_direction_dropdown(driver, "Launch H", "Left")
    elif hla > 0:
        _set_direction_dropdown(driver, "Launch H", "Right")

    # Total spin
    if not _fill_field_by_label(driver, "Spin (", str(round(shot_data["total_spin_rpm"]))):
        # Fallback: try just "Spin" but not "Spin Axis"
        if not _fill_field_by_label(driver, "Spin", str(round(shot_data["total_spin_rpm"]))):
            _log("WARNING: Could not fill Spin field")

    # Spin axis (absolute value + direction)
    sa = shot_data["spin_axis_deg"]
    if not _fill_field_by_label(driver, "Spin Axis", str(round(abs(sa), 1))):
        _log("WARNING: Could not fill Spin Axis field")
    if sa < 0:
        _set_direction_dropdown(driver, "Spin Axis", "Left")
    elif sa > 0:
        _set_direction_dropdown(driver, "Spin Axis", "Right")


def _click_display_shot(driver):
    """Click the DISPLAY SHOT button."""
    try:
        btn = driver.find_element(By.XPATH,
            "//button[contains(translate(., 'display shot', 'DISPLAY SHOT'), 'DISPLAY SHOT')]"
        )
        btn.click()
        return True
    except Exception:
        pass
    # Fallback: search all buttons
    try:
        for btn in driver.find_elements(By.TAG_NAME, "button"):
            if btn.is_displayed():
                text = btn.text.strip().upper()
                if "DISPLAY" in text or "SHOT" in text:
                    btn.click()
                    return True
    except Exception:
        pass
    return False


def _parse_distance(text: str) -> float:
    """Extract numeric value from a text string like '165.3 yds' or '95.2'."""
    match = re.search(r"[\d.]+", text)
    if match:
        return float(match.group())
    return 0.0


def _read_results_row(driver, row_index=0):
    """Read carry/roll/total/height from the results table."""
    result = {}
    try:
        tables = driver.find_elements(By.TAG_NAME, "table")
        for table in tables:
            if not table.is_displayed():
                continue
            rows = table.find_elements(By.TAG_NAME, "tr")
            # Find header row to map column indices
            headers = []
            for row in rows:
                ths = row.find_elements(By.TAG_NAME, "th")
                if ths:
                    headers = [th.text.strip().lower() for th in ths]
                    break

            if not headers:
                continue

            # Find data rows (skip header)
            data_rows = []
            for row in rows:
                tds = row.find_elements(By.TAG_NAME, "td")
                if tds:
                    data_rows.append([td.text.strip() for td in tds])

            if not data_rows:
                continue

            # Read first row (fresh page reload means only 1 row exists)
            target_row = data_rows[0] if data_rows else None
            if not target_row:
                continue

            _log(f"Table headers: {headers}")
            _log(f"Data row: {target_row}")

            # Map known column names to result keys
            col_map = {
                "carry": "carry_yd",
                "carry (yd)": "carry_yd",
                "roll": "roll_yd",
                "roll (yd)": "roll_yd",
                "total": "total_yd",
                "total (yd)": "total_yd",
                "height": "apex_ft",
                "height (ft)": "apex_ft",
                "lateral": "lateral_yd",
                "lateral (yd)": "lateral_yd",
                "time": "time_s",
                "time (s)": "time_s",
            }

            for i, header in enumerate(headers):
                if i < len(target_row):
                    for pattern, key in col_map.items():
                        if pattern in header:
                            try:
                                result[key] = float(target_row[i])
                            except ValueError:
                                result[key] = _parse_distance(target_row[i])
                            break

            if result:
                return result

    except Exception as e:
        _log(f"Error reading results table: {e}")

    return result


def scrape_flightscope(shots: dict, visible: bool = False) -> dict:
    """
    Automate FlightScope trajectory optimizer to get carry/total/apex.

    Uses Selenium with Chrome.
    """
    chrome_options = Options()
    if not visible:
        chrome_options.add_argument("--headless=new")
    chrome_options.add_argument("--window-size=1920,1080")

    _log("Launching Chrome...")
    driver = webdriver.Chrome(options=chrome_options)
    wait = WebDriverWait(driver, 15)
    results = {}

    try:
        # Navigate once and do initial setup
        _log(f"Navigating to {URL}")
        driver.get(URL)

        try:
            wait.until(EC.presence_of_element_located((By.TAG_NAME, "input")))
        except Exception:
            _log("WARNING: No inputs found after 15s")

        time.sleep(2)

        # Dismiss weather popup + toggle wind off (one-time setup)
        _dismiss_weather_popup(driver, wait)
        time.sleep(1)
        _toggle_wind_off(driver)
        time.sleep(1)

        # Process each shot on the same page
        for shot_name, shot_data in shots.items():
            if shot_data is None:
                continue

            _log(f"Processing {shot_name}: speed={shot_data['speed_mph']:.1f} mph, "
                 f"VLA={shot_data['vla_deg']:.1f}, spin={shot_data['total_spin_rpm']:.0f} rpm, "
                 f"axis={shot_data['spin_axis_deg']:.1f}")

            try:
                # Fill form fields
                _fill_shot_form(driver, shot_data)
                time.sleep(0.5)

                # Snapshot existing <td> count before clicking
                td_count_before = len(driver.find_elements(By.CSS_SELECTOR, "table td"))
                _log(f"  td count before DISPLAY SHOT: {td_count_before}")

                # Click DISPLAY SHOT
                if not _click_display_shot(driver):
                    _log(f"ERROR: Could not click DISPLAY SHOT for {shot_name}")
                    continue

                # Wait for NEW <td> elements to appear (up to 15s)
                try:
                    WebDriverWait(driver, 15).until(
                        lambda d: len(d.find_elements(By.CSS_SELECTOR, "table td")) > td_count_before
                    )
                    td_count_after = len(driver.find_elements(By.CSS_SELECTOR, "table td"))
                    _log(f"  td count after DISPLAY SHOT: {td_count_after}")
                except Exception:
                    td_count_after = len(driver.find_elements(By.CSS_SELECTOR, "table td"))
                    _log(f"WARNING: No new table data after 15s (before={td_count_before}, after={td_count_after})")
                time.sleep(1)

                # Read results — first row is the newest (FlightScope prepends)
                table_result = _read_results_row(driver)
                _log(f"  parsed result: {table_result}")

                result_entry = {
                    "filename": shot_data["filename"],
                    "speed_mph": shot_data["speed_mph"],
                    "vla_deg": shot_data["vla_deg"],
                    "hla_deg": shot_data["hla_deg"],
                    "total_spin_rpm": shot_data["total_spin_rpm"],
                    "spin_axis_deg": shot_data["spin_axis_deg"],
                }
                result_entry.update(table_result)

                results[shot_name] = result_entry
                _log(f"  -> carry={table_result.get('carry_yd', '?')} yd, "
                     f"total={table_result.get('total_yd', '?')} yd, "
                     f"apex={table_result.get('apex_ft', '?')} ft")

            except Exception as e:
                _log(f"ERROR scraping {shot_name}: {e}")
                continue

    finally:
        driver.quit()
        _log("Browser closed.")

    return results


def create_manual_reference(shots: dict) -> dict:
    """
    Create a template reference file for manual entry.
    Use this when the automated scraper can't access FlightScope.
    """
    results = {}
    for shot_name, shot_data in shots.items():
        if shot_data is None:
            continue
        results[shot_name] = {
            "filename": shot_data["filename"],
            "speed_mph": shot_data["speed_mph"],
            "vla_deg": shot_data["vla_deg"],
            "total_spin_rpm": shot_data["total_spin_rpm"],
            "spin_axis_deg": shot_data["spin_axis_deg"],
            "carry_yd": 0.0,
            "total_yd": 0.0,
            "apex_ft": 0.0,
            "_note": "Fill in FlightScope values manually",
        }
    return results


def main():
    parser = argparse.ArgumentParser(description="Scrape FlightScope trajectory data for calibration")
    parser.add_argument("--shots", nargs="*", help="Specific shot filenames to scrape (default: all)")
    parser.add_argument("--template", action="store_true", help="Generate empty template for manual entry")
    parser.add_argument("--visible", action="store_true", help="Run with visible Chrome window (default: headless)")
    parser.add_argument("--output", type=str, default=str(OUTPUT_FILE), help="Output file path")
    args = parser.parse_args()

    # Build shot list
    if args.shots:
        shot_map = {}
        for filename in args.shots:
            name = Path(filename).stem
            shot_map[name] = filename
    else:
        shot_map = DEFAULT_SHOTS

    # Load shot data
    shots = {}
    for name, filename in shot_map.items():
        data = load_shot_data(filename)
        if data:
            shots[name] = data

    print(f"Loaded {len(shots)} shots")

    if args.template:
        results = create_manual_reference(shots)
    else:
        results = scrape_flightscope(shots, visible=args.visible)

    # Write output
    output_path = Path(args.output)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with open(output_path, "w") as f:
        json.dump(results, f, indent=2)

    print(f"\nWrote {len(results)} entries to {output_path}")


if __name__ == "__main__":
    main()
