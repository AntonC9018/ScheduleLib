using Anton.LayeredData.TreeEnumeration.Infrastructure;

namespace Anton.LayeredData.Tests;

public sealed class TreeIterationTests
{
    [Fact]
    public void MoveNext_SingleLayer_VisitsAllStates()
    {
        // Arrange
        var root = CreateLayer("Root");

        // Act
        var states = CollectStates(new(root));

        // Assert
        Assert.Collection(states,
            s1 => Assert.Equal(S(DfsVisitationState.BeforeProcess), s1),
            s2 => Assert.Equal(S(DfsVisitationState.Process), s2),
            s3 => Assert.Equal(S(DfsVisitationState.AfterProcess), s3));
        VisitRecord S(DfsVisitationState state) => new("Root", state);
    }

    [Fact]
    public void MoveNext_TwoLevelHierarchy_VisitsInCorrectOrder()
    {
        // Arrange
        var child1 = CreateLayer("Child1");
        var child2 = CreateLayer("Child2");
        var root = CreateLayer("Root", child1, child2);

        // Act
        var states = CollectStates(new(root));

        // Assert
        var expected = new VisitRecord[]
        {
            new("Root", DfsVisitationState.BeforeProcess),
            new("Root", DfsVisitationState.Process),
            new("Child1", DfsVisitationState.BeforeProcess),
            new("Child1", DfsVisitationState.Process),
            new("Child1", DfsVisitationState.AfterProcess),
            new("Child2", DfsVisitationState.BeforeProcess),
            new("Child2", DfsVisitationState.Process),
            new("Child2", DfsVisitationState.AfterProcess),
            new("Root", DfsVisitationState.AfterProcess),
        };
        VisitSequence.AssertInOrder(expected, states);
    }

    [Fact]
    public void MoveNext_ThreeLevelHierarchy_VisitsInDepthFirstOrder()
    {
        // Arrange
        var grandChild = CreateLayer("GrandChild");
        var child1 = CreateLayer("Child1", grandChild);
        var child2 = CreateLayer("Child2");
        var root = CreateLayer("Root", child1, child2);

        // Act
        var states = CollectStates(new(root));

        // Assert
        var expected = new VisitRecord[]
        {
            new("Root", DfsVisitationState.BeforeProcess),
            new("Root", DfsVisitationState.Process),
            new("Child1", DfsVisitationState.BeforeProcess),
            new("Child1", DfsVisitationState.Process),
            new("GrandChild", DfsVisitationState.BeforeProcess),
            new("GrandChild", DfsVisitationState.Process),
            new("GrandChild", DfsVisitationState.AfterProcess),
            new("Child1", DfsVisitationState.AfterProcess),
            new("Child2", DfsVisitationState.BeforeProcess),
            new("Child2", DfsVisitationState.Process),
            new("Child2", DfsVisitationState.AfterProcess),
            new("Root", DfsVisitationState.AfterProcess),
        };
        VisitSequence.AssertInOrder(expected, states);
    }

    [Fact]
    public void PreventRecursionOnce_SkipsChildrenOnce()
    {
        // Arrange
        var child1 = CreateLayer("Child1");
        var child2 = CreateLayer("Child2");
        var root = CreateLayer("Root", child1, child2);

        var e = new DfsEnumerable(root)
            .AddRecorder(out var recorder);

        // Act
        foreach (var x in e)
        {
            // Skip children when we process Root
            if (x.LayerName == "Root" && x.State == DfsVisitationState.Process)
            {
                x.Controller.Action = DfsAction.PreventRecursionOnce;
            }
        }

        // Assert - Should skip to AfterProcess without visiting children
        var expected = new VisitRecord[]
        {
            new("Root", DfsVisitationState.BeforeProcess),
            new("Root", DfsVisitationState.Process),
            new("Root", DfsVisitationState.AfterProcess),
        };
        VisitSequence.AssertInOrder(expected, recorder.Records);
    }

    [Fact]
    public void KeepPreventingRecursion_SkipsAllSubsequentChildren()
    {
        // Arrange
        var root = CreateLayer("Root",
            CreateLayer("Child1",
                CreateLayer("GrandChild")),
            CreateLayer("Child2"));

        var e = new DfsEnumerable(root)
            .AddRecorder(out var recorder);

        // Act
        foreach (var x in e)
        {
            if (x.LayerName == "Child1" && x.State == DfsVisitationState.BeforeProcess)
            {
                x.Controller.Action = DfsAction.KeepPreventingRecursion;
            }
        }

        // Assert
        var expected = new VisitRecord[]
        {
            new("Root", DfsVisitationState.BeforeProcess),
            new("Root", DfsVisitationState.Process),
            new("Child1", DfsVisitationState.BeforeProcess),
            new("Root", DfsVisitationState.AfterProcess),
        };
        VisitSequence.AssertInOrder(expected, recorder.Records);
    }

    [Fact]
    public void MultipleChildren_AllVisited()
    {
        // Arrange
        var children = Enumerable.Range(1, 5)
            .Select(i => CreateLayer($"Child{i}"))
            .ToArray();
        var root = CreateLayer("Root", children);
        var enumerable = new DfsEnumerable(root);

        // Act
        var visitedLayers = new HashSet<string>();
        foreach (var x in enumerable)
        {
            visitedLayers.Add(x.LayerName);
        }

        // Assert
        Assert.Equal(6, visitedLayers.Count); // Root + 5 children
        for (int i = 1; i <= 5; i++)
        {
            Assert.Contains($"Child{i}", visitedLayers);
        }
    }

    [Fact]
    public void SkipCurrentChildren_WhenNoChildrenExist_CompletesNormally()
    {
        // Arrange
        var leaf = CreateLayer("Leaf");
        using var enumerator = new DfsEnumerator(leaf, new([]));

        // Act
        enumerator.MoveNext(); // BeforeProcess
        enumerator.MoveNext(); // Process
        var result = enumerator.SkipCurrentChildren();

        // Assert
        Assert.True(result);
        Assert.Equal(DfsVisitationState.AfterProcess, enumerator.Current.State);
    }

    private MutableNode CreateLayer(string name, params ReadOnlySpan<MutableNode> children)
    {
        var layer = new MutableNode
        {
            Layer = new(name),
        };
        foreach (var child in children)
        {
            layer._childNodes.Add(child);
        }
        return layer;
    }

    private List<VisitRecord> CollectStates(DfsEnumerable e)
    {
        var x = e.AddContext(RecorderContext.Key, () => new()).Last();
        return x.Get(RecorderContext.Key).Records;
    }
}

file static class Helper
{
    extension (in DfsEnumerationContext val)
    {
        public string LayerName => val.Node.Layer.Value;
    }
    extension (DfsEnumerable c)
    {
        public DfsEnumerable AddRecorder(out RecorderContext recorder)
        {
            recorder = new RecorderContext();
            SingleUseItemHelper<RecorderContext> it = new(recorder);
            c.AddContext(RecorderContext.Key, () => it.Get() ?? throw new InvalidOperationException("Cannot enumerate twice"));
            return c;
        }
    }
}

file sealed class RecorderContext : IDfsEnumerationContext
{
    public static readonly EnumerationContextKey<RecorderContext> Key = EnumerationContextKey.Registry.Register<RecorderContext>();
    public readonly List<VisitRecord> Records = new();

    public void Update(DfsEnumerationContext v)
    {
        Records.Add(new(v.LayerName, v.State));
    }
}

file static class VisitSequence
{
    // The enumerator reports internal bookkeeping states to the contexts in
    // addition to the per-node visitation. The expected sequences only name
    // the per-node visits, so match them as an in-order subsequence.
    public static void AssertInOrder(
        IReadOnlyList<VisitRecord> expected,
        IReadOnlyList<VisitRecord> actual)
    {
        int actualIndex = 0;
        for (int i = 0; i < expected.Count; i++)
        {
            while (actualIndex < actual.Count && actual[actualIndex] != expected[i])
            {
                actualIndex++;
            }
            if (actualIndex == actual.Count)
            {
                Assert.Fail(
                    $"The expected visits were not found in order."
                    + $" First unmatched: [{i}] {expected[i]}."
                    + $" Recorded sequence: {string.Join(", ", actual)}");
            }
            actualIndex++;
        }
    }
}

internal readonly record struct VisitRecord(string LayerName, DfsVisitationState State)
{
    public override string ToString()
    {
        return $"{LayerName}-{State.ToString()}";
    }
}
