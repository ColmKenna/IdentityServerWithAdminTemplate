using System.Net;
using System.Net.Http.Json;
using IdentityServerProject.Mobile.Core.Sync.Contract;

namespace IdentityServerProject.Mobile.Core.Sync;

/// <summary>
///     Takes the factory, not a client, and asks for a fresh client per call: this object lives for the
///     whole app run, and a client held that long keeps pooled connections to whatever network the phone
///     was on when it was created.
/// </summary>
public sealed class HttpSyncApi(IHttpClientFactory clients) : ISyncApi
{
    public const string HttpClientName = "sync-api";

    public async Task<PushResponse> PushAsync(PushRequest request, CancellationToken cancellationToken)
    {
        using HttpClient client = clients.CreateClient(HttpClientName);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "api/sync/push", request, SyncJsonContext.Default.PushRequest, cancellationToken);
        EnsureSuccess(response);

        return await response.Content.ReadFromJsonAsync(SyncJsonContext.Default.PushResponse, cancellationToken)
               ?? throw new HttpRequestException("The sync server returned an empty push response.");
    }

    public async Task<PullResponse> PullAsync(string? cursor, int limit, CancellationToken cancellationToken)
    {
        string query = $"api/sync/pull?limit={limit}";
        if (!string.IsNullOrEmpty(cursor)) query += $"&cursor={Uri.EscapeDataString(cursor)}";

        using HttpClient client = clients.CreateClient(HttpClientName);
        using HttpResponseMessage response = await client.GetAsync(query, cancellationToken);
        EnsureSuccess(response);

        return await response.Content.ReadFromJsonAsync(SyncJsonContext.Default.PullResponse, cancellationToken)
               ?? throw new HttpRequestException("The sync server returned an empty pull response.");
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new SyncAuthenticationException($"The sync server answered {(int)response.StatusCode}.");

        response.EnsureSuccessStatusCode();
    }
}
