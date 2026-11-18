# Scope plans for context-dependent dependencies

- **Status:** accepted 2026-11-18 ([#282][282]); implementation tracked by [#247][247].
- **Scope:** how workflows (CLI commands, later GUI actions and a CLI server) receive context
  values such as settings and the loaded schedule through dependency injection.
- **Background:** [research note][research]; prototype on branch `prototype/context-deps-282`
  (never merged), `prototypes/context-deps-282/`.

## Problem

A workflow needs context values (data directory, settings profile, loaded schedule) deep in a
dependency graph. Microsoft DI has no per-scope registrations, so the alternatives are passing
values through every layer, ambient state, or one container per invocation. Today each CLI
command builds its own container from runtime arguments, and resolving `Schedule` before
`InitializeSchedule` silently yields an empty schedule.

## Decision

### Building blocks

- **One root container per process.** Context is data supplied to scopes, never extra
  registrations. No private containers, and no containers built from runtime arguments.
- **Seeds** are raw inputs supplied when a scope opens: `AddSeed<T>()`.
  - Consumers receive them through constructor injection.
  - Keyed seeds: `AddKeyedSeed<SourceArgs>("previous")`, consumed through
    `[FromKeyedServices("previous")]` by an ordinary service that stays unkeyed.
  - The caller owns seeds; the scope never disposes them.
- **Derived values** are built synchronously by factory classes:
  `AddDerived<T, TFactory>()` with `TFactory : IFactory<T>`.
  Factory classes, not lambdas, so the plan can see their inputs.
- **Initialized values** are built asynchronously by initializer classes:
  `AddInitialized<T, TInitializer>()` with `TInitializer : IInitializer<T>`.
  - They run when the scope opens, before the handler is resolved: in dependency order, and
    otherwise sequentially in the order constructors list them, so a cheap check that can fail
    (Google auth) can come before an expensive parse.
  - Each value has a write-once slot; resolving it unfilled fails with "not available".
  - A later registration for the same value replaces the earlier one, removing exactly the
    registrations the earlier one added, so container validation no longer needs its
    dependencies; tests use this to supply fakes. Seeds and derived values are replaced the
    same way, and each kind replaces the others.
- **Bundles:** one initializer produces a value from which factories project others;
  the initializer runs once. `ScheduleBuilder` becomes such an immutable bundle for
  `Schedule`, the lookup module and the teacher remappings.
- **No reflection when opening a scope.** Slots and initializers have untyped internal views
  (`ISlot.Fill(object)`, `IInitializer` via a default interface method); registrations store
  their closed slot types. Opening and initializing use `ValueTask`.

### Plans

- **`ScopePlan<THandler>`** is a singleton derived from the registrations by walking
  constructors from the handler type, built on first use and kept. It lists the seeds the
  workflow needs and the initializers it runs, in order.
- **Shared graph:** plans share one node per `(type, key)` and its memoized closure
  (seeds, initializers, warnings, errors), so a subgraph used by many handlers is stored once.
- **Unregistered roots:** a workflow root may be a concrete type that is not registered.
  It is planned from its constructor without memoizing that node or putting it on the walk
  path, so for every other consumer it stays missing and plans, including cycle reports,
  never depend on which workflow was planned first.
- **Cycles:** the walk memoizes each strongly connected component once it is complete, so every
  node is expanded once. A component with a cycle is reported as one error, the shortest cycle
  through its first member in a fixed order, and its members share one closure; plans therefore
  report the same cycle whichever root reached it first.
  The planned scope creates such a handler itself and disposes it when the scope ends.
- **Opaque spots are warnings, not errors.** Injected `IServiceProvider`/`IServiceScopeFactory`
  and lambda registrations fail only at run time if something is missing.
  Each consumer of an opaque or missing node is named. Opaque spots are converted over time.
- **Hidden needs:** `AddHiddenNeeds<TService, TNeed>()` declares what a service, factory or
  initializer resolves at run time, until it is converted. The warning stays.
- **Errors before any work:** a plan error, a missing seed or a supplied value that is not a
  registered seed is rejected when the scope opens, with nothing initialized.
- **Disposable values are plan errors:** seeds, derived values and initialized values must not
  implement `IDisposable` or `IAsyncDisposable`, because Microsoft DI disposes whatever a
  lambda registration returns when the resolving scope ends, including borrowed values.
  An initializer that opens a resource adds it to its scope's `ScopeResources` and returns a
  non-disposable value that uses it.
- **Verification:** a test builds the plan of every workflow root that uses scope plans and
  checks that none has errors.

### Composition and forks

- **Children that always run with the parent are constructor parameters,** sharing its scope.
  Example: `drive publish` running the export handlers.
- **`ScopeForkFactory<THandler>`** is a scoped service that opens a child scope for a handler,
  for other inputs, repeated runs or conditional execution. Example: grade import forking a
  Moodle-copy workflow per quiz over the parent's registry session.
  - Resolved from a plain scope (the CLI's per-invocation scope), there is nothing to inherit.
- **Forks inherit** the parent's seeds and initialized values by reference, including values the
  fork's own plan does not need, so grandchildren can inherit them too.
  - Seeds the caller supplies replace inherited ones.
  - An initialized value is rebuilt when one of its seeds was replaced, or when it is built
    through dependencies the plan cannot see.
  - The missing-seed check lists only what neither the caller nor the parent supplied.
  - Derived values and ordinary scoped services are created fresh in each scope.
- **Whoever opens a fork disposes it.** A parent that ends with a fork still open disposes the
  fork, then throws `ForkLeftOpenException`. Forks never outlive their parent.
  A scope's `ScopeResources` close after its forks.
  - A planned scope ends once, through one operation shared by its owner and its parent:
    forks left open (waiting for any still opening), then a handler it created and its
    container scope, then its `ScopeResources`.
  - A plain scope that forks are opened from is an `InvocationScope`, which ends the same way:
    its forks, then its container scope, then its `ScopeResources`, even when one of its
    services fails to dispose. A plain Microsoft DI scope still ends its forks when disposed,
    but a failing service can stop that disposal early.
  - The handler is resolved while the fork opens, so a failure ends the fork before it is
    returned.
  - Once a scope starts ending, opening a fork from it fails with `ParentScopeEndedException`,
    before any work. A fork still opening at that moment is not returned: its opener ends it and
    receives that exception, while the parent waits for that ending. A fork is returned under the
    parent's lock, so a parent either sees it returned, and ends it as left open, or rejects it.
  - Each ending reports its failures once, to whoever started it; anyone joining it only waits.
  - Every step is attempted even when some fail; the failures are reported together in one
    `ScopeCleanupException`, which also lists the forks left open.
- **Accepted limitation:** inherited values are shared, so a child mutating one affects the
  parent. Context values should be immutable.

### Settings

- `AddConfigsServices`, `AddOnlineRegistry` and `TreeBuilder` live in the root container.
- A seed `SettingsSelection { ProjectPath?, Profile?, CalendarName? }` comes from CLI options.
- `ResolvedSettings` is an initialized value built from it.
- Derived from it: an immutable `TeacherLayerConfig`, a plain `ScheduleDerivationOptions`
  (no `IOptions`) and `IMarkerDataHelper`. Profile-specific behaviour is data.

### CLI integration (#247)

- CommandDotNet's `UseDependencyResolver` resolves command classes in a per-invocation scope;
  they inject `ScopeForkFactory<THandler>`.
- Command method parameters become seeds.
- Tests replace values through `AddInitialized`/`AddDerived` and run commands through the real
  host, removing the `virtual` seams and the CR1400 suppression.
- The GUI is removed from the solution until it moves to scope plans under [#283][283];
  reverting the removal commit restores it.

### Logging

- One Serilog `ReloadableLogger` (`CreateBootstrapLogger`) is registered in the root container.
  After CommandDotNet parses the arguments, the CLI reloads it into console mode (stderr) or
  JSONL mode writing to the invocation's `CliOutput`. `CliAmbient` is removed.
- This is per process. A CLI server needs per-request routing (deferred).

### Schedule

- The flaws of the mutable singleton `ScheduleBuilder` come from working around Microsoft DI,
  so the schedule model is refactored now: loading produces an immutable value per scope,
  and the builder is used only while loading.
- `ScheduleBuilder` is not a DI service. A schedule initializer turns the source seed into an
  immutable `LoadedSchedule`:
  - `Schedule`, with the lab-merge projection (`ScheduleDerivationOptions`) applied,
    so lesson ids agree everywhere;
  - an immutable `ScheduleLookup` (teachers by last name, groups by name, lessons by course),
    replacing `LookupModule`. Its `CourseNameIndex` finds courses by name and alias, then by
    unified name, replacing the unifier's mutable `SlowCourses`.
- `Schedule` and `ScheduleLookup` are derived from it; `LookupFacade` is an ordinary scoped
  service over them. Its consumers are scoped too. `ScheduleProvider`, `InitializeSchedule`
  and the `LookupModule` registration are removed; the builder keeps `LookupModule` internally
  while loading.
- Lookups never write: fuzzy course matches are computed and returned, not stored as aliases.
- Post-processing runs once. On a cache miss the initializer fills a local builder, builds,
  saves and returns that result; on a cache hit it reads the `Schedule` directly and builds the
  lookup from it (ids are list positions in both). The cache format version is bumped.
- `Remappings` is a singleton built from the static remapping configuration, independent of
  the schedule. The builder and post-load consumers (`TeacherNameMapper`,
  `SubGroupNameRemapper`, the cache identity) share it.

## Rejected and not planned

- Lazy `Deferred<T>` values and explicit `.Init<T>()` initialization: real conditional loads
  happen inside single steps, where a local `Lazy<T>` suffices; conditional child workflows fork.
- Forks outliving their parent (ownership transfer): too complicated for the known cases.

## Deferred ideas

- Keyed derived and initialized values: one factory serving several keys through an
  inherited key, once a command compares two schedules.
- Parallel initializers, opted into by an attribute or a registration option.
- A source generator producing plans at compile time.
- A per-registration "re-initialize in each fork" option, once a stateful session needs it.
- Per-request log routing for a CLI server.
- Reusing a previous builder to recreate a schedule incrementally (for example after a source
  changes) instead of loading from scratch. The once-only build must not rule this out.
- How remapping logic and model pieces used both while building and after loading are shared;
  needs analysis beyond moving `Remappings` out of the builder.

## Migration notes

Codebase findings from the design (November 2026):

- Each CLI command builds its own container and repeats the sequence
  "load settings, build provider, `InitializeSchedule`, open scope, resolve handler"
  in about ten places (`CliHost.cs`, `ExportCommands*.cs`, `Commands.Calendar.cs`, …).
- `InitializeSchedule` mutates a singleton `ScheduleBuilder`; resolving `Schedule` before it
  silently yields an empty schedule (`Registration.cs`, `ScheduleInitialization.cs`).
- Hidden state that should become seeds: `CliAmbient` (`AsyncLocal`),
  the mutable scoped `TeacherLayerConfig`, and `ResolvedSettings` with its private container.
- Sub-workflows already exist: `drive publish` runs export handlers in one scope;
  grade import reuses the registry session through `IConfiguredRegistrySession.Services`.
- Obstacles to convert gradually: about eight classes injecting `IServiceProvider`,
  `ActivatorUtilities` with runtime arguments, about ten lambda registrations,
  and `ScheduleBuilderInitializer` opening its own scope.

[282]: https://github.com/AntonC9018/ScheduleLib/issues/282
[247]: https://github.com/AntonC9018/ScheduleLib/issues/247
[283]: https://github.com/AntonC9018/ScheduleLib/issues/283
[research]: ../context-dependent-dependencies.md
