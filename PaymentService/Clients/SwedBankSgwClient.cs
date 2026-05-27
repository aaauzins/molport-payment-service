using Microsoft.Extensions.Logging;
using PaymentService.Models;
using PaymentService.Services;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;

namespace PaymentService.Clients;

public class SwedBankSgwClient
{
    private readonly HttpClient _http;
    private readonly SwedBankSettings _settings;
    private readonly ILogger<SwedBankSgwClient> _logger;

    private readonly string _sgwBase;
    private readonly string _clientId;

    private static readonly XNamespace Camt053 = "urn:iso:std:iso:20022:tech:xsd:camt.053.001.02";

    private static readonly HashSet<string> IncomingTransactionCodes =
        new(StringComparer.OrdinalIgnoreCase) { "INB", "MK", "MV", "IM", "IMG", "IMB", "IME", "MP" };

    public SwedBankSgwClient(SwedBankSettings settings, ILogger<SwedBankSgwClient> logger)
    {
        _settings = settings;
        _logger = logger;
        _sgwBase = settings.UseSandbox
            ? "https://psd2.api.swedbank.com/partner/sandbox/v1/sgw"
            : "https://psd2.api.swedbank.com/partner/v1/sgw";
        _clientId = settings.UseSandbox ? settings.SandboxClientId : settings.ClientId;

        var handler = new HttpClientHandler();
        if (!string.IsNullOrEmpty(settings.CertificatePath))
        {
            var certPath = Path.IsPathRooted(settings.CertificatePath)
                ? settings.CertificatePath
                : Path.GetFullPath(settings.CertificatePath, AppContext.BaseDirectory);
            var cert = X509CertificateLoader.LoadPkcs12FromFile(
                certPath, settings.CertificatePassword);
            handler.ClientCertificates.Add(cert);

            var daysUntilExpiry = (cert.NotAfter.ToUniversalTime() - DateTime.UtcNow).Days;
            if (daysUntilExpiry < 30)
                _logger.LogWarning("SGW transport certificate expires in {Days} days ({Date:yyyy-MM-dd}) — renew soon",
                    daysUntilExpiry, cert.NotAfter);
            else
                _logger.LogInformation("Loaded SGW transport certificate from {Path}, expires {Date:yyyy-MM-dd} ({Days} days)",
                    certPath, cert.NotAfter, daysUntilExpiry);
        }
        else
        {
            _logger.LogWarning("No SGW transport certificate configured — mTLS will not be established");
        }
        _http = new HttpClient(handler);
    }

    public async Task TestConnectionAsync()
    {
        const string pingXml = """<?xml version="1.0" encoding="UTF-8"?><Ping><Value>Test</Value></Ping>""";
        using var content = new StringContent(pingXml, Encoding.UTF8, "application/xml");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_sgwBase}/communication-tests?client_id={Uri.EscapeDataString(_clientId)}");
        request.Content = content;
        AddCommonHeaders(request, includeAgreementId: true);

        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"SGW communication test {response.StatusCode}: {error}");
        }
        _logger.LogInformation("SGW communication test succeeded");
    }

    public async Task<IEnumerable<NormalizedTransaction>> GetIncomingTransactionsAsync(
        DateOnly from, DateOnly to)
    {
        var msgId = DateTime.UtcNow.ToString("yyyyMMddHHmmssff");
        var requestId = Guid.NewGuid().ToString();
        var requestXml = BuildCamt060Request(msgId, from, to);

        using var content = new StringContent(requestXml, Encoding.UTF8, "application/xml");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_sgwBase}/account-statements?client_id={Uri.EscapeDataString(_clientId)}");
        request.Content = content;
        AddCommonHeaders(request, includeAgreementId: true, requestId: requestId);

        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode(); // expects 204
        _logger.LogInformation("SGW account statement request sent for {From}–{To}", from, to);

        var (xml, trackingId) = await PollInboxAsync(requestId);
        if (xml == null)
            return [];

        var transactions = ParseCamtResponse(xml).ToList();
        _logger.LogInformation("Fetched {Count} incoming bank transactions for {From}–{To}",
            transactions.Count, from, to);

        if (trackingId != null)
            await DeleteMessageAsync(trackingId);

        return transactions;
    }

    private async Task<(string? xml, string? trackingId)> PollInboxAsync(
        string expectedRequestId, int maxAttempts = 20, int delaySeconds = 15)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));

            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_sgwBase}/messages?client_id={Uri.EscapeDataString(_clientId)}");
            AddCommonHeaders(req, includeAgreementId: false);

            var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();

            if (resp.StatusCode == System.Net.HttpStatusCode.NoContent ||
                resp.Content.Headers.ContentLength == 0)
            {
                _logger.LogDebug("SGW inbox empty (attempt {Attempt}/{Max})", i + 1, maxAttempts);
                continue;
            }

            var body = await resp.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(body))
            {
                _logger.LogDebug("SGW inbox empty (attempt {Attempt}/{Max})", i + 1, maxAttempts);
                continue;
            }

            resp.Headers.TryGetValues("X-Request-ID", out var requestIdValues);
            var responseRequestId = requestIdValues?.FirstOrDefault();
            if (responseRequestId != null && responseRequestId != expectedRequestId)
            {
                _logger.LogWarning("SGW inbox: message X-Request-ID {ResponseId} does not match ours {ExpectedId}, skipping",
                    responseRequestId, expectedRequestId);
                continue;
            }

            resp.Headers.TryGetValues("X-Tracking-ID", out var trackingValues);
            var trackingId = trackingValues?.FirstOrDefault();

            _logger.LogInformation("SGW inbox: message received after {Attempt} attempt(s)", i + 1);
            return (body, trackingId);
        }

        _logger.LogWarning("SGW inbox: no response received after {Max} attempts", maxAttempts);
        return (null, null);
    }

    private async Task DeleteMessageAsync(string trackingId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete,
            $"{_sgwBase}/messages?trackingId={Uri.EscapeDataString(trackingId)}&client_id={Uri.EscapeDataString(_clientId)}");
        AddCommonHeaders(req, includeAgreementId: false);

        var resp = await _http.SendAsync(req);
        if (resp.IsSuccessStatusCode)
            _logger.LogDebug("SGW message {TrackingId} deleted from inbox", trackingId);
        else
            _logger.LogWarning("SGW message delete failed for {TrackingId}: {Status}", trackingId, resp.StatusCode);
    }

    private void AddCommonHeaders(HttpRequestMessage req, bool includeAgreementId, string? requestId = null)
    {
        req.Headers.Add("X-Request-ID", requestId ?? Guid.NewGuid().ToString());
        req.Headers.Date = DateTimeOffset.UtcNow;
        if (includeAgreementId)
            req.Headers.Add("X-Agreement-ID", _settings.AgreementId.ToString());
    }

    private IEnumerable<NormalizedTransaction> ParseCamtResponse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var ns = doc.Root?.Name.Namespace ?? Camt053;
        foreach (var entry in doc.Descendants(ns + "Ntry"))
        {
            if (entry.Element(ns + "CdtDbtInd")?.Value != "CRDT") continue;

            var txCode = entry.Descendants(ns + "Cd").FirstOrDefault()?.Value
                ?? entry.Descendants(ns + "Prtry").FirstOrDefault()?.Value;
            if (txCode != null && !IncomingTransactionCodes.Contains(txCode)) continue;

            var amtEl = entry.Element(ns + "Amt");
            if (!decimal.TryParse(amtEl?.Value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var amount)) continue;
            var currency = amtEl?.Attribute("Ccy")?.Value ?? string.Empty;

            var dateStr = entry.Descendants(ns + "Dt").FirstOrDefault()?.Value
                ?? entry.Descendants(ns + "DtTm").FirstOrDefault()?.Value;
            if (!DateTime.TryParse(dateStr, out var txDate)) txDate = DateTime.UtcNow;

            var txDetails = entry.Descendants(ns + "TxDtls").FirstOrDefault() ?? entry;
            var remittance = txDetails.Descendants(ns + "Ustrd").FirstOrDefault()?.Value
                ?? txDetails.Descendants(ns + "AddtlTxInf").FirstOrDefault()?.Value
                ?? string.Empty;
            var endToEndId = txDetails.Descendants(ns + "EndToEndId").FirstOrDefault()?.Value
                ?? string.Empty;

            var description = string.IsNullOrEmpty(remittance) ? endToEndId : remittance;
            var reference = ReferenceExtractor.Extract(description)
                ?? ReferenceExtractor.Extract(endToEndId);

            var txId = string.IsNullOrEmpty(endToEndId)
                ? $"SWB-{txDate:yyyyMMdd}-{amount}-{currency}"
                : $"SWB-{endToEndId}";

            yield return new NormalizedTransaction
            {
                Source = PaymentSource.Swedbank,
                TransactionId = txId,
                TransactionDate = txDate,
                Amount = amount,
                Currency = currency,
                Description = description,
                ExtractedReference = reference
            };
        }
    }

    private string BuildCamt060Request(string msgId, DateOnly from, DateOnly to)
    {
        var msgType = to == DateOnly.FromDateTime(DateTime.Today)
            ? "camt.052.001.02"
            : "camt.053.001.02";

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Document xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                      xmlns="urn:iso:std:iso:20022:tech:xsd:camt.060.001.03">
              <AcctRptgReq>
                <GrpHdr>
                  <MsgId>{msgId}</MsgId>
                  <CreDtTm>{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss}</CreDtTm>
                </GrpHdr>
                <RptgReq>
                  <Id>{msgId}</Id>
                  <ReqdMsgNmId>{msgType}</ReqdMsgNmId>
                  <Acct>
                    <Id><IBAN>{_settings.Iban}</IBAN></Id>
                  </Acct>
                  <AcctOwnr><Pty/></AcctOwnr>
                  <RptgPrd>
                    <FrToDt>
                      <FrDt>{from:yyyy-MM-dd}</FrDt>
                      <ToDt>{to:yyyy-MM-dd}</ToDt>
                    </FrToDt>
                    <Tp>ALLL</Tp>
                  </RptgPrd>
                </RptgReq>
              </AcctRptgReq>
            </Document>
            """;
    }
}
