import json, sys, os
p = r"C:\Users\hp\Documents\Warcraft III\Hero Team Wars\builds\diagnostics\v8-inspect.json"
raw = open(p, encoding="utf-8").read()
# engine response is one JSON line, possibly with BOM
raw = raw.lstrip("\ufeff").strip()
data = json.loads(raw)
result = data.get("result", data)
# inspect_map may nest canonical map
obj = result.get("object_data")
if obj is None and isinstance(result.get("canonical_map"), dict):
    obj = result["canonical_map"].get("object_data")
if obj is None:
    # maybe the whole inspect is the map
    print("top keys:", list(result.keys())[:40] if isinstance(result, dict) else type(result))
    # try nested
    for k,v in (result.items() if isinstance(result, dict) else []):
        if isinstance(v, dict) and "object_data" in v:
            obj = v["object_data"]
            print("found object_data under", k)
            break
        if k == "map" and isinstance(v, dict):
            print("map keys", list(v.keys())[:40])
out = r"C:\Users\hp\Documents\Warcraft III\Hero Team Wars\builds\diagnostics\v8-object-data.json"
open(out, "w", encoding="utf-8").write(json.dumps(obj, indent=2) if obj is not None else json.dumps({"error":"no object_data","keys": list(result.keys()) if isinstance(result, dict) else str(type(result))}, indent=2))
print("wrote", out, "count", len(obj) if isinstance(obj, list) else "n/a")
