#!/usr/bin/env python3
"""Packs every operator package with TixlDistribution=Optional into an installable extension.

For each one it writes TiXL.<Name>.<TiXL version>.nupkg from the package's Release build output, plus
extensions.json, the index the editor's Settings -> Extensions page reads from the GitHub release of the same
version. The package folder sits under tixl/ inside the .nupkg, next to the NuGet metadata, so the file stays a
valid NuGet package. Build the solution in Release first.

usage: pack-extensions.py <output folder> [--version 4.4.0.5]
"""
import argparse
import glob
import hashlib
import json
import os
import re
import sys
import xml.etree.ElementTree as ElementTree
import zipfile
from xml.sax.saxutils import escape

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PACKAGE_FOLDER_IN_ARCHIVE = "tixl"
PACKAGE_TYPE = "TixlOperatorPackage"

# Fixed timestamp, so packing the same build twice gives identical files and checksums
ZIP_TIMESTAMP = (2020, 1, 1, 0, 0, 0)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("output")
    parser.add_argument("--version", default=read_tixl_version())
    arguments = parser.parse_args()
    os.makedirs(arguments.output, exist_ok=True)

    extensions = []
    for csproj in sorted(glob.glob(os.path.join(ROOT, "Operators", "*", "*.csproj"))):
        properties = read_properties(csproj)
        if properties.get("TixlDistribution") != "Optional":
            continue

        name = os.path.basename(os.path.dirname(csproj))
        build_output = os.path.join(os.path.dirname(csproj), ".temp", "bin", "Release", properties.get("TargetFramework", "net10.0"))
        if not os.path.exists(os.path.join(build_output, "OperatorPackage.json")):
            print(f"error: {name} has no Release build output in {build_output}", file=sys.stderr)
            return 1

        package_id = f"TiXL.{name}"
        file_name = f"{package_id}.{arguments.version}.nupkg"
        path = os.path.join(arguments.output, file_name)
        write_package(path, package_id, arguments.version, properties, build_output)
        with open(path, "rb") as file:
            sha256 = hashlib.sha256(file.read()).hexdigest()

        extensions.append({
            "id": package_id,
            "name": name,
            "title": properties.get("Title", name),
            "description": properties.get("Description", ""),
            "licence": properties.get("PackageLicenseExpression", ""),
            "platforms": [p for p in properties.get("TixlPlatforms", "").split(";") if p],
            "file": file_name,
            "size": os.path.getsize(path),
            "sha256": sha256,
        })
        print(f"{file_name}: {os.path.getsize(path) / 1e6:.1f} MB")

    with open(os.path.join(arguments.output, "extensions.json"), "w", encoding="utf-8") as file:
        json.dump({"formatVersion": 1, "tixlVersion": arguments.version, "extensions": extensions}, file, indent=2)
        file.write("\n")
    return 0


def write_package(path, package_id, version, properties, build_output):
    files = []
    for directory, _, file_names in os.walk(build_output):
        for file_name in file_names:
            full_path = os.path.join(directory, file_name)
            files.append((full_path, f"{PACKAGE_FOLDER_IN_ARCHIVE}/{os.path.relpath(full_path, build_output).replace(os.sep, '/')}"))
    files.sort(key=lambda entry: entry[1])

    licence = properties.get("PackageLicenseExpression", "")
    if "LicenseRef-" in licence:
        # nuget.org accepts only SPDX expressions; anything else ships as a licence file
        licence_files = [archive for _, archive in files if f"{PACKAGE_FOLDER_IN_ARCHIVE}/licenses/" in archive]
        licence_element = f'<license type="file">{escape(licence_files[0])}</license>' if licence_files else ""
    else:
        licence_element = f'<license type="expression">{escape(licence)}</license>'

    nuspec = f"""<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>{escape(package_id)}</id>
    <version>{escape(version)}</version>
    <title>{escape(properties.get("Title", package_id))}</title>
    <authors>TiXL</authors>
    <description>{escape(properties.get("Description", package_id))}</description>
    {licence_element}
    <projectUrl>https://github.com/tixl3d/tixl</projectUrl>
    <tags>tixl {escape(properties.get("TixlPlatforms", "").replace(";", " "))}</tags>
    <packageTypes>
      <packageType name="{PACKAGE_TYPE}" />
    </packageTypes>
  </metadata>
</package>
"""
    extensions = sorted({os.path.splitext(archive)[1].lstrip(".").lower() for _, archive in files if os.path.splitext(archive)[1]})
    content_types = ('<?xml version="1.0" encoding="utf-8"?>\n'
                     '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">\n'
                     '  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />\n'
                     '  <Default Extension="nuspec" ContentType="application/octet" />\n'
                     + "".join(f'  <Default Extension="{escape(extension)}" ContentType="application/octet" />\n'
                               for extension in extensions if extension not in ("rels", "nuspec"))
                     + "</Types>\n")
    relationships = ('<?xml version="1.0" encoding="utf-8"?>\n'
                     '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">\n'
                     f'  <Relationship Type="http://schemas.microsoft.com/packaging/2010/07/manifest" Target="/{package_id}.nuspec" Id="R0" />\n'
                     "</Relationships>\n")

    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as archive:
        add_text(archive, "_rels/.rels", relationships)
        add_text(archive, f"{package_id}.nuspec", nuspec)
        add_text(archive, "[Content_Types].xml", content_types)
        for full_path, archive_path in files:
            info = zipfile.ZipInfo(archive_path, ZIP_TIMESTAMP)
            info.compress_type = zipfile.ZIP_DEFLATED
            with open(full_path, "rb") as file:
                archive.writestr(info, file.read())


def add_text(archive, archive_path, text):
    info = zipfile.ZipInfo(archive_path, ZIP_TIMESTAMP)
    info.compress_type = zipfile.ZIP_DEFLATED
    archive.writestr(info, text.encode("utf-8"))


def read_properties(csproj):
    properties = {}
    for element in ElementTree.parse(csproj).getroot().iter():
        tag = element.tag.split("}")[-1]
        if tag in ("TixlDistribution", "TixlPlatforms", "PackageLicenseExpression", "Title", "Description", "TargetFramework"):
            properties[tag] = (element.text or "").strip()
    return properties


def read_tixl_version():
    with open(os.path.join(ROOT, "Tixl.props"), encoding="utf-8") as file:
        return re.search(r"<TixlVersion>([^<]+)</TixlVersion>", file.read()).group(1)


if __name__ == "__main__":
    sys.exit(main())
