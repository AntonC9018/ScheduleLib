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
