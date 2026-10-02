# Research: CP-SAT viability & timetabling modeling patterns

Resolution of [ScheduleLib-6xo.8](beads) — Research: CP-SAT viability & timetabling modeling patterns. Report produced by a research subagent, 2026-09-05.

## 1. Package facts (as of Sep 2026)

- **Package:** `Google.OrTools` (official Google wrapper), **latest stable 9.15.6755, released 2026-01-12** (~5.9M total downloads; previous: 9.14.6206, 2025-06-19). License Apache-2.0.
- **TFMs:** `net8.0` and `net462` (the v9.13 release moved .NET archives/NuGet to .NET 8.0 TFM and dropped .NET 6; netstandard is gone). Native binaries ship as five `Google.OrTools.runtime.*` satellite packages: **linux-x64, linux-arm64, osx-x64, osx-arm64, win-x64** (+ `Google.Protobuf >= 3.33.1`).
- **.NET 10/11:** no `net10.0`/`net11.0` target and none announced, but `net8.0` assets run forward-compatibly on .NET 10/11 runtimes — NuGet itself lists net10.0 as a "computed compatible" TFM. No open issues about preview SDKs or linux-x64 breakage for 9.15; the historically reported problems are (a) native lib not copied on .NET Framework targets (#3747) and (b) glibc-mismatch native load failures on old Linux distros. WSL2/Ubuntu is fine. A .NET 11 preview SDK consuming this package is expected to work; pin the version and smoke-test the native load early.

## 2. C# modeling pattern (verified against official `stable` samples)

Namespace `Google.OrTools.Sat`. Verified API from `NursesSat.cs` / `ShiftSchedulingSat.cs`: `CpModel`, `model.NewBoolVar(name)`, `model.AddExactlyOne(...)`, `model.Add(...)`, `model.Minimize(...)`, `LinearExpr.NewBuilder().AddTerm(...)`, `new CpSolver()`, `solver.StringParameters = "..."`, `solver.Solve(model)` → `CpSolverStatus`, read back with `solver.BooleanValue(v)`, stats via `solver.NumConflicts()` / `solver.WallTime()`.

```csharp
var model = new CpModel();
// x[l,d,s] = lesson l runs on day d, slot s ; r[l,d,s,rm] = ... in room rm
var x = lessons.ToDictionary(l => l.Id, l => model.NewBoolVarArray(days, slots, $"x{l}")); // (pseudo)
foreach (var l in lessons) model.AddExactlyOne(x[l].Flat());                     // one day+slot per lesson

foreach (var t in teachers)                                                       // teacher no-overlap
  foreach (var (d, s) in grid)
    model.Add(LinearExpr.NewBuilder().AddTerms(t.Lessons.Select(l => x[l][d,s])).AtMost(1));
// (groups identical; rooms: r[l,d,s,rm] <= x[l,d,s] channeling + sum(rm for l,d,s)==1
//  + at most one lesson per (d,s,rm) via sum(r) <= 1)

foreach (var (a, b) in blocPairs)                                                 // co-timing
  foreach (var (d, s) in grid) model.Add(x[a][d,s] == x[b][d,s]);

LinearExprBuilder obj = LinearExpr.NewBuilder();                                  // soft preferences
foreach (var (lit, w) in penalties) obj.AddTerm(lit, w);                          // violation literal × weight
model.Minimize(obj);

var solver = new CpSolver();
solver.StringParameters = "num_search_workers:1, random_seed:42, max_time_in_seconds:60";
var status = solver.Solve(model);                                                 // Optimal | Feasible | Infeasible
```

## 3. Determinism

- **`num_search_workers:1`** disables the parallel portfolio — the source of run-to-run non-determinism (workers race on clause sharing; the proto warns several features are "currently non-deterministic" in parallel mode).
- **`random_seed`** reinitializes the solver RNG at each solve; different seeds → different search choices, same model. Set it explicitly.
- With 1 worker + fixed seed + **pinned OR-Tools version**, CP-SAT is deterministic run-to-run and across machines (integer arithmetic; past single-worker nondeterminism bugs #3590/#3943 are closed). Caveats: (a) a wall-clock `max_time_in_seconds` cutoff reintroduces machine variance — for bit-reproducible runs use no limit or `max_deterministic_time`; (b) results may change between OR-Tools versions; (c) `interleave_search` makes search deterministic even multi-worker, at a speed cost.
- Hints don't affect correctness; they steer the search (see §4).

## 4. Performance ballpark

- Scale: the faculty grid (≈10³ lessons × 48 day-slots) yields ~5×10⁴ lesson-slot literals plus room/channeling literals — well inside CP-SAT's comfort zone (community reports of 10⁵–10⁶ literal models; the CP-SAT Primer documents much larger).
- The official `NursesSat.cs` (~25 nurses × 7 days × 3 shifts + channeling, a few thousand literals) solves to optimality in well under a second; community timetabling models of similar shape are instant. Realistic expectation for faculty-size: **seconds for feasibility, seconds-to-minutes to prove optimality** of the weighted objective.
- What dominates: (a) **objective tightness** — proving the last 1–2% of the weighted bound is the long tail (stop at `Optimal`-or-good-`Feasible` with gap limits `absolute/relative_gap_limit`); (b) **constraint density** of the ≤1 cover rows and channeling; (c) on very large models, **presolve time**. Reducing workers to physical cores can even speed solves up.
- **Incremental re-solve:** CP-SAT is stateless — no incremental interface ("no incremental modeling"). Standard pattern: rebuild + **hint the previous solution** ("CP-SAT does a great job accepting hints and cuts down the search time significantly if the hint is good"); for small edits, pin unchanged lessons to constants and let the solver move only affected ones. (Directly relevant to the out-of-scope migration effort later.)

## 5. Alternatives verdict

CP-SAT remains the right call. **Timefold Solver** supports Java/Kotlin (+ a newer but self-admittedly slower Python); there is **no .NET distribution** — using it means a JVM microservice and JSON round-trips, the wrong shape for an in-process engine inside a C# solution. **Microsoft has no first-party offering** (Solver Foundation died ~2012; Z3 is an SMT solver, not scheduling-tuned; nothing in the BCL). **Custom DFS** handles hard constraints but degenerates on weighted multi-objective soft preferences — you'd reinvent heuristic search with no optimality gap; per the CP-SAT Primer, hand-rolled metaheuristics "will have difficulties competing" on solution quality. Remaining .NET-native options are commercial heuristic engines (e.g., Optano) or MIP (HiGHS) — MIP can't express co-timing/channeling as naturally and is weaker on scheduling propagation. `Google.OrTools` is free (Apache-2.0), maintained, ships linux-x64, and its C# CP-SAT surface (`CpModel`/`AddExactlyOne`/`Minimize`) is fully sufficient.

## Sources

- https://www.nuget.org/packages/Google.OrTools
- https://github.com/google/or-tools/releases
- https://developers.google.com/optimization/install/dotnet
- https://raw.githubusercontent.com/google/or-tools/stable/ortools/sat/sat_parameters.proto
- https://github.com/google/or-tools/issues/3943 and https://github.com/google/or-tools/issues/3590
- https://groups.google.com/g/or-tools-discuss/c/lPb1FzhTMt0
- https://github.com/google/or-tools/tree/stable/examples/dotnet (ShiftSchedulingSat.cs, NursesSat.cs, SolveWithTimeLimitSampleSat.cs)
- https://d-krupke.github.io/cpsat-primer/ (parameters.html, big_picture.html, benchmarking.html)
- https://news.ycombinator.com/item?id=48120351
- https://medium.com/suboptimally-speaking/school-timetabling-with-constraint-programming-495f1126c28d
- https://github.com/timefoldai/timefold-solver
- https://stackoverflow.com/questions/70490201/why-or-tools-doesnt-load-in-c-linux
