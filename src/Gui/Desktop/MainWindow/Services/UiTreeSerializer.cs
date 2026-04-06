using Anton.LayeredData;
using Desktop.ViewModelData;
using Microsoft.Extensions.Options;
using ScheduleLib.Helper;

namespace Desktop.MainWindow;

public sealed class UiTreeSerializerSettings
{
    public string DatabaseFilePath { get; set; } = "ui-layers.json";
    public bool OpenAfterSave { get; set; } = false;
}

public interface IUiTreeOutputProvider
{
    public Stream GetWrite(TreeBuilder tree);
    public Stream? GetRead(TreeBuilder tree);
}

public sealed class UiTreeOutputProvider : IUiTreeOutputProvider
{
    private readonly UiTreeSerializerSettings _settings;

    public UiTreeOutputProvider(IOptions<UiTreeSerializerSettings> settings)
    {
        _settings = settings.Value;
    }

    public Stream GetWrite(TreeBuilder tree)
    {
        var output = new FileStream(_settings.DatabaseFilePath, FileMode.Create, FileAccess.Write);
        return output;
    }

    public Stream? GetRead(TreeBuilder tree)
    {
        try
        {
            var output = new FileStream(_settings.DatabaseFilePath, FileMode.Open, FileAccess.Read);
            return output;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}

// public interface IUiTreeSerializer
// {
//     public ValueTask Serialize(TreeBuilder tree);
//     public ValueTask Deserialize(TreeBuilder tree);
// }

public sealed class UiTreeSerializer
{
    private readonly UiTreeSerializerSettings _settings;
    private readonly TreeSerializer _serializer;
    private readonly IUiTreeOutputProvider _outputProvider;

    public UiTreeSerializer(
        TreeSerializer serializer,
        IOptions<UiTreeSerializerSettings> settings,
        IUiTreeOutputProvider outputProvider)
    {
        _serializer = serializer;
        _outputProvider = outputProvider;
        _settings = settings.Value;
    }

    public async ValueTask Serialize(TreeBuilder tree)
    {
        await using var output = _outputProvider.GetWrite(tree);
        await _serializer.SerializeUiLayers(output, tree).ConfigureAwait(false);
        if (_settings.OpenAfterSave)
        {
            ExplorerHelper.TryOpenExplorerAndSelectFile(_settings.DatabaseFilePath);
        }
    }

    public bool CanRead(TreeBuilder tree)
    {
        if (_outputProvider.GetRead(tree) is not { } output)
        {
            return false;
        }
        output.Dispose();
        return true;
    }

    public async ValueTask Deserialize(TreeBuilder tree)
    {
        if (_outputProvider.GetRead(tree) is not { } output)
        {
            return;
        }
        await using (output)
        {
            await _serializer.DeserializeUiLayers(output, tree).ConfigureAwait(false);
        }
    }
}
