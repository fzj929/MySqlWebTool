import argparse
import json
from playwright.sync_api import sync_playwright, expect
parser=argparse.ArgumentParser()
parser.add_argument("--base",default="http://127.0.0.1:5099")
args=parser.parse_args()
with sync_playwright() as p:
    browser=p.chromium.launch(channel="msedge",headless=True)
    page=browser.new_page(viewport={"width":1440,"height":1000})
    errors=[]
    page.on("pageerror",lambda e:errors.append(str(e)))
    # Synthetic credentials and responses: never connect to a real database.
    page.route("**/api/connection/test",lambda route:route.fulfill(json={"ok":True,"serverVersion":"test","elapsedMs":1}))
    page.route("**/api/schema/databases",lambda route:route.fulfill(json={"items":[{"name":"testdb"}]}))
    page.goto(args.base)
    page.wait_for_load_state("networkidle")
    expect(page.get_by_role("checkbox",name="记住连接")).to_be_checked()
    def field(name):
        return page.locator(".conn-form .el-form-item").filter(has_text=name).locator("input").first
    field("主机").fill("test-db.example")
    field("用户").fill("test-user")
    field("密码").fill("synthetic-password")
    field("数据库").fill("testdb")
    page.get_by_role("button",name="连接",exact=True).click()
    expect(page.get_by_role("button",name="断开",exact=True)).to_be_visible()
    records=lambda:page.evaluate("JSON.parse(localStorage.getItem('datapilot:connections') || '[]')")
    assert len(records())==1 and records()[0]["password"]=="synthetic-password"
    page.reload()
    page.wait_for_load_state("networkidle")
    expect(field("主机")).to_have_value("test-db.example")
    expect(field("密码")).to_have_value("synthetic-password")
    page.get_by_role("button",name="PostgreSQL",exact=True).click()
    page.locator(".history .el-select").click()
    page.get_by_role("option").filter(has_text="test-user@test-db.example").click()
    expect(field("主机")).to_have_value("test-db.example")
    expect(field("密码")).to_have_value("synthetic-password")
    expect(page.get_by_role("button",name="MySQL",exact=True)).to_have_attribute("aria-pressed","true")
    field("密码").fill("updated-synthetic-password")
    page.get_by_role("button",name="连接",exact=True).click()
    expect(page.get_by_role("button",name="断开",exact=True)).to_be_visible()
    assert len(records())==1 and records()[0]["password"]=="updated-synthetic-password"
    page.get_by_text("记住连接",exact=True).click()
    field("主机").fill("unsaved.example")
    page.get_by_role("button",name="连接",exact=True).click()
    expect(page.get_by_role("button",name="断开",exact=True)).to_be_visible()
    assert len(records())==1 and records()[0]["host"]=="test-db.example"
    page.get_by_text("记住连接",exact=True).click()
    page.unroute("**/api/connection/test")
    page.route("**/api/connection/test",lambda route:route.fulfill(status=400,json={"error":"Synthetic connection failure"}))
    page.get_by_role("button",name="连接",exact=True).click()
    expect(page.locator(".connection-error")).to_contain_text("Synthetic connection failure")
    assert len(records())==1
    page.get_by_role("button",name="删除连接",exact=True).click()
    page.get_by_role("button",name="确定",exact=True).click()
    expect(page.get_by_role("button",name="删除连接",exact=True)).to_be_disabled()
    assert records()==[]
    page.reload()
    expect(field("密码")).to_have_value("")
    assert not errors,errors
    browser.close()
print("PASS: remember defaults on, password persistence, reload, select across engines, update, opt-out, failure and deletion")
