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
        var iDispatcher = _dispatcher;
        PostArgs<PropertyChangedEventArgs> args = new(id,
            x => base.OnPropertyChanged(x),
            e);
        iDispatcher.Post(args);
    }

    protected override void OnPropertyChanging(PropertyChangingEventArgs e)
    {
        var id = new CallerIdentity(this);
        var iDispatcher = _dispatcher;
        PostArgs<PropertyChangingEventArgs> args = new(id,
            x => base.OnPropertyChanging(x),
            e);
        iDispatcher.Post(args);
    }
}
