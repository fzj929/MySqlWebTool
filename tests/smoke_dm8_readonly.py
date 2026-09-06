"""Read-only live DM8 checks. Credentials come only from process environment."""
import argparse
import json
import os
import urllib.request
import urllib.error

parser = argparse.ArgumentParser()
parser.add_argument('--base', default='http://127.0.0.1:5099')
args = parser.parse_args()
info = {'databaseType': 'dm8', 'host': os.environ['DM8_TEST_HOST'],
        'port': int(os.environ.get('DM8_TEST_PORT', '5236')), 'user': os.environ['DM8_TEST_USER'],
        'password': os.environ['DM8_TEST_PASSWORD'], 'schema': '', 'database': ''}

def call(path, body):
    request = urllib.request.Request(args.base + '/api' + path, json.dumps(body).encode(), {'Content-Type': 'application/json'})
    try:
        with urllib.request.urlopen(request, timeout=90) as response:
            return response.read() if path.endswith('/export') else json.load(response)
    except urllib.error.HTTPError as error:
        raise RuntimeError(path + ': ' + error.read().decode().replace(info['password'], '[redacted]')) from None

result = call('/connection/test', info)
print('PASS: live DM8 connection; version', result['serverVersion'])
dbs = call('/schema/databases', info)['items']
assert dbs
database = dbs[0]['name']
request = {'connection': info, 'database': database}
schemas = call('/schema/schemas', request)['items']
assert schemas
print('PASS: instance and schema listing;', len(schemas), 'schemas')
query = call('/query', {'connection': info, 'sql': 'SELECT 1 AS CHECK_VALUE FROM DUAL', 'limit': 1})
assert query['rowCount'] == 1 and str(query['rows'][0][0]) == '1'
print('PASS: SQL query execution')
typed = call('/query', {'connection': info, 'sql': "SELECT CAST(9223372036854775806 AS BIGINT), CAST('12345678901234567890123456789012345678' AS DECIMAL(38,0)), CAST('中文测试' AS CLOB), CAST(NULL AS VARCHAR(10)) FROM DUAL", 'limit': 1})
assert typed['rows'][0] == ['9223372036854775806', '12345678901234567890123456789012345678', '中文测试', None], 'DM8 value conversion'
print('PASS: BIGINT, 38-digit DECIMAL, Chinese CLOB and NULL literal values')
indexed = call('/query', {'connection': info, 'sql': "SELECT OWNER, TABLE_NAME FROM ALL_CONSTRAINTS WHERE CONSTRAINT_TYPE='P' AND OWNER NOT IN ('SYS','SYSTEM') LIMIT 1", 'limit': 1})
if indexed['rowCount']:
    owner, name = indexed['rows'][0]
    indexed_meta = call('/schema/table', {**request, 'schema': owner, 'table': name})
    assert any(i['primary'] for i in indexed_meta['indexDefinitions'])
    assert any(c['key'] == 'PRI' for c in indexed_meta['columns'])
    indexed_page = call('/query/data', {**request, 'schema': owner, 'table': name, 'where': '1=0', 'page': 2, 'pageSize': 1})
    assert indexed_page['total'] == 0
    print('PASS: existing primary-key metadata, indexes and primary-key pagination SQL')
else:
    print('SKIP: no existing non-system primary key available')
selected = None
for schema in sorted(schemas, key=lambda s: (s['name'] != info['user'], s['name'] in ('SYS', 'SYSTEM'), s['name'])):
    tables = call('/schema/tables', {**request, 'schema': schema['name']})['items']
    table = next((t for t in tables if t['type'] == 'BASE TABLE'), None)
    if table:
        selected = (schema['name'], table['name'])
        break
if not selected:
    print('SKIP: no visible table; metadata/paging/export require an existing table')
else:
    schema, table = selected
    info['schema'] = schema
    target = {**request, 'schema': schema, 'table': table}
    meta = call('/schema/table', target)
    assert meta['columns']
    print('PASS: columns and indexes;', len(meta['columns']), 'columns;', len(meta['indexDefinitions']), 'indexes')
    assert meta['ddl'], 'GET_DDL returned no definition: ' + '; '.join(meta['warnings'])
    print('PASS: native object DDL')
    # WHERE 1=0 verifies SQL generation without scanning or reading business rows.
    page = call('/query/data', {**target, 'where': '1=0', 'page': 2, 'pageSize': 1})
    assert page['rowCount'] == 0 and page['total'] == 0
    print('PASS: metadata-based ordering and second-page SQL (empty filter, no business data)')
    q = lambda name: '"' + name.replace('"', '""') + '"'
    csv = call('/query/export', {'connection': info, 'sql': 'SELECT * FROM ' + q(schema) + '.' + q(table) + ' WHERE 1=0', 'limit': 1})
    assert csv.startswith(b'\xef\xbb\xbf')
    print('PASS: CSV export headers only')
print('Read-only DM8 checks finished; no database objects or data modified')
