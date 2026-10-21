# Historical high-level-parser source snapshot

Parser source preserved from the historical `high-level-parser` branch at `41a8030950abb61f309b98acc9a2c1469d6d8aea`.
The original branch's 4 commit(s) were not reachable from the live GitHub
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

- `src/ScheduleLib/ScheduleLib.Core/Parsing/LessonParser/Internal.cs`
- `src/ScheduleLib/ScheduleLib.Core/Parsing/LessonParser/LessonParser.cs`
- `src/ScheduleLib/ScheduleLib.Helper/Helper/ListExtensions.cs`
- `src/ScheduleLib/ScheduleLib.Helper/Parser/Lexer.cs`
- `src/ScheduleLib/Tests/Parser/HighLevelLessonParserTests.cs`
- `src/ScheduleLib/Tests/Parser/LessonParserHelper.cs`
- `src/ScheduleLib/Tests/Parser/LessonParserTests.cs`
