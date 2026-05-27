using Microsoft.Extensions.Logging.Abstractions;
using PaymentService.Clients;

namespace PaymentService.Tests.Integration;

public class HorizonClientTests : IDisposable
{
    private readonly HorizonClient _sia;
    private readonly HorizonClient _inc;

    public HorizonClientTests()
    {
        var settings = TestConfig.Load().Horizon;
        _sia = new HorizonClient("SIA", settings.BaseUrl, settings.UserNameSia, settings.PasswordSia,
            NullLogger<HorizonClient>.Instance);
        _inc = new HorizonClient("INC", settings.BaseUrl, settings.UserNameInc, settings.PasswordInc,
            NullLogger<HorizonClient>.Instance);
    }

    [Fact]
    public async Task SiaCanAuthenticateAndFetchPaymentOrderTemplate()
    {
        var xml = await _sia.GetTemplateAsync("/rest/TDdmInMu/template/10");
        Assert.False(string.IsNullOrWhiteSpace(xml));
        Assert.Contains("DOK_NR", xml);
    }

    [Fact]
    public async Task IncCanAuthenticateAndFetchPaymentOrderTemplate()
    {
        var xml = await _inc.GetTemplateAsync("/rest/TDdmInMu/template/10");
        Assert.False(string.IsNullOrWhiteSpace(xml));
        Assert.Contains("DOK_NR", xml);
    }

    [Fact]
    public async Task SiaCanFetchMemorialOrderTemplate()
    {
        // Template 235 is SIA's memorial order template (accessible by user "integ")
        var xml = await _sia.GetTemplateAsync("/rest/TDdmMO/template/235");
        Assert.False(string.IsNullOrWhiteSpace(xml));
        Assert.Contains("DOK_NR",    xml);
        Assert.Contains("SUMMA_APM", xml);
    }

    [Fact]
    public async Task IncCanFetchMemorialOrderTemplate()
    {
        // Template 15 is INC's memorial order template (accessible by user "US-INTEG")
        var xml = await _inc.GetTemplateAsync("/rest/TDdmMO/template/15");
        Assert.False(string.IsNullOrWhiteSpace(xml));
        Assert.Contains("DOK_NR",    xml);
        Assert.Contains("SUMMA_APM", xml);
    }

    [Fact]
    public async Task SiaCanFetchCustomerTemplate()
    {
        var xml = await _sia.GetTemplateAsync("/rest/TDdmCustomer/template/190");
        Assert.False(string.IsNullOrWhiteSpace(xml));
        Assert.Contains("KODS", xml);
        Assert.Contains("NOSAUK", xml);
    }

    [Fact]
    public async Task SiaDocumentNumberCheckDoesNotThrow()
    {
        // Uses a synthetic ID that should never exist — just verifies the query round-trip works
        var exists = await _sia.ExistsByDocumentNumberAsync("ch_test_nonexistent_00000");
        Assert.False(exists);
    }

    public void Dispose()
    {
        _sia.Dispose();
        _inc.Dispose();
    }
}
