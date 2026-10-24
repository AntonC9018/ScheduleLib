using ScheduleLib.Builders;
using ScheduleLib.Helper;

namespace ScheduleLib;

/// <summary>
/// Mutable, reusable serialization configuration. Each successful setter replaces
/// its previous value; Output replaces either output form. Invalid setters leave
/// the previous configuration intact. Configuration is retained after execution,
/// including failure or cancellation. This builder is not thread-safe.
/// </summary>
public sealed class ScheduleSerializationBuilder
{
    private Schedule? _input;
    private Stream? _outputStream;
    private string? _outputPath;
    private string _hash = "";

    public ScheduleSerializationBuilder Input(Schedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        _input = schedule;
        return this;
    }

    /// <summary>Writes at the stream's current position without seeking,
    /// truncating or disposing it, including on failure or cancellation.</summary>
    public ScheduleSerializationBuilder Output(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
        {
            throw new ArgumentException(message: "Output stream must be writable.", paramName: nameof(stream));
        }
        _outputStream = stream;
        _outputPath = null;
        return this;
    }

    /// <summary>Resolves the path now and opens files only during execution.
    /// Files opened by the builder are disposed. Publication atomically replaces
    /// the destination after a complete write; callers coordinate concurrent writers.</summary>
    public ScheduleSerializationBuilder Output(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        _outputPath = fullPath;
        _outputStream = null;
        return this;
    }

    /// <summary>Stores the caller's cache-validity identity verbatim. This is
    /// not a checksum computed from the serialized bytes. Defaults to empty.</summary>
    public ScheduleSerializationBuilder Hash(string hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        _hash = hash;
        return this;
    }

    /// <summary>Validates required configuration before writing. Uses the current
    /// configuration for this execution without consuming or resetting it.</summary>
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var input = _input;
        var outputStream = _outputStream;
        var outputPath = _outputPath;
        var hash = _hash;
        if (input is null)
        {
            throw new MissingSerializationConfigurationException(nameof(Input));
        }
        if (outputStream is null)
        {
            if (outputPath is null)
            {
                throw new MissingSerializationConfigurationException(nameof(Output));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        var model = SerializationModels.ConvertToSerializationModel(input, hash);
        if (outputStream is not null)
        {
            await ScheduleSerializer.SerializeModelAsync(model,
                outputStream,
                cancellationToken);
            return;
        }
        await AtomicFilePublisher.Publish(outputPath!,
            Write,
            cancellationToken);

        Task Write(Stream stream, CancellationToken token)
        {
            return ScheduleSerializer.SerializeModelAsync(model,
                stream,
                token);
        }
    }
}

public sealed class MissingSerializationConfigurationException : ScheduleBuildException
{
    public string Configuration { get; }

    public MissingSerializationConfigurationException(string configuration)
        : base($"Serialization requires {configuration} configuration before execution.")
    {
        Configuration = configuration;
    }
}
