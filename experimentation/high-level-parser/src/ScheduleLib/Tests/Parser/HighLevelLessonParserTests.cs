using System.Diagnostics;
using System.Runtime.InteropServices;
using ScheduleLib.Helper.Helper;
using ScheduleLib.Helper.Parsing;
using ScheduleLib.Parsing.Lesson;
using ScheduleLib.Parsing.Lesson.Internal;

namespace ScheduleLib.ParserTests;

using static LessonParserHelper;

public sealed class HighLevelLessonParserTests
{
    [Fact]
    public void Test1()
    {
        var builder = new Builder();
        builder.SubGroup("II");
        builder.LessonOrTeacherName("LessonA");
        builder.LessonModifiers(mods =>
        {
            mods.LessonType(LessonType.Lab);
            mods.Parity(Parity.EvenWeek);
        });
        builder.ItemSeparator();
        builder.LessonOrTeacherName("LessonB");
        builder.LessonModifiers(mods =>
        {
            mods.LessonType(LessonType.Curs);
            mods.Parity(Parity.OddWeek);
        });
        builder.LineSeparator();
        builder.LessonOrTeacherName("N. Nartea");
        builder.ItemSeparator();
        builder.Room("423/4");
        var result = builder.GetResult();
        Assert.Collection(result,
            l1 =>
            {
                Assert.Equal("LessonA", l1.LessonName.Span);
                Assert.Equal(LessonType.Lab, l1.LessonType);
                Assert.Equal(Parity.EvenWeek, l1.Parity);
                Assert.Equal("423/4", l1.RoomName.Span);
                AssertEqualName("N. Nartea", Assert.Single(l1.TeacherNames));
                Assert.Equal(new SubGroup("I"), l1.SubGroup);
            },
            l2 =>
            {
                Assert.Equal("LessonB", l2.LessonName.Span);
                Assert.Equal(LessonType.Curs, l2.LessonType);
                Assert.Equal(Parity.OddWeek, l2.Parity);
                Assert.Equal("423/4", l2.RoomName.Span);
                AssertEqualName("N. Nartea", Assert.Single(l2.TeacherNames));
                Assert.Equal(new SubGroup("I"), l2.SubGroup);
            });
    }
}


public enum NodeType
{
    // All tokens are first converted to this.
    RegularToken,
    // Full lesson name
    LessonName,
    // Basically any text
    LessonNameSegment,
    // The following forms: First, First Last, F. Last, F.-G. Last, Fi. Last etc. (first letter must be capitalized)
    TeacherName,
    // So the segment is any capitalized word, or short word, or F.-G. like this
    TeacherNameSegment,
    // sb1, s.b1, s b 1, I:, II:, III:, I., II., III. kind of like this
    // When I. might be part of teacher, see compound.
    SubGroup,
    // Any parenthesized group (...)
    Modifiers,
    // See special rules for rooms
    Room,
    // e.g. 9:45
    TimeSlot,
    // ,
    ItemSeparator,
    // \n
    LineSeparator,
    // has children and no other type
    Container,
}

public readonly record struct TokenRef(int Index);

public sealed class NodePayload
{
    public TokenRef Token;
    public ReadOnlyMemory<char> RelevantText;
    internal DefaultModifiersList Modifiers;
    public TimeOnly TimeSlot;
    public SubGroup SubGroup;

    public ReadOnlyMemory<char> RoomName
    {
        get => RelevantText;
        set => RelevantText = value;
    }
    public ReadOnlyMemory<char> TeacherName
    {
        get => RelevantText;
        set => RelevantText = value;
    }
    public ReadOnlyMemory<char> LessonName
    {
        get => RelevantText;
        set => RelevantText = value;
    }
}

public record struct InterpretationList
{
    public NodeRef Next;
    public NodeRef Prev;
}

public sealed class Node
{
    public List<NodeRef>? Parents;
    public NodeType Type;
    public InterpretationList InterpretationList;
    public List<NodeRef>? Children;
    public NodePayload? Payload;

    public static Node CreateRegular(TokenRef token)
    {
        return new()
        {
            Type = NodeType.RegularToken,
            Payload = new()
            {
                Token = token,
            },
        };
    }

    public static Node CreateContainer()
    {
        return new()
        {
            Type = NodeType.Container,
            Children = new(),
        };
    }
}

public readonly record struct MaybeNodeRef(NodeRef Value)
{
    public bool IsNull => Value._Value == null;
}

public readonly record struct NodeRef(Node _Value)
{
}

public struct GraphIteration()
{
    public List<Node> Nodes = new();

    public void Add(Node node)
    {
        Nodes.Add(node);
    }
}

public enum UpdateOperation
{
    None = -1,
    BranchedInterpretations,
    Merged,
    Replaced,
    Removed,
    RemovedBranch,
}

public struct GraphIterationBuilder
{
    private readonly ParsingContext _context;
    private readonly Node _container;
    private readonly int _offset;
    public UpdateOperation ExecutedUpdateOperation { get; private set; } = UpdateOperation.None;

    public GraphIterationBuilder(
        ParsingContext context,
        Node container,
        int offset)
    {
        _context = context;
        _container = container;
        _offset = offset;
    }

    public readonly ReadOnlySpan<NodeRef> CurrentNodes => CollectionsMarshal.AsSpan(_container.Children)[_offset ..];
    public readonly NodeRef CurrentNodeRef => CurrentNodes[_offset];
    public readonly Node CurrentNode => GetNode(CurrentNodes[_offset]);
    public readonly Node GetNode(NodeRef ref_) => _context.GetNode(ref_);
    public readonly Token GetToken(TokenRef ref_)
    {
        return _context.LowLevelTokens[ref_.Index];
    }

    private void MarkDidOperation(UpdateOperation op)
    {
        Debug.Assert(ExecutedUpdateOperation == UpdateOperation.None);
        ExecutedUpdateOperation = op;
    }

    internal void AddInterpretation(NodeRef listRoot, NodeRef nodeRefToAdd)
    {
        Debug.Assert(_context.IsInterpretationsListOnlySelf(nodeRefToAdd));

    }

    internal void Replace(NodeRef nodeRefToReplace, NodeRef nodeRefToReplaceWith)
    {
        Debug.Assert(_context.IsInterpretationsListOnlySelf(nodeRefToReplaceWith));
        ChangeRootInList(_context);
        ReplaceInList(_context);
        return;

        void ChangeRootInList(ParsingContext c)
        {
            var rootRef = c.GetInterpretationsListRoot(nodeRefToReplace);
            if (nodeRefToReplace == rootRef)
            {
                var root = c.GetNode(rootRef);
                var newRoot = nodeRefToReplaceWith;
                foreach (var parentRef in root.Parents!)
                {
                    var parent = c.GetNode(parentRef);
                    var children = parent.Children!;
                    var index = children.IndexOf(rootRef);
                    children[index] = newRoot;
                }
            }
        }

        void ReplaceInList(ParsingContext c)
        {
            var replacedNode = c.GetNode(nodeRefToReplace);
            var nextRef = replacedNode.InterpretationList.Next;
            var prevRef = replacedNode.InterpretationList.Prev;
            var prev = c.GetNode(prevRef);
            var next = c.GetNode(nextRef);
            prev.InterpretationList.Next = nodeRefToReplaceWith;
            next.InterpretationList.Prev = nodeRefToReplaceWith;
        }
    }

    internal void Remove(NodeRef removedNodeRef, bool removeAllInterpretations)
    {
        var rootRef = _context.GetInterpretationsListRoot(removedNodeRef);
        if (removeAllInterpretations || _context.IsInterpretationsListOnlySelf(removedNodeRef))
        {
            RemoveList(_context);
        }
        else
        {
            ChangeRootInList(_context);
            RemoveNodeInList(_context);
        }
        return;

        void RemoveList(ParsingContext c)
        {
            var root = c.GetNode(rootRef);
            foreach (var parentRef in root.Parents!)
            {
                var parent = c.GetNode(parentRef);
                var children = parent.Children!;
                var index = children.IndexOf(rootRef);
                children.RemoveAt(index);
            }
        }

        void ChangeRootInList(ParsingContext c)
        {
            var root = c.GetNode(rootRef);
            bool selfIsRoot = removedNodeRef == rootRef;
            // Select a new root.
            if (selfIsRoot)
            {
                // Can be arbitrary
                var newRoot = root.InterpretationList.Next;
                foreach (var parentRef in root.Parents!)
                {
                    var parent = c.GetNode(parentRef);
                    var children = parent.Children!;
                    var index = children.IndexOf(rootRef);
                    children[index] = newRoot;
                }
            }

            // Else, nothing to do, because only the root is in the children list.
        }

        void RemoveNodeInList(ParsingContext c)
        {
            var self = c.GetNode(removedNodeRef);
            var prevRef = self.InterpretationList.Prev;
            Debug.Assert(prevRef != removedNodeRef);
            var prev = c.GetNode(prevRef);
            var nextRef = self.InterpretationList.Next;
            var next = c.GetNode(nextRef);
            next.InterpretationList.Prev = prevRef;
            prev.InterpretationList.Next = nextRef;
        }
    }

    public void RemoveCurrentAndAllInterpretations()
    {
        MarkDidOperation(UpdateOperation.Removed);
        Remove(CurrentNodeRef, removeAllInterpretations: true);
    }

    public void RemoveCurrentInterpretation()
    {
        var c = _container.Children!;
        var self = _context.GetNode(c[_offset]);
        var otherRef = self.OtherInterpretation;
        if (!otherRef.IsNull)
        {
            MarkDidOperation(UpdateOperation.RemovedBranch);
            c[_offset] = otherRef.Value;
        }
        else
        {
            MarkDidOperation(UpdateOperation.Removed);
            c.RemoveAt(_offset);
        }
    }

    public Node WrapSingleCurrentInterpretationDefinitive(NodeType type)
    {
        MarkDidOperation(UpdateOperation.Replaced);
        var current = _context.GetNode(CurrentNodeRef);
        var wrappingNode = new Node
        {
            Type = type,
            Children = [CurrentNodeRef],
            OtherInterpretation = current.OtherInterpretation,
        };
        current.OtherInterpretation = default;
        var ref_ = _context.StoreNode(wrappingNode);
        _container.Children![_offset] = ref_;
        return wrappingNode;
    }

    public Node WrapCurrentDefinitive(NodeType type, int len)
    {
        MarkDidOperation();
        var removedSpan = CurrentNodes[.. len];
        var ret = new Node
        {
            Type = type,
            Children = [.. removedSpan],
        };
        var ref_ = _context.StoreNode(ret);
        _container.Children!.ReplaceRange(_offset, len, ref_);
        return ret;
    }
}

public readonly struct InterpretationsEnumerable
{
    internal readonly ParsingContext Context;
    internal readonly bool IncludeSelf;
    internal readonly NodeRef Start;

    public InterpretationsEnumerable(
        ParsingContext context,
        NodeRef start,
        bool includeSelf)
    {
        Context = context;
        Start = start;
        IncludeSelf = includeSelf;
    }

    public InterpretationsEnumerator GetEnumerator() => new(this);
}

public struct InterpretationsEnumerator
{
    private readonly ParsingContext _context;
    private NodeRef _endExclusive;
    private NodeRef _current;
    private bool _skipMoveNext;

    public InterpretationsEnumerator(InterpretationsEnumerable e)
    {
        _endExclusive = e.Start;
        _context = e.Context;
        _skipMoveNext = e.IncludeSelf;
        _current = e.Start;
    }

    public bool MoveNext()
    {
        if (_skipMoveNext)
        {
            _skipMoveNext = false;
            return true;
        }

        var c = _context.GetNode(_current);
        var nextRef = c.InterpretationList.Next;
        if (nextRef == _endExclusive)
        {
            return false;
        }

        _current = nextRef;
        return true;
    }

    public NodeRef Current => _current;
}

public struct ParsingContext()
{
    public List<Token> LowLevelTokens = new();
    public Node Root = null!;
    public bool IsDone => false;

    public readonly Node GetNode(NodeRef ref_)
    {
        return ref_._Value;
    }

    public readonly NodeRef StoreNode(Node node)
    {
        var ret = new NodeRef(node);
        if (node.InterpretationList == default)
        {
            node.InterpretationList = new()
            {
                Next = ret,
                Prev = ret,
            };
        }
        return ret;
    }

    public readonly Node? GetNode(MaybeNodeRef ref_)
    {
        if (ref_.IsNull)
        {
            return null;
        }
        return GetNode(ref_.Value);
    }

    public readonly InterpretationsEnumerable EnumerateInterpretations(NodeRef ref_)
    {
        return new(this, ref_, includeSelf: false);
    }

    public readonly InterpretationsEnumerable EnumerateInterpretationsAndSelf(NodeRef ref_)
    {
        return new(this, ref_, includeSelf: true);
    }

    public readonly NodeRef GetInterpretationsListRoot(NodeRef ref_)
    {
        var e = EnumerateInterpretationsAndSelf(ref_).GetEnumerator();
        while (e.MoveNext())
        {
            var node = GetNode(e.Current);
            if (node.Parents != null)
            {
                return e.Current;
            }
        }
        Debug.Fail("No root in interpretations list");
        return default;
    }

    public readonly List<NodeRef> GetParents(NodeRef ref_)
    {
        var rootRef = GetInterpretationsListRoot(ref_);
        var root = GetNode(rootRef);
        return root.Parents!;
    }

    public readonly bool IsInterpretationsListOnlySelf(NodeRef ref_)
    {
        var n = GetNode(ref_);
        if (n.InterpretationList.Next == ref_)
        {
            Debug.Assert(n.InterpretationList.Prev == ref_);
            return true;
        }
        return false;
    }
}

public ref struct BufferedLexerScope : ILexerScope
{
    // TODO: Should not refer to the whole context.
    private readonly GraphIterationBuilder _builder;
    private int _appliedOffset = 0;

    public BufferedLexerScope(GraphIterationBuilder builder)
    {
        _builder = builder;
    }

    private int RealOffset(int offset) => _appliedOffset + offset - 1;

    public bool CanPeek(int offset = 1)
    {
        if (RealOffset(offset) < _builder.CurrentNodes.Length)
        {
            return true;
        }
        return false;
    }

    public Token Peek(int offset = 1)
    {
        var ref_ = _builder.CurrentNodes[RealOffset(offset)];
        var r = _builder.GetNode(ref_).Payload!.Token;
        return _builder.GetToken(r);
    }

    public void Move(int amount = 1)
    {
        _appliedOffset += amount;
    }

    public int ConsumedCount => _appliedOffset;
}

public static class NewParser
{
    public static void Parse(List<ReadOnlyMemory<char>> input)
    {
        var c = new ParsingContext();
        var lexer = new Lexer(LessonTokenReader.Instance);
        lexer.Reset(input.GetEnumerator());
        while (lexer.CanPeek())
        {
            c.LowLevelTokens.Add(lexer.Current);
            lexer.Move();
        }
        while (!c.IsDone)
        {
            ApplyRulesForChildren(c, c.Root);
        }
    }

    private static void FirstIteration(ParsingContext c)
    {
        Debug.Assert(c.Root == null);
        var tokens = CollectionsMarshal.AsSpan(c.LowLevelTokens);
        c.Root = Node.CreateContainer();
        CollectionsMarshal.SetCount(c.Root.Children!, tokens.Length);
        var span = CollectionsMarshal.AsSpan(c.Root.Children!);
        for (int i = 0; i < tokens.Length; i++)
        {
            var node = Node.CreateRegular(new(i));
            var nodeRef = c.StoreNode(node);
            span[i] = nodeRef;
        }
    }

    private enum IterationApplicationResult
    {
        Done,
        NotDone,
    }

    private static IterationApplicationResult ApplyRulesForChildren(
        ParsingContext c,
        Node node)
    {
        var root = node;
        var children = root.Children;
        if (children is null)
        {
            throw new NotImplementedException("Children is null for root");
        }
        int currentIndex = children.Count - 1;
        bool changed = false;
        while (currentIndex >= 0)
        {
            var result = ApplyRulesForNodes(new(c, root, currentIndex));
            if (result != ApplicationResult.Nothing)
            {
                changed = true;
            }

            currentIndex--;
        }

        if (changed)
        {
            return IterationApplicationResult.NotDone;
        }
        else
        {
            return IterationApplicationResult.Done;
        }
    }

    private enum ApplicationResult
    {
    }

    private static ApplicationResult ApplyRulesForNodes(
        GraphIterationBuilder c)
    {
        Debug.Assert(c.CurrentNodes.Length != 0);
        var node = c.CurrentNode;

        switch (node.Type)
        {
            case NodeType.RegularToken:
            {
                var token = c.GetToken(node.Payload!.Token);
                if (token.Is(','))
                {
                    c.WrapSingleCurrentInterpretationDefinitive(NodeType.ItemSeparator);
                    return ApplicationResult.Replaced;
                }
                if (token.Type == TokenType.EndOfStream)
                {
                    c.RemoveCurrentInterpretation();
                    return ApplicationResult.Removed;
                }
                {
                    var scope = new BufferedLexerScope(c);
                    // TODO: Too restrictive, now should allow failure at any step - then, interpret it as not a room.
                    if (RoomParser.Instance.TryParseRoom(ref scope))
                    {
                        c.WrapCurrentDefinitive(
                            type: NodeType.Room,
                            len: scope.ConsumedCount);
                        return ApplicationResult.Merged;
                    }
                }
                if (token.Type == LessonTokenType.ModifierGroupStart)
                {
                    throw new NotImplementedException();
                }

                {
                    var scope = new BufferedLexerScope(c);
                    if (TimeSlotParser.ParseTime(ref scope) is { } timeSlot)
                    {
                        var node1 = c.WrapCurrentDefinitive(
                            type: NodeType.TimeSlot,
                            len: scope.ConsumedCount);
                        node1.Payload = new()
                        {
                            TimeSlot = timeSlot,
                        };
                        return ApplicationResult.Merged;
                    }
                }
                break;
            }
            case NodeType.ItemSeparator:
            {
                return ApplicationResult.Nothing;
            }
            default:
            {
                throw new NotImplementedException($"Unhandled node type: {node.Type}");
            }
        }
    }
}

public sealed class Builder
{
    public void SubGroup(string str)
    {
    }

    public void LessonOrTeacherName(string str)
    {
    }

    public void LessonModifiers(Action<ModifierListBuilder> configure)
    {
    }

    public void ItemSeparator()
    {
    }

    public void LineSeparator()
    {
    }

    public void Room(string s)
    {
    }

    public ParsedLesson[] GetResult()
    {
    }
}

public sealed class ModifierListBuilder
{
    public void LessonType(LessonType lab)
    {
    }

    public void Parity(Parity evenWeek)
    {
    }

    public void Text(string str)
    {
    }
}
