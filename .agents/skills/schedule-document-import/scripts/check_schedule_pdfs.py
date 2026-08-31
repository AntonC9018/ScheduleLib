#!/usr/bin/env python3
"""Apply temporary text heuristics to generated schedule PDFs."""

from __future__ import annotations

import argparse
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path


LETTER_SPACED = re.compile(r"(?:[^\W\d_]\s+){5,}[^\W\d_]", re.UNICODE)
DANGLING_TITLE = re.compile(r"^\s*[^\w\s]{1,3}\s+\([A-ZĂÂÎȘȚ][A-ZĂÂÎȘȚ0-9_-]*\)\s*$")
LESSON_WITH_TYPE = re.compile(
    r"^\s*(?:(?:I|II|III|eng|ro|ru)\s*:\s*)?(.*?)\s+\((?:curs|sem|lab)\b",
    re.IGNORECASE,
)


def visible_length(value: str) -> int:
    return sum(character.isalnum() for character in value)


def extract(pdftotext: str, path: Path) -> str:
    with tempfile.TemporaryDirectory(prefix="schedule-pdf-check-") as temp_directory:
        input_path = path
        if not str(path).isascii():
            input_path = Path(temp_directory) / "input.pdf"
            shutil.copyfile(path, input_path)
        result = subprocess.run(
            [pdftotext, "-layout", str(input_path), "-"],
            check=False,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
        )
    if result.returncode:
        detail = result.stderr.decode("utf-8", errors="replace").strip()
        raise RuntimeError(detail or f"pdftotext exited {result.returncode}")
    return result.stdout.decode("utf-8", errors="replace")


def main() -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(errors="backslashreplace")
        sys.stderr.reconfigure(errors="backslashreplace")

    parser = argparse.ArgumentParser()
    parser.add_argument("output_directory", nargs="?", type=Path, default=Path("output"))
    parser.add_argument("--minimum-title-length", type=int, default=2)
    args = parser.parse_args()

    pdftotext = shutil.which("pdftotext")
    if not pdftotext:
        print("pdftotext was not found. Add Poppler to PATH.", file=sys.stderr)
        return 2

    pdfs = sorted(args.output_directory.glob("*.pdf"))
    if not pdfs:
        print(f"No PDFs found under {args.output_directory}", file=sys.stderr)
        return 2

    findings: list[str] = []
    for pdf in pdfs:
        try:
            text = extract(pdftotext, pdf)
        except RuntimeError as error:
            findings.append(f"{pdf}: extraction failed: {error}")
            continue

        for line_number, line in enumerate(text.splitlines(), start=1):
            if DANGLING_TITLE.match(line):
                findings.append(f"{pdf}:{line_number}: dangling punctuation title: {line.strip()!r}")
            if LETTER_SPACED.search(line):
                findings.append(f"{pdf}:{line_number}: letter-spaced text: {line.strip()!r}")
            lesson = LESSON_WITH_TYPE.match(line)
            if lesson:
                title = lesson.group(1).strip()
                length = visible_length(title)
                if 0 < length < args.minimum_title_length:
                    findings.append(
                        f"{pdf}:{line_number}: title shorter than {args.minimum_title_length}: {title!r}"
                    )

    if findings:
        print("\n".join(findings))
        print(f"Findings: {len(findings)}", file=sys.stderr)
        return 1

    print(f"Checked {len(pdfs)} PDFs. No heuristic findings.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
