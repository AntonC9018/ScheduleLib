namespace Anton.LayeredData.TreeEnumeration.Infrastructure;

public static class DfsEnumeratorExtensions
{
    extension<T>(T e) where T : IDfsEnumerator
    {
        public bool SkipCurrentChildren()
        {
            // Maybe do this better.
            e.Action = DfsAction.KeepPreventingRecursion;
            while (e.MoveNext())
            {
                if (e.Current.State == DfsVisitationState.AfterProcess)
                {
                    e.Action = DfsAction.Recurse;
                    return true;
                }
            }
            return false;
        }
    }
}
