#!/usr/bin/env python3
"""Check that every package.json declares what its .asmdef files reference.

Usage (from the repo root):  python tools/check_dependencies.py [package ...]

Rules, per package:
  * An assembly that references another package's assembly needs that package in
    "dependencies" -- unless the reference is optional: the asmdef has a
    versionDefine for that package and lists its define in defineConstraints
    (the assembly then simply does not compile without the package).
    Packages those dependencies pull in transitively count as available.
  * A declared LongLeap dependency must not ask for a higher version than the
    package actually has.
Unity module packages (com.unity.modules.*) are not asmdef references and are
not checked. Exit code 1 when anything is wrong.
"""
import glob
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# Unity assemblies that are not discoverable from this repo -> owning package.
UNITY_ASSEMBLIES = {
    "Unity.Addressables": "com.unity.addressables",
    "Unity.ResourceManager": "com.unity.addressables",
    "Unity.Localization": "com.unity.localization",
    "Unity.InputSystem": "com.unity.inputsystem",
    "Unity.TextMeshPro": "com.unity.ugui",
    "UnityEngine.UI": "com.unity.ugui",
    "Newtonsoft.Json": "com.unity.nuget.newtonsoft-json",
}


def load(path):
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)


def version_tuple(v):
    return tuple(int(x) for x in v.split("-")[0].split("."))


def closure(names, manifests):
    """The packages plus everything they pull in through their own package.json."""
    result, stack = set(), list(names)
    while stack:
        name = stack.pop()
        if name in result:
            continue
        result.add(name)
        stack.extend(manifests.get(name, {}).get("dependencies", {}))
    return result


def main(selected):
    packages = sorted(
        d for d in os.listdir(ROOT)
        if d.startswith("com.longleap.") and os.path.isfile(os.path.join(ROOT, d, "package.json")))
    manifests = {p: load(os.path.join(ROOT, p, "package.json")) for p in packages}
    asmdefs = {p: glob.glob(os.path.join(ROOT, p, "**", "*.asmdef"), recursive=True) for p in packages}

    owner = dict(UNITY_ASSEMBLIES)
    for p, files in asmdefs.items():
        for f in files:
            owner[load(f)["name"]] = p

    errors = []
    for p in selected or packages:
        if p not in manifests:
            errors.append(f"{p}: no such package")
            continue
        declared = manifests[p].get("dependencies", {})
        for f in asmdefs[p]:
            asm = load(f)
            constraints = set(asm.get("defineConstraints", []))
            optional = {vd["name"] for vd in asm.get("versionDefines", []) if vd.get("define") in constraints}
            available = closure(set(declared) | optional, manifests)
            for ref in asm.get("references", []):
                target = owner.get(ref)
                if not target or target == p or target in available:
                    continue
                errors.append(f"{p}: {asm['name']} references {ref} but package.json does not declare {target}")
        for dep, wanted in declared.items():
            if dep in manifests and version_tuple(wanted) > version_tuple(manifests[dep]["version"]):
                errors.append(f"{p}: asks for {dep} {wanted}, but it is {manifests[dep]['version']}")

    for e in errors:
        print(e)
    print(f"{len(selected or packages)} package(s) checked, {len(errors)} problem(s).")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
