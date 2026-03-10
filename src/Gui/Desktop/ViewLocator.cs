using System.Diagnostics.CodeAnalysis;
using AutoConstructor.Attributes;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using CommunityToolkit.Mvvm.ComponentModel;
using Desktop.MvvmEssentials;
using Desktop.NodeData.Common;
using Desktop.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Desktop;

/// <summary>
/// Given a view model, returns the corresponding view if possible.
/// </summary>
[RequiresUnreferencedCode(
    "Default implementation of ViewLocator involves reflection which may be trimmed away.",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
[AutoConstructor]
public sealed partial class ViewLocator : IDataTemplate
{
    private readonly IServiceProvider _sp;

    public Control? Build(object? param)
    {
        if (param is null)
        {
            return null;
        }

        var t = param.GetType();
        if (t.IsAssignableTo(typeof(INodeDataVmHost)))
        {
            return _sp.GetRequiredService<NodeDataVmHostView>();
        }

        var viewType = ViewAndViewModelConverter.TypeFromViewModelToView(t);
        if (viewType != null)
        {
            if (_sp.GetService(viewType) is { } s)
            {
                return (Control?) s;
            }
            return new TextBlock
            {
                Text = $"Service not registered for type {viewType}",
            };
        }

        return new TextBlock
        {
            Text = $"Not found view for type: {param.GetType()}",
        };
    }

    public bool Match(object? data)
    {
        return data is ObservableObject;
    }
}

public static class ViewAndViewModelConverter
{
    public static Type? TypeFromViewModelToView(Type viewModel)
    {
        return ConvertTypeByReplacingName(
            viewModel,
            remove: "ViewModel", // "Model"
            add: "View"); // ""
    }

    public static Type? TypeFromViewToViewModel<T>() where T : Control
    {
        return TypeFromViewToViewModel(typeof(T));
    }

    public static Type? TypeFromViewToViewModel(Type view)
    {
        return ConvertTypeByReplacingName(
            view,
            remove: "View", // ""
            add: "ViewModel"); // "Model"
    }

    public static Type? TypeFromViewModelToView<T>() where T : ViewModelBase
    {
        return TypeFromViewModelToView(typeof(T));
    }

    private static Type? ConvertTypeByReplacingName(Type type, string remove, string add)
    {
        var a = type;
        var aName = a.FullName!;
        var bName = aName.Replace(remove, add);
        // if (!aName.EndsWith(remove, StringComparison.Ordinal))
        // {
        //     return null;
        // }
        // var bName = $"{aName[.. ^remove.Length]}{add}";
        var bType = Type.GetType(bName);
        return bType;
    }
}
