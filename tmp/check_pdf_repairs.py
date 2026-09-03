import re
from pathlib import Path

import pdfplumber


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "output"


def text_of(path: Path) -> str:
    with pdfplumber.open(path) as pdf:
        return "\n".join(page.extract_text() or "" for page in pdf.pages)


teacher_expectations = {
    "A_Curmanscii.pdf": ("I: Progr. C++ (lab,", "II: Progr. C++ (lab,"),
    "D_Lefter.pdf": ("II: DCMIMJ", "I: DCMIMJ"),
}

for filename, expected_fragments in teacher_expectations.items():
    text = text_of(OUTPUT / filename)
    if re.search(r"(?m)^\s*\) \(DJ2502\)\s*$", text):
        raise AssertionError(f"{filename}: dangling course-name parenthesis remains")
    for expected_fragment in expected_fragments:
        if expected_fragment not in text:
            raise AssertionError(f"{filename}: missing repaired text {expected_fragment!r}")
    lines = [line for line in text.splitlines() if "DJ2502" in line or any(fragment in line for fragment in expected_fragments)]
    print(filename, [line.encode("unicode_escape").decode("ascii") for line in lines])

cyber_files = [OUTPUT / "L_Novac.pdf", OUTPUT / "FI2501.pdf", OUTPUT / "FI2502.pdf", OUTPUT / "FI2503.pdf"]
checked = 0
for path in cyber_files:
    if not path.exists():
        continue
    text = text_of(path)
    if re.search(r"S\s+e\s+c\s+u\s+r\s+i\s+t\s+a\s+t\s+e\s+a", text):
        raise AssertionError(f"{path.name}: spaced cybersecurity title remains")
    if "Securitatea ciberne" in text:
        checked += 1
        lines = [line for line in text.splitlines() if "Securitatea" in line]
        print(path.name, [line.encode("unicode_escape").decode("ascii") for line in lines])

if checked == 0:
    raise AssertionError("No generated PDF contained the normalized cybersecurity title")
