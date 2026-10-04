#!/usr/bin/env python3
"""How long ago each third-party NuGet dependency last published.

Reads the PackageReferences out of the repo's csproj files and asks nuget.org when each package last
shipped anything. Age is a prompt to look, not a verdict - see Plan_DependencyHealth.md.

    python3 .agentic/Plans/dependency-age.py [--all]

Microsoft, System.* and test-only packages are skipped unless --all is given.
"""
import concurrent.futures, datetime, glob, gzip, json, os, re, sys, urllib.request

SKIP_PREFIXES = ("Microsoft.", "System.", "xunit", "NuGet.", "coverlet")

def packages_in_repo(root):
    found = {}
    for path in glob.glob(os.path.join(root, "**", "*.csproj"), recursive=True):
        if f"{os.sep}.temp{os.sep}" in path or f"{os.sep}obj{os.sep}" in path:
            continue
        with open(path, encoding="utf-8", errors="ignore") as f:
            for name, version in re.findall(r'<PackageReference\s+Include="([^"]+)"\s+Version="([^"]+)"', f.read()):
                found.setdefault(name, version)
    return found

def latest_release(pkg):
    url = f"https://api.nuget.org/v3/registration5-gz-semver2/{pkg.lower()}/index.json"
    for attempt in range(3):
        try:
            def fetch(u):
                raw = urllib.request.urlopen(urllib.request.Request(u, headers={"Accept-Encoding": "gzip"}), timeout=30).read()
                return json.loads(gzip.decompress(raw) if raw[:2] == b"\x1f\x8b" else raw)

            page = fetch(url)["items"][-1]
            items = page.get("items") or fetch(page["@id"])["items"]
            entry = max(items, key=lambda i: i["catalogEntry"].get("published", ""))["catalogEntry"]
            return pkg, entry["version"], entry.get("published", "")[:10]
        except Exception as e:
            if attempt == 2:
                return pkg, "?", f"unavailable ({type(e).__name__})"
    return pkg, "?", "unavailable"

def main():
    show_all = "--all" in sys.argv
    root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
    pinned = packages_in_repo(root)
    names = [p for p in pinned if show_all or not p.startswith(SKIP_PREFIXES)]

    today = datetime.date.today()
    rows = []
    with concurrent.futures.ThreadPoolExecutor(8) as pool:
        for pkg, version, published in pool.map(latest_release, sorted(names)):
            try:
                age = (today - datetime.date.fromisoformat(published)).days / 365.25
            except ValueError:
                age = -1.0
            rows.append((age, pkg, pinned[pkg], version, published))

    rows.sort(reverse=True)
    print(f"{'age':>7}  {'package':45s} {'pinned':22s} {'newest':22s} published")
    for age, pkg, pin, version, published in rows:
        label = f"{age:4.1f} y" if age >= 0 else "    ?"
        print(f"{label:>7}  {pkg:45s} {pin:22s} {version:22s} {published}")

if __name__ == "__main__":
    main()
