# Scope plans: working design for context-dependent dependencies

Status: **in progress**, tentative and kept malleable.
This file records what has been decided so far in the design interview for [#282][282];
it becomes an ADR in `docs/decisions` once the remaining questions are settled.
Background, prior art and terminology: [context-dependent-dependencies.md][research].
#282 blocks [#247][247] (removing the `virtual` test seams from CLI command classes).

Prototype (throwaway, never merged): branch `prototype/context-deps-282`,
directory `prototypes/context-deps-282/`, solution `ContextDepsPrototype.slnx`.
Sections 4 and 5 of its `Program.cs` show this design and the disposal trap.

## Problem in one paragraph

A workflow (CLI command, GUI action, future CLI-server request) needs context values,
such as the data directory, the settings profile or the loaded schedule,
deep inside a dependency graph.
Microsoft DI has no per-scope registrations, so the options are passing values through every
layer, ambient state, or rebuilding containers.
The goal: one root container per process, context supplied when a scope opens,
consumers that take ordinary constructor parameters, and a graph that can be checked.

## Decided

### Building blocks

- **One root container per process.** The CLI builds it once; a CLI server reuses it.
  Context is data supplied to scopes, never extra registrations.
- **Seeds** are raw inputs supplied when a scope opens (CLI arguments, data directory, profile).
  - Consumers receive them through constructor injection, like any other dependency.
  - Seeds can be keyed: `AddKeyedSeed<SourceArgs>("previous")`,
    consumed through `[FromKeyedServices("previous")]`.
    The plan reads that attribute, including `InheritKey`.
  - Seeds are never disposed by the scope; the caller owns them.
- **Derived values** are built synchronously by factory classes,
  `AddDerived<T, TFactory>()` with `TFactory : IFactory<T>`.
  Factory classes, not lambdas, so the plan can see their inputs.
- **Initialized values** are built asynchronously by initializer classes,
  `AddInitialized<T, TInitializer>()` with `TInitializer : IInitializer<T>`.
  - They run when the scope opens, in dependency order, before the handler is resolved.
  - Each value has a write-once slot. Resolving it before initialization fails with
    "not available" instead of returning empty data.
- **Bundles:** one initializer may produce a value Z from which other values X, Y are projected
  by factory classes. Asking for X resolves through Z, and Z is initialized once.
  This is how `ScheduleBuilder` should become an immutable bundle that
  `Schedule`, the lookup module and the teacher remappings are derived from.
- **No reflection in the opener.** `Slot<T>` implements an internal untyped `ISlot.Fill(object)`;
  `IInitializer<T>` extends an untyped `IInitializer` through a default interface method.
  Registrations store their closed slot types.
  `OpenAsync`/`ForkAsync` and initializers use `ValueTask`.

### The plan

- **`ScopePlan<THandler>`** is derived from the registrations by walking constructors from
  the handler type. It lists the seeds the workflow needs, the initializers it runs,
  and their order.
- **Opaque spots are reported, not rejected.** Injected `IServiceProvider`/`IServiceScopeFactory`
  and lambda registrations are warnings: they fail at run time if something is missing.
  They should be replaced with visible dependencies over time.
- **Hidden needs can be declared at registration** until an opaque type is converted,
  e.g. `services.AddHiddenNeeds<LegacyGradePlanner, LessonQuery>()`.
  The warning stays until the service locator is gone.
  This is also how a dynamic need is added manually.
- **Errors are found before any work:** a missing seed or a broken registration is rejected
  when the scope opens, with nothing initialized.
- **Verification:** a test builds the plan of every workflow root in the CLI and GUI
  and checks that none has errors.
- **Plans are built on first use and kept** by the `ScopePlan<THandler>` singleton (Q43);
  the verification test covers what eager building would catch.
- **Independent initializers run sequentially in constructor order** (Q45),
  so a cheap check that can fail (Google auth) can be listed before an expensive parse.

### Settings (Q51)

- No private containers and no containers built from runtime arguments; the CLI may be
  rewritten freely to get there.
- `AddConfigsServices`, `AddOnlineRegistry` and `TreeBuilder` live in the root container.
- A seed `SettingsSelection { ProjectPath?, Profile?, CalendarName? }` comes from CLI options.
- `ResolvedSettings` is an initialized value built from it (it reads files).
- Derived from it: an immutable `TeacherLayerConfig`, a plain `ScheduleDerivationOptions`
  (no `IOptions`) and `IMarkerDataHelper`. Profile-specific behaviour is data, not registrations.

### Composition and forks

- **Child handlers that always run with the parent are constructor parameters.**
  The parent's plan walks into them, so they share its scope and need nothing extra.
  Example: `drive publish` running the export handlers.
- **`ScopeForkFactory<THandler>`** is the injected, scoped object that opens a new scope
  for a handler. `ScopePlan<THandler>` stays a singleton holding only metadata.
  - It is resolved per scope, so it holds the provider of the scope that asked for it.
  - Resolved from the root, there is nothing to inherit.
  - Used when a child needs a new scope: other inputs, many runs, or conditional execution.
    Example: grade import forks a Moodle-copy workflow per quiz from the registry session.
- **Forks inherit values** from the current scope: seeds and already-initialized values
  are copied, not re-initialized.
  - Anything the caller adds explicitly replaces the inherited value.
  - Values built from a replaced input are rebuilt.
  - Values built through dependencies the plan cannot see are always rebuilt, never copied,
    so hidden service location cannot leave a stale copy.
  - The "missing seeds" check runs at fork time and lists only what neither side supplied.
- **Disposal:** Microsoft DI disposes whatever a lambda registration returns when the resolving
  scope ends, so a fork would dispose borrowed values (prototype section 5 shows it).
  Therefore:
  - The plan rejects seeds and initialized values that implement `IDisposable`
    or `IAsyncDisposable`.
  - An initializer that opens a resource registers it with its scope's `ScopeResources`
    and returns a non-disposable value that uses it.
    Only the scope that ran the initializer closes the resource.
- **No per-registration "re-initialize in each fork" setting** until a stateful session
  breaks (Q44).
- **#247 integration** (Q47): CommandDotNet's `UseDependencyResolver` injects
  `ScopeForkFactory<THandler>`; command method parameters become seeds;
  tests replace values only through `AddInitialized`/`AddDerived` and run commands through
  the real host, removing the `virtual` seams and the CR1400 suppression.
- **Accepted limitations:**
  - Inherited values are shared by reference; a child mutating one affects the parent.
  - Ordinary scoped services (not seeds or initialized values) are created fresh in a fork.

### Deferred ideas

- **Laziness:** `Deferred<T>` values loaded only when awaited.
  The codebase scan found the real conditional loads happen inside single steps,
  where a local `Lazy<T>` is enough; "load only what this workflow needs" covers the rest.
  If revived, a value needed only by a lazy value must also load lazily
  (the prototype showed it loading eagerly), and conditional execution can use it.
  Until then, conditional execution uses a fork.
- **Explicit initialization:** initializers the caller must name with `.Init<X>()`,
  and a per-plan switch disallowing implicit initialization.
  Open point if revived: whether naming X also covers what X depends on (a setting).
- **Parallel initializers:** independent initializers marked by an attribute or a
  registration option could run concurrently. Idea only.
- **Compile-time graph:** a source generator producing the plan statically.

## Open questions

See the latest round in the conversation; recorded here once answered.

- **Plan footprint:** plans for different handlers share subgraphs; duplication must be bounded.
- **Open forks when the parent ends:** dispose, error, or allow ownership transfer.
- **Keyed seeds and keyed values:** no current use case for either.

## Next steps (after the open questions and a demo review)

1. Update the prototype: `ScopeForkFactory<T>`, inheritance rules, the disposal rule,
   removing the lazy and explicit modes.
2. Record the design as an ADR in `docs/decisions`.
3. Add the infrastructure to `ScheduleLib.Helper.DependencyInjection`, with tests.
4. Migrate one path end to end (`query lessons`) to test the design against `Schedule`,
   `ResolvedSettings`, `CliAmbient` and `LookupFacade`, then the remaining #247 commands.

Work happens on `design/context-dependencies`, branched from `feat/registry-history-analysis`
and merged back into it; #247 lands as one PR with a commit per command group.

## Codebase findings that shaped this

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
[research]: context-dependent-dependencies.md
