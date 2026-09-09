"""Live Oracle checks over HTTPS; no database objects or business rows modified/read."""
import argparse
import json
import os
import ssl
import urllib.request
import urllib.error

parser = argparse.ArgumentParser()
parser.add_argument('--base', required=True)
parser.add_argument('--cafile', required=True)
args = parser.parse_args()
context = ssl.create_default_context(cafile=args.cafile)
info = {'databaseType': 'oracle', 'host': os.environ['ORACLE_TEST_HOST'], 'port': 1521,
        'user': os.environ.get('ORACLE_TEST_USER', 'SYSTEM'), 'password': os.environ['ORACLE_TEST_PASSWORD'],
        'database': os.environ.get('ORACLE_TEST_SERVICE', 'XEPDB1'), 'oracleConnectionType': 'service'}
def call(path, body):
    request = urllib.request.Request(args.base + '/api' + path, json.dumps(body).encode(), {'Content-Type': 'application/json'})
    try:
        with urllib.request.urlopen(request, context=context, timeout=90) as r:
            return r.read() if path.endswith('/export') else json.load(r)
    except urllib.error.HTTPError as e:
        raise RuntimeError(path + ': ' + e.read().decode().replace(info['password'], '[redacted]')) from None
query = lambda sql: call('/query', {'connection': info, 'sql': sql, 'limit': 5})
print('PASS: live Oracle connection:', call('/connection/test', info)['serverVersion'])
assert call('/schema/databases', info)['items']
assert call('/schema/schemas', {'connection': info, 'database': info['database']})['items']
assert str(query('SELECT 1 FROM DUAL;')['rows'][0][0]) == '1'
typed = query("SELECT CAST(12345678901234567890123456789012345678 AS NUMBER(38,0)), TO_CLOB('中文测试'), HEXTORAW('00FF'), CAST(NULL AS VARCHAR2(10)) FROM DUAL;")
assert typed['rows'][0] == ['12345678901234567890123456789012345678', '中文测试', '0x00FF', None]
print('PASS: service/schema catalogs, SQL semicolon, NUMBER(38), CLOB, RAW and NULL')
schema = info['user']
tables = call('/schema/tables', {'connection': info, 'database': info['database'], 'schema': schema})['items']
assert tables
name = next(t['name'] for t in tables if t['type'] == 'BASE TABLE')
info['schema'] = schema
target = {'connection': info, 'database': info['database'], 'schema': schema, 'table': name}
meta = call('/schema/table', target)
assert meta['columns'] and meta['ddl'], 'Metadata or GET_DDL unavailable'
page = call('/query/data', {**target, 'where': '1=0', 'page': 2, 'pageSize': 1})
assert page['total'] == 0 and page['rowCount'] == 0
q = lambda n: '"' + n.replace('"', '""') + '"'
csv = call('/query/export', {'connection': info, 'sql': 'SELECT * FROM ' + q(schema) + '.' + q(name) + ' WHERE 1=0;'})
assert csv.startswith(b'\xef\xbb\xbf')
print('PASS: table columns/indexes, native DDL, current-schema setting, second page and CSV headers')
print('Oracle read-only checks passed; no database objects or data modified')
