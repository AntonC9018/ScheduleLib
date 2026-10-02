using Anton.LayeredData;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using Desktop.NodeData.Common;
using Desktop.NodeData.Editor;
using Desktop.NodeData.Features.Registry;
using Desktop.ViewModelData;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Scraping.Common.Config;

namespace Desktop.Tests;

public sealed class PropertySetBuilderTests
{
    private static readonly PropertySetId EditorKey = new("Builder tests");
    private static Context Create(Action<IServiceCollection>? configure = null) => Context.Create(services =>
    {
        services.RegisterBasicOperationsAndMergers<BuilderModel>();
        configure?.Invoke(services);
    });
    private static OwnedViewModel Resolve(Context c) => c.ServiceProvider.GetRequiredService<NodeDataViewModelResolver>().Resolve(EditorKey, c.Data);
    private static PropertyValueViewModel Property(PropertySetViewModel vm, string name) => vm.Properties.Single(x => x.Id.Name == name);
    private static void EnableEditing(Context c)
    {
        c.Main.LayerLevelSelection.LayerLevel = LayerLevel.ProgrammableUser;
        c.Main.NodeSelection.Model.SelectedNode = c.Main.NodeSelection.Model.AllNodes.First(x => !x.IsNull);
        c.Main.EnableSelectedUser();
    }

    [AvaloniaFact]
    public async Task DefaultsApplyInOrderAndDisplayOverridesAreLocal()
    {
        await using var c = Create(services =>
        {
            services.ConfigurePropertySetDefaults(d =>
            {
                d.PropertiesWithType<string>(p => p.Rename("Type default"));
                d.Parent<IBuilderIdentity>(p =>
                {
                    p.Property(x => x.Name).Rename("Parent default");
                    p.Property(x => x.Identity).Rename("Parent default");
                });
            });
            services.ConfigurePropertySet(BuilderModel.Key, b => b.Property(x => x.Name).Rename("Source name"));
            services.AddPropertySetDisplayFactory(EditorKey, b => b.SourceFrom(BuilderModel.Key, layout =>
            {
                layout.IncludeProperty(x => x.Name).Rename("Display name");
                layout.IncludeProperty(x => x.Identity);
                layout.IncludeProperty(x => x.OtherName);
            }));
            services.AddPropertySetDisplayFactory(new("Unmodified layout"), b => b.SourceFrom(BuilderModel.Key));
        });
        using var owned = Resolve(c);
        var vm = Assert.IsType<PropertySetViewModel>(owned.Value);
        Assert.Equal("Display name", Property(vm, "Name").Name);
        Assert.Equal("Parent default", Property(vm, "Identity").Name);
        Assert.Equal("Type default", Property(vm, "OtherName").Name);
        using var other = c.ServiceProvider.GetRequiredService<NodeDataViewModelResolver>().Resolve(new("Unmodified layout"), c.Data);
        Assert.Equal("Source name", Property(Assert.IsType<PropertySetViewModel>(other.Value), "Name").Name);
    }

    [AvaloniaFact]
    public async Task IncludeAllAndMappedPropertiesReadWriteAndRefreshTogether()
    {
        await using var c = Create(services =>
        {
            services.ConfigurePropertySet(BuilderModel.Key, b =>
            {
                b.Property<int?>("DoubleCount").Uses(x => x.Count).Get(x => x * 2).Set((_, value) => value / 2);
                b.Property(x => x.Identity).Hide();
            });
            services.AddPropertySetDisplayFactory(EditorKey, b => b.SourceFrom(BuilderModel.Key));
        });
        using var owned = Resolve(c);
        var vm = Assert.IsType<PropertySetViewModel>(owned.Value);
        var doubled = Property(vm, "DoubleCount");
        Assert.DoesNotContain(vm.Properties, x => x.Id.Name == "Identity");
        Assert.Throws<InvalidOperationException>(() => doubled.Value = 10);
        EnableEditing(c);
        doubled.NumberValue = 10;
        Assert.Equal(5, Property(vm, "Count").Value);
        Property(vm, "Count").NumberValue = 7;
        Assert.Equal(14, doubled.Value);
        doubled.ResetCommand.Execute(null);
        Assert.Null(Property(vm, "Count").Value);
    }

    [AvaloniaFact]
    public async Task NullableEnumAndInlineRegistriesDistinguishDefaultFromZero()
    {
        await using var c = Create(services =>
        {
            services.ConfigurePropertySet(BuilderModel.Key, b => b.Property(x => x.Mode).UseRegistry(opts =>
            {
                opts.Add("First", BuilderMode.First);
                opts.Add("Second", BuilderMode.Second);
            }));
            services.AddPropertySetDisplayFactory(EditorKey, b => b.SourceFrom(BuilderModel.Key));
        });
        using var owned = Resolve(c);
        EnableEditing(c);
        var mode = Property(Assert.IsType<PropertySetViewModel>(owned.Value), "Mode");
        Assert.True(mode.SelectedChoice!.IsDefault);
        mode.SelectedChoice = mode.Choices.Single(x => x.Name == "First");
        Assert.Equal(BuilderMode.First, mode.Value);
        Assert.False(mode.SelectedChoice!.IsDefault);
        mode.SelectedChoice = mode.Choices.Single(x => x.IsDefault);
        Assert.Null(mode.Value);
    }

    [AvaloniaFact]
    public async Task UnregisteredCurrentValueIsPreservedInAChoiceEditor()
    {
        await using var c = Create(services =>
        {
            services.ConfigurePropertySet(BuilderModel.Key, b => b.Property(x => x.Mode).UseRegistry(opts => opts.Add("First", BuilderMode.First)));
            services.AddPropertySetDisplayFactory(EditorKey, b => b.SourceFrom(BuilderModel.Key));
        });
        using var owned = Resolve(c);
        EnableEditing(c);
        var mode = Property(Assert.IsType<PropertySetViewModel>(owned.Value), "Mode");
        mode.Value = BuilderMode.Second;
        Assert.Equal(BuilderMode.Second, mode.SelectedChoice?.Value);
        Assert.Contains(mode.SelectedChoice, mode.Choices);
        Assert.Equal(BuilderMode.Second, mode.Value);
    }

    [AvaloniaFact]
    public async Task MultipleSourcesAndGroupsKeepTheirKeysAndSelection()
    {
        var secondKey = new NodeDataKey<BuilderModel>(new("Second builder source"));
        await using var c = Create(services => services.AddPropertySetDisplayFactory(EditorKey, b => b.Source(root =>
        {
            root.From(BuilderModel.Key).Title("First").Include(g => g.Title("Names").Property(x => x.Name));
            root.From(secondKey).Title("Second").IncludeProperty(x => x.Name);
        })));
        using var owned = Resolve(c);
        var vm = Assert.IsType<PropertySetViewModel>(owned.Value);
        EnableEditing(c);
        var groups = vm.Items.Cast<PropertySetGroupViewModel>().ToArray();
        Assert.Equal(["First", "Second"], groups.Select(x => x.Name));
        var first = groups[0].Properties.Single();
        var second = groups[1].Properties.Single();
        first.TextValue = "First source";
        second.TextValue = "Second source";
        Assert.Equal("First source", first.Value);
        Assert.Equal("Second source", second.Value);
        c.Main.LayerLevelSelection.LayerLevel = LayerLevel.Default;
        Assert.False(vm.IsEditable);
        Assert.Null(first.Value);
        c.Main.LayerLevelSelection.LayerLevel = LayerLevel.UiUser;
        Assert.Equal("First source", first.Value);
        Assert.Equal("Second source", second.Value);
    }

    [AvaloniaFact]
    public async Task CustomEditorsAndViewsUseDiAndAreDisposedOnce()
    {
        await using var c = Create(services =>
        {
            services.AddSingleton<EditorDependency>();
            services.ConfigurePropertySet(BuilderModel.Key, b => b.Property(x => x.Name)
                .UseVm(new ViewModelId<TrackingEditor>()).UseView(new ViewId<TrackingView>()));
            services.AddPropertySetDisplayFactory(EditorKey, b => b.SourceFrom(BuilderModel.Key, layout => layout.IncludeProperty(x => x.Name)));
        });
        using var owned = Resolve(c);
        var vm = Assert.IsType<PropertySetViewModel>(owned.Value);
        var editor = Assert.IsType<TrackingEditor>(vm.Properties.Single().Editor);
        var dependency = c.ServiceProvider.GetRequiredService<EditorDependency>();
        Assert.Same(dependency, editor.Dependency);
        EnableEditing(c);
        editor.SetValue("Custom value");
        Assert.Equal("Custom value", vm.Properties.Single().Value);
        var view = new PropertySetView { DataContext = vm };
        var window = new Window { Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var custom = Assert.Single(window.GetVisualDescendants().OfType<TrackingView>());
            Assert.Same(editor, custom.DataContext);
            Assert.Same(dependency, custom.Dependency);
        }
        finally { window.Close(); }
        var updates = editor.Updates;
        vm.Dispose();
        vm.Dispose();
        c.Main.LayerLevelSelection.LayerLevel = LayerLevel.Default;
        Assert.Equal(updates, editor.Updates);
        Assert.Equal(1, editor.Disposals);
    }

    [AvaloniaFact]
    public async Task GeneratedAvaloniaControlsWriteThroughTheBuilder()
    {
        await using var c = Create(services => services.AddPropertySetDisplayFactory(EditorKey, b => b.SourceFrom(BuilderModel.Key, layout =>
        {
            layout.IncludeProperty(x => x.Enabled);
            layout.IncludeProperty(x => x.Count);
            layout.IncludeProperty(x => x.Name);
            layout.IncludeProperty(x => x.Mode);
        })));
        using var owned = Resolve(c);
        var vm = Assert.IsType<PropertySetViewModel>(owned.Value);
        EnableEditing(c);
        var window = new Window { Content = new PropertySetView { DataContext = vm } };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Single(window.GetVisualDescendants().OfType<CheckBox>()).IsChecked = true;
            Assert.Single(window.GetVisualDescendants().OfType<NumericUpDown>()).Value = 9;
            window.GetVisualDescendants().OfType<TextBox>().Single(x => x.DataContext is PropertyValueViewModel { Id.Name: "Name" }).Text = "Ada";
            var combo = Assert.Single(window.GetVisualDescendants().OfType<ComboBox>());
            combo.SelectedItem = Property(vm, "Mode").Choices.Single(x => Equals(x.Value, BuilderMode.Second));
            Assert.Equal(true, Property(vm, "Enabled").Value);
            Assert.Equal(9, Property(vm, "Count").Value);
            Assert.Equal("Ada", Property(vm, "Name").Value);
            Assert.Equal(BuilderMode.Second, Property(vm, "Mode").Value);
            Property(vm, "Count").NumberValue = 12;
            Assert.Equal(BuilderMode.Second, Property(vm, "Mode").Value);
            Property(vm, "Mode").Value = (BuilderMode)99;
            Assert.Equal((BuilderMode)99, ((PropertyChoice)combo.SelectedItem!).Value);
            combo.SelectedItem = Property(vm, "Mode").Choices.Single(x => x.IsDefault);
            Assert.Null(Property(vm, "Mode").Value);
            Assert.Throws<ArgumentException>(() => Property(vm, "Count").NumberValue = 1.5m);
            Assert.Equal(12, Property(vm, "Count").Value);
            c.Main.LayerLevelSelection.LayerLevel = LayerLevel.Default;
            Assert.False(Assert.Single(window.GetVisualDescendants().OfType<CheckBox>()).IsEffectivelyEnabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task CompiledFactoriesAreSnapshotsAndCustomGroupViewsReceiveTheGroup()
    {
        await using var c = Create(services => services.AddSingleton<EditorDependency>());
        var layout = PropertySet.From(BuilderModel.Key).Title("Original").UseView(new ViewId<TrackingView>());
        layout.IncludeProperty(x => x.Name);
        var builder = new PropertySetDisplayFactoryBuilder(c.ServiceProvider).Source(root => root.Add(layout));
        var factory = builder.CreateFactory();
        layout.Title("Changed after compilation").IncludeProperty(x => x.Count);
        using var built = factory.BuildVm(new(c.ServiceProvider, c.Data));
        var vm = Assert.IsType<PropertySetViewModel>(built.ViewModel);
        var group = Assert.IsType<PropertySetGroupViewModel>(Assert.Single(vm.Items));
        Assert.Equal("Original", group.Name);
        Assert.Single(group.Properties);
        var window = new Window { Content = new PropertySetView { DataContext = vm } };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var custom = Assert.Single(window.GetVisualDescendants().OfType<TrackingView>());
            Assert.Same(group, custom.DataContext);
            Assert.False(custom.IsEffectivelyEnabled);
            EnableEditing(c);
            Assert.True(custom.IsEffectivelyEnabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task FailedCustomEditorInitializationDisposesThePartialModel()
    {
        await using var c = Create(services =>
        {
            services.AddSingleton<EditorDependency>();
            services.ConfigurePropertySet(BuilderModel.Key, b =>
            {
                b.Property(x => x.Name).UseVm(new ViewModelId<TrackingEditor>()).UseView(new ViewId<TrackingView>());
                b.Property(x => x.OtherName).UseVm(new ViewModelId<ThrowingEditor>()).UseView(new ViewId<TrackingView>());
            });
            services.AddPropertySetDisplayFactory(EditorKey, b => b.SourceFrom(BuilderModel.Key));
        });
        Assert.Throws<InvalidOperationException>(() => Resolve(c));
        var dependency = c.ServiceProvider.GetRequiredService<EditorDependency>();
        var editor = Assert.Single(dependency.Editors);
        Assert.Equal(1, editor.Disposals);
        // Neither the failed root subscription nor the custom editor may refresh after cleanup.
        EnableEditing(c);
        Assert.Equal(1, editor.Updates);
    }

    [AvaloniaFact]
    public async Task CredentialsDefaultEditorWorksForAnyCredentialsProperty()
    {
        await using var c = Create(services => services.AddPropertySetDisplayFactory(EditorKey, b => b.SourceFrom(BuilderModel.Key)));
        using var owned = Resolve(c);
        var vm = Assert.IsType<PropertySetViewModel>(owned.Value);
        EnableEditing(c);
        var property = Property(vm, "Credentials");
        var credentials = Assert.IsType<CredentialsPropertyViewModel>(property.Editor);
        Assert.Null(property.Value);
        Assert.Equal("", credentials.Login);
        Assert.Null(property.Value);
        credentials.Login = "teacher";
        credentials.Password = "password";
        var source = Assert.IsType<ValueCredentialsSource>(property.Value);
        Assert.Equal("teacher", source.Value?.Login);
        Assert.Equal("password", source.Value?.Password);
        property.ResetCommand.Execute(null);
        Assert.Null(property.Value);
        Assert.Equal("", credentials.Login);
    }

    [AvaloniaFact]
    public async Task InvalidMappingsAndPropertyNamesFailWhenTheFactoryIsCompiled()
    {
        await using var c = Create(services => services.ConfigurePropertySet(BuilderModel.Key,
            b => b.Property<int?>("Broken").Uses(x => x.Count).Get(value => value)));
        var builder = new PropertySetDisplayFactoryBuilder(c.ServiceProvider).SourceFrom(BuilderModel.Key);
        Assert.Contains("Get and Set", Assert.Throws<ArgumentException>(() => builder.CreateFactory()).Message);
        builder = new PropertySetDisplayFactoryBuilder(c.ServiceProvider).SourceFrom(BuilderModel.Key, layout => layout.IncludeProperty("Unknown"));
        Assert.Contains("Unknown", Assert.Throws<ArgumentException>(() => builder.CreateFactory()).Message);
    }
}

public enum BuilderMode { First, Second }
public interface IBuilderIdentity
{
    string? Name { get; set; }
    string? Identity { get; set; }
}
public sealed class BuilderModel : IBuilderIdentity
{
    public static NodeDataKey<BuilderModel> Key { get; } = new(new("BuilderModel"));
    public string? Name { get; set; }
    public string? Identity { get; set; }
    public string? OtherName { get; set; }
    public bool? Enabled { get; set; }
    public int? Count { get; set; }
    public BuilderMode? Mode { get; set; }
    public CredentialsSource? Credentials { get; set; }
}
public sealed class EditorDependency
{
    public List<TrackingEditor> Editors { get; } = new();
}
public sealed class TrackingEditor : ObservableObject, IPropertyEditorViewModel
{
    private PropertyEditorContext? _context;
    public EditorDependency Dependency { get; }
    public TrackingEditor(EditorDependency dependency)
    {
        Dependency = dependency;
        dependency.Editors.Add(this);
    }
    public int Updates { get; private set; }
    public int Disposals { get; private set; }
    public void Update(PropertyEditorContext context) { _context = context; Updates++; }
    public void SetValue(object? value) => _context!.SetValue(value);
    public void Dispose() { _context = null; Disposals++; }
}
public sealed class TrackingView(EditorDependency dependency) : UserControl
{
    public EditorDependency Dependency { get; } = dependency;
}

public sealed class ThrowingEditor : ObservableObject, IPropertyEditorViewModel
{
    public void Update(PropertyEditorContext context) => throw new InvalidOperationException("Simulated custom editor failure.");
    public void Dispose() { }
}
