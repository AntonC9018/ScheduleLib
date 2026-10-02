#!/usr/bin/env python3
"""Inspect schedule DOCX table text and flag common source defects."""

from __future__ import annotations

import argparse
import re
import sys
import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET


WORD_NS = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
W = f"{{{WORD_NS}}}"
CHECKS = (
    ("letter-spaced word", re.compile(r"(?:[^\W\d_]\s+){5,}[^\W\d_]", re.UNICODE)),
    ("duplicate closing parenthesis", re.compile(r"\)\)")),
    ("repeated room suffix", re.compile(r"\b\d+[A-Za-z]?/\d+(?:/\d+)+\b")),
    ("underscore placeholder", re.compile(r"_{3,}")),
    (
        "group-shaped subgroup label",
        re.compile(r"\b[A-ZĂÂÎȘȚ]+(?:-[A-ZĂÂÎȘȚ]+)?\d{4}\s*:", re.UNICODE),
    ),
    (
        "compact compound modifiers",
        re.compile(r"\((?:lab|curs|sem),\S[^,()]*-[^,()]+,\S[^,()]*-[^,()]+\)", re.IGNORECASE),
    ),
)


def paragraph_text(paragraph: ET.Element) -> str:
    return "".join(node.text or "" for node in paragraph.iter(f"{W}t"))


def cell_text(cell: ET.Element) -> str:
    paragraphs = [paragraph_text(p).strip() for p in cell.iter(f"{W}p")]
    return " | ".join(text for text in paragraphs if text)


def inspect(path: Path, dump: bool) -> int:
    if path.suffix.lower() != ".docx":
        print(f"{path}: expected a .docx file", file=sys.stderr)
        return 2
    try:
        with zipfile.ZipFile(path) as archive:
            root = ET.fromstring(archive.read("word/document.xml"))
    except (FileNotFoundError, KeyError, zipfile.BadZipFile, ET.ParseError) as error:
        print(f"{path}: cannot read DOCX: {error}", file=sys.stderr)
        return 2

    findings = 0
    print(f"--- {path} ---")
    for table_index, table in enumerate(root.iter(f"{W}tbl")):
        for row_index, row in enumerate(table.findall(f"{W}tr")):
            for cell_index, cell in enumerate(row.findall(f"{W}tc")):
                text = cell_text(cell)
                if not text:
                    continue
                location = f"table {table_index}, row {row_index}, cell {cell_index}"
                if dump:
                    print(f"{location}: {text}")
                for label, pattern in CHECKS:
                    if pattern.search(text):
                        findings += 1
                        print(f"SUSPICIOUS [{label}] {location}: {text}")
    print(f"Findings: {findings}")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("documents", nargs="+", type=Path)
    parser.add_argument("--dump", action="store_true", help="print every non-empty table cell")
    args = parser.parse_args()
    return max(inspect(path, args.dump) for path in args.documents)


if __name__ == "__main__":
    raise SystemExit(main())
