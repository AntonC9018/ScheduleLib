# Read-only CLI settings

`config show`, `config get KEY`, `config validate`, and `config profiles` read
settings without loading schedules, resolving credentials, or contacting any
provider. Add `--json` for the existing versioned `CommandResult<T>` envelope.
Text output is indented readable JSON. `show`/`get` include contributing source
names, file paths, and member paths; sources are ordered from low to high
precedence. These are contributions, not a claim that every provided value
survived a later override or code-defined removal. Inspection redacts password,
secret, token, API-key, and access-key fields.

```sh
schedulelib config profiles
schedulelib config show --profile 'Curmanschii Anton' --json
schedulelib config get GoogleCalendarConfig.calendarName --project . --json
schedulelib config get GoogleCalendarConfig.calendarName --calendar-name example --json
schedulelib config validate --project .
```

`--calendar-name` on `show`/`get` demonstrates a typed invocation override; it
belongs to those inspection commands. Shared operational `SettingsArguments`
contain only `--project` and `--profile`. Calendar execution can use a separate
command-specific argument model in its own slice.

## Files and schema version 1

User settings are `ScheduleLib/schedulelib.json` beneath
`Environment.SpecialFolder.ApplicationData` (the OS user configuration
location, including XDG configuration on Linux and roaming AppData on Windows).
There is no environment-variable settings layer. Discovery starts from the
invocation directory and selects the nearest ancestor with `schedulelib.json`.
An explicit `--project DIRECTORY` resolves against the invocation directory;
the directory must exist, but its settings file is optional. No settings command
changes the process working directory. Both files are optional.

```json
{
  "schemaVersion": 1,
  "defaults": {
    "GoogleCalendarConfig": {
      "calendarName": "lessons",
      "credentials": {
        "credentialsPath": "./tokens",
        "saveCredentials": true
      }
    },
    "DeadlinesExcelConfig": {
      "lessonDelayLimit": 4,
      "goodColor": "LightGreen"
    }
  },
  "profiles": {
    "Curmanschii Anton": {
      "GoogleCalendarConfig": {
        "calendarName": "teacher lessons"
      }
    }
  }
}
```

Block names are registered type names: `GoogleCalendarConfig`,
`GoogleDriveConfig`, `MoodleConfig`, `RegistryConfig`,
`RegistryLessonFilterConfig`, `DeadlinesExcelConfig`, `LessonAttendanceConfig`,
`LessonTopicsConfig`, and `LabTasksDatabaseConfig`. Members use camelCase,
except custom representations retained by existing converters (for example
`RegistryConfig.commandProcessingConfig` uses `Process`, `DryRun`, and `Log`).
Colors use known names or `#AARRGGBB`. Enums use names. Polymorphic values retain
the layer serializer's `$type` discriminator. Unsupported versions, keys,
members, duplicate properties, types, and teachers fail with configuration
exit 3. No academic year, semester, schedule source, or teacher identity can be
redefined here; those settings remain configured in C#.

The `profiles` map adds overlays to existing code-defined teachers. It does not
create a second independent profile for a teacher. All profile overlays in both
files are validated, including those not selected for this invocation. Inline
credential/password/OAuth/API secret values are rejected; existing code
credential sources and user-secrets configuration remain supported at runtime.
For example, an `AppConfigCredentialsSource` reference is allowed, while
`ValueCredentialsSource` and `ManualGoogleApiKeysSource` are rejected.

Missing/null blocks or members inherit. Later non-null scalars override;
nested objects merge; lists use the registered key comparers and preserve
unmatched inherited entries. Null list entries are invalid. An empty list adds
no values and does not clear an inherited collection. `credentialsPath`,
`filePath`, and manifest `path` values resolve relative to the JSON file that
provides them, before keyed merging. CLI paths resolve relative to invocation
location. Coded defaults and teacher/source configuration remain intact.

Precedence is code defaults → user defaults → project defaults → selected
teacher's code settings → user teacher overlay → project teacher overlay →
explicit CLI values. Code-defined remove/update actions are retained and still
run through the existing layer engine.

The desktop `TreeSerializer` still reads/writes its original array format,
PascalCase members, `$LayerName`, and registered converters. CLI schema handling
uses cloned serializer options and does not modify desktop persistence.

## Read API and successor slices

`await CliSettings.Load(arguments, invocationDirectory?, userFile?,
cancellationToken, overrides?)` owns a minimal registration-only provider and
returns disposable `ResolvedSettings`. Optional invocation/user-file parameters
support deterministic callers and tests; ordinary CLI calls use OS discovery.
Keep the result alive throughout the operation. `ProjectDirectory`, `Profile`,
`Profiles`, and `Sources` expose discovery/inspection metadata.
`Get<T>(NodeDataKey<T>)` constructs the selected typed value through
`NodePath.ConstructValue`; `Inspect` provides redacted JSON and provenance.
`Validate` constructs every block in every teacher path without mapping or
credential/provider resolution.

After ordinary service registration, call
`resolved.ConfigureServices(services)` before building the runtime provider.
This registers the same coded tree and an `IMarkerDataHelper` that selects the
composed path for each ambient `TeacherLayerConfig`. Existing `DataProvider`
and mapped typed config consumers therefore use the resolved settings.
The selected profile also supplies the default scoped teacher marker; explicit
teacher scopes continue to select their own composed paths. Global scopes can
use the selected/default path without an unrelated teacher prerequisite.
`ProjectDirectory` is also available to cache/output runtime composition.

Configuration writes (#189), persisted remove/reset/clear operations (#190),
and authentication/registry execution belong to successor slices. There are
no write commands or write side effects here. Version 1 currently stores only
values; a successor must add an explicitly validated operations representation
and route it through existing update actions, rather than interpreting JSON
null or an empty list as removal. Preserve the defaults/profiles envelope and
teacher identity constraints when introducing writes, with coordinated atomic
file replacement. No cloud prerequisite should be added to config inspection.
