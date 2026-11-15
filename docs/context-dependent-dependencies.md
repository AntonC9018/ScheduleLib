# Context-dependent dependencies in dependency injection

Research note for [issue #282][issue]. Checked 2026-11-15 against primary sources
(official docs, first-party source code, the authors' own writing).
Where a claim was also tested locally, the note says so; the test used
Microsoft.Extensions.DependencyInjection 10.0.0.
This note does not choose an approach; the decision stays open in #282.

## Problem statement

A **context value** `X` is a dependency whose identity depends on runtime context,
not on its type. In ScheduleLib, `X` is the `Schedule` built from a data directory,
study year and profile.
A root operation `R` resolves a graph `R → A → B → … → C`, and only the deep consumer `C`
needs `X`. The requirements, copied from #282:

- **R1 Transparency:** intermediate services `A`, `B`, … do not mention `X`.
- **R2 Reach:** `X` reaches consumers at any depth, including dependencies of dependencies.
- **R3 Overridability:** a caller above `R` can supply or replace `X`, or an implementation
  that depends on it, without knowing the graph's shape.
- **R4 Coexistence:** several contexts live in one process (GUI schedule switch, server
  requests) and share context-independent services built once.
- **R5 Verifiability:** missing or wrong wiring fails at composition time, not at first use.
- **R6 No ambient state:** no `AsyncLocal`, statics or mutable "current" holders.

## The author's perspective

This section is the maintainer's own line of thinking, lightly edited for clarity.

> If A needs B, its direct dependency, to get a value, passing it as a parameter is fine.
> But if beneath it there is a graph of services and the value must reach all the way down,
> you end up with one of four things:
>
> 1. Passing it through the whole graph, which is obviously bad.
> 2. Packaging it in a context object, which locks the graph to that context.
> 3. DI hacks like `AsyncLocal` or a scoped lazy holder.
> 4. Building a smaller DI container yourself, which is discouraged.
>
> Manually building the graph with a factory that substitutes a runtime argument assumes you
> know (a) exactly the dependencies of your graph, (b) all of them in one place, and
> (c) that the caller above can't override it transparently, only by passing it directly.
>
> Rebuilding a container per context feels like an anti-pattern.
> Assisted-injection factories can't reach the dependencies of a dependency.
>
> There seems to be a hole in Microsoft's DI guidelines here, and people don't seem to
> discuss it.

### Where the sources confirm this view

- **Microsoft defers the feature to other containers.**
  The guidelines recommend the built-in container "unless you need a specific feature that
  it doesn't support", and the list includes *child containers* and *custom lifetime
  management* ([guidelines][ms-guidelines]).
  The same page states that scopes "aren't hierarchical, and there's no special connection
  among scopes".
- **A scope cannot carry registrations.**
  The engine scope is constructed from a provider and a root flag only
  ([`ServiceProviderEngineScope`][ms-scope-src]); the public contract exposes just a
  `ServiceProvider`. The three lifetimes (singleton, scoped, transient) have no
  "one per context" option.
- **Assisted injection stops at one constructor.**
  `ActivatorUtilities.CreateFactory` builds a delegate that instantiates one type with
  "constructor arguments provided directly and/or from an `IServiceProvider`"
  ([API][ms-activator]).
  The direct arguments match the activated type's own constructor; dependencies of those
  dependencies come from the provider, which knows nothing about `X`.
  Guice's `@Assisted` has the same shape: it generates a factory for one implementation
  class ([AssistedInject][guice-assisted]).
- **Ambient context is a known smell.**
  The author of the term "Ambient Context" now classifies it as an anti-pattern
  ([Seemann, 2019][ploeh-antipatterns]).
- **The "hole" is real in .NET's own stack.**
  ASP.NET Core hands `HttpContext` to deep services through `IHttpContextAccessor`, which is
  backed by an `AsyncLocal` ([source][hca-src], [docs][aspnet-httpctx]).
  That is option 3 from the quote, chosen by the framework itself.
- **Seemann discourages per-module child containers.**
  Asked about child containers per feature, he answers that each process "requires only a
  single Composition Root", and that letting modules register into containers couples them to
  a container ([comments][ploeh-root]).
  Note that this addresses plug-in modules, not runtime context values.

### Where established practice offers a way out

- **Child scopes with their own registrations.**
  Autofac accepts a registration lambda in `BeginLifetimeScope`; the registrations "will be
  available only in this lifetime scope" ([docs][autofac-scopes]).
  Dagger subcomponents, Guice child injectors and Guice private modules are the Java
  counterparts ([Dagger][dagger-sub], [Guice][guice-injector]).
- **A cached container per context, not a rebuild per call.**
  Orchard Core keeps one container per tenant in a dictionary and builds it on first use
  ([`ShellHost`][orchard-host], [`ShellContext`][orchard-context]).
  The tenant container is built on the *default* Microsoft container by cloning the host's
  service collection ([`CreateChildContainer`][orchard-child]).
  Host singletons are forwarded with a factory that resolves them from the host container,
  so they are built once and shared.
  This is the strongest counter-example to "rebuilding a container is an anti-pattern":
  the rebuild happens once per context, not once per operation, and sharing is explicit.
- **Seeded scopes.**
  Microsoft's own Blazor guidance registers a scoped `TokenProvider` and fills it after the
  scope exists ([docs][blazor-tokens]). Guice's `SimpleScope` example seeds values per scope
  and binds a provider that throws if nothing was seeded ([CustomScopes][guice-custom]).
  Both match option 3 in the quote; both fail at first use, not at composition.
- **Keyed services with an inherited key.**
  Missing from the quote's list and from #282;
  see [approach E](#e-keyed-services-with-inherited-key).
- **Compile-time composition.** Pure DI and source-generated composition remove the
  verification gap and the container question altogether, at a maintenance cost.

### Trade-offs of the ways out

- **Child scope with registrations:** cheap sharing and local overrides, but it needs a
  third-party container, and singletons cannot see scope registrations.
- **Cached container per context:** isolation on the default container, but a clone cost per
  context, an eviction and disposal policy, and a rule for shared versus per-context services.
- **Seeded scope:** works on the default container, but with temporal coupling and
  verification only at first use.
- **Keyed services:** works on the default container and is verified at build, but needs an
  annotation on intermediates and a mapping from key to context.
- **Pure DI:** compile-time wiring, but a hand-maintained graph and explicit hooks for overrides.

## Approach catalogue

Ratings: ✓ satisfied, ✗ failed, ~ partial or conditional.
Ratings are my reading of the cited sources unless marked *tested*.

### A. Parameter or context object

Mechanism: `X` (or a bag containing it) is a parameter of every method or constructor on the
path. React calls this ["prop drilling"][react-context].

```csharp
class A(B b)   { public void Run(Schedule x) => b.Run(x); }
class B(C c)   { public void Run(Schedule x) => c.Run(x); }
class C        { public void Run(Schedule x) { /* uses x */ } }
```

- R1 ✗, R2 ✓, R3 ~ (caller supplies `X` but cannot swap an inner implementation),
  R4 ✓, R5 ✓, R6 ✓.
- A context object keeps R2 but makes every layer depend on the context type.

### B. Ambient context

Mechanism: a static or `AsyncLocal` holder is set by the entry point and read by `C`.
ASP.NET Core does this for `HttpContext` ([source][hca-src]); Finbuckle does it for the tenant
([`AmbientTenantContext`][finbuckle-ambient]); Autofac.Multitenant asks an
`ITenantIdentificationStrategy` for the tenant "for the current execution context"
([docs][autofac-mt]).

```csharp
class Ambient { static readonly AsyncLocal<Schedule?> s_current = new();
    public static Schedule Current => s_current.Value ?? throw new InvalidOperationException(); }
class C { public void Run() => Ambient.Current.Lessons(); }
```

- R1 ✓, R2 ✓, R3 ✓ (set it above), R4 ~ (works per async flow, fragile around background work),
  R5 ✗, R6 ✗.
- Finbuckle's own docs warn that anything caching the value outside the accessor sees the
  first tenant forever ([options docs][finbuckle-options]); that is lifetime contagion again.
- Seemann: "the more I think about it, the more I think that [...] Ambient Context really is
  an anti-pattern" ([2019][ploeh-antipatterns]); in a reply to a reader he explains how to
  refactor it away by injecting the dependency.

### C. Seeded scope

Mechanism: register a scoped holder, create the scope, fill the holder, resolve `R`.

```csharp
services.AddScoped<ScheduleHolder>();
services.AddScoped(sp => sp.GetRequiredService<ScheduleHolder>().Schedule);
using var scope = provider.CreateScope();
scope.ServiceProvider.GetRequiredService<ScheduleHolder>().Schedule = schedule;
var r = scope.ServiceProvider.GetRequiredService<R>();
```

- R1 ✓, R2 ✓, R3 ~ (value only; registrations are fixed), R4 ✓ (shared singletons, scope per
  context), R5 ✗ (the holder is empty until after scope creation, so no composition-time check
  can see it; this is reasoning, not a tested claim), R6 ✓ for the Microsoft container.
- Singletons cannot depend on `X`; `ValidateScopes` reports the attempt
  ([`ValidateScopes`][ms-validate-scopes]).
- Sources: [Blazor token provider][blazor-tokens]; Guice's version uses a `ThreadLocal`, so it
  is ambient there ([CustomScopes][guice-custom]).

### D. Assisted injection (`ObjectFactory`)

Mechanism: pass `X` as an argument when activating `C`.

```csharp
var factory = ActivatorUtilities.CreateFactory(typeof(C), [typeof(Schedule)]);
var c = (C)factory(provider, [schedule]);
```

- R1 ✓ (for the factory's callers), R2 ✗, R3 ✗, R4 ✓, R5 ~, R6 ✓.
- `X` reaches only `C`'s constructor; `C`'s own dependencies are resolved from the provider.
- Sources: [`ActivatorUtilities.CreateFactory`][ms-activator],
  [Guice AssistedInject][guice-assisted].

### E. Keyed services with inherited key

Mechanism (.NET 8+): the context identity becomes a runtime service key.
`[FromKeyedServices]` with no argument means "inherit the key of the service being resolved"
(`ServiceKeyLookupMode.InheritKey`, [attribute source][ms-keyed-attr],
[lookup source][ms-callsite]), and `KeyedService.AnyKey` registers a fallback for any key
([docs][ms-overview]).

```csharp
services.AddSingleton<Shared>();                       // context-independent
services.AddKeyedTransient<A>(KeyedService.AnyKey);    // B, C likewise
services.AddKeyedScoped<Schedule>(KeyedService.AnyKey, (sp, key) => Load((ContextKey)key!));
class A([FromKeyedServices] B b);                      // no mention of Schedule
class C([FromKeyedServices] Schedule s, Shared shared);
var a = scope.ServiceProvider.GetRequiredKeyedService<A>(contextKey);
```

- *Tested:* the key reached `C` through `A → B → C`; an unkeyed `Shared` singleton was the same
  instance for two keys; a specific key (`"special"`) overrode the `AnyKey` fallback;
  an intermediate without the annotation made `ValidateOnBuild` fail at build time.
- R1 ~ (intermediates carry an annotation and keyed registrations, but never mention `X`),
  R2 ✓, R3 ~ (a caller adds a more specific keyed registration before the provider is built),
  R4 ✓, R5 ✓ for missing dependencies (*tested*, with `ValidateOnBuild`), R6 ✓.
- Caveats: keys must be value-comparable; registrations stay static, so each context needs a
  factory that maps key to `X`; keyed singletons live in the root provider forever, hence the
  scoped lifetime above. Resolving by runtime key is a service-locator call at the root.
- A static key (`[FromKeyedServices("a")]`) cannot name a runtime context; only the inherited
  form propagates one. The Microsoft pages I read (overview, ASP.NET Core DI) do not describe
  this propagation use case, so this is a reading of the API source, confirmed by the test above.

### F. Container per context, cached (Orchard Core shells)

Mechanism: build one container per context on first use, cache it by context key, forward
context-independent singletons from the host.

```csharp
var tenant = host.CreateChildContainer(hostServices);   // clone + forward singletons
tenant.AddSingleton(schedule);                           // context registrations
var provider = tenant.BuildServiceProvider(validateScopes: true);
```

Orchard Core's [`CreateChildContainer`][orchard-child] forwards non-disposable host singletons
as `sp => serviceProvider.GetRequiredService(type)`, and disposable or factory-built ones as
instances resolved immediately from the host. [`ShellHost`][orchard-host] keeps the result in a
`ConcurrentDictionary` keyed by tenant name.

- R1 ✓, R2 ✓, R3 ✓ (register an override in the cloned collection), R4 ✓ (shared by
  forwarding), R5 ✓ (a full build with validation runs per context), R6 ✓.
- Costs: cloning the collection per context; an eviction and disposal policy; a rule for which
  singletons are shared and which are re-registered per context.
- DryIoc offers a built-in version: container copies whose new registrations "are isolated
  from the parent and the vice versa", with `O(1)` snapshots
  ([docs][dryioc-child]). Lamar does not: its nested containers only scope instances, and the
  docs note it "does not yet support" child containers ([docs][lamar-nested]).

### G. Child scope with its own registrations

Mechanism: a nested lifetime scope accepts extra registrations that apply only inside it.

```csharp
using var scope = root.BeginLifetimeScope(b => b.RegisterInstance(schedule));
var r = scope.Resolve<R>();
```

- Autofac: R1 ✓, R2 ✓ for instance-per-dependency and per-scope components, R3 ✓
  ("spot weld" overrides, [docs][autofac-scopes]), R4 ✓, R5 ✗ (see below), R6 ✓.
- Autofac `SingleInstance` components live in the root, so they never see scope registrations
  ([instance scope][autofac-instance]). That is lifetime contagion: whatever depends on `X`
  must itself be scoped.
- Autofac "does not necessarily prevent you from creating captive dependencies"
  ([captive dependencies][autofac-captive]). The pages I read describe no composition-time
  graph validation; missing services surface at resolve time.
- `MultitenantContainer` is this mechanism, one lifetime scope per tenant, plus an ambient
  tenant lookup ([docs][autofac-mt]).
- Guice child injectors only add bindings: "no key may be bound by both an injector and one of
  its ancestors", and parent elements cannot see child bindings
  ([`Injector`][guice-injector]). R3 fails for keys the parent binds.
- Dagger subcomponents extend the parent graph and are checked at compile time
  ([docs][dagger-sub]); `@BindsInstance` supplies runtime values ([API][dagger-binds]).
  Not available in .NET.
- Interop: Autofac wraps a container or a scope in an `AutofacServiceProvider`, and
  `Populate()` can target a tagged child scope ([.NET Core integration][autofac-netcore]).
  Adopting it means replacing Microsoft's container, the step Microsoft's guidelines describe.

### H. Pure DI and compile-time composition

Mechanism: compose by hand (or generate the code) per context, in the Composition Root.

```csharp
static R Compose(Schedule x, SharedServices shared)
{
    var c = new C(x, shared.Parser);
    return new R(new A(new B(c)));
}
```

- Hand-written: R1 ✓ (only the composer knows `X`), R2 ✓, R3 ✗ without explicit hooks,
  R4 ✓ if `SharedServices` is built once, R5 ✓ (compiler), R6 ✓.
  Costs: the composer must know the whole graph, the exact point the author's quote makes.
- Seemann: Pure DI is "Dependency Injection without a DI Container" and in many cases better
  than using one ([Pure DI][ploeh-puredi]); the Composition Root is where it belongs
  ([Composition Root][ploeh-root]). He describes the trade-off as high maintenance versus
  strong typing ([When to use a DI Container][ploeh-when]).
- Pure.DI generates this code at compile time and reports missing dependencies as build errors
  ([README][puredi-readme]). Its *composition arguments* pass runtime state "without global
  statics": `new Composition(timeout: 30, ...)` makes the value available "wherever [it is]
  required" ([docs][puredi-args]). A composition instance per context fits R1, R2, R5, R6.
  Whether shared singletons can be passed in (R4) and how overrides work (R3) were not tested.

### I. Reader monad and implicit parameters (the conceptual model)

C# has no equivalent, but the functional world names the idea.

- The *Reader monad*: "computations which read values from a shared environment", with
  `local` to run a sub-computation in a modified environment
  ([mtl `Control.Monad.Reader`][mtl-reader]).
- GHC's `ImplicitParams` give "dynamic scoping with static types": the dependency appears as a
  type constraint `(?x :: t) => …`, "automatically propagated" to callers
  ([GHC guide][ghc-implicit]).
- Takeaway: the ideal is a value that flows implicitly (R1, R2) yet is tracked by the type
  system (R5) and rebinding is local (R3, R4). Ambient state loses the typing; the container
  approaches try to recover it.
- React's `Context` is the UI analogue: a parent provides a value to any depth without
  props, with a default if no provider exists ([react.dev][react-context]).
  The default value is the same weakness as an ambient fallback.

### Summary matrix

| Approach | R1 | R2 | R3 | R4 | R5 | R6 |
| --- | --- | --- | --- | --- | --- | --- |
| A. Parameter or context object | ✗ | ✓ | ~ | ✓ | ✓ | ✓ |
| B. Ambient (`AsyncLocal`) | ✓ | ✓ | ✓ | ~ | ✗ | ✗ |
| C. Seeded scope | ✓ | ✓ | ~ | ✓ | ✗ | ✓ |
| D. Assisted injection | ✓ | ✗ | ✗ | ✓ | ~ | ✓ |
| E. Keyed services, inherited key | ~ | ✓ | ~ | ✓ | ✓ | ✓ |
| F. Cached container per context | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| G. Autofac child scope | ✓ | ✓ | ✓ | ✓ | ✗ | ✓ |
| H. Pure DI / Pure.DI | ✓ | ✓ | ✗ | ✓ | ✓ | ✓ |

## Vocabulary for further reading

- **Robot legs problem.** "How to create a robot with a two Leg objects, the left one injected
  with a LeftFoot, and the right one with a RightFoot", with one `Leg` class
  ([Guice FAQ][guice-faq]). Guice's answer is one `PrivateModule` per variant:
  each has its own bindings, exposes only the annotated result, and inherits the rest from the
  parent environment ([`PrivateModule`][guice-private]).
  Both variants are known at configuration time, so it solves a static version of our problem.
- **Contextual binding.** Ninject selects an implementation using the binding metadata and the
  *target* being injected into ([wiki][ninject-ctx]). That is context in the sense of
  "injection site", not "runtime value". I found no Guice documentation using this term.
- **Prop drilling.** React's name for option A ([react.dev][react-context]).
- **Dynamic scoping, implicit parameters, Reader monad.**
  See [section I](#i-reader-monad-and-implicit-parameters-the-conceptual-model).
- **Multi-tenancy.** Orchard Core shells (container per tenant, [source][orchard-host]),
  Autofac.Multitenant ([docs][autofac-mt]), Finbuckle.MultiTenant per-tenant options
  ([docs][finbuckle-options]).
- **Captive dependency.** A longer-lived service holding a shorter-lived one; coined by Seemann
  ([post][ploeh-captive]) and quoted in Microsoft's guidelines ([docs][ms-guidelines]).
  `ValidateScopes` checks that scoped services are never resolved from the root provider
  ([API][ms-validate-scopes]); `ValidateOnBuild` checks that all services can be created at
  build time, excluding open generics ([API][ms-validate-build]).
- **Lifetime contagion.** Not an established term; #282 uses it for the transitive effect
  Seemann describes: a singleton's dependencies become effectively singletons too
  ([Captive Dependency][ploeh-captive]). With context values, the context's lifetime spreads to
  every consumer of `X`.
- **Composition Root, Pure DI, Register Resolve Release.** Seemann
  ([Composition Root][ploeh-root], [Pure DI][ploeh-puredi]).
- **Ambient Context.** Seemann's pattern turned anti-pattern ([2019][ploeh-antipatterns]).

## Corrections to #282

Each item names what #282 says and what the sources show.

1. **Keyed services are missing and are not "static keys only".**
   #282 does not list keyed services. The `object` key can be a runtime value, and
   `[FromKeyedServices]` with no argument inherits the parent's key, which carries the key down
   the graph (*tested*, see [section E](#e-keyed-services-with-inherited-key)).
   A fixed key such as `"cache1"` indeed cannot name a runtime context.
2. **"One container per context, rebuilt on demand: fails R4 sharing" is imprecise.**
   Orchard Core shares host singletons across per-context containers on the default
   container, by forwarding registrations ([source][orchard-child]).
   Sharing is possible; the real costs are cloning, eviction and classifying services.
3. **"Child scope with its own registrations: none in principle" is too strong.**
   - Guice child injectors cannot override a key the parent binds ([`Injector`][guice-injector]).
   - Autofac singletons do not see scope registrations, and Autofac does not prevent captive
     dependencies ([instance scope][autofac-instance], [captive][autofac-captive]).
   - Lamar's nested containers do not carry registrations ([docs][lamar-nested]).
   - The Guice robot-legs answer is `PrivateModule`, not child injectors.
4. **"Contextual binding (Ninject, Guice)".**
   Ninject's term concerns the injection target ([wiki][ninject-ctx]), and I found no Guice
   feature of that name. It is a neighbour of the problem, not the same problem.
5. **Multi-tenancy examples are not equal.**
   Finbuckle and Autofac.Multitenant both resolve the tenant from ambient execution context
   ([Finbuckle][finbuckle-ambient], [Autofac][autofac-mt]), so they fail R6 by design.
   Orchard Core's shell is the one that is a cached container per tenant.
6. **Seeded scope row is accurate, with an addition.**
   Guice's official seeded scope uses a `ThreadLocal` and a provider that throws when the key
   was not seeded ([CustomScopes][guice-custom]); so even the reference implementation is
   ambient and checked only at first use.
7. **Microsoft documentation claim is confirmed.**
   The guidelines list child containers and custom lifetime management
   ([docs][ms-guidelines]). The list does not mention name-based injection,
   which keyed services cover since .NET 8 ([docs][ms-overview]).
8. **`ValidateScopes` and captive dependencies: confirmed, with a default to note.**
   Scope validation covers scoped services consumed from the root and from singletons
   ([guidelines][ms-guidelines]). Its default follows the hosting environment (development)
   ([API][ms-validate-scopes]); the `ServiceProviderOptions` property itself defaults to
   `false` ([source][ms-options-src]).
9. **Outside #282: ASP.NET Core is not a "seeded scope" example.**
   The framework creates a scope per request ([docs][aspnet-di]) and gives deep services
   `HttpContext` through an `AsyncLocal` accessor ([source][hca-src]).
   Seeding appears in Blazor's token provider ([docs][blazor-tokens]).

## Implications for ScheduleLib

No decision here; these are inputs for the discussion in #282.

- **Server constraint.** Build context-independent services (parsers, registries, lookup
  tables) once, in a root composition, and key per-context compositions by data dir, year and
  profile. Approaches C, E, F and G support this; A, B and D do not help with it.
- **Ambient state today.** `CliAmbient` wraps an `AsyncLocal<CliOutput?>`
  (`src/MainCli/CliOutput.cs`), the pattern Seemann now calls an anti-pattern.
  Replacing it is part of R6.
- **Verifiability is the discriminator.** Only E (with `ValidateOnBuild`), F (build per context
  with validation), H and compiler-checked approaches meet R5 on the default container.
  Seeded scopes, which #282 says the GUI already approximates with `CreateMarkerScope`, do not.
- **Shortlist for the out-of-tree example** (#282's next step): C as the baseline,
  E and F on the default container, G with Autofac, H with Pure.DI.
- **Open questions to settle by experiment.**
  - Does `ValidateOnBuild` stay useful when `X` comes from a factory registration?
    The documentation only mentions open generics as excluded; I tested the missing-dependency
    case only.
  - What eviction and disposal policy fits F for the GUI schedule switch?
  - Can approach H share one set of singletons between compositions, and how do tests replace a
    provider without subclassing (the #247 constraint)?

## Sources

[issue]: https://github.com/AntonC9018/ScheduleLib/issues/282

[ms-guidelines]: https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/guidelines
[ms-overview]: https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/overview
[aspnet-di]: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection
[aspnet-httpctx]: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/http-context
[blazor-tokens]: https://learn.microsoft.com/en-us/aspnet/core/blazor/security/server/additional-scenarios
[ms-activator]: https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.activatorutilities.createfactory
[ms-validate-scopes]: https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.serviceprovideroptions.validatescopes
[ms-validate-build]: https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.serviceprovideroptions.validateonbuild
[ms-scope-src]: https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.Extensions.DependencyInjection/src/ServiceLookup/ServiceProviderEngineScope.cs
[ms-keyed-attr]: https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.Extensions.DependencyInjection.Abstractions/src/FromKeyedServicesAttribute.cs
[ms-callsite]: https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.Extensions.DependencyInjection/src/ServiceLookup/CallSiteFactory.cs
[ms-options-src]: https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.Extensions.DependencyInjection/src/ServiceProviderOptions.cs
[hca-src]: https://github.com/dotnet/aspnetcore/blob/main/src/Http/Http/src/HttpContextAccessor.cs

[autofac-scopes]: https://autofac.readthedocs.io/en/latest/lifetime/working-with-scopes.html
[autofac-instance]: https://autofac.readthedocs.io/en/latest/lifetime/instance-scope.html
[autofac-captive]: https://autofac.readthedocs.io/en/latest/lifetime/captive-dependencies.html
[autofac-netcore]: https://autofac.readthedocs.io/en/latest/integration/netcore.html
[autofac-mt]: https://autofac.readthedocs.io/en/latest/advanced/multitenant.html

[guice-faq]: https://github.com/google/guice/wiki/FrequentlyAskedQuestions
[guice-private]: https://github.com/google/guice/blob/master/core/src/com/google/inject/PrivateModule.java
[guice-injector]: https://github.com/google/guice/blob/master/core/src/com/google/inject/Injector.java
[guice-assisted]: https://github.com/google/guice/wiki/AssistedInject
[guice-custom]: https://github.com/google/guice/wiki/CustomScopes
[ninject-ctx]: https://github.com/ninject/Ninject/wiki/Contextual-Binding

[orchard-child]: https://github.com/OrchardCMS/OrchardCore/blob/main/src/OrchardCore/OrchardCore/Shell/Builders/Extensions/ServiceProviderExtensions.cs
[orchard-host]: https://github.com/OrchardCMS/OrchardCore/blob/main/src/OrchardCore/OrchardCore/Shell/ShellHost.cs
[orchard-context]: https://github.com/OrchardCMS/OrchardCore/blob/main/src/OrchardCore/OrchardCore.Abstractions/Shell/Builders/ShellContext.cs
[finbuckle-options]: https://www.finbuckle.com/MultiTenant/Docs/Options
[finbuckle-ambient]: https://github.com/Finbuckle/Finbuckle.MultiTenant/blob/main/src/Finbuckle.MultiTenant.Abstractions/AmbientTenantContext.cs

[dagger-sub]: https://dagger.dev/dev-guide/subcomponents
[dagger-binds]: https://dagger.dev/api/latest/dagger/BindsInstance.html
[dryioc-child]: https://github.com/dadhi/DryIoc/blob/master/docs/DryIoc.Docs/KindsOfChildContainer.md
[lamar-nested]: https://jasperfx.github.io/lamar/documentation/ioc/nested-containers/
[puredi-readme]: https://github.com/DevTeam/Pure.DI/blob/master/README.md
[puredi-args]: https://github.com/DevTeam/Pure.DI/blob/master/readme/composition-arguments.md

[ploeh-root]: https://blog.ploeh.dk/2011/07/28/CompositionRoot/
[ploeh-puredi]: https://blog.ploeh.dk/2014/06/10/pure-di/
[ploeh-captive]: https://blog.ploeh.dk/2014/06/02/captive-dependency/
[ploeh-when]: https://blog.ploeh.dk/2012/11/06/WhentouseaDIContainer/
[ploeh-antipatterns]: https://blog.ploeh.dk/2019/01/21/some-thoughts-on-anti-patterns/

[react-context]: https://react.dev/learn/passing-data-deeply-with-context
[ghc-implicit]: https://downloads.haskell.org/ghc/latest/docs/users_guide/exts/implicit_parameters.html
[mtl-reader]: https://hackage.haskell.org/package/mtl/docs/Control-Monad-Reader.html
