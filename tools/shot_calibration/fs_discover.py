#!/usr/bin/env python
"""
FS Trajectory Optimizer discovery script.

Launches Chrome, navigates to the FS trajectory optimizer,
dismisses the weather popup, toggles wind OFF, and dumps all interactive
elements, screenshots, and rendered HTML for building scraper selectors.

Requirements:
    pip install selenium

Usage:
    python tools/shot_calibration/fs_discover.py
    python tools/shot_calibration/fs_discover.py --fill-test-shot
"""

import argparse
import json
import os
import time
from pathlib import Path

from selenium import webdriver
from selenium.webdriver.chrome.options import Options
from selenium.webdriver.chrome.service import Service
from selenium.webdriver.common.by import By
from selenium.webdriver.common.keys import Keys
from selenium.webdriver.support.ui import WebDriverWait
from selenium.webdriver.support import expected_conditions as EC

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
OUTPUT_DIR = REPO_ROOT / "tools" / "shot_calibration"
URL = os.environ.get("GOLF_SOURCE_URL", "")

# Driver test shot -- typical driver launch
TEST_SHOT = {
    "speed_mph": 167.0,
    "vla_deg": 10.4,
    "hla_deg": -1.2,
    "total_spin_rpm": 2611,
    "spin_axis_deg": -2.5,
}


def log(msg):
    """Verbose logging helper."""
    print(f"  [discover] {msg}")


def dismiss_weather_popup(driver, wait):
    """Wait for and dismiss the 'Weather Condition Setup' popup by clicking SAVE."""
    log("Looking for weather popup...")
    try:
        # Look for a button containing "SAVE" text -- the red save button on the popup
        save_btn = wait.until(
            EC.element_to_be_clickable((By.XPATH, "//button[contains(translate(., 'save', 'SAVE'), 'SAVE')]"))
        )
        log(f"Found SAVE button: '{save_btn.text.strip()}' -- clicking to dismiss popup")
        save_btn.click()
        time.sleep(1)
        log("Weather popup dismissed.")
    except Exception as e:
        log(f"No weather popup found or could not dismiss: {e}")
        # Try alternative: any modal overlay close button
        try:
            close_btns = driver.find_elements(By.CSS_SELECTOR, ".v-dialog button, .modal button, [class*='dialog'] button")
            for btn in close_btns:
                txt = btn.text.strip().upper()
                if txt in ("SAVE", "OK", "CLOSE", "DISMISS"):
                    log(f"Found alternative close button: '{btn.text.strip()}' -- clicking")
                    btn.click()
                    time.sleep(1)
                    break
        except Exception:
            log("No alternative close button found either. Continuing...")


def toggle_wind_off(driver):
    """Toggle wind OFF if it's currently ON."""
    log("Looking for Wind toggle...")
    try:
        # Find button/element with text "OFF" near the Wind toggle area
        # The page has Wind ON/OFF buttons -- click "OFF"
        off_buttons = driver.find_elements(By.XPATH,
            "//*[contains(translate(., 'off', 'OFF'), 'OFF')]"
        )
        for btn in off_buttons:
            if btn.is_displayed() and btn.tag_name in ("button", "div", "span", "a"):
                parent_text = ""
                try:
                    parent = btn.find_element(By.XPATH, "./..")
                    parent_text = parent.text[:100]
                except Exception:
                    pass
                # Check if this is near the Wind toggle
                if "wind" in parent_text.lower() or "wind" in btn.get_attribute("class").lower() if btn.get_attribute("class") else False:
                    log(f"Found Wind OFF button: '{btn.text.strip()}' -- clicking")
                    btn.click()
                    time.sleep(0.5)
                    log("Wind toggled OFF.")
                    return

        # Fallback: click any button with exactly "OFF" text
        for btn in off_buttons:
            if btn.is_displayed() and btn.text.strip().upper() == "OFF":
                log(f"Clicking OFF button (fallback): tag={btn.tag_name}")
                btn.click()
                time.sleep(0.5)
                log("Wind toggled OFF (fallback).")
                return

        log("Could not find a Wind OFF toggle. Wind may already be off or selector needs update.")
    except Exception as e:
        log(f"Error toggling wind off: {e}")


def dump_elements(driver):
    """Dump all interactive elements with their attributes."""
    print("\n" + "=" * 80)
    print("ELEMENT DISCOVERY")
    print("=" * 80)

    for tag in ["input", "select", "button", "textarea"]:
        elements = driver.find_elements(By.TAG_NAME, tag)
        if not elements:
            continue

        print(f"\n--- <{tag}> elements ({len(elements)}) ---")
        for i, el in enumerate(elements):
            attrs = {}
            for attr in ["id", "name", "type", "value", "placeholder",
                         "aria-label", "class", "data-testid", "role",
                         "min", "max", "step", "readonly", "disabled"]:
                val = el.get_attribute(attr)
                if val:
                    attrs[attr] = val

            visible = el.is_displayed()
            text = el.text[:100] if el.text else ""

            # Also try to find associated label text
            label_text = ""
            try:
                el_id = el.get_attribute("id")
                if el_id:
                    labels = driver.find_elements(By.CSS_SELECTOR, f"label[for='{el_id}']")
                    if labels:
                        label_text = labels[0].text.strip()
            except Exception:
                pass
            if not label_text:
                try:
                    # Check parent/sibling for label-like text
                    parent = el.find_element(By.XPATH, "./..")
                    siblings = parent.find_elements(By.XPATH, "./*")
                    for sib in siblings:
                        if sib != el and sib.text.strip():
                            label_text = sib.text.strip()[:60]
                            break
                except Exception:
                    pass

            print(f"  [{i}] visible={visible} text='{text}' label='{label_text}'")
            for k, v in attrs.items():
                print(f"       {k}={v}")
            print()


def dump_output_containers(driver):
    """Look for result/output text containers."""
    print("\n" + "=" * 80)
    print("OUTPUT / RESULT CONTAINERS")
    print("=" * 80)

    selectors = [
        "[class*='result']",
        "[class*='output']",
        "[class*='carry']",
        "[class*='total']",
        "[class*='apex']",
        "[class*='distance']",
        "[class*='chart']",
        "[class*='trajectory']",
        "[class*='summary']",
        "[class*='stat']",
        "[data-carry]",
        "[data-total]",
        "[data-apex]",
        "table",
        "canvas",
        "svg",
    ]

    for sel in selectors:
        try:
            elements = driver.find_elements(By.CSS_SELECTOR, sel)
            if elements:
                print(f"\n--- {sel} ({len(elements)} found) ---")
                for i, el in enumerate(elements):
                    tag = el.tag_name
                    cls = el.get_attribute("class") or ""
                    text = el.text[:200] if el.text else "(no text)"
                    visible = el.is_displayed()
                    size = el.size
                    print(f"  [{i}] <{tag}> visible={visible} size={size}")
                    print(f"       class={cls}")
                    print(f"       text={text}")
                    print()
        except Exception:
            pass


def dump_vue_data(driver):
    """Try to extract Vue component data from the page."""
    print("\n" + "=" * 80)
    print("VUE.JS APP DATA")
    print("=" * 80)

    scripts = [
        "if (window.__VUE_DEVTOOLS_GLOBAL_HOOK__) return 'Vue devtools hook found'; return 'No Vue hook';",
        "var app = document.querySelector('#app'); if (app && app.__vue_app__) return JSON.stringify(Object.keys(app.__vue_app__)); return 'No Vue 3 app';",
        "var app = document.querySelector('#app'); if (app && app.__vue__) return JSON.stringify(Object.keys(app.__vue__.$data)); return 'No Vue 2 app';",
        "if (window.__INITIAL_STATE__) return JSON.stringify(window.__INITIAL_STATE__); return 'No initial state';",
    ]

    for script in scripts:
        try:
            result = driver.execute_script(f"return (function(){{ {script} }})()")
            print(f"  {result}")
        except Exception as e:
            print(f"  Error: {e}")


def capture_network_log(driver):
    """Dump captured network requests (Performance log)."""
    print("\n" + "=" * 80)
    print("NETWORK REQUESTS (XHR/Fetch)")
    print("=" * 80)

    try:
        logs = driver.get_log("performance")
        api_calls = []
        for entry in logs:
            try:
                msg = json.loads(entry["message"])["message"]
                method = msg.get("method", "")
                if method == "Network.requestWillBeSent":
                    params = msg.get("params", {})
                    req = params.get("request", {})
                    url = req.get("url", "")
                    req_method = req.get("method", "")
                    req_type = params.get("type", "")

                    if any(ext in url for ext in [".js", ".css", ".png", ".svg", ".ico", ".woff"]):
                        continue

                    api_calls.append({
                        "method": req_method,
                        "url": url,
                        "type": req_type,
                        "postData": req.get("postData", ""),
                    })

                elif method == "Network.responseReceived":
                    params = msg.get("params", {})
                    resp = params.get("response", {})
                    url = resp.get("url", "")
                    status = resp.get("status", 0)
                    mime = resp.get("mimeType", "")

                    if "json" in mime or "api" in url.lower():
                        print(f"  RESPONSE: {status} {url} ({mime})")

            except (json.JSONDecodeError, KeyError):
                continue

        if api_calls:
            print(f"\n  Found {len(api_calls)} non-asset requests:")
            for call in api_calls:
                print(f"    {call['method']} {call['url']}")
                if call["postData"]:
                    print(f"      Body: {call['postData'][:300]}")
        else:
            print("  No API calls captured.")

    except Exception as e:
        print(f"  Could not read performance logs: {e}")


def fill_test_shot(driver, wait):
    """Fill in a test shot using label-based field matching."""
    print("\n" + "=" * 80)
    print("FILLING TEST SHOT")
    print("=" * 80)

    # Map label text fragments to test values
    field_map = {
        "Ball": str(TEST_SHOT["speed_mph"]),
        "Launch V": str(TEST_SHOT["vla_deg"]),
        "Launch H": str(abs(TEST_SHOT["hla_deg"])),
        "Spin (": str(TEST_SHOT["total_spin_rpm"]),
        "Spin Axis": str(abs(TEST_SHOT["spin_axis_deg"])),
    }

    log(f"Attempting to fill {len(field_map)} fields by label text...")

    for label_fragment, value in field_map.items():
        try:
            filled = _fill_field_by_label(driver, label_fragment, value)
            if filled:
                log(f"  Filled '{label_fragment}' with {value}")
            else:
                log(f"  Could not find field for '{label_fragment}'")
        except Exception as e:
            log(f"  Error filling '{label_fragment}': {e}")

    # Handle Left/Right dropdowns for HLA and Spin Axis (negative = Left)
    if TEST_SHOT["hla_deg"] < 0:
        _set_direction_dropdown(driver, "Launch H", "Left")
    if TEST_SHOT["spin_axis_deg"] < 0:
        _set_direction_dropdown(driver, "Spin Axis", "Left")

    # Click DISPLAY SHOT
    log("Looking for DISPLAY SHOT button...")
    try:
        display_btn = driver.find_element(By.XPATH,
            "//button[contains(translate(., 'display shot', 'DISPLAY SHOT'), 'DISPLAY SHOT')]"
        )
        log(f"Found button: '{display_btn.text.strip()}' -- clicking")
        display_btn.click()
        time.sleep(3)
        log("Clicked DISPLAY SHOT, waiting for results...")
    except Exception as e:
        log(f"Could not find DISPLAY SHOT button: {e}")
        # Fallback: try any prominent red button
        try:
            buttons = driver.find_elements(By.TAG_NAME, "button")
            for btn in buttons:
                if btn.is_displayed() and btn.text.strip():
                    text = btn.text.strip().upper()
                    if "DISPLAY" in text or "SHOT" in text:
                        log(f"Fallback clicking: '{btn.text.strip()}'")
                        btn.click()
                        time.sleep(3)
                        break
        except Exception:
            pass

    # Dump results table
    log("Looking for results table...")
    try:
        tables = driver.find_elements(By.TAG_NAME, "table")
        for i, table in enumerate(tables):
            if table.is_displayed():
                text = table.text[:500]
                log(f"Table [{i}]: {text}")
    except Exception as e:
        log(f"Error reading tables: {e}")

    # Also dump body text for result detection
    body_text = driver.find_element(By.TAG_NAME, "body").text
    log(f"Page text after submit (first 600 chars):\n{body_text[:600]}")


def _fill_field_by_label(driver, label_fragment, value):
    """Find an input field by its nearby label text and fill it."""
    # Strategy 1: Find element containing label text, then find nearby input
    try:
        label_els = driver.find_elements(By.XPATH,
            f"//*[contains(text(), '{label_fragment}')]"
        )
        for label_el in label_els:
            if not label_el.is_displayed():
                continue
            # Look for input in same parent container
            try:
                parent = label_el.find_element(By.XPATH, "./..")
                inputs = parent.find_elements(By.TAG_NAME, "input")
                if not inputs:
                    # Go one more level up
                    grandparent = parent.find_element(By.XPATH, "./..")
                    inputs = grandparent.find_elements(By.TAG_NAME, "input")
                for inp in inputs:
                    if inp.is_displayed():
                        inp.click()
                        inp.send_keys(Keys.CONTROL + "a")
                        inp.send_keys(value)
                        return True
            except Exception:
                continue
    except Exception:
        pass

    return False


def _set_direction_dropdown(driver, label_fragment, direction):
    """Set a Left/Right dropdown near a label."""
    log(f"Setting direction dropdown near '{label_fragment}' to '{direction}'...")
    try:
        label_els = driver.find_elements(By.XPATH,
            f"//*[contains(text(), '{label_fragment}')]"
        )
        for label_el in label_els:
            if not label_el.is_displayed():
                continue
            # Look for select/dropdown in parent area
            parent = label_el.find_element(By.XPATH, "./..")
            grandparent = parent.find_element(By.XPATH, "./..")
            # Try select element
            selects = grandparent.find_elements(By.TAG_NAME, "select")
            for sel in selects:
                if sel.is_displayed():
                    from selenium.webdriver.support.ui import Select
                    Select(sel).select_by_visible_text(direction)
                    log(f"  Set select to '{direction}'")
                    return
            # Try Vuetify-style dropdown: look for clickable with Left/Right text
            clickables = grandparent.find_elements(By.XPATH,
                f".//*[contains(text(), '{direction}')]"
            )
            for el in clickables:
                if el.is_displayed() and el.tag_name in ("div", "span", "button", "option"):
                    el.click()
                    log(f"  Clicked '{direction}' element")
                    return
    except Exception as e:
        log(f"  Could not set direction: {e}")


def main():
    parser = argparse.ArgumentParser(description="Discover FS trajectory page structure")
    parser.add_argument("--fill-test-shot", action="store_true",
                        help="Fill in a test driver shot and capture results")
    parser.add_argument("--headless", action="store_true",
                        help="Run in headless mode (default: visible)")
    args = parser.parse_args()

    chrome_options = Options()
    if args.headless:
        chrome_options.add_argument("--headless=new")

    # Enable performance logging to capture network requests
    chrome_options.set_capability("goog:loggingPrefs", {"performance": "ALL"})
    chrome_options.add_argument("--window-size=1920,1080")

    print(f"Launching Chrome and navigating to {URL}")
    driver = webdriver.Chrome(options=chrome_options)
    wait = WebDriverWait(driver, 15)

    try:
        driver.get(URL)

        # Wait for Vue app to render
        log("Waiting for page to render...")
        try:
            wait.until(EC.presence_of_element_located((By.TAG_NAME, "input")))
            log("Input elements found.")
        except Exception:
            log("No input elements found after 15s, continuing anyway...")

        # Extra wait for Vue reactivity
        time.sleep(2)

        print(f"Page title: {driver.title}")
        print(f"Current URL: {driver.current_url}")

        # Step 1: Dismiss weather popup
        dismiss_weather_popup(driver, wait)

        # Step 2: Toggle wind OFF
        time.sleep(1)
        toggle_wind_off(driver)

        # Step 3: Run all discovery steps
        time.sleep(1)
        dump_elements(driver)
        dump_output_containers(driver)
        dump_vue_data(driver)

        if args.fill_test_shot:
            fill_test_shot(driver, wait)
            time.sleep(2)

        capture_network_log(driver)

        # Save screenshot
        screenshot_path = OUTPUT_DIR / "debug_fs.png"
        driver.save_screenshot(str(screenshot_path))
        print(f"\nScreenshot saved to {screenshot_path}")

        # Save rendered HTML
        html_path = OUTPUT_DIR / "debug_fs.html"
        with open(html_path, "w", encoding="utf-8") as f:
            f.write(driver.page_source)
        print(f"HTML saved to {html_path}")

        print("\nDiscovery complete. Review the output above and the saved files.")

    finally:
        driver.quit()


if __name__ == "__main__":
    main()
