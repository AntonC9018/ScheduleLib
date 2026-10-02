using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Microsoft.Extensions.DependencyInjection;

namespace Desktop.NodeData.Common;

/// <summary>Renders compiled property-set layouts without a configuration-specific XAML view.</summary>
public sealed class PropertySetView : UserControl
{
    public PropertySetView()
    {
        DataContextChanged += (_, _) => Content = DataContext is PropertySetViewModel vm
            ? BuildGroup(vm, vm.ServiceProvider) : null;
    }

    private static Control CustomView(Type type, object context, IServiceProvider sp)
    {
        var control = (Control)ActivatorUtilities.CreateInstance(sp, type);
        control.DataContext = context;
        return control;
    }

    private static Control BuildGroup(PropertySetGroupViewModel group, IServiceProvider sp)
    {
        if (group.ViewType is { } type)
        {
            var custom = CustomView(type, group, sp);
            custom.Bind(IsEnabledProperty, new Binding(nameof(group.IsEditable)) { Source = group });
            return custom;
        }
        var panel = new StackPanel { Spacing = 20 };
        if (!string.IsNullOrWhiteSpace(group.Name))
        {
            panel.Children.Add(new TextBlock { Text = group.Name, FontSize = 18, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        }
        foreach (var item in group.Items)
        {
            if (item is PropertySetGroupViewModel child)
            {
                if (panel.Children.Count != 0) panel.Children.Add(new Separator());
                panel.Children.Add(BuildGroup(child, sp));
            }
            else panel.Children.Add(BuildProperty((PropertyValueViewModel)item, sp));
        }
        return panel;
    }

    private static Control BuildProperty(PropertyValueViewModel property, IServiceProvider sp)
    {
        var panel = new StackPanel { Spacing = 6, DataContext = property };
        panel.Bind(IsEnabledProperty, new Binding(nameof(property.IsEditable)) { Source = property });
        var valueType = Nullable.GetUnderlyingType(property.ValueType) ?? property.ValueType;
        Control editor;
        if (property.ViewType is { } viewType) editor = CustomView(viewType, (object?)property.Editor ?? property, sp);
        else if (property.Editor is not null) editor = new ContentControl { Content = property.Editor };
        else if (property.HasChoices)
        {
            var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            combo.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(property.Choices)) { Source = property });
            combo.Bind(ComboBox.SelectedItemProperty, Binding(property, nameof(property.SelectedChoice)));
            editor = combo;
        }
        else if (valueType == typeof(bool))
        {
            var checkbox = new CheckBox { Content = property.Name, IsThreeState = Nullable.GetUnderlyingType(property.ValueType) is not null };
            checkbox.Bind(CheckBox.IsCheckedProperty, Binding(property, nameof(property.BooleanValue)));
            editor = checkbox;
        }
        else if (PropertyValueViewModel.IsNumber(valueType))
        {
            var numeric = new NumericUpDown { HorizontalAlignment = HorizontalAlignment.Stretch,
                FormatString = PropertyValueViewModel.IsInteger(valueType) ? "0" : "0.################" };
            numeric.Bind(NumericUpDown.ValueProperty, Binding(property, nameof(property.NumberValue)));
            editor = numeric;
        }
        else
        {
            var text = new TextBox();
            text.Bind(TextBox.TextProperty, Binding(property, nameof(property.TextValue)));
            editor = text;
        }
        if (editor is not CheckBox) panel.Children.Add(new TextBlock { Text = property.Name });
        panel.Children.Add(editor);
        if (!string.IsNullOrWhiteSpace(property.Description))
        {
            panel.Children.Add(new TextBlock { Text = property.Description, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Opacity = 0.65 });
        }
        // Choice editors already expose Default; nullable checkboxes expose the indeterminate state.
        if (property.CanReset && !property.HasChoices && editor is not CheckBox)
        {
            panel.Children.Add(new Button { Content = "Use inherited value", Command = property.ResetCommand });
        }
        return panel;
    }

    private static Binding Binding(object source, string path) => new(path)
    {
        Source = source,
        Mode = BindingMode.TwoWay,
        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
    };
}
