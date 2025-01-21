using Argon;
using ScheduleLib;

namespace App.Tests;

public sealed class PermutationTests
{
    private readonly VerifySettings Settings = CreateSettings();

    private static VerifySettings CreateSettings()
    {
        var s = new VerifySettings();
        s.AddExtraSettings(json => json.DefaultValueHandling = DefaultValueHandling.Include);
        return s;
    }

    [Fact]
    public Task SwapTest()
    {
        int[] items = [1, 2, 3];
        var actionsEnumerator = new PermutationActionEnumerator(items.Length);

        List<SwapAction> actions = new();
        while (actionsEnumerator.MoveNext())
        {
            actions.Add(actionsEnumerator.Current);
        }
        return Verify(actions, Settings);
    }

    [Fact]
    public Task Test()
    {
        int[] items = [1, 2, 3];
        var permutationsEnumerable = PermutationHelper.Generate(items);
        var permutations = permutationsEnumerable.Select(x => x.ToArray()).ToArray();
        return Verify(permutations, Settings);
    }
}
