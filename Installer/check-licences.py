#!/usr/bin/env python3
"""Fails when a TiXL release would ship something under a licence outside the allowlist.

Checks, for the Editor and every operator package with TixlDistribution=Bundled:
  - the package's own PackageLicenseExpression (its code plus the natives in its dependencies/ folder),
  - the licence of every NuGet package it restores, read from the package's .nuspec,
  - every file in Dependencies/, which must be listed in Dependencies/third-party.json.
Optional packages are installed on demand and not checked. Run after `dotnet restore t3.sln`.
"""
import glob
import json
import os
import re
import sys
import xml.etree.ElementTree as ElementTree

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

ALLOWED = {
    "MIT", "MIT-0", "BSD-2-Clause", "BSD-3-Clause", "Apache-2.0", "Apache-2.0 WITH LLVM-exception", "Zlib",
    "Unlicense", "MS-PL",
    # Only dynamically linked, which keeps TiXL itself under its own licence
    "LGPL-2.1-only", "LGPL-2.1-or-later", "LGPL-3.0-only", "LGPL-3.0-or-later",
}

# NuGet packages whose .nuspec names a licence file or URL instead of an SPDX expression; checked by hand.
NUGET_LICENCES_CHECKED_BY_HAND = {
    "Microsoft.ML.OnnxRuntime": "MIT",
    "Microsoft.ML.OnnxRuntime.Managed": "MIT",
    "JeremyAnsel.Media.Dds": "MIT",
    "NETStandard.Library": "MIT",
    "SharpDX": "MIT",
    "SharpDX.D3DCompiler": "MIT",
    "SharpDX.Direct2D1": "MIT",
    "SharpDX.Direct3D11": "MIT",
    "SharpDX.DXGI": "MIT",
    "SharpDX.Mathematics": "MIT",
    "SharpDX.XInput": "MIT",
    "Rug.Osc": "MIT",
    "OpenGL.Net": "MIT",
    "StbImageWriteSharp": "Unlicense OR MIT",
    "System.Data.DataSetExtensions": "MIT",
    "runtime.native.System.Data.SqlClient.sni": "MIT",
    "Microsoft.DotNet.PlatformAbstractions": "MIT",
    # The package declares nothing; the project (mellinoe/ImGui.NET) and the native cimgui/Dear ImGui are MIT
    "ImGui.NET": "MIT",
    # Six Labors Split License: Apache-2.0 for use in open-source software, which TiXL is
    "SixLabors.Fonts": "Apache-2.0",
    "SixLabors.ImageSharp": "Apache-2.0",
}


def main() -> int:
    problems = []
    nuget_root = os.environ.get("NUGET_PACKAGES") or os.path.join(os.path.expanduser("~"), ".nuget", "packages")

    projects = [("Editor", os.path.join(ROOT, "Editor", "Editor.csproj"))]
    for csproj in sorted(glob.glob(os.path.join(ROOT, "Operators", "*", "*.csproj"))):
        properties = read_properties(csproj)
        name = os.path.basename(os.path.dirname(csproj))
        distribution = properties.get("TixlDistribution", "")
        if distribution not in ("Bundled", "Optional", "DevOnly"):
            problems.append(f"{name}: TixlDistribution must be Bundled, Optional or DevOnly, is '{distribution}'")
            continue
        if not properties.get("TixlPlatforms"):
            problems.append(f"{name}: TixlPlatforms is missing")
        licence = properties.get("PackageLicenseExpression", "")
        if not licence:
            problems.append(f"{name}: PackageLicenseExpression is missing")
        if distribution != "Bundled":
            continue
        if licence and not is_allowed(licence):
            problems.append(f"{name}: bundled, but its own licence '{licence}' is not allowed")
        projects.append((name, csproj))

    for name, csproj in projects:
        assets = find_assets_file(csproj)
        if assets is None:
            problems.append(f"{name}: no project.assets.json; run `dotnet restore t3.sln` first")
            continue
        for package, version in restored_packages(assets):
            licence = nuget_licence(nuget_root, package, version)
            if licence is None:
                problems.append(f"{name}: NuGet package {package} {version} declares no SPDX licence and is not "
                                f"in NUGET_LICENCES_CHECKED_BY_HAND")
            elif not is_allowed(licence):
                problems.append(f"{name}: NuGet package {package} {version} is licensed '{licence}'")

    problems.extend(check_dependencies_folder())

    if problems:
        print("Licence check failed:")
        for problem in sorted(set(problems)):
            print("  " + problem)
        return 1

    print(f"Licence check passed: Editor and {len(projects) - 1} bundled operator packages.")
    return 0


def read_properties(csproj):
    properties = {}
    for element in ElementTree.parse(csproj).getroot().iter():
        tag = element.tag.split("}")[-1]
        if tag in ("TixlDistribution", "TixlPlatforms", "PackageLicenseExpression", "BaseIntermediateOutputPath"):
            properties[tag] = (element.text or "").strip()
    return properties


def find_assets_file(csproj):
    directory = os.path.dirname(csproj)
    for candidate in (os.path.join(directory, ".temp", "obj", "project.assets.json"),
                      os.path.join(directory, "obj", "project.assets.json")):
        if os.path.exists(candidate):
            return candidate
    return None


def restored_packages(assets_path):
    with open(assets_path, encoding="utf-8") as file:
        libraries = json.load(file)["libraries"]
    for key, library in libraries.items():
        if library.get("type") == "package":
            package, version = key.split("/")
            yield package, version


def nuget_licence(nuget_root, package, version):
    if package in NUGET_LICENCES_CHECKED_BY_HAND:
        return NUGET_LICENCES_CHECKED_BY_HAND[package]
    for nuspec in glob.glob(os.path.join(nuget_root, package.lower(), version.lower(), "*.nuspec")):
        with open(nuspec, encoding="utf-8", errors="ignore") as file:
            match = re.search(r'<license\s+type="expression"\s*>([^<]+)</license>', file.read())
        if match:
            return match.group(1).strip()
    return None


def is_allowed(expression):
    """True if some alternative of an SPDX expression consists only of allowed licences."""
    expression = expression.replace("(", " ").replace(")", " ")
    for alternative in re.split(r"\s+OR\s+", expression):
        parts = [part.strip() for part in re.split(r"\s+AND\s+", alternative) if part.strip()]
        if parts and all(part in ALLOWED for part in parts):
            return True
    return False


def check_dependencies_folder():
    dependencies = os.path.join(ROOT, "Dependencies")
    with open(os.path.join(dependencies, "third-party.json"), encoding="utf-8") as file:
        listed = json.load(file)["files"]

    problems = []
    for directory, _, files in os.walk(dependencies):
        for file_name in files:
            relative = os.path.relpath(os.path.join(directory, file_name), dependencies).replace(os.sep, "/")
            if relative.startswith("licenses/") or relative.endswith(".md") or relative == "third-party.json":
                continue
            entry = listed.get(relative)
            if entry is None:
                problems.append(f"Dependencies/{relative} is not listed in Dependencies/third-party.json")
            elif not is_allowed(entry["licence"]):
                problems.append(f"Dependencies/{relative} is licensed '{entry['licence']}'")

    for relative, entry in listed.items():
        if not entry.get("builtInCi") and not os.path.exists(os.path.join(dependencies, relative)):
            problems.append(f"Dependencies/third-party.json lists {relative}, which does not exist")
    return problems


if __name__ == "__main__":
    sys.exit(main())
