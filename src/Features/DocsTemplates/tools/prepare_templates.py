#!/usr/bin/env python3

"""Inspect DOCX text or convert the legacy own-responsibility placeholders.

The six incoming DOCX forms are prepared by ``prepare_templates.ps1`` through
Microsoft Word. That script adds fixed-position overlay fields and deliberately
does not rewrite source paragraphs. Legacy tag conversion here requires fields
already inside text boxes; it cannot turn inline table-cell fields into overlays.
"""

from __future__ import annotations

import argparse
import os
import tempfile
import zipfile
from pathlib import Path
from xml.dom import Node, minidom


WORD_DOCUMENT_XML = "word/document.xml"


def text_nodes(paragraph: minidom.Element) -> list[minidom.Text]:
    result: list[minidom.Text] = []
    for text_element in paragraph.getElementsByTagName("w:t"):
        for child in text_element.childNodes:
            if child.nodeType == Node.TEXT_NODE:
                result.append(child)
    return result


def paragraph_text(paragraph: minidom.Element) -> str:
    return "".join(node.data for node in text_nodes(paragraph))


def dump(path: Path) -> None:
    with zipfile.ZipFile(path) as archive:
        document = minidom.parseString(archive.read(WORD_DOCUMENT_XML))

    print(path.name)
    for index, paragraph in enumerate(document.getElementsByTagName("w:p")):
        text = paragraph_text(paragraph)
        if text.strip():
            print(f"{index:04}: {text!r}")


def replace_in_paragraph(paragraph: minidom.Element, old: str, new: str) -> bool:
    nodes = text_nodes(paragraph)
    combined = "".join(node.data for node in nodes)
    start = combined.find(old)
    if start < 0:
        return False

    end = start + len(old)
    offsets: list[tuple[minidom.Text, int, int]] = []
    offset = 0
    for node in nodes:
        node_end = offset + len(node.data)
        offsets.append((node, offset, node_end))
        offset = node_end

    first_index = next(i for i, (_, _, node_end) in enumerate(offsets) if start < node_end)
    last_index = next(i for i, (_, _, node_end) in enumerate(offsets) if end <= node_end)
    first_node, first_start, _ = offsets[first_index]
    last_node, last_start, _ = offsets[last_index]
    prefix = first_node.data[: start - first_start]
    suffix = last_node.data[end - last_start :]

    if first_index == last_index:
        first_node.data = prefix + new + suffix
        return True

    first_node.data = prefix + new
    for index in range(first_index + 1, last_index):
        offsets[index][0].data = ""
    last_node.data = suffix
    return True


def prepare_own_responsibility(path: Path) -> None:
    replacements = {
        '<#<Content Select="./Name"/>#>': "{{Name}}",
        '<#<Content Select="./Function"/>#>': "{{Function}}",
        '<#<Content Select="./Department"/>#>': "{{Department}}",
        '<#<Content Select="./Faculty"/>#>': "{{Faculty}}",
        '<#<Content Select="./Date"/>#>': "{{DocumentDate}}",
    }

    with zipfile.ZipFile(path, "r") as source:
        document = minidom.parseString(source.read(WORD_DOCUMENT_XML))
        for paragraph in document.getElementsByTagName("w:p"):
            if paragraph.getElementsByTagName("w:txbxContent"):
                continue
            if not any(old in paragraph_text(paragraph) for old in replacements):
                continue
            ancestor = paragraph.parentNode
            while ancestor is not None and getattr(ancestor, "tagName", None) != "w:txbxContent":
                ancestor = ancestor.parentNode
            if ancestor is None:
                raise RuntimeError(
                    "Legacy fields are inline, not in text boxes. Prepare anchored overlays "
                    "with Microsoft Word before converting these tags."
                )
        for old, new in replacements.items():
            count = 0
            for paragraph in document.getElementsByTagName("w:p"):
                while replace_in_paragraph(paragraph, old, new):
                    count += 1
            if count == 0:
                raise RuntimeError(f"Legacy overlay placeholder not found: {old}")

        new_xml = document.toxml(encoding="UTF-8", standalone=True)
        with tempfile.NamedTemporaryFile(
            prefix=path.stem + "-", suffix=path.suffix, dir=path.parent, delete=False
        ) as temporary_file:
            temporary_path = Path(temporary_file.name)

        try:
            with zipfile.ZipFile(temporary_path, "w") as target:
                for item in source.infolist():
                    data = new_xml if item.filename == WORD_DOCUMENT_XML else source.read(item.filename)
                    target.writestr(item, data)
            os.replace(temporary_path, path)
        finally:
            temporary_path.unlink(missing_ok=True)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=("dump", "prepare-own-responsibility"))
    parser.add_argument("paths", nargs="+", type=Path)
    args = parser.parse_args()

    for path in args.paths:
        if args.mode == "dump":
            dump(path)
        else:
            prepare_own_responsibility(path)


if __name__ == "__main__":
    main()
