using Anton.LayeredData.TreeEnumeration.Infrastructure;

namespace Anton.LayeredData;

public sealed class ParentContext() : IDfsEnumerationContext
{
    public static readonly EnumerationContextKey<ParentContext> Key = EnumerationContextKey.Registry.Register<ParentContext>();
    private readonly Stack<MutableNode> _stack = new();

    public void Update(DfsEnumerationContext context)
    {
        switch (context.State)
        {
            case DfsVisitationState.BeforeChildren:
            {
                _stack.Push(context.Node);
                break;
            }
            case DfsVisitationState.AfterChildren:
            {
                _stack.Pop();
                break;
            }
        }
    }

    public MutableNode? Parent => _stack.Count > 0
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
