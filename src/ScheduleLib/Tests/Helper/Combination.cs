namespace ScheduleLib.Tests;

public sealed class CombinationTests
{
    [Fact]
    public void Test()
    {
        int[] arr = [1, 2, 3];
        int[] output = new int[2];

        List<int[]> results = new();
        var resultsEnumerable = CombinationHelper.Generate(items: arr, resultMem: output);
        foreach (var r in resultsEnumerable)
        {
            results.Add(r.ToArray());
        }

        void Equal(int[] a, int[] b)
        {
            Assert.Equal(expected: a, actual: b);
        }

        Assert.Collection(
            results,
            x => Equal(a: x, b: [1, 2]),
            x => Equal(a: x, b: [1, 3]),
            x => Equal(a: x, b: [2, 3]));
    }
}
