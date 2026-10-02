# Schedule domain

ScheduleLib models university teaching schedules and the student populations to which lessons apply.

## Language

**Group**:
The complete population of students enrolled under one university group identity, including every combination of its subgroups and specializations.
_Avoid_: Cohort, group family

**SubGroup**:
A mutually exclusive student partition within a Group, such as a numeric, language, or language-proficiency partition. Different kinds of subgroups may coexist independently.
_Avoid_: Specialization, Group

**Specialization**:
A named study-track partition within a Group, separate from its subgroups.
_Avoid_: Specialty, specialization subgroup

**Group partition**:
The subgroup, specialization, and alternative restriction that determines which students in a Group attend a lesson. A lesson may have at most one subgroup restriction, one specialization restriction, and one alternative restriction.
_Avoid_: Subgroup combination

**Subgroup combination**:
One possible student population obtained by selecting an active value from every subgroup dimension and, when active, one specialization and one alternative.
_Avoid_: Group partition, compound subgroup

**Whole-Group schedule**:
An administrative schedule containing every lesson associated with a Group, regardless of subgroup, specialization, or alternative.
_Avoid_: Student schedule

**Group language**:
Metadata describing the Group's language. It is independent of language subgroups inside that Group.
_Avoid_: Language subgroup

**Building**:
The building (block/corp) a Room belongs to, carried as the code after the slash in room labels (e.g. `237/4` → Building 4, `216a/4a` → Building 4a). Named Building; Block is reserved as a term.
_Avoid_: Block, corp

**Room**:
A physical teaching space where a lesson can take place, identified by its Building and its room Number — the number as displayed, including any letter suffix (`145a`), which denotes a different room, not an alias. The floor is deduced from the leading digit of the number where the scheme encodes one. A lesson may reference no Room (sports, online).
_Avoid_: Classroom, cabinet, auditorium

**Room feature**:
An equipment property of a Room that lessons can require. Countable features carry a count (computers); the rest are flags — known ones: VR headsets, 3D printer, air conditioning, interactive whiteboard, regular whiteboard. Feature requirements follow the same declaration mechanics as the computer need.
_Avoid_: Amenity, room kind

**Placeholder room**:
The provisional Room (`____`) standing in for a lesson's room that is deliberately not chosen yet. It is not a real room: no validation applies to it and the solver never assigns it. A lesson carrying it is scheduled normally and receives a regular Room.
_Avoid_: No room (a lesson with no Room at all — sports, online), null room

**Room pin**:
A deliberate assignment of a specific Room to a chosen set of lessons. A pin replaces the automatic room requirements (seats and computers) for the lessons it covers and is never questioned.
_Avoid_: Fix room, hardcode, reserved room

**Computer need**:
The number of a lesson's participants who need a computer at the same time. A Room satisfies a lesson only if its computer count reaches the lesson's computer need. Zero unless declared otherwise; a lab lesson's need defaults to its attendance. Declarations may re-scope it per course, teacher, or group, as a requirement or a preference.
_Avoid_: Has computers, computer room, lab requirement
