"""Optional live-engine regression. Use a dedicated test database with DDL permission."""
import argparse
import json
import urllib.request
import uuid
from pathlib import Path

parser=argparse.ArgumentParser()
parser.add_argument("--base",default="http://127.0.0.1:5080")
parser.add_argument("--connections",required=True,help="JSON array of API ConnectionInfo objects, each pointing to a test database")
args=parser.parse_args()
def call(path,body):
    req=urllib.request.Request(args.base+"/api"+path,json.dumps(body).encode(),{"Content-Type":"application/json"})
    with urllib.request.urlopen(req,timeout=90) as response:
        return response.read() if path.endswith("/export") else json.load(response)
for info in json.loads(Path(args.connections).read_text(encoding="utf-8-sig")):
    kind=info["databaseType"]
    assert kind in ("mysql","sqlserver","postgresql","dm8","oracle")
    if kind=="oracle": assert info.get("schema") and info.get("database"),"Choose a service and dedicated test schema"
    assert info.get("schema") if kind=="dm8" else info.get("database"),"Choose an existing dedicated test database/schema"
    info.setdefault("database", "")
    table="datapilot_test_"+uuid.uuid4().hex[:12]
    schema=info.get("schema") or ("dbo" if kind=="sqlserver" else "public")
    def q(name):
        return ("`"+name.replace("`","``")+"`") if kind=="mysql" else ("["+name.replace("]","]]")+"]") if kind=="sqlserver" else ('"'+name.replace('"','""')+'"')
    qualified=q(info["database"] if kind=="mysql" else schema)+"."+q(table)
    query=lambda sql:call("/query",{"connection":info,"sql":sql})
    call("/connection/test",info)
    call("/schema/databases",info)
    if kind!="mysql": call("/schema/schemas",{"connection":info,"database":info["database"]})
    created=False
    try:
        query("CREATE TABLE "+qualified+" (id INTEGER NOT NULL PRIMARY KEY, name VARCHAR(80), amount DECIMAL(18,4))")
        created=True
        query("CREATE INDEX "+q(table+"_ix")+" ON "+qualified+" (name)")
        for values in ["(1,'alpha',123.4567)","(2,'beta',NULL)","(3,'gamma',3.5)"]:
            query("INSERT INTO "+qualified+" VALUES "+values)
        result=query("SELECT * FROM "+qualified+" ORDER BY id")
        assert result["rowCount"]==3 and result["rows"][0][2]=="123.4567"
        meta=call("/schema/table",{"connection":info,"database":info["database"],"schema":schema,"table":table})
        assert len(meta["columns"])==3 and meta["indexDefinitions"] and "CREATE TABLE" in meta["ddl"]
        page=call("/query/data",{"connection":info,"database":info["database"],"schema":schema,"table":table,"page":2,"pageSize":2})
        assert page["total"]==3 and page["rowCount"]==1
        csv=call("/query/export",{"connection":info,"sql":"SELECT * FROM "+qualified+" ORDER BY id"})
        assert csv.startswith(b"\xef\xbb\xbf") and b"alpha" in csv
        assert query("UPDATE "+qualified+" SET name='updated' WHERE id=1")["rowsAffected"]==1
        print("PASS:",kind,"connection, catalogs, metadata, DDL, paging, decimal, CSV and writes")
    finally:
        if created: query("DROP TABLE "+qualified)
