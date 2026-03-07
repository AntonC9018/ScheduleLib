using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Desktop.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    private readonly IDispatcher _dispatcher;

    protected ViewModelBase(IDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        _dispatcher.Post(this, x => base.OnPropertyChanged(x), e);
    }
    protected override void OnPropertyChanging(PropertyChangingEventArgs e)
    {
        _dispatcher.Post(this, x => base.OnPropertyChanging(x), e);
    }
}

