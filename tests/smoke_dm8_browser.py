"""DM8 frontend contract tests with synthetic API responses, not a live DM8 test."""
import argparse
from pathlib import Path
from playwright.sync_api import sync_playwright, expect

parser = argparse.ArgumentParser()
parser.add_argument('--base', default='http://127.0.0.1:5099')
args = parser.parse_args()
with sync_playwright() as p:
    browser = p.chromium.launch(channel='msedge', headless=True)
    page = browser.new_page(viewport={'width': 1440, 'height': 1000})
    errors = []
    page.on('pageerror', lambda e: errors.append(str(e)))
    page.route('**/api/connection/test', lambda r: r.fulfill(json={'ok': True, 'serverVersion': 'DM8-mock', 'elapsedMs': 1}))
    page.route('**/api/schema/databases', lambda r: r.fulfill(json={'items': [{'name': 'DM8'}]}))
    page.route('**/api/schema/schemas', lambda r: r.fulfill(json={'items': [{'name': 'TEST_SCHEMA'}]}))
    page.route('**/api/schema/tables', lambda r: r.fulfill(json={'items': [{'name': 'TEST_TABLE', 'type': 'BASE TABLE', 'schema': 'TEST_SCHEMA'}]}))
    def data(route):
        request = route.request.post_data_json
        assert request['connection']['databaseType'] == 'dm8'
        assert request['schema'] == 'TEST_SCHEMA'
        route.fulfill(json={'columns': ['ID'], 'columnTypes': [{'name': 'ID', 'dataType': 'INTEGER'}], 'rows': [[1]], 'total': 1, 'rowCount': 1, 'page': 1, 'pageSize': 100, 'elapsedMs': 1})
    page.route('**/api/query/data', data)
    page.goto(args.base)
    page.wait_for_load_state('networkidle')
    page.get_by_role('button', name='达梦 DM8', exact=True).click()
    def field(label):
        return page.locator('.conn-form .el-form-item').filter(has_text=label).locator('input').first
    expect(field('端口')).to_have_value('5236')
    expect(field('用户')).to_have_value('SYSDBA')
    expect(field('模式')).to_have_value('')
    expect(page.get_by_role('checkbox', name='记住连接')).to_be_checked()
    field('用户').fill('TEST_USER')
    field('密码').fill('synthetic;password="safe"')
    field('模式').fill('TEST_SCHEMA')
    page.get_by_role('button', name='连接', exact=True).click()
    expect(page.get_by_role('button', name='断开', exact=True)).to_be_visible()
    page.reload()
    page.wait_for_load_state('networkidle')
    expect(field('模式')).to_have_value('TEST_SCHEMA')
    expect(field('密码')).to_have_value('synthetic;password="safe"')
    page.get_by_role('button', name='连接', exact=True).click()
    page.locator('.node-name').get_by_text('DM8', exact=True).click()
    page.locator('.node-name').get_by_text('TEST_SCHEMA', exact=True).click()
    page.locator('.node-name').get_by_text('TEST_TABLE', exact=True).click()
    expect(page.locator('.data-panel')).to_contain_text('TEST_TABLE')
    expect(field('模式')).to_have_value('TEST_SCHEMA')
    header = page.locator('.deck-top').bounding_box()
    history = page.locator('.history').bounding_box()
    assert history['y'] >= header['y'] and history['y'] + history['height'] <= header['y'] + header['height'] + 1
    Path('tests/.artifacts').mkdir(parents=True, exist_ok=True)
    page.screenshot(path='tests/.artifacts/dm8-browser.png')
    assert not errors, errors
    browser.close()
print('PASS: DM8 defaults, remembered credentials/schema, schema tree and data request (mock API)')
