using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Taskel.Api.Tests;

/// <summary>JSON helpers matching the API's wire format (camelCase, enums as strings).</summary>
public static class ApiJson
{
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
