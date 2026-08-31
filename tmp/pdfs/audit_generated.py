import json
import re
import unicodedata
import zipfile
from collections import defaultdict
from pathlib import Path

import pdfplumber
from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / "output"
RENDERED = ROOT / "tmp" / "pdfs" / "document-consistency"
CACHE = ROOT / "src" / "MainCli" / "bin" / "Debug" / "net10.0-windows" / "data" / "schedule_2026_1.json"
DOCX = ROOT / "src" / "Features" / "Integration" / "data" / "2026_sem1" / "zi" / "01.09.26" / "Orar_An_I Lic.docx"
REPORT = ROOT / "tmp" / "pdfs" / "document-consistency-report.txt"
CONTACTS = ROOT / "tmp" / "pdfs" / "document-consistency-contacts"


def fold(value: str) -> str:
    value = value.replace("\x00", "")
    value = unicodedata.normalize("NFKD", value).casefold()
    value = "".join(c for c in value if not unicodedata.combining(c))
    return "".join(c for c in value if c.isalnum())


def teacher_file(teacher: dict) -> str:
    first = "-".join((p.get("Short") or "").rstrip(".") for p in teacher["FirstName"] if p.get("Short"))
    last = "-".join(teacher["LastName"])
    return f"{first}_{last}.pdf"


def parity_overlap(a: str, b: str) -> bool:
    return a == "EveryWeek" or b == "EveryWeek" or a == b


def main() -> None:
    schedule = json.loads(CACHE.read_text(encoding="utf-8"))
    lessons = schedule["RegularLessons"]
    groups = schedule["Groups"]
    teachers = schedule["Teachers"]
    courses = schedule["Courses"]

    pdfs = sorted(OUTPUT.glob("*.pdf"), key=lambda p: p.name.casefold())
    extracted = {}
    problems = []
    for pdf in pdfs:
        with pdfplumber.open(pdf) as doc:
            pages = [page.extract_text() or "" for page in doc.pages]
            extracted[pdf.name] = "\n".join(pages)
            if len(doc.pages) != 1:
                problems.append(f"PAGE_COUNT {pdf.name}: {len(doc.pages)}")
            if not any(p.strip() for p in pages):
                problems.append(f"BLANK_TEXT {pdf.name}")
            bad_nul = sum(p.count("\x00") for p in pages)
            bad_repl = sum(p.count("\ufffd") for p in pages)
            if bad_nul or bad_repl:
                problems.append(f"TEXT_LAYER {pdf.name}: NUL={bad_nul}, replacement={bad_repl}")

    expected_teacher = {}
    for tid, teacher in enumerate(teachers):
        teacher_lessons = [l for l in lessons if tid in l["Teachers"]]
        if teacher_lessons:
            expected_teacher[teacher_file(teacher)] = teacher_lessons

    expected_group = {}
    for gid, group in enumerate(groups):
        group_lessons = [l for l in lessons if gid in l["Groups"]]
        if not group_lessons:
            continue
        expected_group[f'{group["Name"]}.pdf'] = group_lessons
        subgroups = sorted({l.get("SubGroup") for l in group_lessons if l.get("SubGroup")})
        for subgroup in subgroups:
            expected_group[f'{group["Name"]}_{subgroup}.pdf'] = [
                l for l in group_lessons if not l.get("SubGroup") or l.get("SubGroup") == subgroup
            ]

    actual = set(extracted)
    expected = set(expected_teacher) | set(expected_group)
    for name in sorted(expected - actual):
        problems.append(f"MISSING_PDF {name}")
    for name in sorted(actual - expected):
        problems.append(f"UNEXPECTED_PDF {name}")

    def check_content(name: str, relevant: list[dict], teacher_view: bool) -> None:
        if name not in extracted:
            return
        hay = fold(extracted[name])
        wanted_courses = {courses[l["Course"]]["Names"][0] for l in relevant}
        wanted_rooms = {l["Room"] for l in relevant if l.get("Room")}
        for course in sorted(wanted_courses):
            if fold(course) not in hay:
                problems.append(f"MISSING_COURSE {name}: {course}")
        for room in sorted(wanted_rooms):
            if fold(room) not in hay:
                problems.append(f"MISSING_ROOM {name}: {room}")
        if not teacher_view:
            wanted_last_names = {
                part
                for lesson in relevant
                for tid in lesson["Teachers"]
                for part in teachers[tid]["LastName"]
            }
            for last in sorted(wanted_last_names):
                if fold(last) not in hay:
                    problems.append(f"MISSING_TEACHER {name}: {last}")

    for name, relevant in expected_teacher.items():
        check_content(name, relevant, True)
    for name, relevant in expected_group.items():
        check_content(name, relevant, False)

    by_slot = defaultdict(list)
    for index, lesson in enumerate(lessons):
        by_slot[(lesson["Period"], lesson["DayOfWeek"], lesson["TimeSlot"])].append((index, lesson))
    conflicts = []
    for slot, items in by_slot.items():
        for pos, (ia, a) in enumerate(items):
            for ib, b in items[pos + 1:]:
                if not parity_overlap(a["Parity"], b["Parity"]):
                    continue
                same_teachers = set(a["Teachers"]) & set(b["Teachers"])
                same_room = a.get("Room") and a.get("Room") == b.get("Room")
                same_groups = set(a["Groups"]) & set(b["Groups"])
                subgroup_overlap = (
                    not a.get("SubGroup") or not b.get("SubGroup") or a.get("SubGroup") == b.get("SubGroup")
                )
                resources = []
                if same_teachers:
                    resources.append("teacher=" + ",".join(str(x) for x in sorted(same_teachers)))
                if same_room:
                    resources.append("room=" + a["Room"])
                if same_groups and subgroup_overlap:
                    resources.append("group=" + ",".join(str(x) for x in sorted(same_groups)))
                if resources:
                    same_event = (
                        a["Course"] == b["Course"]
                        and a["Teachers"] == b["Teachers"]
                        and a.get("Room") == b.get("Room")
                        and a["Parity"] == b["Parity"]
                    )
                    if not same_event:
                        conflicts.append(
                            f"CONFLICT {slot} lessons {ia}/{ib} {' '.join(resources)}: "
                            f"{courses[a['Course']]['Names'][0]} vs {courses[b['Course']]['Names'][0]}"
                        )

    with zipfile.ZipFile(DOCX) as archive:
        xml = archive.read("word/document.xml").decode("utf-8")
    text_nodes = re.findall(r"<w:t(?: [^>]*)?>(.*?)</w:t>", xml)
    source_text = " ".join(text_nodes)
    source_text = (source_text.replace("&amp;", "&").replace("&lt;", "<").replace("&gt;", ">"))
    parenthetical = sorted(set(re.findall(r"\([^()]{1,40}\)", source_text)))
    prefix_like = [x for x in parenthetical if re.search(r"[A-Za-zĂÂÎȘȚăâîșț]\.", x)]

    CONTACTS.mkdir(parents=True, exist_ok=True)
    font = ImageFont.load_default()
    batch_size = 3
    for start in range(0, len(pdfs), batch_size):
        batch = pdfs[start:start + batch_size]
        images = [Image.open(RENDERED / f"{i + 1:03}.png").convert("RGB") for i in range(start, start + len(batch))]
        label_h = 28
        width = max(img.width for img in images)
        height = sum(img.height + label_h for img in images)
        canvas = Image.new("RGB", (width, height), "white")
        y = 0
        draw = ImageDraw.Draw(canvas)
        for pdf, img in zip(batch, images):
            draw.rectangle((0, y, width, y + label_h), fill="#d9e7f5")
            draw.text((8, y + 7), pdf.name, fill="black", font=font)
            y += label_h
            canvas.paste(img, (0, y))
            y += img.height
        canvas.save(CONTACTS / f"contact-{start // batch_size + 1:02}.png")

    lines = [
        f"PDFS={len(pdfs)} EXPECTED={len(expected)} TEACHER={len(expected_teacher)} GROUP={len(expected_group)}",
        f"PROBLEMS={len(problems)}",
        *problems,
        f"CONFLICTS={len(conflicts)}",
        *conflicts,
        f"SOURCE_PREFIX_LIKE={len(prefix_like)}",
        *prefix_like,
        f"SOURCE_PARENTHETICAL={len(parenthetical)}",
        *parenthetical,
    ]
    REPORT.write_text("\n".join(lines), encoding="utf-8")
    print("\n".join(lines[:250]))


if __name__ == "__main__":
    main()
