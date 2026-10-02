# Avalonia property-set builder

The builder separates reusable property behavior from each editor's display layout. It reads and writes the selected node through `NodeDataAccessor<T>`, so changes follow the existing layer selection and editability rules.

## Register an editor

Use a configuration model with public readable/writable properties and a `NodeDataKey<T>`. The following example assumes `Settings` has nullable `bool Enabled`, nullable `int Count`, and nullable `string Name` properties, and a `Settings.Key`.

```csharp
services.ConfigurePropertySet(Settings.Key, properties =>
{
    properties.Property(x => x.Enabled).Rename("Enable processing");
    properties.Property(x => x.Count).Describe("Maximum number of items.");
});

services.AddPropertySetDisplayFactory(new PropertySetId("Settings"), display =>
    display.SourceFrom(Settings.Key, layout =>
    {
        layout.Include(group =>
        {
            group.Title("Processing");
            group.IncludeProperty(x => x.Enabled);
            group.IncludeProperty(x => x.Count);
        });
        layout.IncludeProperty(x => x.Name);
    }));
```

The existing editor host resolves `IPropertySetViewModelFactory` by its `PropertySetId`. `BuildVm` returns an owned `NodeDataViewModelResult`; disposing it releases the generated tree. `ViewLocator` renders its `PropertySetViewModel` using `PropertySetView`.

`SourceFrom(key)` with no layout callback includes all supported public properties. With a callback, use `IncludeAll()` or explicitly include properties. `Hide()` excludes a property, including explicit inclusions. `IncludeProperty(...).Rename(...)` changes just that display instance. Nested `Include` groups use the same source; add another `SourceFrom` to compose independent models or distinct keys with the same model type. `Source(root => root.Add(PropertySet.From(key)...))` supports composing prebuilt layouts.

## Map a property

A synthetic property can adapt an existing field. Both transformations are required; `Set` receives the existing source value and must return the replacement source value.

```csharp
services.ConfigurePropertySet(Settings.Key, properties =>
    properties.Property<int?>("DoubleCount")
        .Uses(x => x.Count)
        .Get(count => count * 2)
        .Set((count, displayed) => displayed / 2)
        .Rename("Double count"));
```

Include synthetic properties by name: `layout.IncludeProperty("DoubleCount")`. Access expressions must point directly to a public readable/writable property. The registry editor's `DryRun` mapping shows how to adapt a compound configuration while preserving its other flags.

## Defaults and choices

`ConfigurePropertySetDefaults` supports `PropertiesWithType<TValue>` and `Parent<TParent>` rules. Type rules match assignable property types, and parent rules match assignable model types. Resolution applies reflected properties, type defaults, parent defaults, source-specific configuration, then display overrides. Rules within each level run in registration order. Nullable value types should be configured using their nullable type.

Enums get named choices automatically. `UseDefaultRegistry()` uses the registered `Registry<T>` (unwrapping nullable value types). `UseRegistry(options => ...)` creates an isolated choice catalog for that property, preserving its comparer. An unknown current value remains selectable rather than being silently replaced.

Nullable/reference properties provide a **Default** choice or an inheritance reset command. `NullValue(value)` supplies an explicit reset sentinel. Nullable booleans use the indeterminate checkbox state. Reset writes the null/sentinel into the selected editable source; the model's existing inheritance rules determine the effective value. A named zero enum value remains distinct from Default.

The built-in controls cover booleans, strings, numeric values, and enums or registered choices. Integer editors reject fractional values. Read-only layers disable controls and reject view-model writes.

## Custom editors and views

`UseVm(new ViewModelId<MyEditorViewModel>())` selects a concrete `IPropertyEditorViewModel`, constructed with the active editor's service provider. Implement `INotifyPropertyChanged`, `Update(PropertyEditorContext)`, and `Dispose()`. `Update` refreshes the display without writing. User edits call `context.SetValue`, which enforces source editability. The context exposes the effective parent, effective property value, and `IsEditable`.

Use `UseView(new ViewId<MyEditorView>())` to choose a concrete Avalonia control. Without an explicit view, custom view models use the existing `FooViewModel` → `FooView` naming convention in the view model's assembly. Each view instance is constructed through dependency injection. A property view receives the custom editor view model as its data context, or `PropertyValueViewModel` when no custom editor is selected. A group view receives `PropertySetGroupViewModel` with its child `Items` and flattened `Properties`.

`CredentialsPropertyViewModel` and `CredentialsPropertyView` are a working example registered as a type default. Reads never manufacture local credentials, and writes replace the credential source through the context instead of mutating an inherited object.

## Validation and lifetime

`CreateFactory()` compiles an immutable layout snapshot and rejects missing properties, incomplete mappings, unsupported automatic editors, and invalid view/view-model types. Later builder mutations do not change an existing factory. Generated view models refresh after node-data changes, dispose custom editors exactly once, and clean up partial initialization when an editor fails. Custom views are created per rendering instance rather than shared between Avalonia parents.

`PropertySetBuilderTests` verifies the generated controls, layer changes, mapping/reset behavior, multiple sources, configuration precedence, custom dependency injection, snapshots, and cleanup.
