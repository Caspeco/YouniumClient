using Newtonsoft.Json.Linq;

namespace Younium;

public class YouniumToken
{
    public YouniumToken() { }

    private YouniumToken(JObject data)
    {
        AccessToken = data.Value<string>("accessToken");
        Errors = data.Value<JArray>("errors")?.ToObject<string[]>();
        RefreshToken = data.Value<string>("refreshToken");
        Expires = data.Value<string>("expires");
    }

    public string? AccessToken { get; set; }
    public string?[]? Errors { get; set; }
    public string? RefreshToken { get; set; }
    public string? Expires { get; set; }

    public static void SetSsoSecretGetter(Func<string, ValueTask<string>> func) => _SecretFunc = func;
    private static Func<string, ValueTask<string>>? _SecretFunc = x => throw new NotImplementedException(x);

    private static Task<T?> GetCachedOrCallDummy<T>(string key, Func<Task<T?>> doFetch, int ttlMinutes) where T : class => doFetch();
    public static Func<string, Func<Task<YouniumToken?>>, int, Task<YouniumToken?>> GetCachedOrCall = GetCachedOrCallDummy;

    // TODO handling of token Expires
    public static Task<YouniumToken?> GetJwtTokenCached(HttpClient _httpClient, bool isProd) =>
        GetCachedOrCall($"{nameof(YouniumToken)}{YouniumClient.GetProdSandboxString(isProd)}",
            async () => await GetJwtAsync(_httpClient, isProd), 30);

    // does not follow RequestClientCredentialsTokenAsync
    public static async Task<YouniumToken> GetJwtAsync(HttpClient httpClient, bool isProd) =>
        new(await GetJObjectFrom(httpClient.PostAsJsonAsync(
            YouniumClient.GetApi(isProd) + "/auth/token",
            new
            {
                clientId = await (_SecretFunc ?? throw new NullReferenceException())($"Younium{YouniumClient.GetProdSandboxString(isProd)}ClientId"),
                secret = await (_SecretFunc ?? throw new NullReferenceException())($"Younium{YouniumClient.GetProdSandboxString(isProd)}Secret"),
            })));

    private static async Task<JObject> GetJObjectFrom(Task<HttpResponseMessage> respTask) => 
        JObject.Parse(await (await respTask).Content.ReadAsStringAsync());
}
