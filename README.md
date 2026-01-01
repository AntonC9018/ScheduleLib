# ScheduleLib

## Overview

This repository is a collection of utilities for doing university related tasks.

The primary goal of this project was to be able to **interact with the schedule in code
and generate various representations of the schedule**,
but its features have been extended to 
*online registry automation*, 
*attendance and lesson topics import*, 
*automatic contract document completion with personal information* among others.

## Schedule features

### Models

All interactions with the schedule are done against an immutable schedule model (the `Schedule` type).
It's basically like a flat database of various schedule-related entities, 
linked between each other with strongly-typed identifiers.

You can see all models [here](src/ScheduleLib/Model/Schedule.cs) (they are only conceptually immutable).

### Source-of-truth format

The schedule is currently made manually (not by me) and provided in the form of a Word document.
It is delivered by email on demand to me personally.
There aren't any public pages that Word document is available at.

All other representations must be derived from said Word document.
This includes the PDF documents on the [official FMI website](https://fmi.usm.md/orar/),
which is not my responsibility.

Parsing Word seemed easier than processing the PDF's generated from Word,
because they would lose the concept of tables and such,
hence parsing Word is the way that the schedule information gets into the program.

> The Word documents provided by the university usually contain a few errors,
> which make the parser trip up, so have to be fixed manually.

### Word parser

The word parser is one of the most complex aspects of this application.
The most complex parts are the following:
- [WordScheduleParser](src/ScheduleLib/Parsing/WordScheduleParser.cs), 
  which deals with Word itself, using the Microsoft OpenXML library.
- [LessonParser](src/ScheduleLib/Parsing/LessonParser/LessonParser.cs), 
  which parses the strings in a singular cell in a schedule table.
- [CourseNameParser](src/ScheduleLib/Parsing/CourseNameParser.cs) and
  [CourseNameUnifierModule](src/ScheduleLib/Parsing/CourseNameUnifierModule.cs),
  which make sure similar course names are considered the same.

The rules around the document format do not officially exist
and have been derived empirically from the existing schedule Word documents.
Hence, this part of the code is very sensitive to the format
and has tons and heaps of special cases to deal with the inconsistencies.

### The JSON representation

The JSON representation is generated from the source-of-truth schedule in order to:
- Cache the schedule representation, so loading back into memory is faster.
- Implement integration tests that could verify the parsing code doesn't break. 
  This is needed to deal with the aforementioned sensitivity of the parser to the format,
  with the parser having to be adjusted to include new edge cases when new rules are discovered.
- Simplify potential integration with other tools to allow them to load the schedule structure
  without reimplementing the Word parser.

The JSON conversion is done using `System.Text.JSON`. 
You can find the relevant code [here](src/ScheduleLib/Model/ImmutableModels/Serializer.cs).

