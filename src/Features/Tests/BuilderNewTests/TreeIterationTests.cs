namespace Anton.LayeredConfig.Tests;

public sealed class TreeIterationTests
{
    private sealed class RecorderConsumer : ILayerStateEnumerationConsumer
    {
        public readonly List<VisitRecord> Records = new();

        public void Consume(LayerStateEnumerator.Value v)
        {
            Records.Add(new(v.LayerName, v.State));
        }
    }

    [Fact]
    public void MoveNext_SingleLayer_VisitsAllStates()
    {
        // Arrange
        var root = CreateLayer("Root");

        // Act
        var states = CollectStates(new(root));

        // Assert
        Assert.Collection(states,
            s1 => Assert.Equal(S(VisitorState.BeforeProcess), s1),
            s2 => Assert.Equal(S(VisitorState.Process), s2),
            s3 => Assert.Equal(S(VisitorState.AfterProcess), s3));
        VisitRecord S(VisitorState state) => new("Root", state);
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
            new("Root", VisitorState.BeforeProcess),
            new("Root", VisitorState.Process),
            new("Child1", VisitorState.BeforeProcess),
            new("Child1", VisitorState.Process),
            new("Child1", VisitorState.AfterProcess),
            new("Child2", VisitorState.BeforeProcess),
            new("Child2", VisitorState.Process),
            new("Child2", VisitorState.AfterProcess),
            new("Root", VisitorState.AfterProcess),
        };
        Assert.Equal(expected, states);
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
            new("Root", VisitorState.BeforeProcess),
            new("Root", VisitorState.Process),
            new("Child1", VisitorState.BeforeProcess),
            new("Child1", VisitorState.Process),
            new("GrandChild", VisitorState.BeforeProcess),
            new("GrandChild", VisitorState.Process),
            new("GrandChild", VisitorState.AfterProcess),
            new("Child1", VisitorState.AfterProcess),
            new("Child2", VisitorState.BeforeProcess),
            new("Child2", VisitorState.Process),
            new("Child2", VisitorState.AfterProcess),
            new("Root", VisitorState.AfterProcess),
        };
        Assert.Equal(expected, states);
    }

    [Fact]
    public void MoveNext_EmptyStack_ReturnsFalse()
    {
        // Arrange
        var root = CreateLayer("Root");
        var enumerator = new LayerStateEnumerator(root);

        // Act - Consume all items
        while (enumerator.MoveNext()) { }

        // Assert
        Assert.False(enumerator.MoveNext());
    }

    [Fact]
    public void PreventRecursionOnce_SkipsChildrenOnce()
    {
        // Arrange
        var child1 = CreateLayer("Child1");
        var child2 = CreateLayer("Child2");
        var root = CreateLayer("Root", child1, child2);

        var recorder = new RecorderConsumer();
        var enumerator = new LayerStateEnumerator(root)
            .WithConsumer(recorder);

        // Act
        while (enumerator.MoveNext())
        {
            var current = enumerator.Current;

            // Skip children when we process Root
            if (current.LayerName == "Root"
                && current.State == VisitorState.Process)
            {
                enumerator.Action = VisitorAction.PreventRecursionOnce;
            }
        }

        // Assert - Should skip to AfterProcess without visiting children
        var expected = new VisitRecord[]
        {
            new("Root", VisitorState.BeforeProcess),
            new("Root", VisitorState.Process),
            new("Root", VisitorState.AfterProcess),
        };
        Assert.Equal(expected, recorder.Records);
    }

    [Fact]
    public void KeepPreventingRecursion_SkipsAllSubsequentChildren()
    {
        // Arrange
        var root = CreateLayer("Root",
            CreateLayer("Child1",
                CreateLayer("GrandChild")),
            CreateLayer("Child2"));

        var recorder = new RecorderConsumer();
        var enumerator = new LayerStateEnumerator(root)
            .WithConsumer(recorder);

        // Act
        while (enumerator.MoveNext())
        {
            var current = enumerator.Current;
            if (current.LayerName == "Child1" && current.State == VisitorState.BeforeProcess)
            {
                enumerator.Action = VisitorAction.KeepPreventingRecursion;
            }
        }

        // Assert
        var expected = new VisitRecord[]
        {
            new("Root", VisitorState.BeforeProcess),
            new("Root", VisitorState.Process),
            new("Child1", VisitorState.BeforeProcess),
            new("Root", VisitorState.AfterProcess),
        };
        Assert.Equal(expected, recorder.Records);
    }

    [Fact]
    public void SkipCurrentChildren_SkipsChildrenAndReturnsToAfterProcess()
    {
        // Arrange
        var grandChild = CreateLayer("GrandChild");
        var child1 = CreateLayer("Child1", grandChild);
        var child2 = CreateLayer("Child2");
        var root = CreateLayer("Root", child1, child2);
        var recorder = new RecorderConsumer();
        var enumerator = new LayerStateEnumerator(root)
            .WithConsumer(recorder);

        // Act
        while (enumerator.MoveNext())
        {
            var current = enumerator.Current;

            // Skip children of Child1
            if (current.LayerName == "Child1" && current.State == VisitorState.Process)
            {
                enumerator.SkipCurrentChildren();
            }
        }

        // Assert
        var expected = new VisitRecord[]
        {
            new("Root", VisitorState.BeforeProcess),
            new("Root", VisitorState.Process),
            new("Child1", VisitorState.BeforeProcess),
            new("Child1", VisitorState.Process),
            new("Child1", VisitorState.AfterProcess),
            new("Child2", VisitorState.BeforeProcess),
            new("Child2", VisitorState.Process),
            new("Child2", VisitorState.AfterProcess),
            new("Root", VisitorState.AfterProcess),
        };
        Assert.Equal(expected, recorder.Records);

        // Verify GrandChild was not visited
        var layerNames = recorder.Records.Select(s => s.LayerName).ToList();
        Assert.DoesNotContain("GrandChild", layerNames);
    }

    [Fact]
    public void SkipCurrentChildren_AtRoot_SkipsAllChildren()
    {
        // Arrange
        var child1 = CreateLayer("Child1");
        var child2 = CreateLayer("Child2");
        var root = CreateLayer("Root", child1, child2);
        var recorder = new RecorderConsumer();
        var enumerator = new LayerStateEnumerator(root)
            .WithConsumer(recorder);

        // Act
        while (enumerator.MoveNext())
        {
            var current = enumerator.Current;

            if (current.LayerName == "Root" && current.State == VisitorState.Process)
            {
                enumerator.SkipCurrentChildren();
            }
        }

        // Assert
        var expected = new VisitRecord[]
        {
            new("Root", VisitorState.BeforeProcess),
            new("Root", VisitorState.Process),
            new("Root", VisitorState.AfterProcess),
        };
        Assert.Equal(expected, recorder.Records);
    }

    [Fact]
    public void Current_InitialState_HasStartState()
    {
        // Arrange
        var root = CreateLayer("Root");
        var enumerator = new LayerStateEnumerator(root);

        // Assert
        Assert.Equal(VisitorState.Start, enumerator.Current.State);
    }

    [Fact]
    public void Current_AfterMoveNext_ReflectsCurrentFrame()
    {
        // Arrange
        var root = CreateLayer("Root");
        var enumerator = new LayerStateEnumerator(root);

        // Act
        enumerator.MoveNext();
        var current = enumerator.Current;

        // Assert
        Assert.Equal("Root", current.LayerName);
        Assert.Equal(VisitorState.BeforeProcess, current.State);
    }

    [Fact]
    public void MultipleChildren_AllVisited()
    {
        // Arrange
        var children = Enumerable.Range(1, 5)
            .Select(i => CreateLayer($"Child{i}"))
            .ToArray();
        var root = CreateLayer("Root", children);
        var enumerator = new LayerStateEnumerator(root);

        // Act
        var visitedLayers = new HashSet<string>();
        while (enumerator.MoveNext())
        {
            var current = enumerator.Current;
            visitedLayers.Add(current.LayerName);
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
        var enumerator = new LayerStateEnumerator(leaf);

        // Act
        enumerator.MoveNext(); // BeforeProcess
        enumerator.MoveNext(); // Process
        var result = enumerator.SkipCurrentChildren();

        // Assert
        Assert.True(result);
        Assert.Equal(VisitorState.AfterProcess, enumerator.Current.State);
    }

    private MutableLayer CreateLayer(string name, params ReadOnlySpan<MutableLayer> children)
    {
        var layer = new MutableLayer { Name = new(name) };
        foreach (var child in children)
        {
            layer._childLayers.Add(new(child));
        }
        return layer;
    }

    private List<VisitRecord> CollectStates(LayerStateEnumerator enumerator)
    {
        var recorder = new RecorderConsumer();
        var wrapped = enumerator.WithConsumer(recorder);
        while (wrapped.MoveNext())
        {
        }
        return recorder.Records;
    }

    private readonly record struct VisitRecord(string LayerName, VisitorState State);
}

file static class Helper
{
    extension (LayerStateEnumerator.Value val)
    {
        public string LayerName => val.Layer.Name.Value;
    }
}
