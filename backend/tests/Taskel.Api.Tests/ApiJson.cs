using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Taskel.Api.Tests;

/// <summary>JSON helpers matching the API's wire format (camelCase, enums as strings).</summary>
public static class ApiJson
{
    /// <summary>
    /// Request bodies for a theory, as JSON text: plain strings keep each row serializable, so test
    /// runners list them one by one. Turn a row back into a body with <see cref="Element"/>.
    /// </summary>
    public static TheoryData<string> Bodies(params object[] bodies) =>
        new(bodies.Select(body => System.Text.Json.JsonSerializer.Serialize(body)));

    public static System.Text.Json.JsonElement Element(string json) =>
        System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();

    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static Task<HttpResponseMessage> PostApiAsync<T>(this HttpClient client, string url, T body) =>
        client.PostAsJsonAsync(url, body, Options);

    public static Task<HttpResponseMessage> PutApiAsync<T>(this HttpClient client, string url, T body) =>
        client.PutAsJsonAsync(url, body, Options);

    public static Task<T?> GetApiAsync<T>(this HttpClient client, string url) =>
        client.GetFromJsonAsync<T>(url, Options);

    public static Task<T?> ReadApiAsync<T>(this HttpContent content) =>
        content.ReadFromJsonAsync<T>(Options);
}
