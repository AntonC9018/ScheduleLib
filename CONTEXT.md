# Schedule domain

ScheduleLib models university teaching schedules and the student populations to which lessons apply.

## Language

**Group**:
The complete population of students enrolled under one university group identity, including every combination of its student partitions and curriculum choices.
_Avoid_: Cohort, group family

**SubGroup**:
A mutually exclusive student partition within a Group, such as a numeric, language, or language-proficiency partition. Different kinds of subgroups may coexist independently.
_Avoid_: Specialization, Group

**Specialization**:
A named study-track partition within a Group, separate from its subgroups.
_Avoid_: Specialty, specialization subgroup

**Choice bloc**:
A choice package defined by one curriculum-plan version for one program, attendance mode, and semester. A student selects exactly one of its curriculum options; the selected option then becomes compulsory for that student. Its ordinal is local to that semester and plan.
_Avoid_: Specialization, parallel set, year-wide bloc

**Curriculum option**:
A course's membership in a particular Choice bloc, including its plan-specific discipline code and eligibility annotations. It is distinct from the stable identity of the Course itself.
_Avoid_: Course, Teaching section

**Student choice**:
The Curriculum option selected by one student for one Choice bloc.
_Avoid_: Specialization

**Teaching section**:
An actually opened delivery of a Course for a defined student population and language. It may combine students from several Groups or programs and may be divided into its own laboratory subgroups.
_Avoid_: Choice bloc, Curriculum option, Group

**Group partition**:
The restrictions that determine which students in a Group attend a lesson. They may refer to subgroups, specialization, or selected Curriculum options from one or more Choice blocs.
_Avoid_: Subgroup combination

**Subgroup combination**:
One possible student population obtained by selecting an active value from every subgroup dimension, a specialization when applicable, and one Curriculum option from each active Choice bloc.
_Avoid_: Group partition, compound subgroup

**Choice vector**:
The combination of one Student choice for every active Choice bloc in the same semester. It identifies a possible student schedule for that semester; choices from different semesters do not multiply its schedule count.
_Avoid_: Full-year schedule

**Whole-Group schedule**:
An administrative schedule containing every lesson associated with a Group, regardless of subgroup, specialization, or curriculum choice.
_Avoid_: Student schedule

**Group language**:
Metadata describing the Group's language. It is independent of language subgroups inside that Group.
_Avoid_: Language subgroup
