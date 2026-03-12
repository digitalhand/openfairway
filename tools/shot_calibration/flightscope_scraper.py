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
    python tools/shot_calibration/flightscope_scraper.py --shots driver2 --visible
    python tools/shot_calibration/flightscope_scraper.py --shots driver1.json wood1.json
    python tools/shot_calibration/flightscope_scraper.py --visible
"""

import argparse
import json
import math
import os
import re
import shutil
import sys
import time
from pathlib import Path

from selenium import webdriver
from selenium.common.exceptions import TimeoutException
from selenium.webdriver.chrome.options import Options
from selenium.webdriver.common.action_chains import ActionChains
from selenium.webdriver.common.by import By
from selenium.webdriver.common.keys import Keys
from selenium.webdriver.support.ui import WebDriverWait
from selenium.webdriver.support import expected_conditions as EC

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
DATA_DIR = REPO_ROOT / "assets" / "data"
OUTPUT_FILE = REPO_ROOT / "assets" / "data" / "SOT" / "flightscope_reference.json"
URL = "https://trajectory.flightscope.com/"
PREFERRED_BROWSER_PATHS = [
    "/usr/bin/google-chrome-stable",
]
BROWSER_COMMAND_PREFERENCES = [
    "google-chrome",
    "google-chrome-stable",
    "chrome",
    "chromium-browser",
    "chromium",
    "brave-browser",
    "brave",
]
FIELD_ACTION_DELAY_SEC = 2.0
PRE_DISPLAY_CLICK_DELAY_SEC = 2.0
RESULT_WAIT_TIMEOUT_SEC = 15
SUBMIT_RETRY_COUNT = 2
RETRY_COOLDOWN_SEC = 2.0
DEBUG_ARTIFACT_DIR = REPO_ROOT / "tools" / "shot_calibration" / "debug_runs"
SNAP_BRAVE_BINARY = Path("/snap/brave/current/opt/brave.com/brave/brave-browser")

# Map of shot name -> filename for the standard regression and calibration set.
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
    "approach_test_shot": "approach_test_shot.json",
    "bump_and_run": "bump_and_run.json",
    "bump_and_run_slow": "bump_and_run_slow.json",
    "bump_test_shot": "bump_test_shot.json",
    "checked": "checked_test_shot.json",
    "chip_test_shot": "chip_test_shot.json",
    "drive_test_shot": "drive_test_shot.json",
    "flop": "flop_test_shot.json",
    "p_wedge_1": "p_wedge_shot_1.json",
    "topped_test_shot": "topped_test_shot.json",
    "wedge_shot_1": "wedge_shot_1.json",
    "wedge_shot_2": "wedge_shot_2.json",
}


class SubmitBlockedError(RuntimeError):
    """Raised when a submit attempt is blocked and should not be retried."""

    def __init__(self, reason: str):
        super().__init__(reason)
        self.reason = reason


class SubmitAttemptError(RuntimeError):
    """Raised for retryable submit failures."""

    def __init__(self, reason: str, message: str):
        super().__init__(message)
        self.reason = reason


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


def _create_driver(visible: bool):
    """Create a Selenium ChromeDriver session (Chrome preferred, Brave fallback)."""
    browser_cmd, browser_path = _resolve_browser_binary()
    if browser_path is None:
        raise FileNotFoundError(
            "Could not find a supported browser command in PATH: "
            f"{', '.join(BROWSER_COMMAND_PREFERENCES)}"
        )

    chrome_options = Options()
    if not visible:
        chrome_options.add_argument("--headless=new")
    chrome_options.add_argument("--window-size=1920,1080")

    # Anti-detection: hide automation signals to improve reCAPTCHA v3 score
    chrome_options.add_argument("--disable-blink-features=AutomationControlled")
    chrome_options.add_experimental_option("excludeSwitches", ["enable-automation"])
    chrome_options.add_experimental_option("useAutomationExtension", False)
    chrome_options.add_argument(
        "--user-agent=Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 "
        "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"
    )

    # Enable performance logging for network diagnostics
    chrome_options.set_capability("goog:loggingPrefs", {"performance": "ALL"})

    chrome_options.binary_location = browser_path
    _log(f"Launching browser command: {browser_cmd} ({browser_path})")
    driver = webdriver.Chrome(options=chrome_options)

    # Remove navigator.webdriver flag via CDP
    driver.execute_cdp_cmd("Page.addScriptToEvaluateOnNewDocument", {
        "source": "Object.defineProperty(navigator, 'webdriver', {get: () => undefined});"
    })

    return driver


def _resolve_browser_binary():
    """Resolve preferred browser binary path for Selenium."""
    for raw_path in PREFERRED_BROWSER_PATHS:
        explicit_path = Path(raw_path)
        if explicit_path.exists():
            return explicit_path.name, str(explicit_path)

    for command in BROWSER_COMMAND_PREFERENCES:
        resolved = shutil.which(command)
        if resolved is None:
            continue

        # Snap launcher points to /usr/bin/snap; use real Brave binary when available.
        if command in ("brave", "brave-browser") and resolved.startswith("/snap/bin/"):
            if SNAP_BRAVE_BINARY.exists():
                return command, str(SNAP_BRAVE_BINARY)

        return command, resolved

    return None, None


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
                        # Use native setter + dispatch events to trigger Vue/Vuetify reactivity
                        try:
                            driver.execute_script("""
                                const nativeSetter = Object.getOwnPropertyDescriptor(
                                    window.HTMLInputElement.prototype, 'value').set;
                                nativeSetter.call(arguments[0], arguments[1]);
                                arguments[0].dispatchEvent(new Event('input', { bubbles: true }));
                                arguments[0].dispatchEvent(new Event('change', { bubbles: true }));
                            """, inp, str(value))
                        except Exception:
                            # Fallback to send_keys if JS setter fails
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


def _wait_after_form_action(label):
    """Apply fixed pacing between form actions."""
    _log(f"Waiting {FIELD_ACTION_DELAY_SEC:.1f}s after {label}")
    time.sleep(FIELD_ACTION_DELAY_SEC)


def _fill_shot_form(driver, shot_data):
    """Fill all form fields for a single shot."""
    # VLA
    if not _fill_field_by_label(driver, "Launch V", str(round(shot_data["vla_deg"], 1))):
        _log("WARNING: Could not fill Launch V field")
    _wait_after_form_action("Launch V")

    # Ball speed
    if not _fill_field_by_label(driver, "Ball", str(round(shot_data["speed_mph"], 1))):
        _log("WARNING: Could not fill Ball speed field")
    _wait_after_form_action("Ball speed")

    # HLA (absolute value + direction)
    hla = shot_data["hla_deg"]
    if not _fill_field_by_label(driver, "Launch H", str(round(abs(hla), 1))):
        _log("WARNING: Could not fill Launch H field")
    if hla < 0:
        _set_direction_dropdown(driver, "Launch H", "Left")
    elif hla > 0:
        _set_direction_dropdown(driver, "Launch H", "Right")
    _wait_after_form_action("Launch H / direction")

    # Total spin
    if not _fill_field_by_label(driver, "Spin (", str(round(shot_data["total_spin_rpm"]))):
        # Fallback: try just "Spin" but not "Spin Axis"
        if not _fill_field_by_label(driver, "Spin", str(round(shot_data["total_spin_rpm"]))):
            _log("WARNING: Could not fill Spin field")
    _wait_after_form_action("Spin")

    # Spin axis (absolute value + direction)
    sa = shot_data["spin_axis_deg"]
    if not _fill_field_by_label(driver, "Spin Axis", str(round(abs(sa), 1))):
        _log("WARNING: Could not fill Spin Axis field")
    if sa < 0:
        _set_direction_dropdown(driver, "Spin Axis", "Left")
    elif sa > 0:
        _set_direction_dropdown(driver, "Spin Axis", "Right")
    _wait_after_form_action("Spin Axis / direction")


def _is_button_clickable(button) -> bool:
    """Check if a candidate submit button is interactable."""
    try:
        if not button.is_displayed() or not button.is_enabled():
            return False
        if button.get_attribute("disabled"):
            return False
        aria_disabled = (button.get_attribute("aria-disabled") or "").strip().lower()
        if aria_disabled == "true":
            return False
        return True
    except Exception:
        return False


def _find_display_shot_button(driver):
    """Find a visible, enabled submit button for shot display."""
    selector_order = [
        (By.CSS_SELECTOR, ".bottom-actions button[type='submit']", "bottom_actions_submit"),
        (By.XPATH, "//button[@type='submit']", "submit_type"),
        (
            By.XPATH,
            "//button[contains(translate(., 'display shot', 'DISPLAY SHOT'), 'DISPLAY SHOT')]",
            "display_shot_text",
        ),
    ]

    for by, selector, source in selector_order:
        try:
            for btn in driver.find_elements(by, selector):
                if _is_button_clickable(btn):
                    return btn, source
        except Exception:
            continue

    return None, "not_found"


def _is_recaptcha_challenge_visible(driver) -> bool:
    """Detect a visible captcha challenge frame, not just the passive badge."""
    challenge_selectors = [
        "iframe[src*='recaptcha/api2/bframe']",
        "iframe[title*='challenge']",
        "iframe[src*='hcaptcha']",
    ]
    try:
        for selector in challenge_selectors:
            for frame in driver.find_elements(By.CSS_SELECTOR, selector):
                if not frame.is_displayed():
                    continue
                size = frame.size or {}
                if (size.get("width", 0) or 0) > 120 and (size.get("height", 0) or 0) > 80:
                    return True
    except Exception:
        return False
    return False


def _has_active_overlay_or_dialog(driver) -> bool:
    """Detect active, visible overlays/dialogs that can block interactions."""
    script = """
const selectors = [
  '.v-overlay--active',
  '.v-dialog--active',
  '.v-dialog__content--active',
  '.modal.show',
  '[role="dialog"][aria-modal="true"]',
];
for (const selector of selectors) {
  for (const el of document.querySelectorAll(selector)) {
    const style = window.getComputedStyle(el);
    if (style.display === 'none' || style.visibility === 'hidden') {
      continue;
    }
    const rect = el.getBoundingClientRect();
    if (rect.width < 20 || rect.height < 20) {
      continue;
    }
    return true;
  }
}
return false;
"""
    try:
        return bool(driver.execute_script(script))
    except Exception:
        return False


def _describe_click_obstruction(driver, element) -> str:
    """
    Return obstruction details when the click target center is covered.
    Empty string means no obstruction was detected.
    """
    script = """
const target = arguments[0];
if (!target) return 'missing_target';
const rect = target.getBoundingClientRect();
if (rect.width < 1 || rect.height < 1) return 'zero_size_target';
const x = rect.left + rect.width / 2;
const y = rect.top + rect.height / 2;
if (x < 0 || y < 0 || x > window.innerWidth || y > window.innerHeight) {
  return 'target_offscreen';
}
const topEl = document.elementFromPoint(x, y);
if (!topEl) return 'no_element_at_click_point';
if (target === topEl || target.contains(topEl)) return '';
const tag = (topEl.tagName || '').toLowerCase();
const id = topEl.id ? '#' + topEl.id : '';
const cls = (typeof topEl.className === 'string' && topEl.className.trim())
  ? '.' + topEl.className.trim().replace(/\\s+/g, '.')
  : '';
return `covered_by:${tag}${id}${cls}`;
"""
    try:
        detail = driver.execute_script(script, element)
    except Exception:
        return "obstruction_check_failed"
    return detail or ""


def _click_display_shot(driver, wait):
    """Click the DISPLAY SHOT button."""
    try:
        def resolve_submit_button(drv):
            button, selector_source = _find_display_shot_button(drv)
            if button is None:
                return False
            return button, selector_source

        btn, source = wait.until(resolve_submit_button)
        if btn is None:
            return {"ok": False, "reason": "submit_not_clickable", "detail": "submit_button_not_found"}

        _log(f"Resolved submit button via selector: {source}")
        driver.execute_script(
            "arguments[0].scrollIntoView({block:'center', inline:'center'});",
            btn,
        )
        obstruction = _describe_click_obstruction(driver, btn)
        if obstruction:
            _log(f"Submit button obstruction detected: {obstruction}")
            if ("overlay" in obstruction.lower() or "dialog" in obstruction.lower()
                    or _has_active_overlay_or_dialog(driver)):
                return {"ok": False, "reason": "blocked_overlay", "detail": obstruction}
            return {"ok": False, "reason": "submit_not_clickable", "detail": obstruction}

        if _is_recaptcha_challenge_visible(driver):
            return {"ok": False, "reason": "blocked_captcha", "detail": "captcha_challenge_visible"}

        _log(f"Waiting {PRE_DISPLAY_CLICK_DELAY_SEC:.1f}s before pressing DISPLAY SHOT")
        time.sleep(PRE_DISPLAY_CLICK_DELAY_SEC)

        # Simulate mouse movement through form inputs to improve reCAPTCHA v3 score
        try:
            actions = ActionChains(driver)
            for inp in driver.find_elements(By.TAG_NAME, "input")[:3]:
                if inp.is_displayed():
                    actions.move_to_element(inp).pause(0.3)
            actions.move_to_element(btn).pause(0.5).click().perform()
        except Exception:
            _log("ActionChains click failed, trying native click")
            try:
                btn.click()
            except Exception:
                _log("Native click failed, trying JS requestSubmit fallback")
                driver.execute_script("""
                    const form = arguments[0].closest('form');
                    if (form && form.requestSubmit) form.requestSubmit(arguments[0]);
                    else arguments[0].click();
                """, btn)
        return {"ok": True, "reason": None, "detail": None}
    except TimeoutException:
        _log("ERROR: DISPLAY SHOT button was not ready in time")
        return {"ok": False, "reason": "submit_not_clickable", "detail": "submit_button_timeout"}
    except Exception:
        _log("ERROR: Failed clicking DISPLAY SHOT")
        return {"ok": False, "reason": "submit_not_clickable", "detail": "submit_click_exception"}


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


def _extract_api_requests(driver):
    """Extract API requests to FlightScope from performance logs."""
    api_entries = []
    try:
        logs = driver.get_log("performance")
        for entry in logs:
            try:
                msg = json.loads(entry["message"])["message"]
                method = msg.get("method", "")
                params = msg.get("params", {})
                # Capture request/response events for trajectory API
                if method == "Network.requestWillBeSent":
                    url = params.get("request", {}).get("url", "")
                    if "flightscope" in url or "trajectory" in url:
                        api_entries.append({
                            "type": "request",
                            "url": url,
                            "method": params.get("request", {}).get("method"),
                            "postData": params.get("request", {}).get("postData"),
                            "timestamp": entry.get("timestamp"),
                        })
                elif method == "Network.responseReceived":
                    url = params.get("response", {}).get("url", "")
                    if "flightscope" in url or "trajectory" in url:
                        api_entries.append({
                            "type": "response",
                            "url": url,
                            "status": params.get("response", {}).get("status"),
                            "timestamp": entry.get("timestamp"),
                        })
            except (json.JSONDecodeError, KeyError):
                continue
    except Exception:
        pass
    return api_entries


def _capture_debug_artifacts(driver, shot_name: str, attempt: int, reason: str):
    """Capture debugging artifacts when submit did not produce results."""
    DEBUG_ARTIFACT_DIR.mkdir(parents=True, exist_ok=True)
    timestamp = int(time.time())
    safe_reason = re.sub(r"[^a-zA-Z0-9_-]", "_", reason)[:40]
    base = f"{shot_name}_attempt{attempt}_{timestamp}_{safe_reason}"
    html_path = DEBUG_ARTIFACT_DIR / f"{base}.html"
    screenshot_path = DEBUG_ARTIFACT_DIR / f"{base}.png"
    network_path = DEBUG_ARTIFACT_DIR / f"{base}_network.json"

    try:
        html_path.write_text(driver.page_source, encoding="utf-8")
        driver.save_screenshot(str(screenshot_path))
        api_requests = _extract_api_requests(driver)
        network_path.write_text(json.dumps(api_requests, indent=2), encoding="utf-8")
        _log(f"Saved debug artifacts: {html_path.name}, {screenshot_path.name}, {network_path.name}")
    except Exception as e:
        _log(f"WARNING: Failed to save debug artifacts: {e}")


def _collect_result_state(driver):
    """Collect lightweight table state used to detect result updates."""
    state = {
        "rows_count": 0,
        "first_row": (),
        "has_no_data": False,
    }

    try:
        for table in driver.find_elements(By.TAG_NAME, "table"):
            if not table.is_displayed():
                continue

            rows = table.find_elements(By.TAG_NAME, "tr")
            data_rows = []
            for row in rows:
                tds = row.find_elements(By.TAG_NAME, "td")
                if tds:
                    values = tuple(td.text.strip() for td in tds)
                    data_rows.append(values)

            if data_rows:
                state["rows_count"] = len(data_rows)
                state["first_row"] = data_rows[0]
                return state

            table_text = table.text.strip().lower()
            if "no data available" in table_text:
                state["has_no_data"] = True
    except Exception:
        return state

    return state


def _has_result_updated(before_state, current_state):
    """Detect if submitting DISPLAY SHOT produced new table content."""
    if current_state["rows_count"] > before_state["rows_count"]:
        return True
    if before_state["has_no_data"] and not current_state["has_no_data"] and current_state["rows_count"] > 0:
        return True
    if current_state["rows_count"] > 0 and current_state["first_row"] != before_state["first_row"]:
        return True
    return False


def _build_failed_result_entry(shot_data, reason: str) -> dict:
    """Build a failed result payload while preserving shot inputs."""
    return {
        "filename": shot_data["filename"],
        "speed_mph": shot_data["speed_mph"],
        "vla_deg": shot_data["vla_deg"],
        "hla_deg": shot_data["hla_deg"],
        "total_spin_rpm": shot_data["total_spin_rpm"],
        "spin_axis_deg": shot_data["spin_axis_deg"],
        "_status": "failed",
        "_reason": reason,
    }


def scrape_flightscope(
    shots: dict,
    visible: bool = False,
) -> dict:
    """
    Automate FlightScope trajectory optimizer to get carry/total/apex.

    Uses Selenium ChromeDriver with Brave.
    """
    driver = _create_driver(visible)
    wait = WebDriverWait(driver, 15)
    results = {}
    shot_statuses = {}

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

            table_result = None
            failure_reason = "submit_failed"
            for attempt in range(1, SUBMIT_RETRY_COUNT + 1):
                try:
                    _log(f"  submit attempt {attempt}/{SUBMIT_RETRY_COUNT}")
                    _fill_shot_form(driver, shot_data)
                    before_state = _collect_result_state(driver)
                    _log(f"  table state before submit: {before_state}")

                    click_result = _click_display_shot(driver, wait)
                    if not click_result["ok"]:
                        failure_reason = click_result["reason"] or failure_reason
                        if failure_reason.startswith("blocked_"):
                            raise SubmitBlockedError(failure_reason)
                        raise SubmitAttemptError(
                            failure_reason,
                            f"DISPLAY SHOT click failed ({click_result.get('detail', 'no detail')})",
                        )

                    try:
                        WebDriverWait(driver, RESULT_WAIT_TIMEOUT_SEC).until(
                            lambda d: _has_result_updated(before_state, _collect_result_state(d))
                        )
                    except TimeoutException as e:
                        if _is_recaptcha_challenge_visible(driver):
                            raise SubmitBlockedError("blocked_captcha") from e
                        if _has_active_overlay_or_dialog(driver):
                            raise SubmitBlockedError("blocked_overlay") from e
                        after_state = _collect_result_state(driver)
                        if not _has_result_updated(before_state, after_state):
                            raise SubmitBlockedError("blocked_no_result_update") from e
                        raise SubmitAttemptError("result_not_updated", "Timed out waiting for table update") from e

                    after_state = _collect_result_state(driver)
                    _log(f"  table state after submit: {after_state}")

                    table_result = _read_results_row(driver)
                    if table_result:
                        break
                    raise SubmitAttemptError(
                        "result_not_updated",
                        "Result table updated but parser found no carry/total/apex values",
                    )
                except SubmitBlockedError as e:
                    failure_reason = e.reason
                    _log(f"ERROR: submit blocked for {shot_name}: {failure_reason}")
                    if failure_reason == "blocked_no_result_update":
                        _log(
                            "HINT: reCAPTCHA v3 may be silently rejecting headless requests. "
                            "Try --visible mode, or on Linux headless use: "
                            "xvfb-run python tools/shot_calibration/flightscope_scraper.py --visible"
                        )
                    _capture_debug_artifacts(driver, shot_name, attempt, failure_reason)
                    break
                except SubmitAttemptError as e:
                    failure_reason = e.reason
                    _log(f"WARNING: submit attempt {attempt} failed for {shot_name}: {e}")
                    _capture_debug_artifacts(driver, shot_name, attempt, failure_reason)
                    if attempt < SUBMIT_RETRY_COUNT:
                        _log(f"Retrying shot after {RETRY_COOLDOWN_SEC:.1f}s cooldown")
                        time.sleep(RETRY_COOLDOWN_SEC)
                except Exception as e:
                    failure_reason = "submit_failed"
                    _log(f"WARNING: submit attempt {attempt} failed for {shot_name}: {e}")
                    _capture_debug_artifacts(driver, shot_name, attempt, failure_reason)
                    if attempt < SUBMIT_RETRY_COUNT:
                        _log(f"Retrying shot after {RETRY_COOLDOWN_SEC:.1f}s cooldown")
                        time.sleep(RETRY_COOLDOWN_SEC)

            if not table_result:
                _log(f"ERROR: {shot_name} failed ({failure_reason})")
                results[shot_name] = _build_failed_result_entry(shot_data, failure_reason)
                shot_statuses[shot_name] = {"status": "failed", "reason": failure_reason}
                continue

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
            shot_statuses[shot_name] = {"status": "success"}
            _log(f"  -> carry={table_result.get('carry_yd', '?')} yd, "
                 f"total={table_result.get('total_yd', '?')} yd, "
                 f"apex={table_result.get('apex_ft', '?')} ft")

        if shot_statuses:
            _log("Shot status summary:")
            for shot_name, status in shot_statuses.items():
                if status["status"] == "success":
                    _log(f"  {shot_name}: success")
                else:
                    _log(f"  {shot_name}: failed({status['reason']})")

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


def _resolve_shot_arg(shot_arg: str):
    """
    Accepts:
      - alias keys from DEFAULT_SHOTS (for example: driver2)
      - bare shot stem (for example: wood1)
      - explicit filename (for example: wood1.json)
    """
    if shot_arg in DEFAULT_SHOTS:
        return shot_arg, DEFAULT_SHOTS[shot_arg]

    name = Path(shot_arg).stem
    filename = Path(shot_arg).name
    if not filename.endswith(".json"):
        filename = f"{filename}.json"
    return name, filename


def main():
    parser = argparse.ArgumentParser(description="Scrape FlightScope trajectory data for calibration")
    parser.add_argument("--shots", nargs="*", help="Specific shot filenames to scrape (default: all)")
    parser.add_argument("--template", action="store_true", help="Generate empty template for manual entry")
    parser.add_argument("--visible", action="store_true", help="Run with visible browser window (default: headless)")
    parser.add_argument("--output", type=str, default=str(OUTPUT_FILE), help="Output file path")
    args = parser.parse_args()

    # Build shot list
    if args.shots:
        shot_map = {}
        for shot_arg in args.shots:
            shot_name, filename = _resolve_shot_arg(shot_arg)
            shot_map[shot_name] = filename
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
