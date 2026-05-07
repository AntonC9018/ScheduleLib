using ScheduleLib.Helper;

namespace ScheduleLib;

public static class LanguageSearch
{
    extension(Schedule schedule)
    {
        public Language GetLanguage(
            in LessonGroups groups,
            bool shouldValidateSingleLanguage = true)
        {
            RetHelper ret = new(shouldValidateSingleLanguage);
            schedule.GetLanguageImpl(groups, ref ret);
            return ret.GetResult();
        }

        public Language GetLanguage(ReadOnlySpan<AnyLessonId> lessons)
        {
            var ret = new RetHelper(shouldValidateSameLanguage: true);
            foreach (var lessonId in lessons)
            {
                ref readonly var groups = ref schedule.Get(lessonId).Lesson.Groups;
                schedule.GetLanguageImpl(groups, ref ret);
                if (ret.ShouldStopAfterFirst)
                {
                    break;
                }
            }
            return ret.GetResult();
        }

        private void GetLanguageImpl(
            in LessonGroups groups,
            ref RetHelper ret)
        {
            foreach (var g in groups)
            {
                var lang = schedule.Get(g).Language;
                ret.Add(lang);

                if (ret.ShouldStopAfterFirst)
                {
                    return;
                }
            }
        }
    }

    private struct RetHelper()
    {
        private EnumBitArray<Language> _values = new();
        private readonly bool _shouldValidateSameLanguage;
        public readonly bool ShouldStopAfterFirst => _shouldValidateSameLanguage;

        public RetHelper(bool shouldValidateSameLanguage) : this()
        {
            _shouldValidateSameLanguage = shouldValidateSameLanguage;
        }

        public void Add(Language lang)
        {
            var before = _values;
            _values.Set(lang);
            if (ShouldStopAfterFirst)
            {
                return;
            }

            if (before != _values)
            {
                throw NotSupportedMultiLang();
            }
        }

        public Language GetResult()
        {
            if (_values.GetFirstSet() is not { } onlyLang)
            {
                throw NoLanguage();
            }
            return onlyLang;
        }
    }

    private static NotSupportedException NotSupportedMultiLang()
    {
        return new NotSupportedException("Multi-language courses not allowed.");
    }

    private static NotSupportedException NoLanguage()
    {
        return new NotSupportedException("Language could not be determined for these lessons.");
    }
}
