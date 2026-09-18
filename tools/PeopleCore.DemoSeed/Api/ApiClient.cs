using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PeopleCore.DemoSeed.Api;

/// <summary>
/// JSON over HTTP to one PeopleCore site. Every call names the step it belongs to, so a refusal
/// says what the seeder was doing when it happened. Request bodies are never repeated in an error:
/// some of them carry passwords.
/// </summary>
public sealed class ApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<JsonNode?> GetAsync(string step, string path, string token) =>
        SendAsync(step, HttpMethod.Get, path, null, token);

    public Task<JsonNode?> PostAsync(string step, string path, object? body, string token) =>
        SendAsync(step, HttpMethod.Post, path, body is null ? null : JsonContent.Create(body, options: Json), token);

    public Task<JsonNode?> PutAsync(string step, string path, object? body, string token) =>
        SendAsync(step, HttpMethod.Put, path, body is null ? null : JsonContent.Create(body, options: Json), token);

    public Task<JsonNode?> PostCsvAsync(string step, string path, string csv, string token)
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        var form = new MultipartFormDataContent { { file, "file", "attendance.csv" } };
        return SendAsync(step, HttpMethod.Post, path, form, token);
    }

    public async Task<string> SignInAsync(string email, string password)
    {
        var node = await SendAsync($"Sign in as {email}", HttpMethod.Post, "api/auth/login",
            JsonContent.Create(new { email, password }, options: Json), token: null);
        return node?["token"]?.GetValue<string>()
            ?? throw new SeedException($"Sign in as {email}", "POST", "api/auth/login", 200, "No token in the response.");
    }

    private async Task<JsonNode?> SendAsync(string step, HttpMethod method, string path, HttpContent? content, string? token)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new SeedException(step, method.Method, path, (int)response.StatusCode, ReadMessage(body));

        return string.IsNullOrWhiteSpace(body) ? null : JsonNode.Parse(body);
    }

    /// <summary>The most useful sentence in an error body: ProblemDetails detail, a message, validation errors, or the text itself.</summary>
    /// <remarks>Never throws: any field with an unexpected shape is skipped rather than blowing up.</remarks>
    internal static string ReadMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "(no body)";

        if (TryParseObject(body) is JsonObject json)
        {
            if (StringValue(json["detail"]) is { } detail) return detail;
            if (StringValue(json["message"]) is { } message) return message;
            if (json["errors"] is JsonObject errors)
            {
                var lines = errors
                    .Select(e => (e.Key, Values: (e.Value as JsonArray)?.Select(StringValue).Where(s => s is { Length: > 0 }).ToArray() ?? []))
                    .Where(e => e.Values.Length > 0)
                    .Select(e => $"{e.Key}: {string.Join(" ", e.Values)}")
                    .ToArray();
                if (lines.Length > 0) return string.Join("; ", lines);
            }
            if (StringValue(json["title"]) is { } title) return title;
        }

        return body.Length > 300 ? body[..300] : body;
    }

    private static JsonObject? TryParseObject(string body)
    {
        try
        {
            return JsonNode.Parse(body) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The node's value when, and only when, it is a JSON string. Never throws.</summary>
    private static string? StringValue(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;
}
