#!/usr/bin/env python3
"""
split_topic_docs.py — one-time migration: move each ui: topic's doc body out of the single
ui-topics.md registry into its own file under references/topics/ui/<Id>.md, and reduce
ui-topics.md to metadata only (heading + id/synonyms/parent/classes).

After this, the per-topic .md is the single hand-edited source of the doc body; ui-topics.md keeps
just the metadata, and analysis_to_index.py records a `docFile` pointer in topics.json that the
editor loads lazily. Run once; safe to re-run (idempotent — a metadata-only ui-topics.md yields no
bodies to move).
"""

import re
from pathlib import Path

HERE = Path(__file__).resolve().parent
HELP = HERE.parent
MD = HELP / "references" / "topics" / "ui-topics.md"
OUTDIR = HELP / "references" / "topics" / "ui"


def main():
    OUTDIR.mkdir(parents=True, exist_ok=True)
    lines = MD.read_text(encoding="utf-8").replace("\r\n", "\n").split("\n")

    out = []
    i = 0
    while i < len(lines) and not lines[i].startswith("## "):   # preamble
        out.append(lines[i])
        i += 1

    written = 0
    while i < len(lines):
        header = lines[i]
        i += 1
        meta = []
        while i < len(lines) and lines[i].strip() != "" and not lines[i].startswith("## "):
            meta.append(lines[i])
            i += 1
        while i < len(lines) and lines[i].strip() == "":
            i += 1
        body = []
        while i < len(lines) and not lines[i].startswith("## "):
            body.append(lines[i])
            i += 1

        topic_id = None
        for m in meta:
            mm = re.match(r"id:\s*(\S+)\s*$", m)
            if mm:
                topic_id = mm.group(1)

        body_text = "\n".join(body).strip()
        if topic_id and body_text and not body_text.lower().startswith("_todo"):
            (OUTDIR / f"{topic_id}.md").write_text(body_text + "\n", encoding="utf-8", newline="\n")
            written += 1

        out.append(header)
        out.extend(meta)
        out.append("")   # blank line closes the metadata block

    MD.write_text("\n".join(out).rstrip("\n") + "\n", encoding="utf-8", newline="\n")
    print(f"wrote {written} topic doc file(s) to {OUTDIR.relative_to(HELP)}; ui-topics.md is now metadata-only")


if __name__ == "__main__":
    main()
