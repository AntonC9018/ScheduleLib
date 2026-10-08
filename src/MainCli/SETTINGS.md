# CLI settings

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

Persisted remove/reset/clear operations use the optional `operations` member
described below. Configuration inspection has no cloud prerequisite.

## Scoped writes

`config set KEY VALUE --scope user|project` replaces a local override with a
typed JSON value. Quote strings as JSON; shell quotes protect those JSON quotes:

```sh
schedulelib config set GoogleCalendarConfig.calendarName '"lessons"' --scope project --project .
schedulelib config set DeadlinesExcelConfig.lessonDelayLimit 4 --scope user
schedulelib config set GoogleCalendarConfig.calendarName '"teacher lessons"' --scope project --project . --profile 'Curmanschii Anton'
schedulelib config unset GoogleCalendarConfig.calendarName --scope project --project .
```

The scope is required. Project writes require a discovered project settings file
or an existing directory selected with `--project`. A missing file is created
when setting a value or adding an explicit suppression action. `--profile` edits the selected scope's overlay for an
existing teacher; omitting it edits defaults. Dotted keys address object members;
set a complete collection to edit its local value. `unset` removes only the local
property/block and its suppression actions, then prunes empty containers, restoring inherited values. Unsetting
an absent override succeeds without creating a settings file. JSON null retains
the existing inheritance semantics.

Keys, typed values, secrets, and all resulting teacher paths are validated before
publishing. Relative paths remain stored as entered and resolve against the edited
settings file. Editors wait cancellably on `<settings-file>.lock`, re-read under
the lease, and atomically replace the complete file; concurrent edits preserve
each other's unrelated properties. Failed staging or cancellation before
publication preserves the previous settings. The lock file remains as a
coordination marker; its exclusive open handle owns the lease. Settings edits
load no schedules, credentials, or provider clients. Their schema 1 result reports
file, scope, profile, key, and whether the persisted document changed; it does
not echo the submitted value.

The internal `CliSettings.Edit` entry point accepts deterministic invocation/user
paths for tests. `LoadCore` validates an in-memory replacement through the existing
layer engine. Set/unset/remove/clear share this validation/publication boundary.

## Explicit suppression and collection clearing

```sh
schedulelib config remove GoogleCalendarConfig --scope project
schedulelib config remove LessonAttendanceConfig.sources --item '{"filePath":"/absolute/attendance.xlsx"}' --scope project
schedulelib config clear LessonAttendanceConfig.sources --scope user --profile 'Curmanschii Anton'
schedulelib config unset LessonAttendanceConfig.sources --scope project
```

`remove` takes a registered block, or a collection path with `--item` containing
an item in that collection's typed JSON representation. The registered comparer
matches its identity: attendance sources use `filePath`, topic fallback providers
use `lessonType`, and topic manifest sources use their typed `path`. Include a
`$type` discriminator for polymorphic collections. File identities are resolved
relative to the file being edited; use an absolute path to match an entry defined
in another directory. A missing item produces a configuration error before
publication. `clear` accepts registered collections. Scalar removal and clearing
whole blocks are unsupported.

Actions persist separately from values in the schema 1 CLI envelope:

```json
{
  "schemaVersion": 1,
  "operations": {
    "defaults": [
      {"operation": "remove", "key": "GoogleCalendarConfig"},
      {"operation": "clear", "key": "LessonAttendanceConfig.sources"}
    ],
    "profiles": {
      "Curmanschii Anton": [
        {"operation": "remove", "key": "LessonTopicsConfig.fallbackProviders", "item": {"lessonType": "Lab"}}
      ]
    }
  }
}
```

Each scope/profile applies its values followed by its actions through the existing
layer update mechanism. Higher layers can supply values again. The optional
`reset` operation on a whole block uses its registered reset behavior; it is
available through direct JSON editing. Unknown operation names, action members,
blocks, collection members and profile identities fail validation. Desktop
serialized tree files retain their existing format and serializer options.

Null values continue to inherit, and ordinary empty lists retain the engine's
keyed merge behavior. `unset KEY` removes that scope/profile's local value and
all actions at KEY or beneath it, restoring inheritance. `set KEY VALUE` also
removes actions at/beneath KEY and any ancestor suppression so the new value can
participate in resolution. Unrelated scopes and profiles retain their actions.
Persisted item removals remain valid if the underlying entry later disappears;
reload treats the already absent entry as suppressed.
