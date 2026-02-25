using Anton.LayeredData.TreeEnumeration.Infrastructure;

namespace Anton.LayeredData.TreeEnumeration;

public static class DfsEnumerableExtensions
{
    extension(IEnumerable<DfsEnumerationContext> builder)
    {
        public IEnumerable<DfsEnumerationContext> SelectWithState(
            DfsVisitationState state)
        {
            foreach (var x in builder)
            {
                if (x.State == state)
                {
                    yield return x;
                }
            }
        }

        public IEnumerable<DfsEnumerationContext> SkipLayers(int count)
        {
            foreach (var x in builder)
            {
                if (count == 0)
                {
                    yield return x;
                }
                else if (x.State == DfsVisitationState.Process)
                {
                    count--;
                }
            }
        }
    }
}
