using System.Collections.Immutable;
using Anton.LayeredConfig.TreeEnumeration.Infrastructure;

namespace Anton.LayeredConfig;

public sealed class ParentContext() : IDfsEnumerationContext
{
    public static readonly EnumerationContextKey<ParentContext> Key = EnumerationContextKey.Registry.Register<ParentContext>();
    private readonly Stack<MutableLayer> _stack = new();

    public void Update(DfsEnumerationContext context)
    {
        switch (context.State)
        {
            case DfsVisitationState.BeforeChildren:
            {
                _stack.Push(context.Layer);
                break;
            }
            case DfsVisitationState.AfterChildren:
            {
                _stack.Pop();
                break;
            }
        }
    }

    public MutableLayer? Parent => _stack.Count > 0
        ? _stack.Peek()
        : null;
}

public static class ParentEnumerationExtensions
{
    public static DfsEnumerable AddParent(this DfsEnumerable e)
    {
        e.AddContext(ParentContext.Key, () => new());
        return e;
    }
}
