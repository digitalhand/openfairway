#!/usr/bin/env python
"""
FS Trajectory Optimizer scraper.

Reads shot data from assets/data/*.json, enters each shot into
the FS trajectory optimizer (URL from GOLF_SOURCE_URL env var),
and captures the carry/total/apex results.

Outputs: assets/data/SOT/fs_reference.json

Requirements:
    pip install selenium undetected-chromedriver

Usage:
    python tools/shot_calibration/fs_scraper.py
    python tools/shot_calibration/fs_scraper.py --shots driver2 --visible
    python tools/shot_calibration/fs_scraper.py --shots driver1.json wood1.json
    python tools/shot_calibration/fs_scraper.py --visible
    python tools/shot_calibration/fs_scraper.py --session assets/data/shot_session_3 --visible
    python tools/shot_calibration/fs_scraper.py --retry-failed    # re-attempt failed shots
    python tools/shot_calibration/fs_scraper.py --force           # ignore existing, start fresh
"""

import argparse
import json
import math
import os
import random
import re
import shutil
import sys
import time
from pathlib import Path

import undetected_chromedriver as uc
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
OUTPUT_FILE = REPO_ROOT / "assets" / "data" / "SOT" / "fs_reference.json"
URL = os.environ.get("GOLF_SOURCE_URL", "")
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
FIELD_ACTION_DELAY_SEC = 2.5
PRE_DISPLAY_CLICK_DELAY_SEC = 3.0
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


def _human_delay(base_sec: float, jitter_fraction: float = 0.4):
    """Sleep for a randomized duration around base_sec (±jitter_fraction)."""
    jitter = base_sec * jitter_fraction
    time.sleep(max(0.05, base_sec + random.uniform(-jitter, jitter)))


_FS_DOMAIN_FRAGMENT = "trajectory"
_API_URL_MARKERS = ("trajectory",)


def _is_on_fs_page(driver) -> bool:
    """Check if the browser is already on the FS trajectory page."""
    try:
        current = driver.current_url or ""
        return _FS_DOMAIN_FRAGMENT in current
    except Exception:
        return False


def _check_recaptcha_token_status(driver) -> str:
    """Check if a reCAPTCHA response token is present."""
    script = """
    const ta = document.querySelector('textarea[name="g-recaptcha-response"]');
    if (!ta) return 'no_textarea';
    return ta.value ? 'token_present:' + ta.value.length : 'empty_token';
    """
    try:
        return driver.execute_script(script)
    except Exception:
        return "check_failed"


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


def load_shot_data(filename: str, data_dir: Path = None) -> dict:
    """Load a shot JSON file and extract ball data fields."""
    path = (data_dir or DATA_DIR) / filename
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

    # Filter out shots outside the trajectory optimizer's useful input range
    if speed < 45:
        print(f"  SKIP: {filename} — speed {speed:.1f} mph < 45 mph")
        return None
    if vla <= 5:
        print(f"  SKIP: {filename} — VLA {vla:.1f}° <= 5°")
        return None
    if vla > 45:
        print(f"  SKIP: {filename} — VLA {vla:.1f}° > 45° (too steep for trajectory optimizer)")
        return None
    if total_spin < 1000 or total_spin > 12000:
        print(f"  SKIP: {filename} — total spin {total_spin:.0f} RPM outside 1000–12000 range")
        return None
    if hla <= -45 or hla >= 45:
        print(f"  SKIP: {filename} — HLA {hla:.1f}° outside ±45° range")
        return None
    if spin_axis <= -45 or spin_axis >= 45:
        print(f"  SKIP: {filename} — spin axis {spin_axis:.1f}° outside ±45° range")
        return None

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


def _create_driver(visible: bool, debug_port: int = None, browser_profile: str = None):
    """Create a browser session via undetected-chromedriver.

    When *debug_port* is provided, attaches to an already-running Chrome
    instance (started with ``--remote-debugging-port=<port>``) using plain
    Selenium instead (undetected-chromedriver cannot attach to an existing
    browser).

    When *browser_profile* is provided (non-debug-port mode), uses a persistent
    Chrome user-data-dir so reCAPTCHA v3 can build engagement history across runs.
    """
    # Debug-port mode: attach to existing browser with plain Selenium
    if debug_port:
        chrome_options = Options()
        chrome_options.add_experimental_option(
            "debuggerAddress", f"localhost:{debug_port}",
        )
        _log(f"Attaching to existing browser on localhost:{debug_port}")
        return webdriver.Chrome(options=chrome_options)

    # Normal mode: use undetected-chromedriver to bypass bot detection
    browser_cmd, browser_path = _resolve_browser_binary()
    if browser_path is None:
        raise FileNotFoundError(
            "Could not find a supported browser command in PATH: "
            f"{', '.join(BROWSER_COMMAND_PREFERENCES)}"
        )

    options = uc.ChromeOptions()
    options.add_argument("--window-size=1920,1080")

    if browser_profile:
        profile_path = Path(browser_profile)
        profile_path.mkdir(parents=True, exist_ok=True)
        options.add_argument(f"--user-data-dir={profile_path}")
        _log(f"Using persistent browser profile: {profile_path}")

    _log(f"Launching undetected-chromedriver with: {browser_cmd} ({browser_path})")
    driver = uc.Chrome(
        options=options,
        browser_executable_path=browser_path,
        headless=not visible,
    )

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


def _warm_up_page(driver):
    """Simulate natural browsing behavior before form interaction.

    reCAPTCHA v3 builds a behavioral profile from page load. Scrolling,
    hovering, and pausing before interacting raises the trust score.
    """
    _log("Warming up page with natural browsing behavior...")
    try:
        actions = ActionChains(driver)

        # Scroll down slowly
        driver.execute_script("window.scrollBy({top: 300, behavior: 'smooth'})")
        _human_delay(1.0, jitter_fraction=0.3)

        # Hover over a few visible elements (labels, headings)
        hoverable_selectors = ["h1", "h2", "h3", "label", "p", "span"]
        hoverable = []
        for sel in hoverable_selectors:
            hoverable.extend(
                el for el in driver.find_elements(By.TAG_NAME, sel) if el.is_displayed()
            )
        if hoverable:
            for el in random.sample(hoverable, min(len(hoverable), 3)):
                try:
                    actions.move_to_element(el).pause(random.uniform(0.3, 0.7)).perform()
                    actions = ActionChains(driver)
                except Exception:
                    pass

        _human_delay(0.8, jitter_fraction=0.4)

        # Scroll back to top
        driver.execute_script("window.scrollTo({top: 0, behavior: 'smooth'})")
        _human_delay(0.8, jitter_fraction=0.3)
        _log("Page warm-up complete.")
    except Exception as e:
        _log(f"WARNING: Page warm-up failed (non-fatal): {e}")


def _between_shot_micro_interaction(driver):
    """Add subtle natural behavior between shots to maintain reCAPTCHA score."""
    try:
        # Scroll the results table into view
        tables = driver.find_elements(By.TAG_NAME, "table")
        for table in tables:
            if table.is_displayed():
                driver.execute_script(
                    "arguments[0].scrollIntoView({behavior:'smooth', block:'center'});",
                    table,
                )
                break

        # Move mouse to a random non-input element
        non_inputs = [
            el for el in driver.find_elements(By.CSS_SELECTOR, "h1, h2, h3, label, th, td, p")
            if el.is_displayed()
        ]
        if non_inputs:
            target = random.choice(non_inputs)
            ActionChains(driver).move_to_element(target).pause(
                random.uniform(0.2, 0.5)
            ).perform()

        # Small random scroll offset
        offset = random.randint(-50, 80)
        driver.execute_script(f"window.scrollBy({{top: {offset}, behavior: 'smooth'}})")
    except Exception:
        pass


CHAR_INPUT_DELAY_SEC = 0.08


def _type_value_char_by_char(driver, element, value: str):
    """Clear a field and type value one character at a time.

    Typing per-character with short delays ensures Vue/Vuetify reactive
    input handlers register each keystroke, which bulk JS setter or
    full-string send_keys can skip.
    """
    # Select all + delete to clear existing content
    element.send_keys(Keys.CONTROL + "a")
    element.send_keys(Keys.DELETE)
    for char in str(value):
        element.send_keys(char)
        _human_delay(CHAR_INPUT_DELAY_SEC, jitter_fraction=0.6)


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
                        ActionChains(driver).move_to_element(inp).pause(
                            random.uniform(0.1, 0.3)
                        ).click().perform()
                        _type_value_char_by_char(driver, inp, value)
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
    """Apply humanized pacing between form actions."""
    _log(f"Waiting ~{FIELD_ACTION_DELAY_SEC:.1f}s after {label}")
    _human_delay(FIELD_ACTION_DELAY_SEC)


def _fill_shot_form(driver, shot_data):
    """Fill all form fields for a single shot."""
    try:
        first_input = driver.find_element(By.TAG_NAME, "input")
        if first_input.is_displayed():
            driver.execute_script(
                "arguments[0].scrollIntoView({behavior:'smooth', block:'center'});",
                first_input,
            )
            _human_delay(0.5, jitter_fraction=0.5)
    except Exception:
        pass

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

        _log(f"Waiting ~{PRE_DISPLAY_CLICK_DELAY_SEC:.1f}s before pressing DISPLAY SHOT")
        _human_delay(PRE_DISPLAY_CLICK_DELAY_SEC)

        # Simulate mouse movement through form inputs to improve reCAPTCHA v3 score
        try:
            actions = ActionChains(driver)
            visible_inputs = [inp for inp in driver.find_elements(By.TAG_NAME, "input")
                              if inp.is_displayed()]
            sample_count = min(len(visible_inputs), random.randint(2, 4))
            for inp in random.sample(visible_inputs, sample_count):
                actions.move_to_element(inp).pause(random.uniform(0.15, 0.5))
            actions.move_to_element(btn).pause(random.uniform(0.3, 0.8)).click().perform()
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
    """Extract API requests to FS trajectory optimizer from performance logs."""
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
                    if any(m in url for m in _API_URL_MARKERS):
                        api_entries.append({
                            "type": "request",
                            "url": url,
                            "method": params.get("request", {}).get("method"),
                            "postData": params.get("request", {}).get("postData"),
                            "timestamp": entry.get("timestamp"),
                        })
                elif method == "Network.responseReceived":
                    url = params.get("response", {}).get("url", "")
                    if any(m in url for m in _API_URL_MARKERS):
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
    try:
        _ = driver.current_url
    except Exception:
        _log("WARNING: Browser session is stale, cannot capture debug artifacts")
        return

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


def _save_results(results: dict, output_path: Path):
    """Incrementally persist results after each shot."""
    if output_path is None:
        return
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with open(output_path, "w") as f:
        json.dump(results, f, indent=2)


def _load_existing_results(output_path: Path) -> dict:
    """Load previously scraped results from output file, if it exists."""
    if output_path and output_path.exists():
        with open(output_path) as f:
            return json.load(f)
    return {}


def _is_completed(entry: dict) -> bool:
    """Check if a result entry represents a successful scrape (has carry data, no failure status)."""
    return entry.get("carry_yd") is not None and entry.get("_status") != "failed"


def scrape_fs(
    shots: dict,
    visible: bool = False,
    debug_port: int = None,
    output_path: Path = None,
    browser_profile: str = None,
    existing_results: dict = None,
    retry_failed: bool = False,
) -> dict:
    """
    Automate FS trajectory optimizer to get carry/total/apex.

    When *debug_port* is set, attaches to an existing browser and leaves it
    running after scraping completes.

    When *existing_results* is provided, already-completed shots are skipped.
    Failed shots are also skipped unless *retry_failed* is True.
    """
    driver = _create_driver(visible, debug_port=debug_port, browser_profile=browser_profile)
    wait = WebDriverWait(driver, 15)
    results = dict(existing_results or {})
    shot_statuses = {}

    try:
        # Skip navigation when already on FS page (preserves reCAPTCHA v3 score)
        already_on_page = debug_port and _is_on_fs_page(driver)

        if already_on_page:
            _log("Already on FS page — skipping navigation to preserve reCAPTCHA score")
            try:
                wait.until(EC.presence_of_element_located((By.TAG_NAME, "input")))
            except Exception:
                _log("WARNING: No inputs found, falling back to navigation")
                already_on_page = False

        if not already_on_page:
            _log(f"Navigating to {URL}")
            driver.get(URL)
            try:
                wait.until(EC.presence_of_element_located((By.TAG_NAME, "input")))
            except Exception:
                _log("WARNING: No inputs found after 15s")
            time.sleep(2)
            _dismiss_weather_popup(driver, wait)
            time.sleep(1)
            _toggle_wind_off(driver)
            time.sleep(1)
            _warm_up_page(driver)

        # Process each shot on the same page
        for shot_index, (shot_name, shot_data) in enumerate(shots.items()):
            if shot_data is None:
                continue

            if shot_name in results:
                existing = results[shot_name]
                if _is_completed(existing):
                    _log(f"Skipping {shot_name} (already completed)")
                    continue
                if existing.get("_status") == "failed" and not retry_failed:
                    _log(f"Skipping {shot_name} (previously failed, use --retry-failed to re-attempt)")
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
                    token_status = _check_recaptcha_token_status(driver)
                    _log(f"  reCAPTCHA token: {token_status}")
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
                            "xvfb-run python tools/shot_calibration/fs_scraper.py --visible"
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
                _save_results(results, output_path)
                shot_statuses[shot_name] = {"status": "failed", "reason": failure_reason}
                if shot_index < len(shots) - 1:
                    if failure_reason.startswith("blocked_"):
                        inter_shot_sec = random.uniform(8.0, 15.0)
                    else:
                        inter_shot_sec = random.uniform(1.5, 4.0)
                    _log(f"Inter-shot pause: {inter_shot_sec:.1f}s")
                    _between_shot_micro_interaction(driver)
                    time.sleep(inter_shot_sec)
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
            _save_results(results, output_path)
            shot_statuses[shot_name] = {"status": "success"}
            _log(f"  -> carry={table_result.get('carry_yd', '?')} yd, "
                 f"total={table_result.get('total_yd', '?')} yd, "
                 f"apex={table_result.get('apex_ft', '?')} ft")

            if shot_index < len(shots) - 1:
                inter_shot_sec = random.uniform(1.5, 4.0)
                _log(f"Inter-shot pause: {inter_shot_sec:.1f}s")
                _between_shot_micro_interaction(driver)
                time.sleep(inter_shot_sec)

        if shot_statuses:
            _log("Shot status summary:")
            for shot_name, status in shot_statuses.items():
                if status["status"] == "success":
                    _log(f"  {shot_name}: success")
                else:
                    _log(f"  {shot_name}: failed({status['reason']})")

    finally:
        if debug_port:
            _log("Detaching from browser (leaving it running).")
        else:
            driver.quit()
            _log("Browser closed.")

    return results


def create_manual_reference(shots: dict) -> dict:
    """
    Create a template reference file for manual entry.
    Use this when the automated scraper can't access the trajectory optimizer.
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
            "_note": "Fill in FS reference values manually",
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


def _discover_session_shots(session_dir: Path) -> dict:
    """Auto-discover all *.json files in a session directory as shot map."""
    shot_map = {}
    for path in sorted(session_dir.glob("*.json")):
        shot_map[path.stem] = path.name
    return shot_map


def main():
    parser = argparse.ArgumentParser(description="Scrape FS trajectory data for calibration")
    parser.add_argument("--shots", nargs="*", help="Specific shot filenames to scrape (default: all)")
    parser.add_argument("--session", type=str, default=None, help="Session directory path (auto-discovers shots, outputs to session dir)")
    parser.add_argument("--template", action="store_true", help="Generate empty template for manual entry")
    parser.add_argument("--visible", action="store_true", help="Run with visible browser window (default: headless)")
    parser.add_argument("--debug-port", type=int, default=None, help="Attach to existing Chrome on this debugging port (e.g. 9222)")
    parser.add_argument(
        "--browser-profile", type=str,
        default=str(Path.home() / ".config/openfairway/scraper-profile"),
        help="Chrome user-data-dir for persistent profile (default: ~/.config/openfairway/scraper-profile)",
    )
    parser.add_argument("--output", type=str, default=None, help="Output file path")
    parser.add_argument("--retry-failed", action="store_true", help="Re-scrape shots that previously failed")
    parser.add_argument("--force", action="store_true", help="Ignore existing results and start from scratch")
    args = parser.parse_args()

    # Resolve session mode
    session_dir = Path(args.session) if args.session else None
    if session_dir and not session_dir.is_absolute():
        session_dir = REPO_ROOT / session_dir
    data_dir = session_dir if session_dir else DATA_DIR
    output_path = Path(args.output) if args.output else (
        session_dir / "fs_reference.json" if session_dir else OUTPUT_FILE
    )

    # Build shot list
    if args.shots:
        shot_map = {}
        for shot_arg in args.shots:
            shot_name, filename = _resolve_shot_arg(shot_arg)
            shot_map[shot_name] = filename
    elif session_dir:
        shot_map = _discover_session_shots(session_dir)
    else:
        shot_map = DEFAULT_SHOTS

    # Load shot data
    shots = {}
    for name, filename in shot_map.items():
        data = load_shot_data(filename, data_dir=data_dir)
        if data:
            shots[name] = data

    print(f"Loaded {len(shots)} shots")

    # Load existing results for resume capability
    existing_results = {} if args.force else _load_existing_results(output_path)

    if existing_results:
        completed = sum(1 for e in existing_results.values() if _is_completed(e))
        failed = sum(1 for e in existing_results.values() if e.get("_status") == "failed")
        remaining = len(shots) - completed - (0 if args.retry_failed else failed)
        print(f"Resume: {completed} completed, {failed} failed, {remaining} remaining")

    if args.template:
        results = create_manual_reference(shots)
        # Write template output
        output_path.parent.mkdir(parents=True, exist_ok=True)
        with open(output_path, "w") as f:
            json.dump(results, f, indent=2)
    else:
        results = scrape_fs(
            shots, visible=args.visible, debug_port=args.debug_port,
            output_path=output_path, browser_profile=args.browser_profile,
            existing_results=existing_results, retry_failed=args.retry_failed,
        )

    print(f"\nWrote {len(results)} entries to {output_path}")


if __name__ == "__main__":
    main()
