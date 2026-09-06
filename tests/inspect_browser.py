import argparse
import json
from pathlib import Path
from playwright.sync_api import sync_playwright
parser = argparse.ArgumentParser()
parser.add_argument("--base", default="http://127.0.0.1:5099")
args = parser.parse_args()
with sync_playwright() as p:
    browser = p.chromium.launch(channel="msedge", headless=True)
    page = browser.new_page(viewport={"width":1440,"height":1000})
    page.goto(args.base)
    page.wait_for_load_state("networkidle")
    print(json.dumps({"title":page.title(),"buttons":page.get_by_role("button").all_text_contents()},ensure_ascii=True))
    out=Path(__file__).parent/".artifacts"
    out.mkdir(exist_ok=True)
    page.screenshot(path=str(out/"initial.png"),full_page=True)
    browser.close()

