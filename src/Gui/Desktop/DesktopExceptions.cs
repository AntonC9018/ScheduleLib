namespace Desktop;

public sealed class ViewModelsWithDisposePatternException(string viewModelNames)
    : InvalidOperationException(
        $"The following ViewModels implement a Dispose pattern, which is not allowed:\n  {viewModelNames}")
{
    public string ViewModelNames { get; } = viewModelNames;
}
