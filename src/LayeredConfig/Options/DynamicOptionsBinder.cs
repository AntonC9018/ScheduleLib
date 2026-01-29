using AutoConstructor.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace ScheduleLib.Scraping.Common.Config;

public interface IMarkedConfigurationSectionResolver
{
    public IConfigurationSection? Get(IConfiguration c, string serviceKey);
}

[AutoConstructor]
public sealed partial class MarkedDynamicOptionsResolver<T>
    where T : class
{
    private readonly DynamicOptionsBinder<T> _binder;
    private readonly IMarkedConfigurationSectionResolver _sectionResolver;
    private readonly IConfiguration _configuration;

    public T? Resolve(string serviceKey)
    {
        var section = _sectionResolver.Get(_configuration, serviceKey);
        if (section is null)
        {
            return null;
        }

        var ret = _binder.Get(section);
        return ret;
    }
}

public sealed class DynamicOptionsBinder<T>
    where T : class
{
    private readonly IValidateOptions<T>[] _validators;

    public DynamicOptionsBinder(
        IEnumerable<IValidateOptions<T>> validators)
    {
        _validators = validators.ToArray();
    }

    public T Get(IConfigurationSection section)
    {
        var value = section.Get<T>();
        if (value is null)
        {
            throw new InvalidOperationException("Failed to bind for some reason.");
        }

        var failures = new List<string>();
        foreach (var v in _validators)
        {
            var result = v.Validate(name: null, value);
            if (result is not null && result.Failed)
            {
                failures.AddRange(result.Failures);
            }
        }
        if (failures.Count > 0)
        {
            throw new OptionsValidationException(optionsName: "dynamic", typeof(T), failures);
        }

        return value;
    }
}
