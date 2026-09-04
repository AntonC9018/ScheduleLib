import html
import os
import re
import tempfile
import zipfile
from pathlib import Path


DOCX = Path(
    r"src\Features\Integration\data\2026_sem1\zi\01.09.26\Orar_An_I Lic.docx"
)
TARGETS = {
    "S e c u r i t a t e a    c i b e r n e t i c ă   (curs)",
    "Securitatea  cibernetică (curs)",
}
REPLACEMENT_PARTS = ("Securitatea ", "c", "ibernetică (curs)")


def replace_paragraph(match: re.Match[str]) -> str:
    paragraph = match.group(0)
    text_matches = list(
        re.finditer(r"(<w:t(?:\s[^>]*)?>)(.*?)(</w:t>)", paragraph, re.DOTALL)
    )
    combined = "".join(html.unescape(item.group(2)) for item in text_matches)
    if combined not in TARGETS:
        return paragraph
    if len(text_matches) != len(REPLACEMENT_PARTS):
        raise RuntimeError(
            f"Expected three text runs for {combined!r}, found {len(text_matches)}"
        )

    result = paragraph
    for item, replacement in reversed(list(zip(text_matches, REPLACEMENT_PARTS))):
        escaped = html.escape(replacement, quote=False)
        result = result[: item.start(2)] + escaped + result[item.end(2) :]
    replace_paragraph.count += 1
    return result


replace_paragraph.count = 0


with zipfile.ZipFile(DOCX, "r") as source:
    document_xml = source.read("word/document.xml").decode("utf-8")
    entries = [(entry, source.read(entry.filename)) for entry in source.infolist()]

updated_xml = re.sub(
    r"<w:p(?:\s[^>]*)?>.*?</w:p>", replace_paragraph, document_xml, flags=re.DOTALL
)
if replace_paragraph.count != 2:
    raise RuntimeError(f"Expected two unique paragraphs, changed {replace_paragraph.count}")

handle, temp_name = tempfile.mkstemp(suffix=".docx", dir=DOCX.parent)
os.close(handle)
temp_path = Path(temp_name)
try:
    with zipfile.ZipFile(temp_path, "w") as destination:
        for entry, payload in entries:
            if entry.filename == "word/document.xml":
                payload = updated_xml.encode("utf-8")
            destination.writestr(entry, payload)
    os.replace(temp_path, DOCX)
finally:
    temp_path.unlink(missing_ok=True)

print(f"Updated {replace_paragraph.count} unique schedule cells in {DOCX}")
