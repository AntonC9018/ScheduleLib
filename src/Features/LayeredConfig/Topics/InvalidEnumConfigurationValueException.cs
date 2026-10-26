using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;

namespace ScheduleLib.Application.Core.Topics;

public enum InvalidEnumConfigurationValueReason
{
    Empty,
    Numeric,
    NotAllowed,
}

public sealed class InvalidEnumConfigurationValueException : TypeConverterException
{
    private InvalidEnumConfigurationValueException(
        ITypeConverter typeConverter,
        MemberMapData memberMapData,
        string? text,
        CsvContext context,
        Type enumType,
        InvalidEnumConfigurationValueReason reason,
        IReadOnlyList<string> allowedValues)
        : base(
            typeConverter,
            memberMapData,
            text: text,
            context,
            message: CreateMessage(enumType,
                text,
                reason,
                allowedValues))
    {
        EnumType = enumType;
        Reason = reason;
        AllowedValues = allowedValues;
    }

    public Type EnumType { get; }
    public InvalidEnumConfigurationValueReason Reason { get; }
    public IReadOnlyList<string> AllowedValues { get; }

    internal static InvalidEnumConfigurationValueException ForEmptyValue(
        ITypeConverter typeConverter,
        MemberMapData memberMapData,
        string? text,
        CsvContext context,
        Type enumType)
    {
        return new InvalidEnumConfigurationValueException(
            typeConverter,
            memberMapData,
            text,
            context,
            enumType,
            InvalidEnumConfigurationValueReason.Empty,
            Array.Empty<string>());
    }

    internal static InvalidEnumConfigurationValueException ForNumericValue(
        ITypeConverter typeConverter,
        MemberMapData memberMapData,
        string? text,
        CsvContext context,
        Type enumType)
    {
        return new InvalidEnumConfigurationValueException(
            typeConverter,
            memberMapData,
            text,
            context,
            enumType,
            InvalidEnumConfigurationValueReason.Numeric,
            Array.Empty<string>());
    }

    internal static InvalidEnumConfigurationValueException ForNotAllowedValue(
        ITypeConverter typeConverter,
        MemberMapData memberMapData,
        string? text,
        CsvContext context,
        Type enumType,
        IEnumerable<string> allowedValues)
    {
        var allowedValuesSnapshot = Array.AsReadOnly(allowedValues.ToArray());
        return new InvalidEnumConfigurationValueException(
            typeConverter,
            memberMapData,
            text,
            context,
            enumType,
            InvalidEnumConfigurationValueReason.NotAllowed,
            allowedValuesSnapshot);
    }

    private static string CreateMessage(
        Type enumType,
        string? text,
        InvalidEnumConfigurationValueReason reason,
        IReadOnlyList<string> allowedValues)
    {
        return reason switch
        {
            InvalidEnumConfigurationValueReason.Empty =>
                $"Empty value is not allowed for enum {enumType.Name}.",
            InvalidEnumConfigurationValueReason.Numeric =>
                $"Numeric value '{text}' is not allowed for enum {enumType.Name}.",
            InvalidEnumConfigurationValueReason.NotAllowed =>
                $"Value '{text}' is not allowed for enum {enumType.Name}. Allowed values: {string.Join(", ", allowedValues)}",
            _ => throw new ArgumentOutOfRangeException(nameof(reason),
                reason,
                null),
        };
    }
}
