"""End-to-end API regression against a running DataPilot instance (no external DB required)."""
import argparse
from contextlib import closing
import json
import sqlite3
import tempfile
import urllib.request
import urllib.error
import uuid
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--base", default="http://127.0.0.1:5099")
args = parser.parse_args()
base = args.base
checks = 0

def check(condition, message):
    global checks
    assert condition, message
    checks += 1
    print("PASS:", message)

def request(path, data=None, method=None, raw=False, headers=None):
    body = json.dumps(data).encode() if data is not None else None
    req = urllib.request.Request(base + "/api" + path, body, headers or {"Content-Type": "application/json"}, method=method)
    try:
        with urllib.request.urlopen(req, timeout=30) as response:
            content = response.read()
            return response.status, content if raw else json.loads(content)
    except urllib.error.HTTPError as error:
        return error.code, error.read().decode()

def upload(name, content):
    boundary = uuid.uuid4().hex
    payload = (f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{name}"\r\nContent-Type: application/octet-stream\r\n\r\n').encode() + content + f"\r\n--{boundary}--\r\n".encode()
    req = urllib.request.Request(base + "/api/sqlite/files", payload, {"Content-Type": "multipart/form-data; boundary=" + boundary})
    try:
        with urllib.request.urlopen(req, timeout=30) as response: return response.status, json.load(response)
    except urllib.error.HTTPError as error: return error.code, error.read().decode()

with tempfile.TemporaryDirectory(prefix="datapilot-test-") as temp:
    db = Path(temp) / "sample.db"
    with closing(sqlite3.connect(db)) as connection:
        connection.executescript("""
            CREATE TABLE parent (id INTEGER PRIMARY KEY, name TEXT);
            CREATE TABLE "odd""table" (id INTEGER PRIMARY KEY, parent_id INTEGER REFERENCES parent(id), name TEXT, value REAL, payload BLOB);
            CREATE INDEX idx_name ON "odd""table"(name DESC);
            CREATE VIEW sample_view AS SELECT id,name FROM "odd""table";
            INSERT INTO parent VALUES (1,'parent');
            INSERT INTO "odd""table" VALUES (1,1,'hello, "CSV"',1.25,X'00FF'), (2,1,'second',NULL,NULL),(3,1,'third',3.5,NULL);
        """)
    status, item = upload("sample.db", db.read_bytes())
    check(status == 200, "SQLite upload and integrity check")
    file_id = item["id"]
    info = {"databaseType": "sqlite", "fileId": file_id, "database": "main"}
    try:
        status, result = request("/connection/test", info)
        check(status == 200 and result["ok"], "SQLite connection")
        status, result = request("/schema/databases", info)
        check(result["items"][0]["name"] == "main", "SQLite database list")
        status, result = request("/schema/tables", {"connection": info, "database": "main"})
        check(len(result["items"]) == 3 and any(x["type"] == "VIEW" for x in result["items"]), "Tables and views")
        status, result = request("/schema/table", {"connection": info, "database": "main", "table": 'odd"table'})
        check(status == 200 and len(result["columns"]) == 5 and "CREATE INDEX" in result["ddl"], "Structure, quoted identifiers and native DDL")
        restored = sqlite3.connect(":memory:")
        restored.executescript(result["ddl"])
        check(restored.execute("SELECT count(*) FROM sqlite_schema WHERE type='table'").fetchone()[0] == 1, "SQLite DDL recreates table and index")
        restored.close()
        status, result = request("/query/data", {"connection": info, "database": "main", "table": 'odd"table', "page": 2, "pageSize": 2})
        check(status == 200 and result["total"] == 3 and len(result["rows"]) == 1, "Pagination with primary key ordering")
        status, result = request("/query", {"connection": info, "sql": 'SELECT * FROM "odd""table"', "limit": 1})
        check(result["truncated"] and result["rowCount"] == 1 and result["rows"][0][4] == "0x00FF", "Row limits and binary conversion")
        status, result = request("/query", {"connection": info, "sql": "PRAGMA table_info(parent)"})
        check(status == 200 and result["isQuery"] and result["rowCount"] == 2, "PRAGMA returns a result set")
        status, result = request("/query", {"connection": info, "sql": "INSERT INTO parent(name) VALUES ('new') RETURNING id"})
        check(status == 200 and result["isQuery"] and result["rows"], "INSERT RETURNING result")
        status, result = request("/query", {"connection": info, "sql": "UPDATE parent SET name='changed' WHERE id=1"})
        check(status == 200 and result["rowsAffected"] == 1, "Write affected row count")
        status, result = request("/query", {"connection": info, "sql": "SELECT 1 AS first; INSERT INTO parent(name) VALUES ('batch');"})
        check(status == 200 and result["rows"][0][0] == "1", "Batch result and subsequent statement")
        status, csv = request("/query/export", {"connection": info, "sql": 'SELECT name FROM "odd""table" ORDER BY id'}, raw=True)
        check(status == 200 and csv.startswith(b"\xef\xbb\xbf") and b'hello, ""CSV""' in csv, "UTF-8 BOM CSV escaping")
        status, result = request("/query", {"connection": {**info, "readOnly": True}, "sql": "DELETE FROM parent"})
        check(status == 400, "Read-only access rejects writes")
        for sql in ["ATTACH DATABASE ':memory:' AS evil", "VACUUM INTO 'escape.db'", "PRAGMA writable_schema=ON", "SELECT load_extension('evil')"]:
            status, result = request("/query", {"connection": info, "sql": sql})
            check(status == 400, "Restricted SQLite operation: " + sql)
        status, result = request("/connection/test", {**info, "fileId": "../appsettings.json"})
        check(status == 400, "Path traversal rejected")
        status, result = upload("bad.db", b"not a database" * 100)
        check(status == 400, "Non-SQLite payload rejected")
        status, result = upload("bad.db", b"SQLite format 3\x00" + bytes(1000))
        check(status == 400, "Corrupt database rejected")
        status, result = request("/sqlite/files")
        check(any(x["id"] == file_id for x in result["items"]), "Managed file listing")
        status, snapshot = request("/sqlite/files/" + file_id + "/download", raw=True)
        downloaded = Path(temp) / "download.db"
        downloaded.write_bytes(snapshot)
        with closing(sqlite3.connect(downloaded)) as connection:
            check(status == 200 and connection.execute("SELECT name FROM parent WHERE id=1").fetchone()[0] == "changed", "Downloaded snapshot contains writes")
    finally:
        status, result = request("/sqlite/files/" + file_id, method="DELETE")
        check(status == 200, "Delete managed file")
    status, result = request("/connection/test", info)
    check(status == 400, "Deleted file cannot be reopened")
print(f"{checks} API checks passed")
