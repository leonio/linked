#!/usr/bin/env python3
"""Look things up in GitHub's OpenAPI description without loading 14 MB into your head.

  python3 tools/ghapi.py find notifications          # list operations whose id/path/summary match
  python3 tools/ghapi.py op activity/list-notifications-for-authenticated-user
  python3 tools/ghapi.py op "GET /notifications"     # same thing, by method and path
  python3 tools/ghapi.py schema thread               # fields of a schema, one level deep
  python3 tools/ghapi.py schema thread --depth 2     # expand nested schemas too

The description is downloaded once to .cache/github-openapi.json (git-ignored).
Delete that file to refresh it.
"""
import json
import os
import sys
import urllib.request

URL = "https://raw.githubusercontent.com/github/rest-api-description/main/descriptions/api.github.com/api.github.com.json"
CACHE = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), ".cache", "github-openapi.json")
METHODS = ("get", "post", "put", "patch", "delete")


def load():
    if not os.path.exists(CACHE):
        os.makedirs(os.path.dirname(CACHE), exist_ok=True)
        print(f"downloading {URL} ...", file=sys.stderr)
        urllib.request.urlretrieve(URL, CACHE)
    with open(CACHE, encoding="utf-8") as f:
        return json.load(f)


def deref(doc, node):
    while isinstance(node, dict) and "$ref" in node:
        target = doc
        for part in node["$ref"].lstrip("#/").split("/"):
            target = target[part]
        node = target
    return node


def ref_name(node):
    return node["$ref"].rsplit("/", 1)[-1] if isinstance(node, dict) and "$ref" in node else None


def type_of(doc, node):
    """A short C#-minded description of a schema node."""
    name = ref_name(node)
    if name:
        return name
    if not isinstance(node, dict):
        return "?"
    for key in ("oneOf", "anyOf", "allOf"):
        if key in node:
            return key + "(" + " | ".join(type_of(doc, n) for n in node[key]) + ")"
    t = node.get("type")
    if t == "array":
        return type_of(doc, node.get("items", {})) + "[]"
    if t == "string":
        if "enum" in node:
            return "enum(" + ",".join(str(e) for e in node["enum"]) + ")"
        return {"date-time": "DateTimeOffset", "uri": "string(uri)"}.get(node.get("format"), "string")
    if t == "integer":
        return "long" if node.get("format") == "int64" else "int"
    if t == "object":
        return "object{...}" if node.get("properties") else "object"
    return t or "?"


def print_schema(doc, node, depth, indent="  ", seen=()):
    name = ref_name(node)
    node = deref(doc, node)
    for key in ("allOf", "oneOf", "anyOf"):
        if key in node and "properties" not in node:
            for option in node[key]:
                print(f"{indent}-- {key}: {type_of(doc, option)}")
                if depth > 0 and (ref_name(option) or option.get("properties")):
                    print_schema(doc, option, depth - 1, indent + "  ", seen)
            return
    if node.get("type") == "array":
        print(f"{indent}(array of {type_of(doc, node.get('items', {}))})")
        print_schema(doc, node.get("items", {}), depth, indent, seen)
        return
    required = set(node.get("required", []))
    for prop, sub in node.get("properties", {}).items():
        resolved = deref(doc, sub)
        flags = []
        if prop not in required:
            flags.append("optional")
        if resolved.get("nullable") or sub.get("nullable"):
            flags.append("nullable")
        suffix = f"  ({', '.join(flags)})" if flags else ""
        print(f"{indent}{prop}: {type_of(doc, sub)}{suffix}")
        child = ref_name(sub) or (ref_name(resolved.get("items", {})) if resolved.get("type") == "array" else None)
        expandable = child or resolved.get("properties")
        if depth > 1 and expandable and child not in seen:
            target = sub if not (resolved.get("type") == "array") else resolved.get("items", {})
            print_schema(doc, target, depth - 1, indent + "    ", seen + ((child,) if child else ()) + ((name,) if name else ()))


def operations(doc):
    for path, item in doc["paths"].items():
        for method in METHODS:
            if method in item:
                yield method.upper(), path, item[method]


def cmd_find(doc, text):
    text = text.lower()
    for method, path, op in operations(doc):
        if text in f"{op['operationId']} {path} {op.get('summary', '')}".lower():
            print(f"{method:6} {path}\n       {op['operationId']}  --  {op.get('summary', '')}")


def cmd_op(doc, key, depth):
    for method, path, op in operations(doc):
        if key in (op["operationId"], f"{method} {path}"):
            break
    else:
        sys.exit(f"no operation '{key}'. Try: python3 tools/ghapi.py find <text>")

    print(f"{method} {path}")
    print(f"operationId: {op['operationId']}")
    print(f"summary: {op.get('summary', '')}")
    apps = op.get("x-github", {}).get("enabledForGitHubApps")
    print(f"docs: {op.get('externalDocs', {}).get('url', '')}")
    if apps is False:
        print("auth: OAuth App token only (not available to GitHub App tokens) -- fine for this app")
    print("parameters:")
    for p in op.get("parameters", []):
        p = deref(doc, p)
        need = "required" if p.get("required") else "optional"
        default = p.get("schema", {}).get("default")
        extra = f", default {default}" if default is not None else ""
        print(f"  {p['name']} (in {p['in']}, {need}{extra}): {type_of(doc, p.get('schema', {}))}")
    body = op.get("requestBody", {}).get("content", {}).get("application/json", {}).get("schema")
    if body:
        print(f"request body: {type_of(doc, body)}")
        print_schema(doc, body, depth)
    for status, response in op["responses"].items():
        response = deref(doc, response)
        schema = response.get("content", {}).get("application/json", {}).get("schema")
        headers = ", ".join(response.get("headers", {}))
        line = f"response {status}: {type_of(doc, schema) if schema else '(no body)'}"
        print(line + (f"   headers: {headers}" if headers else ""))
        if schema and status.startswith("2"):
            print_schema(doc, schema, depth)


def cmd_schema(doc, name, depth):
    schemas = doc["components"]["schemas"]
    if name not in schemas:
        near = [s for s in schemas if name.lower() in s.lower()][:20]
        sys.exit(f"no schema '{name}'. Similar: {', '.join(near) or 'none'}")
    print(f"schema {name}")
    print_schema(doc, {"$ref": f"#/components/schemas/{name}"}, depth)


def main(argv):
    depth = 1
    if "--depth" in argv:
        i = argv.index("--depth")
        depth = int(argv[i + 1])
        del argv[i:i + 2]
    if len(argv) != 2 or argv[0] not in ("find", "op", "schema"):
        sys.exit(__doc__)
    doc = load()
    {"find": lambda: cmd_find(doc, argv[1]),
     "op": lambda: cmd_op(doc, argv[1], depth),
     "schema": lambda: cmd_schema(doc, argv[1], depth)}[argv[0]]()


if __name__ == "__main__":
    main(sys.argv[1:])
