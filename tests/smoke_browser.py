"""Real browser smoke tests using the published app and a temporary SQLite database."""
import argparse
import json
import re
import sqlite3
import tempfile
from contextlib import closing
from pathlib import Path
from playwright.sync_api import sync_playwright, expect

parser = argparse.ArgumentParser()
parser.add_argument("--base", default="http://127.0.0.1:5099")
args = parser.parse_args()
with tempfile.TemporaryDirectory(prefix="datapilot-browser-") as temp:
    db=Path(temp)/"browser-smoke.db"
    with closing(sqlite3.connect(db)) as conn:
        conn.executescript("CREATE TABLE customers(id INTEGER PRIMARY KEY,name TEXT); INSERT INTO customers VALUES(1,'Ada'),(2,'Linus'); CREATE INDEX ix_name ON customers(name);")
        conn.executemany("INSERT INTO customers VALUES (?,?)", [(i, "Customer "+str(i)) for i in range(3,158)])
        conn.commit()
    with sync_playwright() as p:
        browser=p.chromium.launch(channel="msedge",headless=True)
        page=browser.new_page(viewport={"width":1440,"height":1000})
        errors=[]
        page.on("pageerror",lambda e: errors.append(str(e)))
        page.goto(args.base)
        page.wait_for_load_state("networkidle")
        expect(page).to_have_title("DataPilot 数据库工作台")
        for label,port in [("SQL Server","1433"),("PostgreSQL","5432"),("MySQL","3306")]:
            page.get_by_role("button",name=label,exact=True).click()
            expect(page.locator(".conn-form .el-input-number input")).to_have_value(port)
        page.get_by_role("button",name="SQLite",exact=True).click()
        expect(page.get_by_role("button",name="上传数据库",exact=True)).to_be_visible()
        file_id=None
        try:
            with page.expect_response(lambda r: r.url.endswith("/api/sqlite/files") and r.request.method=="POST") as uploaded:
                page.locator("input[type=file]").set_input_files(str(db))
            response=uploaded.value
            assert response.status==200
            file_id=response.json()["id"]
            expect(page.get_by_role("button",name="连接",exact=True)).to_be_enabled()
            page.get_by_role("button",name="连接",exact=True).click()
            expect(page.get_by_role("button",name="断开",exact=True)).to_be_visible()
            page.locator(".node-name").filter(has_text="main").click()
            page.locator(".node-name").filter(has_text="customers").click()
            expect(page.locator(".data-panel")).to_contain_text("Ada")
            def check_scroll(panel):
                selector = panel + " .el-table__body-wrapper .el-scrollbar__wrap"
                scroll = page.locator(selector)
                expect(scroll).to_be_visible()
                page.wait_for_function("(s) => { const e=document.querySelector(s); return e.clientHeight>0 && e.scrollHeight>e.clientHeight; }", arg=selector)
                scroll.hover()
                page.mouse.wheel(0, 700)
                page.wait_for_function("(s) => document.querySelector(s).scrollTop>0", arg=selector)
                scroll.evaluate("(e) => e.scrollTop=e.scrollHeight")
                page.wait_for_function("(s) => { const e=document.querySelector(s); return e.scrollTop+e.clientHeight>=e.scrollHeight-2; }", arg=selector)
                header = page.locator(panel + " .el-table__header-wrapper")
                expect(header).to_be_in_viewport()
                scroll.evaluate("(e) => e.scrollTop=0")
            check_scroll(".data-panel")
            expect(page.locator(".pager")).to_be_in_viewport()
            page.set_viewport_size({"width":1280,"height":720})
            check_scroll(".data-panel")
            page.set_viewport_size({"width":1440,"height":1000})
            out=Path(__file__).parent/".artifacts"
            page.screenshot(path=str(out/"sqlite-data.png"),full_page=True)
            page.get_by_role("tab",name="表结构").click()
            expect(page.locator(".ddl")).to_contain_text("CREATE TABLE customers")
            expect(page.locator(".ddl")).to_contain_text("CREATE INDEX ix_name")
            page.get_by_role("button",name="发送到查询").click()
            expect(page.locator("textarea")).to_have_value(re.compile("CREATE TABLE"))
            page.locator("textarea").fill("SELECT name FROM customers ORDER BY id")
            page.get_by_role("button",name="执行",exact=True).click()
            expect(page.locator(".result-wrap")).to_contain_text("Linus")
            check_scroll(".query-panel")
            with page.expect_download() as downloaded:
                page.locator(".query-panel").get_by_role("button",name="导出 CSV").click()
            assert downloaded.value.suggested_filename.endswith(".csv")
            page.get_by_role("button",name="断开",exact=True).click()
            page.get_by_text("只读访问",exact=True).click()
            expect(page.get_by_role("checkbox",name="只读访问")).to_be_checked()
            page.get_by_role("button",name="连接",exact=True).click()
            expect(page.get_by_role("button",name="断开",exact=True)).to_be_visible()
            page.locator("textarea").fill("DELETE FROM customers")
            page.get_by_role("button",name="执行",exact=True).click()
            expect(page.locator(".el-notification").filter(has_text="readonly").first).to_be_visible()
            with page.expect_download() as downloaded:
                page.get_by_role("button",name="下载数据库",exact=True).click()
            assert downloaded.value.suggested_filename.endswith(".db")
            page.get_by_role("button",name="删除文件",exact=True).click()
            page.get_by_role("button",name="确定",exact=True).click()
            expect(page.get_by_role("button",name="删除文件",exact=True)).to_be_disabled()
            assert not errors, errors
            print("PASS: Browser engine defaults, upload, connect, table data, DDL, SQL, CSV, read-only, snapshot download and delete; no JavaScript errors")
        finally:
            if file_id: page.request.delete(args.base+"/api/sqlite/files/"+file_id)
            browser.close()
