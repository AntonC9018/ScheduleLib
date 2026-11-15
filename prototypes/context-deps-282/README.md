# PROTOTYPE: context-dependent dependencies (#282)

Throwaway console app; never merge this branch. It compares three ways to deliver a
context value (`Schedule`) deep into a dependency graph:

1. Seeded scope (Microsoft DI): one root, a set-once scoped holder seeded when the scope opens.
2. Keyed services with an inherited key (Microsoft DI).
3. Autofac child lifetime scopes with per-scope registrations.
4. Modular seeding: seeds, factory classes, async initializers and a `ScopePlan` derived
   from the registrations (`ScopePlan.cs`, `ScopePlanDemo.cs`).

Run: `dotnet run` in this directory. Open `ContextDepsPrototype.slnx` in an IDE.
