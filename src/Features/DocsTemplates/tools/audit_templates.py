#!/usr/bin/env python3
"""Check prepared DOCX structure; Word rendering is still required for placement."""

import argparse
import re
import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET


W = "{http://schemas.openxmlformats.org/wordprocessingml/2006/main}"
WP = "{http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing}"
V = "{urn:schemas-microsoft-com:vml}"
TOKEN = re.compile(r"\{\{[^{}]+\}\}")
SOURCES = {
    "acord_suplimentar.docx": "1. acord suplimentar  de modificare 2026.docx",
    "cerere_angajare_didactica.docx": "3. Cerere angajare_didactica.docx",
    "contract_individual_de_munca.docx": "5. CIM didactica 2026 red.3.docx",
    "declaratie_consimtamant.docx": "7. DECLARAȚIE DE CONSIMȚĂMÂNT.docx",
    "declaratie_informare.docx": "8. DECLARAȚIE DE INFORMARE.docx",
    "fisa_postului_asistent_universitar.docx": "10. fișa_Asistent universitar actualizat 2026.docx",
}


def read_document(path):
    with zipfile.ZipFile(path) as archive:
        assert archive.testzip() is None, f"Corrupt archive: {path.name}"
        for name in archive.namelist():
            if name.endswith(".xml"):
                ET.fromstring(archive.read(name))
        return ET.fromstring(archive.read("word/document.xml"))


def static_paragraphs(document):
    """Preserve paragraph boundaries, spaces, underscores, tabs and explicit breaks."""
    for parent in document.iter():
        for child in list(parent):
            if child.tag == W + "txbxContent":
                parent.remove(child)
    return [
        "".join(
            node.text or "" if node.tag == W + "t" else "\t" if node.tag == W + "tab" else "\n"
            for node in paragraph.iter()
            if node.tag in {W + "t", W + "tab", W + "br", W + "cr"}
        )
        for paragraph in document.iter(W + "p")
    ]


def audit(source, prepared, legacy=False):
    original = read_document(source)
    document = read_document(prepared)
    parents = {child: parent for parent in document.iter() for child in parent}
    tokens = []
    for paragraph in document.iter(W + "p"):
        # Only this paragraph's runs, not nested text-box paragraphs.
        text = "".join(node.text or "" for run in paragraph.findall(W + "r") for node in run.findall(W + "t"))
        matches = TOKEN.findall(text)
        if not matches:
            continue
        ancestors = []
        parent = parents.get(paragraph)
        while parent is not None:
            ancestors.append(parent.tag)
            parent = parents.get(parent)
        assert W + "txbxContent" in ancestors, f"Inline field in {prepared.name}"
        assert WP + "anchor" in ancestors or V + "shape" in ancestors, f"Unanchored field in {prepared.name}"
        run_tokens = [token for run in paragraph.findall(W + "r") for node in run.findall(W + "t") for token in TOKEN.findall(node.text or "")]
        assert run_tokens == matches, f"Split token cannot be rendered in {prepared.name}"
        tokens.extend(matches)
    assert tokens, f"No output fields in {prepared.name}"
    xml = ET.tostring(document, encoding="unicode")
    assert len(TOKEN.findall(xml)) == len(tokens), f"Uninspected field in {prepared.name}"
    if legacy:
        assert "&lt;#" not in xml, f"Legacy expression remains in {prepared.name}"
    if prepared.name == "contract_individual_de_munca.docx":
        for size in document.iter(W + "pgSz"):
            # Word can round A4 dimensions by a twip.
            assert abs(int(size.get(W + "w")) - 11906) <= 1, f"Non-A4 width in {prepared.name}"
            assert abs(int(size.get(W + "h")) - 16838) <= 1, f"Non-A4 height in {prepared.name}"
    else:
        assert [size.attrib for size in original.iter(W + "pgSz")] == [size.attrib for size in document.iter(W + "pgSz")], f"Page size changed in {prepared.name}"
    expected = static_paragraphs(original)
    if legacy:
        # The editable reconstruction has expression-only cells where the PDF
        # has blank underlined cells. Only those five legacy tags are removed.
        expression = re.compile(r'<#<Content Select="\./(?:Name|Function|Department|Faculty|Date)"/>#>')
        assert sum(len(expression.findall(text)) for text in expected) == 5
        expected = [expression.sub("", text) for text in expected]
    assert expected == static_paragraphs(document), f"Source paragraph content changed in {prepared.name}"
    print(f"PASS {prepared.name}: source text preserved; {len(tokens)} boxed tokens (including fallbacks)")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("incoming", type=Path)
    parser.add_argument("templates", type=Path)
    parser.add_argument("--legacy", type=Path, help="Legacy templates directory for own-responsibility check")
    args = parser.parse_args()
    for output_name, source_name in SOURCES.items():
        audit(args.incoming / source_name, args.templates / output_name)
    if args.legacy:
        name = "declaratie_proprie_raspundere.docx"
        audit(args.legacy / name, args.templates / name, legacy=True)
    print("Structural audit only: coordinates, white fill, clipping and pagination require Word inspection.")


if __name__ == "__main__":
    main()
