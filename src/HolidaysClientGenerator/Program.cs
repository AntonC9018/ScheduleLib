// ReSharper disable UnusedMember.Global

using CommandDotNet;
using NJsonSchema.CodeGeneration.CSharp;
using NSwag;
using NSwag.CodeGeneration.CSharp;

public sealed class Logic
{
    public static int Main(string[] args)
    {
        return new AppRunner<Logic>().Run(args);
    }

    [DefaultCommand]
    public async Task Generate(
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        OpenApiDocument document;
        {
            using var httpClient = new HttpClient();
            var json = await httpClient.GetStringAsync("https://openholidaysapi.org/swagger/v1/swagger.json", cancellationToken);
            document = await OpenApiDocument.FromJsonAsync(json, cancellationToken: cancellationToken);
        }
        document.BasePath = "https://openholidaysapi.org";

        const string className = "OpenHolidaysClient";
        var settings = new CSharpClientGeneratorSettings
        {
            ClassName = className,
            CSharpGeneratorSettings =
            {
                Namespace = "OpenHolidays",
                JsonLibrary = CSharpJsonLibrary.SystemTextJson,
                GenerateNullableReferenceTypes = true,
            },
        };

        var generator = new CSharpClientGenerator(document, settings);
        var code = generator.GenerateFile();
        var fileName = Path.Combine(outputDirectory, className + ".cs");
        await File.WriteAllTextAsync(fileName, code, cancellationToken);
    }
}


