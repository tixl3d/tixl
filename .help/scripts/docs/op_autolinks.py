"""MkDocs hook that turns `[OperatorName]` in prose into links to the operator reference.

Loads `.help/docs/operators/index.json` (written by `ExportWikiDocumentation.cs`) once at
build start, then rewrites bracketed operator references in every page's markdown.

Resolution rules (see `.agentic/Plans/Plan_UpdateHelp.md`, Section 4c):

- `[Lib.image.color.AdjustColors]` — full path → link to that operator.
- `[lib.image.color.AdjustColors]` — lowercase-prefix tolerant → normalised and resolved.
- `[AdjustColors]` — short name with exactly one match → linked.
- `[Value]` — short name with more than one match → left as-is, with a build warning.
- Anything else → left as-is (likely prose, not an operator).

Never touches:

- Fenced code blocks (``` … ```) or indented code blocks.
- Inline code spans (`…`).
- Existing markdown links (`[Foo](…)` — detected by the trailing `(`).
- Reference-style link definitions (`[Foo]: …` at column 0).

Silent unless there's something actionable to report, so we don't drown the build log.
"""

from __future__ import annotations

import json
import logging
import posixpath
import re
from pathlib import Path
from typing import Any

log = logging.getLogger("mkdocs.plugins.op_autolinks")

# `[Name]` or `[Some.Path.Name]` — bracketed identifier, not followed by `(` or `:`.
# Requires the inside to start with a letter and contain only [A-Za-z0-9.]; spaces rule it out,
# so English phrases in brackets (e.g. `[some note]`) are ignored.
_BRACKET_RE = re.compile(
    r"(?<!\\)\[([A-Za-z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)*)\](?![\(\[:])"
)

_FENCE_RE = re.compile(r"^(\s*)(```+|~~~+)")
_INLINE_CODE_RE = re.compile(r"`[^`\n]*`")

_index: dict[str, Any] | None = None
_index_error_reported = False


def _load_index(config) -> dict[str, Any] | None:
    global _index, _index_error_reported
    if _index is not None:
        return _index

    docs_dir = Path(config["docs_dir"])
    index_path = docs_dir / "operators" / "index.json"
    if not index_path.is_file():
        if not _index_error_reported:
            log.info(
                "op_autolinks: %s not found — operator autolinks disabled. "
                "Run the 'Documentation → Export as WIKI' menu in TiXL to generate it.",
                index_path,
            )
            _index_error_reported = True
        return None

    try:
        with index_path.open(encoding="utf-8") as f:
            _index = json.load(f)
    except Exception as exc:  # noqa: BLE001 — surface any load failure to the user
        log.warning("op_autolinks: failed to load %s: %s", index_path, exc)
        _index_error_reported = True
        return None

    by_fullpath = _index.get("by_fullpath", {})
    by_shortname = _index.get("by_shortname", {})
    log.info(
        "op_autolinks: loaded %d operators (%d short names) from %s",
        len(by_fullpath),
        len(by_shortname),
        index_path,
    )
    return _index


def _resolve(name: str, index: dict[str, Any]) -> tuple[str | None, list[str]]:
    """Return (fullpath, candidates). fullpath is None if unresolved or ambiguous."""
    by_fullpath: dict[str, dict[str, str]] = index.get("by_fullpath", {})
    by_shortname: dict[str, list[str]] = index.get("by_shortname", {})

    if "." in name:
        # Normalise a lowercase or mixed-case dotted prefix back to the canonical form we
        # stored in `by_fullpath`. The canonical form starts with a PascalCase root segment
        # (`Lib.…`) while intermediate segments are lowercased already. A fully-lowercased
        # input like `lib.image.color.AdjustColors` should still resolve.
        candidates = [
            full for full in by_fullpath if full.lower() == name.lower()
        ]
        if len(candidates) == 1:
            return candidates[0], candidates
        return None, candidates

    matches = by_shortname.get(name, [])
    if len(matches) == 1:
        return matches[0], matches
    return None, matches


def _fullpath_to_docs_relpath(fullpath: str) -> str:
    """`Lib.io.audio.AudioReaction` → `operators/lib/io/audio/AudioReaction.md` (docs_dir-relative)."""
    ns, _, op = fullpath.rpartition(".")
    ns_dir = ns.lower().replace(".", "/")
    return f"operators/{ns_dir}/{op}.md"


def _relative_link(page_src: str, target_docs_path: str) -> str:
    """Compute the markdown link path from `page_src` to `target_docs_path`.

    Both paths are docs_dir-relative (forward slashes). Result is also forward-slash.
    """
    page_dir = posixpath.dirname(page_src.replace("\\", "/"))
    if not page_dir:
        return target_docs_path
    return posixpath.relpath(target_docs_path, page_dir)


def _rewrite_segment(text: str, index: dict[str, Any], page_src: str) -> str:
    def replace(match: re.Match[str]) -> str:
        name = match.group(1)
        # Heuristic: plain prose words inside brackets (e.g. `[note]`) — skip anything that
        # looks lowercase-only or single-word-lowercase. We still accept lowercase dotted
        # prefixes (`lib.image.…`) because those are intentional namespace references.
        if "." not in name and not name[0].isupper():
            return match.group(0)

        fullpath, candidates = _resolve(name, index)
        if fullpath is None:
            if len(candidates) > 1:
                log.warning(
                    "op_autolinks: ambiguous [%s] in %s — candidates: %s. "
                    "Qualify with the full namespace.",
                    name,
                    page_src,
                    ", ".join(candidates),
                )
            return match.group(0)

        entry = index["by_fullpath"].get(fullpath, {})
        summary = entry.get("summary", "") or ""

        target_docs_path = _fullpath_to_docs_relpath(fullpath)
        link = _relative_link(page_src, target_docs_path)

        # Show only the last path segment in the rendered link; nobody wants to read
        # `Lib.field.adjust.PushPullSDF` inline.
        label = name.rsplit(".", 1)[-1]
        title_attr = f' "{summary}"' if summary else ""
        return f"[{label}]({link}{title_attr})"

    return _BRACKET_RE.sub(replace, text)


def _rewrite_markdown(markdown: str, index: dict[str, Any], page_src: str) -> str:
    """Walk the markdown line-by-line so we can skip fenced code blocks cleanly."""
    out: list[str] = []
    in_fence = False
    fence_marker: str | None = None

    for line in markdown.splitlines(keepends=True):
        stripped = line.lstrip()

        if in_fence:
            out.append(line)
            if fence_marker and stripped.startswith(fence_marker):
                in_fence = False
                fence_marker = None
            continue

        fence_match = _FENCE_RE.match(line)
        if fence_match:
            in_fence = True
            fence_marker = fence_match.group(2)[0] * 3  # normalise to 3-char marker
            out.append(line)
            continue

        # Indented code block (4+ spaces after a blank line) — best effort: skip lines that
        # look like code. Cheap heuristic; we accept the odd miss rather than carrying a full
        # markdown parser into a build hook.
        if line.startswith("    ") and not line.lstrip().startswith(("-", "*", "+", "1.")):
            out.append(line)
            continue

        # Protect inline code spans from replacement, then restore them.
        spans: list[str] = []

        def _stash(match: re.Match[str]) -> str:
            spans.append(match.group(0))
            return f"\x00CODE{len(spans) - 1}\x00"

        protected = _INLINE_CODE_RE.sub(_stash, line)
        rewritten = _rewrite_segment(protected, index, page_src)

        if spans:
            def _restore(match: re.Match[str]) -> str:
                return spans[int(match.group(1))]

            rewritten = re.sub(r"\x00CODE(\d+)\x00", _restore, rewritten)

        out.append(rewritten)

    return "".join(out)


# MkDocs hook entry point.
def on_page_markdown(markdown: str, page, config, files):  # noqa: ARG001 — mkdocs API
    index = _load_index(config)
    if index is None:
        return markdown

    page_src = page.file.src_path if page and page.file else "<unknown>"

    # Don't rewrite the generated operator pages themselves — they already link correctly,
    # and we'd double-wrap the namespace back-link.
    if page_src.startswith("operators" + ("/" if "/" in page_src else "\\")):
        return markdown

    return _rewrite_markdown(markdown, index, page_src)
