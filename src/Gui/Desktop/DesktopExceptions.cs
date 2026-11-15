namespace Desktop;

public sealed class ViewModelsWithDisposePatternException(string viewModelNames)
    : InvalidOperationException(
        $"The following ViewModels implement a Dispose pattern, which is not allowed:\n  {viewModelNames}")
{
    public string ViewModelNames { get; } = viewModelNames;
}

public sealed class RootScopeDisposableServiceTypeException(Type serviceType)
    : InvalidOperationException($"Tried to instantiate a disposable service ({serviceType}) from the root scope.")
{
    public Type ServiceType { get; } = serviceType;
}

public sealed class RootScopeDisposableServiceInstanceException(Type serviceType)
    : InvalidOperationException($"Resolved a disposable service instance ({serviceType}) from the root scope.")
{
    public Type ServiceType { get; } = serviceType;
}

public sealed class AsyncDelegateExecutionException()
    : InvalidOperationException("Expected to be able to run the delegate synchronously.");

public sealed class BorrowedItemAlreadyInUseException()
    : InvalidOperationException("Cannot get this item because it is already in use.");

public sealed class ReturnedItemNotInUseException()
    : InvalidOperationException("Trying to return item that is not in use.");
