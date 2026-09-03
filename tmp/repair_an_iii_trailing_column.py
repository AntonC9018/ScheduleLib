from __future__ import annotations

import copy
import os
import sys
import tempfile
import zipfile
from pathlib import Path

from lxml import etree


W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
NS = {"w": W}


def cell_width(cell: etree._Element) -> int:
    span = cell.find("./w:tcPr/w:gridSpan", NS)
    return int(span.get(f"{{{W}}}val")) if span is not None else 1


def repair(path: Path) -> None:
    with zipfile.ZipFile(path, "r") as source:
        document_xml = source.read("word/document.xml")

    root = etree.fromstring(document_xml)
    tables = root.xpath("//w:body/w:tbl", namespaces=NS)
    assert len(tables) == 6, f"expected 6 schedule tables, found {len(tables)}"

    # Table 2 is the Friday/Saturday continuation for M2401/I2401/IA2401/IA2402.
    target_rows = tables[2].xpath("./w:tr", namespaces=NS)
    assert len(target_rows) == 10, f"expected 10 affected rows, found {len(target_rows)}"

    template = tables[1].xpath("./w:tr[1]/w:tc[last()]", namespaces=NS)
    assert len(template) == 1
    template_cell = template[0]
    assert "".join(template_cell.itertext()).strip() == ""
    assert cell_width(template_cell) == 1
    assert template_cell.find("./w:tcPr/w:vMerge", NS) is None

    for index, row in enumerate(target_rows):
        cells = row.xpath("./w:tc", namespaces=NS)
        total_width = sum(cell_width(cell) for cell in cells)
        assert total_width == 5, f"row {index}: expected width 5, found {total_width}"
        row.append(copy.deepcopy(template_cell))

    repaired_xml = etree.tostring(
        root,
        xml_declaration=True,
        encoding="UTF-8",
        standalone=True,
    )

    fd, temp_name = tempfile.mkstemp(suffix=".docx", dir=path.parent)
    os.close(fd)
    temp_path = Path(temp_name)
    try:
        with zipfile.ZipFile(path, "r") as source, zipfile.ZipFile(temp_path, "w") as target:
            for item in source.infolist():
                payload = repaired_xml if item.filename == "word/document.xml" else source.read(item.filename)
                target.writestr(item, payload)
        os.replace(temp_path, path)
    finally:
        temp_path.unlink(missing_ok=True)


if __name__ == "__main__":
    assert len(sys.argv) == 2, "usage: repair_an_iii_trailing_column.py <document.docx>"
    repair(Path(sys.argv[1]))
