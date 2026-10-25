using System.Text;
using ScheduleLib.Helper;

namespace ScheduleLib.OnlineRegistry;

/// <summary>
/// Readable <c>(scope)</c> suffixes for grouped registry topics.
/// Formats, appends, and splits validated audience strings.
/// </summary>
/// <remarks>
/// Canonical <see cref="Format"/> layout (documented choice):
/// <code>
/// Parent1 (ro), Parent2 (en) /alt:Alt /spec:Spec /I
/// </code>
/// <list type="bullet">
/// <item>Parents are ordered by <see cref="Group.Name"/> (<see cref="StringComparison.Ordinal"/>),
/// then language, then id, and joined with <c>", "</c>.</item>
/// <item>Each parent carries its teaching language <c>(ro|ru|en)</c> (lowercase via
/// <see cref="LanguageHelper.GetName"/>) when the language is known.
/// <see cref="Language.None"/> (and any non-teaching value) emits no suffix.</item>
/// <item>Explicit partition dimensions follow the parents, each as a <c>/</c> token
/// joined with single spaces, in <see cref="PartitionDimensions.DisplayOrder"/>
/// (alternative, specialization, subgroup):
/// <c>/alt:Value</c>, <c>/spec:Value</c>, and the subgroup token.</item>
/// <item>Subgroup tokens: language subgroups emit <c>/ro|/ru|/en</c> (canonical short
/// form, so <c>eng</c> becomes <c>/en</c>); numeric subgroups emit their canonical
/// upper-roman form (<c>/I</c>, <c>/II</c>); every other subgroup emits
/// <c>/sub:Value</c> verbatim.</item>
/// <item>Specialization and alternative values always carry <c>spec:</c>/<c>alt:</c>
/// prefixes (even when only one dimension is active) so multi-dimension scopes
/// stay unambiguous.</item>
/// </list>
/// <see cref="TrySplit"/> treats scopes as opaque: a terminal balanced <c>(...)</c>
/// is a scope only when its complete contents equal a supplied known audience
/// string (case-insensitive) or a safe language/numeric/spacing alias of one.
/// Arbitrary topic text such as <c>Feature (optional)</c> never matches.
/// </remarks>
internal static class RegistryTopicScope
{
    internal static string Format(Schedule schedule, LessonGroups groups, GroupPartitionKey partition)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        var ids = new List<GroupId>();
        foreach (var id in groups)
        {
            ids.Add(id);
        }
        ids.Sort((a, b) =>
        {
            global::ScheduleLib.Group get = schedule.Get(a);
            string strA2 = get.Name;
            global::ScheduleLib.Group get2 = schedule.Get(b);
            string strB2 = get2.Name;
            global::System.StringComparison comparisonType = StringComparison.Ordinal;
            int comparison = string.Compare(strA: strA2, strB: strB2, comparisonType);
            if (comparison != 0)
            {
                return comparison;
            }
            global::ScheduleLib.Language CalculateTarget()
            {
                global::ScheduleLib.Group get3 = schedule.Get(b);
                global::ScheduleLib.Language target = get3.Language;
                return target;
            }

            comparison = schedule.Get(a).Language
                .CompareTo(CalculateTarget());
            if (comparison != 0)
            {
                return comparison;
            }
            return a.Value.CompareTo(b.Value);
        });

        var parents = new List<string>(ids.Count);
        foreach (var id in ids)
        {
            var group = schedule.Get(id);
            if (group.Language is Language.Ro or Language.Ru or Language.En)
            {
                global::System.Collections.Generic.List<string> list = parents;
                string item = $"{group.Name} ({group.Language.GetName()})";
                list.Add(item);
            }
            else
            {
                parents.Add(group.Name);
            }
        }
        var basePart = string.Join(", ", parents);

        var tokens = new List<string>(3);
        if (!string.IsNullOrEmpty(partition.Alternative.Value))
        {
            global::System.Collections.Generic.List<string> list2 = tokens;
            string text = $"/alt:{partition.Alternative.Value}";
            string item2 = (string) text;
            list2.Add(item2);
        }
        if (!string.IsNullOrEmpty(partition.Specialization.Value))
        {
            global::System.Collections.Generic.List<string> list3 = tokens;
            string text2 = $"/spec:{partition.Specialization.Value}";
            string item3 = (string) text2;
            list3.Add(item3);
        }
        if (!string.IsNullOrEmpty(partition.SubGroup.Value))
        {
            global::System.Collections.Generic.List<string> list4 = tokens;
            string item4 = FormatSubGroupToken(partition.SubGroup.Value!);
            list4.Add(item4);
        }

        if (tokens.Count == 0)
        {
            return basePart;
        }
        if (basePart.Length == 0)
        {
            return string.Join(" ", tokens);
        }
        return (string) $"{basePart} {string.Join(" ", tokens)}";
    }

    private static string FormatSubGroupToken(string value)
    {
        if (string.Equals(value, SpecialSubGroups.Eng.Value, StringComparison.OrdinalIgnoreCase))
        {
            return "/en";
        }
        if (LanguageHelper.ParseName(value.AsSpan()) is { } language
            && language is Language.Ro or Language.Ru or Language.En)
        {
            return (string) $"/{language.GetName()}";
        }
        var upper = value.ToUpperInvariant();
        if (NumberHelper.FromRoman(upper.AsSpan()) is not null)
        {
            return (string) $"/{upper}";
        }
        if (int.TryParse(value, out int arabic))
        {
            if (arabic >= 1)
            {
                if (arabic <= 10)
                {
                    string toRoman = NumberHelper.ToRoman(arabic);
                    string text = $"/{toRoman}";
                    string text2 = (string) text;
                    return text2;
                }
            }
        }
        return (string) $"/sub:{value}";
    }

    /// <summary>
    /// Appends <c> (scope)</c> to a non-blank topic, replacing an already-recognized
    /// trailing scope suffix (canonical or alias) so re-emitting the same scope is idempotent.
    /// Null/empty/whitespace topics are returned unchanged to avoid concealing missing topics.
    /// </summary>
    internal static string? Append(string? topic, string scope, IReadOnlyCollection<string> knownScopes)
    {
        ArgumentNullException.ThrowIfNull(knownScopes);
        if (string.IsNullOrWhiteSpace(scope))
        {
            throw new ArgumentException(message: "Scope must not be empty.", paramName: nameof(scope));
        }
        if (string.IsNullOrWhiteSpace(topic))
        {
            return topic;
        }
        if (TrySplit(topic: topic, knownScopes, baseTopic: out var baseTopic, scope: out _))
        {
            if (baseTopic!.Length == 0)
            {
                return (string) $"({scope})";
            }
            return (string) $"{baseTopic} ({scope})";
        }
        return (string) $"{topic.TrimEnd()} ({scope})";
    }

    /// <summary>
    /// Splits a terminal balanced <c>(scope)</c> only when its complete contents match a
    /// supplied known audience string (case-insensitive) or a safe alias of one.
    /// Topic-internal parentheses and unknown trailing parentheses leave the topic intact
    /// (<c>false</c> with null outputs). Alias matches return the canonical known scope.
    /// </summary>
    internal static bool TrySplit(string? topic, IReadOnlyCollection<string> knownScopes, out string? baseTopic, out string? scope)
    {
        baseTopic = null;
        scope = null;
        ArgumentNullException.ThrowIfNull(knownScopes);
        if (string.IsNullOrWhiteSpace(topic) || knownScopes.Count == 0)
        {
            return false;
        }
        var trimmed = topic.TrimEnd();
        if (!trimmed.EndsWith(')'))
        {
            return false;
        }
        int depth = 0;
        int open = -1;
        for (int i = trimmed.Length - 1; i >= 0; i--)
        {
            char c = trimmed[i];
            if (c == ')')
            {
                depth++;
            }
            else if (c == '(')
            {
                depth--;
                if (depth == 0)
                {
                    open = i;
                    break;
                }
                if (depth < 0)
                {
                    return false;
                }
            }
        }
        if (open < 0)
        {
            return false;
        }
        string text = trimmed;
        int startIndex2 = open + 1;
        int int32 = trimmed.Length - open;
        int length2 = int32 - 2;
        string substring = text.Substring(startIndex: startIndex2, length: length2);
        var inner = substring.Trim();
        if (inner.Length == 0)
        {
            return false;
        }
        foreach (var known in knownScopes)
        {
            if (string.IsNullOrWhiteSpace(known))
            {
                continue;
            }
            if (string.Equals(inner, known, StringComparison.OrdinalIgnoreCase))
            {
                baseTopic = trimmed.Substring(startIndex: 0, length: open)
                    .TrimEnd();
                scope = known;
                return true;
            }
        }
        var normalizedInner = NormalizeAlias(inner);
        foreach (var known in knownScopes)
        {
            if (string.IsNullOrWhiteSpace(known))
            {
                continue;
            }
            string a = normalizedInner;
            string b = NormalizeAlias(known);
            global::System.StringComparison comparisonType = StringComparison.OrdinalIgnoreCase;
            if (string.Equals(a, b, comparisonType))
            {
                baseTopic = trimmed.Substring(startIndex: 0, length: open)
                    .TrimEnd();
                scope = known;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Safe older-spelling normalization for language/numeric partitions plus spacing
    /// tolerance around <c>/</c>. Applied to both sides before comparison, so only
    /// exact-variant matches count; arbitrary text is never stripped.
    /// Covers: <c>(eng)</c> to <c>(en)</c>, <c>/eng</c> to <c>/en</c>,
    /// <c>/1</c> to <c>/I</c> (1-10), and <c> /</c> to <c>/</c>.
    /// </summary>
    private static string NormalizeAlias(string value)
    {
        var result = value.Trim();
        result = result.Replace(" (", "(", StringComparison.Ordinal);
        while (result.Contains(" /", StringComparison.Ordinal))
        {
            result = result.Replace(" /", "/", StringComparison.Ordinal);
        }
        result = result.Replace("(eng)", "(en)", StringComparison.OrdinalIgnoreCase);
        result = ReplaceToken(value: result, oldToken: "/eng", newToken: "/en");
        for (int number = 10; number >= 1; number--)
        {
            string value2 = result;
            string oldToken2 = "/" + number;
            string toRoman = NumberHelper.ToRoman(number);
            string text = $"/{toRoman}";
            string newToken2 = (string) text;
            string replaceToken = ReplaceToken(value: value2, oldToken: oldToken2, newToken: newToken2);
            result = replaceToken;
        }
        return result;
    }

    private static string ReplaceToken(string value, string oldToken, string newToken)
    {
        var result = new StringBuilder(value.Length);
        int i = 0;
        while (i < value.Length)
        {
            bool conditionResult;
            if (i + oldToken.Length <= value.Length)
            {
                if (value.AsSpan(start: i, length: oldToken.Length)
                    .Equals(oldToken, StringComparison.OrdinalIgnoreCase))
                {
                    if (i + oldToken.Length == value.Length)
                    {
                        conditionResult = true;
                    }
                    else
                    {
                        conditionResult = char.IsLetterOrDigit(value[i + oldToken.Length]);
                        conditionResult = !(conditionResult);
                    }
                }
                else
                {
                    conditionResult = false;
                }
            }
            else
            {
                conditionResult = false;
            }

            if (conditionResult)
            {
                result.Append(newToken);
                i += oldToken.Length;
            }
            else
            {
                result.Append(value[i]);
                i++;
            }
        }
        return result.ToString();
    }
}
