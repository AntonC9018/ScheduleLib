namespace App.Tests;

public sealed class CombinationTests
{
    [Fact]
    public void Test()
    {
        int[] arr = [1, 2, 3];
        int[] output = new int[2];

        List<int[]> results = new();
        var resultsEnumerable = CombinationHelper.Generate(arr, output);
        foreach (var r in resultsEnumerable)
        {
            results.Add(r.ToArray());
        }

        void Equal(int[] a, int[] b)
        {
            Assert.Equal(a, b);
        }

        Assert.Collection(results,
            x => Equal(x, [1, 2]),
            x => Equal(x, [1, 3]),
            x => Equal(x, [2, 3]));
    }
}
