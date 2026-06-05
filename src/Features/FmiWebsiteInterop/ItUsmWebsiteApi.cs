using System.Text.Json;
using FmiWebsiteInterop.Teachers;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib;
using ScheduleLib.Parsing;

namespace FmiWebsiteInterop.Api;

public static class ItUsmWebsiteApi
{
    public static void Register(IServiceCollection services)
    {
        services.AddHttpClient<ItUsmWebsiteHttpClient>(x =>
        {
            x.BaseAddress = new("https://it.usm.md/api/");
        });
        services.AddSingleton<ItUsmWebsiteTeacherDataProvider>();
        services.AddSingleton<ISlugProvider>(sp => sp.GetRequiredService<ItUsmWebsiteTeacherDataProvider>());
    }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    extension (ItUsmWebsiteHttpClient c)
    {
        public async Task<ItUsmTeacherModel[]> GetAllTeachers(CancellationToken cancellationToken)
        {
            Uri uri;
            // {
            //     var urib = new UriBuilder(c.HttpClient.BaseAddress!)
            //     {
            //         Path = "teachers/paginated-teachers",
            //     };
            //     var query = HttpUtility.ParseQueryString("");
            //     query.Add("page", "1");
            //     query.Add("size", "1000");
            //     query.Add("sortDirection", "ASC");
            //     urib.Query = query.ToString();
            //     uri = urib.Uri;
            // }
            uri = new("teachers/paginated-teachers?page=1&size=1000&sortDirection=ASC", UriKind.Relative);

            using var response = await c.HttpClient.GetAsync(uri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new ItUsmWebsiteHttpException();
            }

            await using var outputStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var responseModel = await JsonSerializer.DeserializeAsync<TeachersResponseModel>(
                outputStream,
                options: JsonOptions,
                cancellationToken: cancellationToken);
            if (responseModel is null)
            {
                throw new ItUsmWebsiteLogicException("Response model is null");
            }
            if (!responseModel.Last)
            {
                throw new NotImplementedException("Multipage responses");
            }

            var remoteTeachers = responseModel.Content;
            var ret = new ItUsmTeacherModel[remoteTeachers.Length];
            using var parser = new NameParser();
            for (int i = 0; i < remoteTeachers.Length; i++)
            {
                var remoteTeacher = remoteTeachers[i];
                var name = new Name(new()
                {
                    FirstName = ParsePart(remoteTeacher.FirstName),
                    LastName = ParsePart(remoteTeacher.LastName),
                });
                ret[i] = new()
                {
                    Name = name,
                    Slug = remoteTeacher.Slug,
                    UserId = remoteTeacher.UserId,
                    DidacticTitle = remoteTeacher.DidacticTitle,
                    ScientificGrade = remoteTeacher.ScientificGrade,
                };
                continue;

                NameParts<string?> ParsePart(string s)
                {
                    parser.Load(s.AsMemory());
                    var lexer = parser.Scope();
                    var ret1 = NameHelper.ParseNamePart(ref lexer, out bool isIncompleteDoubleName);
                    if (isIncompleteDoubleName)
                    {
                        throw new InvalidOperationException($"Invalid double name on the website '{s}'");
                    }
                    return ret1;
                }
            }
            return ret;
        }
    }
}

public sealed class ItUsmWebsiteHttpException : ItUsmWebsiteException
{
}

public sealed class ItUsmWebsiteLogicException : ItUsmWebsiteException
{
    public ItUsmWebsiteLogicException(string s) : base(s)
    {
    }
}

public abstract class ItUsmWebsiteException : Exception
{
    public ItUsmWebsiteException()
    {
    }

    public ItUsmWebsiteException(string s) : base(s)
    {
    }
}

public sealed class ItUsmWebsiteHttpClient
{
    public ItUsmWebsiteHttpClient(HttpClient httpClient)
    {
        HttpClient = httpClient;
    }

    public HttpClient HttpClient { get; }
}

public sealed class TeachersResponseModel
{
    public required ItUsmTeacherResponseModel[] Content { get; set; }
    public required Pageable Pageable { get; set; }
    public required int TotalPages { get; set; }
    public required int TotalElements { get; set; }
    public required int NumberOfElements { get; set; }
    public required int Size { get; set; }
    public required int Number { get; set; }
    public required bool Last { get; set; }
    public required bool First { get; set; }
    public required bool Empty { get; set; }
}

public sealed class ItUsmTeacherResponseModel
{
    public required int UserId { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? ProfileImageUrl { get; set; }
    public required string Slug { get; set; }
    public string? DidacticTitle { get; set; }
    public string? ScientificGrade { get; set; }
}

public sealed class ItUsmTeacherModel
{
    public required int UserId { get; set; }
    public required string Slug { get; set; }
    public required Name Name { get; set; }
    public string? DidacticTitle { get; set; }
    public string? ScientificGrade { get; set; }
}

public sealed class Pageable
{
    public required int PageNumber { get; set; }
    public required int PageSize { get; set; }
    public required int Offset { get; set; }
}
