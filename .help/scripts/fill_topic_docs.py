#!/usr/bin/env python3
"""
fill_topic_docs.py — splice drafted topic docs into ui-topics.md.

Reads a JSON map { "<id>": "<doc body>" } (id = the topic's `id:` value, no `ui:` prefix) and, for
each topic block in references/topics/ui-topics.md whose body is still `_TODO_`, replaces that
placeholder with the drafted body. A missing id, an empty body, or a literal "_TODO_" leaves the
placeholder untouched. After running, `analysis_to_index.py` folds the docs into topics.json.

    python fill_topic_docs.py drafts.json
"""

import json
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
HELP = HERE.parent
MD = HELP / "references" / "topics" / "ui-topics.md"


def main():
    if len(sys.argv) < 2:
        sys.exit("usage: python fill_topic_docs.py <drafts.json>")

    drafts = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
    lines = MD.read_text(encoding="utf-8").split("\n")

    out = []
    current_id = None
    filled = []
    left = []

    for ln in lines:
        if ln.startswith("## "):
            current_id = None
        m = re.match(r"id:\s*(\S+)\s*$", ln)
        if m:
            current_id = m.group(1)

        if ln.strip() == "_TODO_":
            body = drafts.get(current_id, "").strip() if current_id else ""
            if body and body != "_TODO_":
                out.append(body)
                filled.append(current_id)
                continue
            left.append(current_id)

        out.append(ln)

    MD.write_text("\n".join(out), encoding="utf-8")
    print(f"filled {len(filled)} topic doc(s)")
    if left:
        print(f"left _TODO_ ({len(left)}): " + ", ".join(x or '?' for x in left))


if __name__ == "__main__":
    main()
