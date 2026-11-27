using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DisputePortal.Modules.Disputes.Application;
using DisputePortal.SharedKernel.Web;

namespace DisputePortal.IntegrationTests.Infrastructure;

/// <summary>The handful of HTTP calls the tests repeat, and how they read the API's answers.</summary>
internal static class PortalHttp
{
    /// <summary>The API's JSON conventions: camelCase names and enums as text.</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Raises a dispute. Each call uses a new idempotency key unless one is given.</summary>
    public static Task<HttpResponseMessage> SubmitDisputeAsync(
        this HttpClient client, Guid transactionId, string reasonId, string? comment = null, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/disputes")
        {
            Content = JsonContent.Create(new SubmitDisputeRequest(transactionId, reasonId, comment), options: Json),
        };
        request.Headers.Add(ApiConventions.IdempotencyKeyHeader, idempotencyKey ?? Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> PostAsync(this HttpClient client, string path) => client.PostAsync(path, content: null);

    public static Task<HttpResponseMessage> PostAsync<T>(this HttpClient client, string path, T body) =>
        client.PostAsJsonAsync(path, body, Json);

    public static async Task<T> GetAsync<T>(this HttpClient client, string path) =>
        await (await client.GetAsync(path)).ReadAsync<T>();

    /// <summary>Checks the status code, showing the response body when it is not the expected one, and reads the body.</summary>
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(expected, body);
        return JsonSerializer.Deserialize<T>(body, Json) ?? throw new JsonException("The response body was empty.");
    }

    /// <summary>Checks that the response is a problem document with this status and this stable error code.</summary>
    public static async Task ShouldBeProblemAsync(this HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, body);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        JsonDocument.Parse(body).RootElement.GetProperty("code").GetString().ShouldBe(code);
    }
}
