using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Desktop.MvvmEssentials;

public abstract class ViewModelBase : ObservableObject
{
    protected readonly IDispatcher _dispatcher;

    protected ViewModelBase(IDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        var id = new CallerIdentity(this);
        PostArgs<PropertyChangedEventArgs> args = new(
            id,
            x => base.OnPropertyChanged(x),
            e);
        _dispatcher.Post(args);
    }

    protected override void OnPropertyChanging(PropertyChangingEventArgs e)
    {
        var id = new CallerIdentity(this);
        PostArgs<PropertyChangingEventArgs> args = new(
            id,
            x => base.OnPropertyChanging(x),
            e);
        _dispatcher.Post(args);
    }
}
