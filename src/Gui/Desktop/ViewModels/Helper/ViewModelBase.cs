using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Desktop.ViewModels;

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
        _dispatcher.Post<PropertyChangedEventArgs>(new(id, x => base.OnPropertyChanged(x), e));
    }
    protected override void OnPropertyChanging(PropertyChangingEventArgs e)
    {
        var id = new CallerIdentity(this);
        _dispatcher.Post<PropertyChangingEventArgs>(new(id, x => base.OnPropertyChanging(x), e));
    }
}

