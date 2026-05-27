using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using System.Xml.Linq;

namespace PaymentService.Clients;

public sealed class HorizonClient : IDisposable
{
    private readonly string _baseUrl;
    private readonly string _username;
    private readonly string _password;
    private readonly ILogger<HorizonClient> _logger;
    private readonly HttpClient _http;
    private volatile bool _authenticated;
    private readonly SemaphoreSlim _authLock = new(1, 1);

    public string AccountName { get; }

    public HorizonClient(string accountName, string baseUrl, string username, string password,
        ILogger<HorizonClient> logger)
    {
        AccountName = accountName;
        _baseUrl = baseUrl.TrimEnd('/');
        _username = username;
        _password = password;
        _logger = logger;

        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true
        };
        _http = new HttpClient(handler);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/xml");
    }

    private async Task AuthenticateAsync()
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_username}:{_password}"));
        using var req = new HttpRequestMessage(HttpMethod.Get, _baseUrl + "/rest/user");
        req.Headers.Add("Authorization", $"Basic {credentials}");
        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        _authenticated = true;
        _logger.LogDebug("Horizon [{Account}]: authenticated", AccountName);
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (_authenticated) return;
        await _authLock.WaitAsync();
        try
        {
            if (!_authenticated)
                await AuthenticateAsync();
        }
        finally
        {
            _authLock.Release();
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage resp)
    {
        if (resp.IsSuccessStatusCode) return;
        var body = await resp.Content.ReadAsStringAsync();
        throw new HttpRequestException(
            $"{(int)resp.StatusCode} {resp.ReasonPhrase} — {body[..Math.Min(800, body.Length)]}",
            null, resp.StatusCode);
    }

    // Horizon requires Content-Type: application/xml on all requests, including GETs
    private HttpRequestMessage BuildGetRequest(string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Content-Type", "application/xml");
        return req;
    }

    private async Task<string> GetAsync(string path)
    {
        var url = _baseUrl + path;
        await EnsureAuthenticatedAsync();
        using var req1 = BuildGetRequest(url);
        var resp = await _http.SendAsync(req1);
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
        {
            resp.Dispose();
            _authenticated = false;
            await EnsureAuthenticatedAsync();
            using var req2 = BuildGetRequest(url);
            resp = await _http.SendAsync(req2);
        }
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadAsStringAsync();
    }

    private async Task<string> PostAsync(string path, string xml)
    {
        var url = _baseUrl + path;
        await EnsureAuthenticatedAsync();
        var resp = await _http.PostAsync(url, new StringContent(xml, Encoding.UTF8, "application/xml"));
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
        {
            _authenticated = false;
            await EnsureAuthenticatedAsync();
            resp = await _http.PostAsync(url, new StringContent(xml, Encoding.UTF8, "application/xml"));
        }
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadAsStringAsync();
    }

    public Task<string> GetTemplateAsync(string templateUrl) => GetAsync(templateUrl);

    public async Task<bool> ExistsByDocumentNumberAsync(string documentNumber)
    {
        var escaped = documentNumber.Replace("'", "''");
        var xml = await GetAsync($"/rest/TDdmMUSar/query?filter=PDOK.DOK_NR eq '{escaped}'&columns=PDOK.PK_DOK");
        return XDocument.Parse(xml).Descendants().Any(e => e.Name.LocalName == "row");
    }

    public async Task<string?> GetCustomerRestIdByCodeAsync(string billingCode)
    {
        var xml = await GetAsync($"/rest/TDdmKlSar/query?filter=K.KODS eq {billingCode}");
        return XDocument.Parse(xml)
            .Descendants().FirstOrDefault(e => e.Name.LocalName == "PK_KLIENTS")
            ?.Descendants().FirstOrDefault(e => e.Name.LocalName == "href")?.Value;
    }

    public async Task<string?> GetCountryRestIdByCodeAsync(string countryCode)
    {
        var xml = await GetAsync($"/rest/TdmSLDValsts/query?filter=DV.KODS eq {countryCode}");
        return XDocument.Parse(xml)
            .Descendants().FirstOrDefault(e => e.Name.LocalName == "PK_VALSTS")
            ?.Descendants().FirstOrDefault(e => e.Name.LocalName == "href")?.Value;
    }

    public async Task<string> SaveAsync(string templateUrl, string xml)
    {
        var result = await PostAsync(templateUrl, xml);
        var href = XDocument.Parse(result)
            .Descendants().FirstOrDefault(e => e.Name.LocalName == "href")?.Value;
        if (string.IsNullOrEmpty(href) || !href.StartsWith("/rest/"))
            throw new InvalidOperationException(
                $"Horizon [{AccountName}] save to {templateUrl} returned unexpected response: {result[..Math.Min(500, result.Length)]}");
        return href;
    }

    public void Dispose()
    {
        _http.Dispose();
        _authLock.Dispose();
    }
}
