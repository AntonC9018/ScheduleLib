using System.Text;
using ScheduleLib.Helper;

namespace ScheduleLib.OnlineRegistry;

public readonly struct CommandProcessingConfig
{
    internal UnsizedBitArray32 _impl { get; init; }

    public CommandProcessingConfigBuilder Builder() => new(this);

    public readonly CommandProcessingConfig WithProcess(LessonEquationCommandTypes types)
    {
        var b = Builder();
        b.Process().Set(types);
        return b.Build();
    }

    public readonly CommandProcessingConfig WithDryRun(LessonEquationCommandTypes types)
    {
        var b = Builder();
        b.DryRun().Set(types);
        return b.Build();
    }

    public readonly CommandProcessingConfig WithLog(LessonEquationCommandTypes types)
    {
        var b = Builder();
        b.Log().Set(types);
        return b.Build();
    }

    public static CommandProcessingConfig None => new();
    public static CommandProcessingConfig Process => None.WithProcess(LessonEquationCommandTypes.All);
    public static CommandProcessingConfig DryRun => None.WithDryRun(LessonEquationCommandTypes.All);
    public static CommandProcessingConfig Log => None.WithLog(LessonEquationCommandTypes.All);

    /// <summary>
    /// Masks out the "process" that are also on "dry run".
    /// </summary>
    /// <value></value>
    public readonly CommandProcessingConfig Normalized
    {
        get
        {
            var b = Builder();
            var dryRun = b.DryRun().RestoredSlice;
            var doNotProcess = dryRun;
            b.Process().ClearArray(doNotProcess);
            return b.Build();
        }
    }

    private readonly EnumBitArray<LessonEquationCommandType> CreatePortion(CommandProcessingConfigPortion portion)
    {
        // just use the builder to not reimplement anything, but this is messy.
        var b = Builder();
        var slice = b.Portion(portion).RestoredSlice;
        return slice;
    }

    private readonly bool CheckAny(CommandProcessingConfigPortion portion, LessonEquationCommandTypes types)
    {
        var p = CreatePortion(portion);
        var other = types.AsBitArray();
        return p.Intersect(other).AreAnySet;
    }
    private readonly bool CheckOne(CommandProcessingConfigPortion offset, LessonEquationCommandType type)
    {
        var p = CreatePortion(offset);
        return p.IsSet(type);
    }

    public readonly bool HasProcess(LessonEquationCommandType type) => CheckOne(CommandProcessingConfigPortion.Process, type);
    public readonly bool HasAnyProcess(LessonEquationCommandTypes types) => CheckAny(CommandProcessingConfigPortion.Process, types);

    public readonly bool HasDryRun(LessonEquationCommandType type) => CheckOne(CommandProcessingConfigPortion.DryRun, type);
    public readonly bool HasAnyDryRun(LessonEquationCommandTypes types) => CheckAny(CommandProcessingConfigPortion.DryRun, types);

    public readonly bool HasLog(LessonEquationCommandType type) => CheckOne(CommandProcessingConfigPortion.Log, type);
    public readonly bool HasAnyLog(LessonEquationCommandTypes types) => CheckAny(CommandProcessingConfigPortion.Log, types);

    public override string ToString()
    {
        var sb = new StringBuilder();
        var value = Normalized;
        foreach (var t in new EnumMembers<CommandProcessingConfigPortion>())
        {
            var portion = value.CreatePortion(t);
            sb.Append(Enum.GetName(t));
            sb.Append('{');

            if (portion.AreAllSet)
            {
                sb.Append("All");
            }
            else
            {
                var list = new ListStringBuilder(sb, ",");
                foreach (var i in portion.SetValues())
                {
                    list.Append(i.ToString());
                }
            }

            sb.Append('}');
            sb.AppendLine();
        }
        return sb.ToString();
    }
}

public struct CommandProcessingConfigBuilder
{
    internal UnsizedBitArray32 _impl;

    public CommandProcessingConfigBuilder()
    {
        _impl = new(0);
    }

    public CommandProcessingConfigBuilder(CommandProcessingConfig c)
    {
        _impl = c._impl;
    }

    public CommandProcessingConfig Build() => new()
    {
        _impl = _impl,
    };
}

public enum CommandProcessingConfigPortion
{
    Process,
    DryRun,
    Log,
}

public static class CommandProcessingConfigBuilderExtensions
{
    extension (ref CommandProcessingConfigBuilder b)
    {
        public OffsetBitArrayForEnumRef<LessonEquationCommandType> Portion(CommandProcessingConfigPortion portion)
        {
            int offset = portion switch
            {
                CommandProcessingConfigPortion.Process => 0,
                CommandProcessingConfigPortion.DryRun => 8,
                CommandProcessingConfigPortion.Log => 16,
                _ => throw Unreachable(),
            };
            return b._impl.EnumPortionRef<LessonEquationCommandType>(offset: offset);
        }

        public OffsetBitArrayForEnumRef<LessonEquationCommandType> Process() => b.Portion(CommandProcessingConfigPortion.Process);
        public OffsetBitArrayForEnumRef<LessonEquationCommandType> DryRun() => b.Portion(CommandProcessingConfigPortion.DryRun);
        public OffsetBitArrayForEnumRef<LessonEquationCommandType> Log() => b.Portion(CommandProcessingConfigPortion.Log);
    }

    extension (OffsetBitArrayForEnumRef<LessonEquationCommandType> b)
    {
        public void Set(LessonEquationCommandTypes mask)
        {
            b.SetArray(mask.AsBitArray());
        }
    }

    extension (LessonEquationCommandTypes mask)
    {
        public EnumBitArray<LessonEquationCommandType> AsBitArray()
        {
            var t = new UnsizedBitArray32((uint) mask);
            return new(t);
        }
    }
}
