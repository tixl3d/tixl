"""Rewrite `<!-- TIXL_MOVED_BANNER -->` blocks in the t3.wiki checkout to point at
the rendered help.tixl.app URLs instead of the GitHub source.

Reads the mapping table from `.agentic/Plans/Plan_UpdateHelp.md` (Section 5), so the
plan stays the single source of truth. Idempotent — runs safely multiple times.

Usage:

    python .help/scripts/wiki/update_banners.py [--wiki-dir ../t3.wiki] [--dry-run]

The script expects a sibling t3.wiki checkout by default.
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
PLAN_FILE = REPO_ROOT / ".agentic" / "Plans" / "Plan_UpdateHelp.md"

SITE_BASE = "https://help.tixl.app"

BANNER_MARKER = "<!-- TIXL_MOVED_BANNER -->"

# The banner block is the marker comment plus one contiguous run of lines starting
# with `>` (the blockquote). Everything after that first non-quote line is preserved.
_BANNER_BLOCK_RE = re.compile(
    r"^" + re.escape(BANNER_MARKER) + r"\n((?:>.*\n)+)",
    re.MULTILINE,
)


def _parse_mapping_table(plan_text: str) -> dict[str, str]:
    """Return {wiki_basename: rendered_url_path} from the plan's Section 5 table.

    wiki_basename is the leftmost column stripped of backticks (e.g. `help.AddingFonts`).
    rendered_url_path is the third column (e.g. `/advanced/AddingFonts/`). Rows with `—`
    in the URL column are skipped (not-migrated pages).
    """
    mapping: dict[str, str] = {}
    in_table = False
    for line in plan_text.splitlines():
        stripped = line.strip()
        if stripped.startswith("| Legacy wiki page"):
            in_table = True
            continue
        if in_table:
            if not stripped.startswith("|"):
                break  # table ended
            # Skip the separator row (|---|---|---|)
            if set(stripped.replace("|", "").strip()) <= {"-", " "}:
                continue
            cells = [c.strip() for c in stripped.strip("|").split("|")]
            if len(cells) < 3:
                continue
            wiki_cell, _source_cell, url_cell = cells[0], cells[1], cells[2]
            url_path = url_cell.strip("`").strip()
            if url_path in ("—", "-", ""):
                continue
            wiki_name = wiki_cell.strip("`").strip()
            if not wiki_name:
                continue
            mapping[wiki_name] = url_path
    return mapping


def _new_banner(rendered_url_path: str) -> str:
    full_url = SITE_BASE + rendered_url_path
    return (
        f"{BANNER_MARKER}\n"
        f"> ⚠ **This page has moved.** The current version is at "
        f"[{full_url.removeprefix('https://')}]({full_url}).\n"
        f"> This wiki copy is no longer maintained — edits made here will be lost.\n"
    )


def _rewrite_file(path: Path, rendered_url_path: str, *, dry_run: bool) -> str:
    """Returns one of: "updated", "already-current", "no-banner", "missing"."""
    if not path.is_file():
        return "missing"

    text = path.read_text(encoding="utf-8")
    if BANNER_MARKER not in text:
        return "no-banner"

    new_block = _new_banner(rendered_url_path)
    replaced, count = _BANNER_BLOCK_RE.subn(new_block, text, count=1)
    if count == 0:
        # Marker present but block shape unexpected — surface it rather than guessing.
        return "no-banner"

    if replaced == text:
        return "already-current"

    if not dry_run:
        path.write_text(replaced, encoding="utf-8")
    return "updated"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--wiki-dir",
        default=str(REPO_ROOT.parent / "t3.wiki"),
        help="Path to the t3.wiki checkout (default: sibling of the repo root).",
    )
    parser.add_argument("--dry-run", action="store_true", help="Don't write files.")
    args = parser.parse_args()

    wiki_dir = Path(args.wiki_dir).resolve()
    if not wiki_dir.is_dir():
        print(f"error: wiki dir not found: {wiki_dir}", file=sys.stderr)
        return 2

    plan_text = PLAN_FILE.read_text(encoding="utf-8")
    mapping = _parse_mapping_table(plan_text)
    if not mapping:
        print("error: no mapping rows parsed from plan", file=sys.stderr)
        return 2

    counts = {"updated": 0, "already-current": 0, "no-banner": 0, "missing": 0}
    for wiki_name, url_path in sorted(mapping.items()):
        wiki_path = wiki_dir / f"{wiki_name}.md"
        status = _rewrite_file(wiki_path, url_path, dry_run=args.dry_run)
        counts[status] += 1
        marker = {"updated": "+", "already-current": "=", "no-banner": "!", "missing": "?"}[status]
        print(f"  {marker} {wiki_name} → {url_path} [{status}]")

    print()
    print("Summary:")
    for k, v in counts.items():
        print(f"  {k:>16}: {v}")
    if args.dry_run:
        print("(dry run — no files written)")
    return 0 if counts["no-banner"] == 0 and counts["missing"] == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
