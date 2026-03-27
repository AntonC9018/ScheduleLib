using System.Runtime.InteropServices;
using FmiWebsiteInterop.Api;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Parsing;
using ScheduleLib.Theses.Parsing;

namespace FmiWebsiteInterop.Teachers;

public interface ISlugProvider
{
    public ValueTask<TeacherSlugMap> SlugMap(CancellationToken cancellationToken);
}

// TODO: Probably don't want to have the client be stored here.
public sealed class ItUsmWebsiteTeacherDataProvider(
    ItUsmWebsiteHttpClient _client,
    [FromKeyedServices(ThesisListParser.TeacherNameRemapperKey)] INameRemapper _remap)

    : ISlugProvider
{
    private ItUsmTeacherModel[]? _data;

    // Not supposed to be used from multiple threads currently.
    public async Task Init(CancellationToken cancellationToken)
    {
        if (_data is not null)
        {
            throw new InvalidOperationException("Tried to initialize the provider a second time!");
        }
        _data = await _client.GetAllTeachers(cancellationToken);
    }

    public ValueTask<ItUsmTeacherModel[]> BeforeQuerying(CancellationToken cancellationToken)
    {
        if (_data is null)
        {
            throw new InvalidOperationException("Must initialize before querying!");
        }
        _ = cancellationToken;
        return ValueTask.FromResult(_data);
    }

    public async ValueTask<ItUsmTeacherModel[]> Get(CancellationToken cancellationToken)
    {
        var data = await BeforeQuerying(cancellationToken);
        return data;
    }

    public async ValueTask<TeacherSlugMap> SlugMap(CancellationToken cancellationToken)
    {
        var data = await BeforeQuerying(cancellationToken);
        var ret = new TeacherSlugMap(data.Length);
        foreach (var remoteTeacher in data)
        {
            var slug = remoteTeacher.Slug;
            var name = remoteTeacher.Name;
            name = _remap.RemapName(name);

            ref var t = ref CollectionsMarshal.GetValueRefOrAddDefault(ret, name, out bool exists);
            if (exists && t != slug)
            {
                throw new InvalidOperationException("Something wrong, slugs don't match for a duplicate? teacher");
            }

            t = slug;

        }
        return ret;
    }
}

public sealed class TeacherSlugMap : Dictionary<Name, string>
{
    public TeacherSlugMap(int capacity) : base(capacity, Name_IgnoreDiacritics_AllowNoPatronymic_EqualityComparer.Instance)
    {
    }
}
