# Research: Parsing pipeline recon for seeding

Resolution of [ScheduleLib-6xo.10](beads) — Research: Parsing pipeline recon for seeding. Report produced by a research subagent, 2026-09-05. Worktree `feat/schedule-engine-wayfinder` @ f513629.

Method: read the actual source (model, parsing, builder, DI wiring, output tasks) in this worktree; every claim cites `file:line` relative to the repo root. Where the wayfinder map's paths drifted, the actual path is given (see §0).

## 0. Path corrections to the vetted map

- `GroupCombinations.cs` and `ImplicitSplitConfig.cs` live under `src/ScheduleLib/ScheduleLib.Core/Model/`, not directly under `ScheduleLib.Core/` (`src/ScheduleLib/ScheduleLib.Core/Model/GroupCombinations.cs`, `src/ScheduleLib/ScheduleLib.Core/Model/ImplicitSplitConfig.cs`).
- The alternative registrations referenced as "Config.cs:83–94" are there (`src/ScheduleDefaults/Config.cs:83-94`), plus two specialization scopes above them (52–76) and the overlap allowlist (104–172).

## 1. Pipeline end to end

```
data/<year>_sem<N>/{zi,fr}/<dd.MM.yy>/*.doc(x)|*.xlsx      (src/Features/Integration/data/)
  -> ScheduleDirectoryDiscovery.DiscoverDirectories         (…/Initialization/ScheduleDirectoryDiscovery.cs:12-46)
     dir name "2026_sem1" -> StudyYear+Semester; subdir "zi"/"fr" -> AttendanceMode (:26-38)
  -> GetLoaders():  zi = Word dir loader + Excel MasterConfig; fr = Excel FrConfig (:143-175)
  -> ScheduleLoader.Load                                    (…/Initialization/ScheduleLoader.cs:26-92)
     MD5 hash over all inputs + ImplicitSplitConfig (:31-47, 442-573); JSON cache
     data\schedule_<year>_<sem>.json via ScheduleSerializer; cache hit = replay into builder (:62-76)
  -> loader components (IScheduleLoaderComponent, :95-99):
     * Word: ParseDocumentDirIntoSchedule                   (src/Features/Integration/Tasks.cs:65-144)
       .doc -> .docx conversion (ConvertDocToDocx, :118-130); dd.MM.yy subdirs -> Period start (:99-109)
       -> WordScheduleParser.ParseToSchedule                (src/ScheduleLib/ScheduleLib.Core/Parsing/WordScheduleParser.cs:419)
     * Excel: ExcelScheduleParser (Fr/Master configs)       (src/ScheduleLib/ScheduleLib.Core/Parsing/ExcelScheduleParser.cs:28-45)
     * ConsultationsLoaderComponent: Drive xlsx -> LessonType.Consultation lessons (ScheduleLoader.cs:217-420)
     * EnrichWithTeacherFullNamesFromWebsite (it.usm.md)    (ScheduleLoader.cs:145-178)
  -> all components write into ONE ScheduleBuilder (DocParseContext.Schedule, WordScheduleParser.cs:23-49)
  -> ScheduleBuilder.Build()                                (src/ScheduleLib/ScheduleLib.Core/Model/ScheduleBuilder.cs:76-94)
     Preprocess -> Validate -> ClassifySubGroups -> DropEngSubGroupFromEnglishGroups
     -> AssignImplicitSplits -> NormalizeLanguageProficiency -> ValidateLessonOverlaps
     -> SanityChecks -> CreateDefaultModel (:76-88; steps in §3/§4)
  -> immutable Schedule (src/ScheduleLib/ScheduleLib.Core/Model/Schedule.cs:13-31)
  -> DI: ScheduleProvider wraps the builder; Schedule is a scoped DI service
     (src/Features/Integration/Registration.cs:208-214; src/Features/Integration/Tasks/Initialization/ScheduleInitialization.cs:10-24)

Outputs (MainCli AppTask enum, src/MainCli/AppTasks.cs:20-37; menu Program.cs:14-43):
  PerGroupAndPerTeacherPdfs -> QuestPDF per-group/per-combination/per-teacher PDFs
     (src/Features/Integration/Tasks/GeneratePdfsForGroupsAndTeachers.cs:28-138)
  JsonSchedulesForWebsite   -> per-teacher JSON via TeacherGrouping + WebsiteJsonScheduleHelper
     (src/MainCli/AppTasks.cs:146-185; src/Features/FmiWebsiteInterop/Schedule.cs:30)
  AllTeachersExcel / FreeRooms / TableOfAllLabLessons (xlsx), UpdateCalendar (Google), CreateLessonsInRegistry,
  UploadDocsToDrive (Drive sync) — all consume the same Schedule / FilteredSchedule.
```

The cache replay path is notable for the engine: cached JSON already contains post-`AssignImplicitSplits` lessons, and `ScheduleProvider.Get()` still runs the full `Build()` pipeline over it; the preprocessing is idempotent by design (e.g. `NormalizeLanguageProficiency` leaves already-normalized data alone, `Model/Builders/LessonGroupPostProcessing.cs:62-66`).

## 2. Canonical types per entity

All in `src/ScheduleLib/ScheduleLib.Core/Model/Schedule.cs` unless noted.

| Entity | Canonical type | Definition | Populated at parse/build by |
|---|---|---|---|
| Schedule container | `Schedule` — `ImmutableArray`s: `WeeklyLessons`, `OneTimeLessons`, `Groups`, `Teachers`, `Courses`, `Periods` | Schedule.cs:13-31 | `CreateDefaultModel`, Model/ScheduleBuilder.cs:185-279 |
| Lesson (instance) | `WeeklyLesson` = `LessonBase{LessonData}` + `WeeklyLessonDate{Parity, DayOfWeek, TimeSlot, Period}` (:706-714, :315-321); `OneTimeLesson` + `OneTimeLessonDate{Date, TimeSlot}` (:715-727, :323-327) | | Word: `AddOrMergeLesson` WordScheduleParser.cs:874-1007; Excel handlers; consultations ScheduleLoader.cs:404-418 |
| Lesson data (composition) | `LessonData`: `Groups` (`LessonGroups`), `Course`, `Teachers` (`ImmutableArray<TeacherId>`), `Room` (`RoomId`), `Type` (`LessonType`), `SubGroup`, `Specialization`, `Alternative` | :542-562 | `BuildBase` Model/ScheduleBuilder.cs:187-203 |
| Academic group | `Group` (class): `Name` (canonical, language stripped), `Grade`, `GroupNumber`, `QualificationType`, `Faculty`, `AttendanceMode`, `Specialty`, `Language` | :998-1012 | parsed from the raw label by `GroupParseContext.Parse` (Parsing/GroupParser.cs:59-195: `DU-`/`FR`/`M` prefixes -> AttendanceMode/Qualification; year -> Grade from current StudyYear, :37-41); get-or-add `ScheduleBuilder.Group` Model/Builders/GroupBuilder.cs:48-75 |
| SubGroup | `SubGroup` — null-backed readonly string; `All` = null; `CreateNumeric` roman | :770-782 | parser labels via `SubGroupPrefixMatcher` (Model/SpecialSubGroups.cs:15-91); classification in §3 |
| Specialization | `Specialization` — null-backed readonly string; `All` = null | :791-802 | same parse path; known values as extensions `src/ScheduleDefaults/Specializations.cs:8-19`; aggregate `Specializations.AllKnown` :830-845 |
| Alternative | `Alternative` — null-backed readonly string; `All` = null | :810-821 | only from `ImplicitSplitConfig` scope assignments (§4); not a parser label per docs/domain-model.md:72-77 |
| Teacher | `Teacher`: `PersonName` (first-name parts + last-name parts) + `PersonContacts` | :1014-1049 | `DocParseContext.GetOrAddTeacher` WordScheduleParser.cs:204-258 (name remaps + diacritics resolution); website full-name enrichment |
| Course | `Course`: `Names` — unified name variants sorted longest-first; `FullName` = `Names[0]` | :533-540 | `GetOrAddCourse` -> `CourseNameUnifierModule` WordScheduleParser.cs:189-202; unification in `Preprocess` Model/ScheduleBuilder.cs:96-116 with `ScheduleDefaults/Config.cs:24-40` |
| Room | **`RoomId(string? Id)` — just a string; there is no room table on `Schedule`** | :898-902 | `GetOrAddRoom` = `ScheduleBuilder.Room(name)` Model/ScheduleBuilder.cs:326-330 (identity); lookup `Schedule.Get(RoomId)` returns the string Schedule.cs:1059-1064 |
| Time | `TimeSlot` = index into `LessonTimeConfig.TimeSlotStarts` (default 7 slots × 90 min, Model/ImmutableModels/TimeSlot.cs:5-34); `Parity` {OddWeek, EvenWeek, EveryWeek} (Model/ImmutableModels/Parity.cs:1-32); `Period` = DateOnly range per doc snapshot (Model/ImmutableModels/Period.cs:5-43) | | day/time columns WordScheduleParser.cs:600-700; Periods from dd.MM.yy dir names Tasks.cs:86-108 |
| Partition identity | `GroupPartitionKey(SubGroup, Specialization, Alternative)` :621-624; `LessonData.GroupPartitionKey` :626-631; `PartitionDimension` enum + dimension-agnostic access :569-614 | | |
| Active partitions / student combinations | `GroupCombination` (Model/GroupCombinations.cs:19-102) and `GroupPartitionInfo` per group (:109-181); discovery `GroupPartitionInfoByGroup.Build` (:189-356) exposed as `schedule.GetGroupPartitionInfo(registry)` (:365-370) | | derived, not parsed |

Also relevant: `FilteredSchedule` (Model/FilteredSchedule.cs:64-72) already flattens a filtered view to `Lessons`/`Groups`/`TimeSlots`/`Days`/`Teachers` arrays; `GroupFilter` carries `SubGroups[]`, `Specializations[]`, `Alternatives[]`, `OneOfGroupIds[]` (:29-35). No `SubGroups` collection on `Group` — subgroups are *observed from lessons* (`Schedule.SubGroupsByGroup`, Schedule.cs:1137-1157).

## 3. How split and parallel lessons are represented today

- **One lesson = one partition value on each dimension.** `LessonData` stores a single `SubGroup`, `Specialization`, `Alternative` (Schedule.cs:558-560); a subgroup split (e.g. I/II of the same course at the same slot) is *two lessons*. There is no "split group" parent object.
- **Specialization/alternative labels ride in as subgroup labels.** The Word parser matches the cell's group name or in-cell hint against `SubGroupPrefixMatcher` (WordScheduleParser.cs:117-173, `SetCommonProps`); `ClassifySubGroups` (LessonGroupPostProcessing.cs:12-47) then moves registered specialization values out of the subgroup field post-parse.
- **Implicit splits duplicate lessons.** `AssignImplicitSplits` (Model/Builders/ImplicitSplitAssignment.cs:13-279) resolves each lesson's course against `ImplicitSplitConfig` scopes (`Model/ImplicitSplitConfig.cs:10-91`, configured in `src/ScheduleDefaults/Config.cs:47-96`) per participating group; groups resolving to different (specialization, alternative) values split the lesson into copies, each with its own subgroup set and stamped value (`SplitAccumulator`, :288-344; copies appended :128-200). "One lesson stores one value" is the invariant (:8-11).
- **Multi-group merged lessons** are one lesson with `LessonGroups` holding up to 16 GroupIds (inline array, Schedule.cs:329-334). Merging happens at parse time: a second occurrence of the same course with identical (Parity, Day, TimeSlot, SubGroup, Room, Type, Period) merges Groups+Teachers into the existing lesson instead of adding a row (`AddOrMergeLesson`/`MaybeMergeIntoAnExistingLesson`, WordScheduleParser.cs:874-1007; `Merge`, Model/Builders/LessonBuilder.cs:866-917). Mixed attendance modes are allowed only Zi+Dual (LessonBuilder.cs:474-493).
- **Parallel alternatives** (electives sharing a slot by design) are plain overlapping lessons; `ValidateLessonOverlaps` (Model/Builders/LessonOverlapValidation.cs:17-33) fails a pair only when same Period+day+slot, parities intersect (:92-100), a group is shared (:128-141) and the split dimensions don't separate them (:102-119); a configured allowlist tolerates the rest (`src/ScheduleDefaults/Config.cs:104-172`). There is **no first-class "bloc" entity** tying parallel alternatives together — docs/wayfinder/group-structure-2026-2027.md §4 gap 2.
- **Week parity** splits occupancy instead of splitting lessons: Odd/Even lessons at the same slot are distinct and non-overlapping (`WeeklyLessonDate.Parity`, Schedule.cs:315-321; `Parity.IsMatch`, Parity.cs:8-32).
- **Website display merging is presentation-only**: the JSON exporter re-groups by (course, type, room) then concatenates entries sharing (slot, week type) — FmiWebsiteInterop/Schedule.cs:52-110 — it does not change the model.

## 4. The seeding seam

State that is complete enough to seed "teachers -> courses -> groups -> lesson instances with composition + weekly count":

After `ScheduleBuilder.Build()` (i.e. the `Schedule` DI service, Registration.cs:209-214) every `WeeklyLesson` already is a lesson instance with the exact composition the builder spec declares: course (CourseId), teacher set, room, target groups (`LessonGroups`), partition key (subgroup/spec/alternative, implicit splits applied), lesson type, and its hand-written day+slot+parity+period (Schedule.cs:542-562, 706-721). Only day×slot×room would be re-solved; everything else is given. The PDF task demonstrates the canonical consumption pattern: `schedule.GetGroupPartitionInfo(registry)` for per-group active partitions and per-combination filters, then `schedule.Filter(...)` — GeneratePdfsForGroupsAndTeachers.cs:40-77, 111-138.

Weekly counts are derivable, not stored: `Parity.EveryWeek` ⇒ every `StudyWeek` of the semester; `Odd/Even` ⇒ the half matching parity. `ScheduleDefaults.Config.StudyWeeks` enumerates the semester's Mondays with parity flags (Config.cs:444-476, 15 fall + 13 spring weeks for 2026-27); the parity-to-date mapping service is `ManualWeeklyScheduledDateProvider` (Registration.cs:183-190). OneTimeLessons (session `(prel)`/exam entries) are dated, not weekly — they are a separate seeding category.

**Seam option A — consume the `Schedule` model directly.** A builder facade takes `Schedule` (+ `SpecializationRegistry`, + `LessonTimeConfig` for slot count, + `StudyWeeks` for week counts), enumerates `EnumerateWeeklyLessons()`/`EnumerateOneTimeLessons()`, and maps each lesson to a solver variable keyed by `WeeklyLessonId`. Pros: zero new representation; exactly what every existing consumer (PDF, JSON, Excel) does; ids are stable within one `Schedule` instance; `GetGroupPartitionInfo` and `GroupFilter` machinery is reusable for per-cohort constraint groups. Cons: the engine couples to Core internals — ref-struct accessors (`WeeklyLessonRef`), inline-array `LessonGroups`, string-keyed rooms, and builder preprocessing semantics become part of the engine's contract.

**Seam option B — a flattened projection** (a seed DTO pass producing one record per lesson instance with resolved names/ids, week counts, and subgroup sizes). Precedents already in-tree: `SerializationModels.ScheduleModel` (the cache shape, Model/ImmutableModels/Serializer.cs:90-167) and `FilteredSchedule`'s flattened arrays (FilteredSchedule.cs:64-72). Pros: engine owns a small stable input contract; deserialization-based tests without Word inputs; room/slot/teacher tables materialized once. Cons: a second mapping layer that must track model evolution (three partition dimensions today; the group-structure doc anticipates a section-level subgroup axis), plus a second place to keep in sync with implicit-split semantics.

In both options the natural insertion point is DI-level: the engine would consume the scoped `Schedule` (or a provider producing the projection) the same way `GeneratePdfsForGroupsAndTeachersTaskHandler` does — no new loader component, no touching `ScheduleBuilder` (parse-time mutable state with pre-split lessons is *not* the right seam).

## 5. Gaps/risks this recon exposes for seeding

1. **Rooms are strings, full stop.** No room entity, capacity, kind, or building anywhere (Schedule.cs:898-902; confirmed by docs/wayfinder/research/room-inventory.md §2). The solver's room dimension must be bootstrapped by collecting distinct `RoomId` values from lessons and attaching capacity tiers from outside the model. Also: the hand-written schedule shares rooms across simultaneous lessons (room-inventory §2 "Mixed rooms"), so seeded room assignments cannot be treated as feasible-without-changes.
2. **Weekly count is derived, not stored.** Parity + semester week list gives it, but the engine must pick the semester (`PeriodId` filter — output tasks use `WithLatestPeriod`, AppTasks.cs:155-157; `LatestPeriodId` Period.cs:93-96) and owns the parity→count policy. `OneTimeLessons` don't fit a weekly-count shape at all.
3. **Composition is explicit only post-`Build`.** Raw parse output can carry a specialization inside the `SubGroup` field (§3); seeding must consume the built `Schedule` (or cache replay + `Build`), never mid-parse builder state. The cache hash already includes the implicit-split config, so a rebuilt schedule stays consistent (ScheduleLoader.cs:41-47).
4. **No first-class bloc / parallel-alternative set.** To "keep composition exactly" for electives the engine must either derive blocs from observed same-slot alternatives (per grade, like the allowlist entries) or extend the model — a grilling-ticket decision (group-structure §4 gap 2).
5. **Section-level lab subgroups don't fit the group-scoped SubGroup dimension** and subgroup *sizes* (the capacity data for labs) exist only in the contingent/optional-course xlsx, not in the schedule model (group-structure §4 gap 1, §5). Seeded lessons know their subgroup *value* but not its student count.
6. **Identity is positional.** Lesson/teacher/group/course ids are array indices with no persistence across rebuilds; `LessonGroups` caps merged groups at 16 (Schedule.cs:329-334). Any engine artifact keyed by ids must be rebuilt with the schedule.
7. **Cache stores groups as name+language only** (Serializer.cs:135-139, 196-200); on reload the full `Group` is re-derived by re-parsing the name against the current study year (Serializer.cs:247-252, GroupBuilder.cs:32-46). Group metadata (grade/faculty/mode) is therefore name-parse-dependent — fine today, but a seeding projection that wants typed group identity should go through the re-parsed `Schedule`, not the cache JSON.
8. **`DJ` name collision** (specialization value vs program code) and string-based label matching generally: any engine-side mapping that keys on names inherits the ambiguity — group-structure §4 gap 4 recommends typed identity, names display-only.
9. **Consultations** are weekly lessons with *no groups* and a `Consultație` course (ScheduleLoader.cs:373-418; validation allows empty groups only for Consultation, LessonBuilder.cs:434-440). The engine must decide to pin, drop, or schedule them (teacher no-overlap matters, rooms usually absent).
10. **`OneForEachAttendanceMode<T>` handles only Zi/FrecventaRedusa** — indexing `Dual` throws (Schedule.cs:943-980), while `AttendanceMode.Dual` is a first-class group mode merged with Zi in lessons (LessonBuilder.cs:474-479). Any per-mode engine table needs care (group-structure §4: AttendanceMode.Dual second-class).

## Sources

- Code (this worktree, paths relative to repo root): every `file:line` cited inline above — principally `src/ScheduleLib/ScheduleLib.Core/Model/Schedule.cs`, `Model/GroupCombinations.cs`, `Model/ImplicitSplitConfig.cs`, `Model/ScheduleBuilder.cs`, `Model/FilteredSchedule.cs`, `Model/ImmutableModels/{TimeSlot,Parity,Period,Serializer}.cs`, `Model/Builders/{LessonBuilder,LessonGroupPostProcessing,ImplicitSplitAssignment,LessonOverlapValidation,GroupBuilder}.cs`, `Model/SpecialSubGroups.cs`, `Parsing/{WordScheduleParser,ExcelScheduleParser,GroupParser}.cs`, `Parsing/LessonParser/LessonParser.cs`, `src/ScheduleDefaults/{Config,Specializations}.cs`, `src/Features/Integration/{Registration,Tasks}.cs`, `src/Features/Integration/Tasks/Initialization/{ScheduleBuilderInitializerImpl,ScheduleLoader,ScheduleDirectoryDiscovery,ScheduleInitialization}.cs`, `src/Features/Integration/Tasks/GeneratePdfsForGroupsAndTeachers.cs`, `src/Features/FmiWebsiteInterop/Schedule.cs`, `src/MainCli/{Program,AppTasks}.cs`.
- Docs: `CONTEXT.md`, `docs/domain-model.md`, `docs/subgroup-model-update-spec.md`, `docs/wayfinder/group-structure-2026-2027.md`, `docs/wayfinder/research/{cpsat-viability,room-inventory}.md`, `.agents/skills/schedule-document-import/SKILL.md`.
