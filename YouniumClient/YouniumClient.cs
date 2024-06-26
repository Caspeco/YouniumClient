using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;

namespace Younium;

public class YouniumClient(bool isProd, string legalEntity)
{
    public bool IsProd => isProd;
    private static readonly HttpClient _httpClient = new();

    public YouniumToken? TokenCachePublic => TokenCache;
    private YouniumToken? TokenCache;

    private async Task<YouniumToken> EnsureHttpTokenAsync()
    {
        var existingTokenExpires = TokenCache?.Expires is null ? (DateTime?)null : DateTime.Parse(TokenCache.Expires);
        if (existingTokenExpires is not null && DateTime.UtcNow.AddMinutes(30) < existingTokenExpires)
        {
            return TokenCache!;
        }
        var token = await YouniumToken.GetJwtTokenCached(_httpClient, isProd);
        TokenCache = token;
        if (string.IsNullOrEmpty(token!.AccessToken))
            throw new Exception(string.Join(", ", token!.Errors ?? ["No token"]));
        var headers = _httpClient.DefaultRequestHeaders;
        headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken!);
        headers.Accept.Clear();
        headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        headers.Remove("legal-entity");
        headers.Add("legal-entity", legalEntity);
        return token;
    }

    internal static string GetProdSandboxString(bool isProd) => isProd ? "Prod" : "Sandbox";
    internal static string GetApi(bool isProd) => isProd ? "https://api.younium.com" : "https://api.sandbox.younium.com";

    private static HttpRequestMessage SetHeaders(HttpRequestMessage request, string apiver = "2.1")
    {
        var headers = request.Headers;
        headers.Add("api-version", apiver);
        return request;
    }

    private async Task<JObject> SendAsync(HttpRequestMessage request, string apiver = "2.1")
    {
        await EnsureHttpTokenAsync();
        var resp = await _httpClient.SendAsync(SetHeaders(request, apiver));

        var json = await resp.Content.ReadAsStringAsync();
        if (json.StartsWith('{') && json.EndsWith('}'))
        {
            // Caller needs to deal with the type
            // Such as {"errors":{"message":["No accounts could be found"]},"type":"https://tools.ietf.org/html/rfc7231#section-6.5.1","title":"One or more validation errors occurred.","status":400,"traceId":"00-3d2bb54c2d84258e9d15f951f7c337a8-2ad73a76575f4b69-01"}
            return JObject.Parse(json);
        }
        var tok = JToken.Parse(json);
        if (!resp.IsSuccessStatusCode)
        {
            Console.WriteLine($"{resp.StatusCode} {tok}");
        }
        resp.EnsureSuccessStatusCode();
        return tok as JObject ?? new JObject { ["data"] = tok };
    }

    private Task<JObject> SendAsync(HttpMethod method, string endpoint, JObject jobj)
    {
        var request = new HttpRequestMessage(method, new Uri($"{GetApi(isProd)}/{endpoint}"));
        var content = new StringContent(jobj.ToString(Formatting.None), null, "application/json");
        request.Content = content;
        return SendAsync(request);
    }

    private async Task<YouniumPageResult> GetPage(Uri uri)
    {
        var jobj = await SendAsync(new HttpRequestMessage(HttpMethod.Get, uri));
        return jobj.ToObject<YouniumPageResult>()!;
    }

    private static Uri GetPageUri(Uri src, int pageNumber)
    {
        UriBuilder builder = new(src);
        // TODO use query builder and change pageNumber instead?
        if (builder.Query.Contains('?'))
            builder.Query += "&";
        builder.Query += $"pageNumber={pageNumber}&pageSize=100";
        return builder.Uri;
    }

    public Task<YouniumPageResult> GetPages(string endpoint) => GetPages(new Uri($"{GetApi(isProd)}/{endpoint}"));

    private async Task<YouniumPageResult> GetPages(Uri uri)
    {
        var res = await GetPage(GetPageUri(uri, 1));
        var resdict =
            Enumerable.Range(res.pageNumber + 1, res.totalPages - res.pageNumber)
            .AsParallel()
            .ToDictionary(n => n, async n => await GetPage(GetPageUri(uri, n)))
            .OrderBy(kvp => kvp.Key);
        foreach (var d in resdict)
        {
            var pgRes = await d.Value;
            res.Add(pgRes);
        }
        // TODO verify that all urls was fetched?
        while (res.nextPage is not null && res.lastPage is null)
        {
            var pgRes = await GetPage(res.nextPage);
            res.Add(pgRes);
        }
        return res;
    }

    protected static string GetFilterQuery(string? filter) => filter is null ? "" : $"?filter={filter}";

    public Task<YouniumPageResult> GetProductsAsync() => GetPages("Products");

    public Task<YouniumPageResult> GetAccountsAsync(string? filter) => GetPages($"accounts{GetFilterQuery(filter)}");

    public Task<YouniumPageResult> GetSubscriptionsAsync(string? filter = null) => GetPages($"Subscriptions{GetFilterQuery(filter)}");

    public Task<YouniumPageResult> GetUsagesAsync(string? filter = null) => GetPages($"Usage{GetFilterQuery(filter)}");

    public async Task<JObject> CreateAsync(string endpoint, YouniumId youniumObject)
    {
        var resp = await SendAsync(HttpMethod.Post, endpoint, JObject.FromObject(youniumObject, new JsonSerializer { NullValueHandling = NullValueHandling.Ignore }));
        youniumObject.id = resp.Value<string>("id");
        return resp;
    }

    public async Task<JObject> CreateAsync(string endpoint, JObject youniumObject)
    {
        var resp = await SendAsync(HttpMethod.Post, endpoint, youniumObject);
        youniumObject["id"] = resp.Value<string>("id");
        return resp;
    }

    public Task<JObject> UpdateAsync(string endpoint, JObject youniumObject)
    {
        return SendAsync(HttpMethod.Patch, endpoint, youniumObject);
    }
}

