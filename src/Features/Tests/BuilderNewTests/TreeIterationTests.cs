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
        global::Anton.LayeredData.TreeEnumeration.Infrastructure.DfsEnumerable e = new(root);
        var states = CollectStates(e);

        // Assert
        Assert.Collection(states,
            s1 =>
            {
                global::Anton.LayeredData.Tests.VisitRecord expected2 = S(DfsVisitationState.BeforeProcess);
                global::Anton.LayeredData.Tests.VisitRecord actual2 = s1;
                Assert.Equal(expected: expected2, actual: actual2);
            },
            s2 =>
            {
                global::Anton.LayeredData.Tests.VisitRecord expected2 = S(DfsVisitationState.Process);
                global::Anton.LayeredData.Tests.VisitRecord actual2 = s2;
                Assert.Equal(expected: expected2, actual: actual2);
            },
            s3 =>
            {
                global::Anton.LayeredData.Tests.VisitRecord expected2 = S(DfsVisitationState.AfterProcess);
                global::Anton.LayeredData.Tests.VisitRecord actual2 = s3;
                Assert.Equal(expected: expected2, actual: actual2);
            });
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
        global::Anton.LayeredData.TreeEnumeration.Infrastructure.DfsEnumerable e = new(root);
        var states = CollectStates(e);

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
        VisitSequence.AssertInOrder(expected: expected, actual: states);
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
        global::Anton.LayeredData.TreeEnumeration.Infrastructure.DfsEnumerable e = new(root);
        var states = CollectStates(e);

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
        VisitSequence.AssertInOrder(expected: expected, actual: states);
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
            bool conditionResult;
            if (x.LayerName == "Root")
            {
                conditionResult = x.State == DfsVisitationState.Process;
            }
            else
            {
                conditionResult = false;
            }

            if (conditionResult)
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
        VisitSequence.AssertInOrder(expected: expected, actual: recorder.Records);
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
            bool conditionResult;
            if (x.LayerName == "Child1")
            {
                conditionResult = x.State == DfsVisitationState.BeforeProcess;
            }
            else
            {
                conditionResult = false;
            }

            if (conditionResult)
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
        VisitSequence.AssertInOrder(expected: expected, actual: recorder.Records);
    }

    [Fact]
    public void MultipleChildren_AllVisited()
    {
        // Arrange
        var children = Enumerable.Range(start: 1, count: 5)
            .Select(i =>
            {
                string CalculateName()
                {
                    string name = $"Child{i}";
                    return name;
                }

                return CreateLayer(CalculateName());
            })
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
        Assert.Equal(expected: 6, actual: visitedLayers.Count); // Root + 5 children
        for (int i = 1; i <= 5; i++)
        {
            string expected2 = $"Child{i}";
            global::System.Collections.Generic.HashSet<string> set = visitedLayers;
            Assert.Contains(expected2, set);
        }
    }

    [Fact]
    public void SkipCurrentChildren_WhenNoChildrenExist_CompletesNormally()
    {
        // Arrange
        var leaf = CreateLayer("Leaf");
        global::Anton.LayeredData.TreeEnumeration.Infrastructure.EnumerationContextCollection CalculateEnumerationContextCollection()
        {
            global::Anton.LayeredData.TreeEnumeration.Infrastructure.EnumerationContextCollection enumerationContextCollection = new([]);
            return enumerationContextCollection;
        }

        using var enumerator = new DfsEnumerator(leaf, CalculateEnumerationContextCollection());

        // Act
        enumerator.MoveNext(); // BeforeProcess
        enumerator.MoveNext(); // Process
        var result = enumerator.SkipCurrentChildren();

        // Assert
        Assert.True(result);
        Assert.Equal(expected: DfsVisitationState.AfterProcess, actual: enumerator.Current.State);
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
    extension(in DfsEnumerationContext val)
    {
        public string LayerName => val.Node.Layer.Value;
    }
    extension(DfsEnumerable c)
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
        global::System.Collections.Generic.List<global::Anton.LayeredData.Tests.VisitRecord> list = Records;
        global::Anton.LayeredData.Tests.VisitRecord item = new(v.LayerName, v.State);
        list.Add(item);
    }
}

file static class VisitSequence
{
    // The enumerator reports internal bookkeeping states to the contexts in
    // addition to the per-node visitation. The expected sequences only name
    // the per-node visits, so filter only the documented bookkeeping states
    // (BeforeChildren, ProcessChild, AfterChildren) and then require an exact
    // match, so prohibited child visits fail instead of being skipped.
    public static void AssertInOrder(
        IReadOnlyList<VisitRecord> expected,
        IReadOnlyList<VisitRecord> actual)
    {
        var filtered = new List<VisitRecord>(actual.Count);
        foreach (var record in actual)
        {
            if (record.State is DfsVisitationState.BeforeChildren
                or DfsVisitationState.ProcessChild
                or DfsVisitationState.AfterChildren)
            {
                continue;
            }
            filtered.Add(record);
        }
        if (filtered.Count != expected.Count)
        {
            Assert.Fail(
                (string) $"{($"The expected visits did not match exactly (expected {expected.Count}, recorded {filtered.Count}).")}{($" Expected: {string.Join(", ", expected)}.")}{($" Recorded sequence: {string.Join(", ", actual)}")}");
        }
        for (int i = 0; i < expected.Count; i++)
        {
            if (filtered[i] != expected[i])
            {
                Assert.Fail(
                    (string) $"{($"The expected visits did not match exactly.")}{($" First mismatch: [{i}] expected {expected[i]} but was {filtered[i]}.")}{($" Expected: {string.Join(", ", expected)}.")}{($" Recorded sequence: {string.Join(", ", actual)}")}");
            }
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
