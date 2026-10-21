# Historical lesson-parser-refactor source snapshot

Parser source preserved from the historical `lesson-parser-refactor` branch at `ccee22bc975e4648ad3617890126fe0bf41f8f46`.
The original branch's 1 commit(s) were not reachable from the live GitHub
branches at the migration inventory. This snapshot has a parent in the cleaned
current history (`ba380a2ab51b265a014dfabf1792ebfb1cda0dc6`); it does not republish the original branch ancestry.

Only the changed `.cs` files below `src/ScheduleLib` are retained, under their
original relative paths in this folder. Schedule spreadsheets, generated images,
other local data and unrelated application/registry edits are excluded.

This is an archival experiment, not an integrated implementation. It is outside
application project directories and is not added to the current solution. Its
historical API assumptions and incomplete tests have not been made compatible
with the current implementation. The original files are preserved byte-for-byte.

Retained files:

- `src/ScheduleLib/ScheduleLib.Core/Parsing/LessonParser/LessonParser.cs`
- `src/ScheduleLib/Tests/Parser/LessonParserTests.cs`
