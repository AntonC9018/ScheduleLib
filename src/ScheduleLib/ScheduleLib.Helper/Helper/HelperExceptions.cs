namespace ScheduleLib.Helper;

public sealed class EmptySizedItemArrayException(Type elementType)
    : InvalidOperationException($"SizedItemArray<{elementType.Name}> is empty.")
{
    public Type ElementType { get; } = elementType;
}

public enum SequencePairCountViolation
{
    Empty,
    SingleElement,
    MoreThanTwoElements,
}

public sealed class SequencePairCountException : InvalidOperationException
{
    private SequencePairCountException(
        SequencePairCountViolation violation,
        Type elementType,
        string message)
        : base(message)
    {
        Violation = violation;
        ElementType = elementType;
    }

    public SequencePairCountViolation Violation { get; }
    public Type ElementType { get; }

    public static SequencePairCountException Empty(Type elementType)
    {
        return new(
            SequencePairCountViolation.Empty,
            elementType,
            "Sequence contains no elements");
    }

    public static SequencePairCountException SingleElement(Type elementType)
    {
        return new(
            SequencePairCountViolation.SingleElement,
            elementType,
            "Sequence contains only 1 element");
    }

    public static SequencePairCountException MoreThanTwoElements(Type elementType)
    {
        return new(
            SequencePairCountViolation.MoreThanTwoElements,
            elementType,
            "Sequence contains more than 2 items");
    }
}

public sealed class SequenceTooShortException : InvalidOperationException
{
    public SequenceTooShortException(
        Type elementType,
        int requestedIndex,
        int actualCount)
        : base($"The list was too short: requested index {requestedIndex}, actual count {actualCount}.")
    {
        ElementType = elementType;
        RequestedIndex = requestedIndex;
        ActualCount = actualCount;
    }

    public Type ElementType { get; }
    public int RequestedIndex { get; }
    public int ActualCount { get; }
}

public sealed class DuplicateMergedSequenceItemException(Type elementType)
    : InvalidOperationException($"Merging two sequences of {elementType.Name} with duplicates is not allowed.")
{
    public Type ElementType { get; } = elementType;
}

public enum NonAllocSequenceViolation
{
    Empty,
    MultipleElements,
}

public sealed class NonAllocSequenceSingleException : InvalidOperationException
{
    private NonAllocSequenceSingleException(
        NonAllocSequenceViolation violation,
        Type elementType,
        string message)
        : base(message)
    {
        Violation = violation;
        ElementType = elementType;
    }

    public NonAllocSequenceViolation Violation { get; }
    public Type ElementType { get; }

    public static NonAllocSequenceSingleException Empty(Type elementType)
    {
        return new(
            NonAllocSequenceViolation.Empty,
            elementType,
            "No elements");
    }

    public static NonAllocSequenceSingleException MultipleElements(Type elementType)
    {
        return new(
            NonAllocSequenceViolation.MultipleElements,
            elementType,
            "More than one element");
    }
}

public sealed class UnsupportedRomanNumeralException(int value)
    : NotSupportedException($"Roman numerals are not supported for value {value}.")
{
    public int Value { get; } = value;
}
