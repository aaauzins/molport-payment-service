using Microsoft.Extensions.Logging.Abstractions;
using PaymentService.Clients;
using PaymentService.Models;

namespace PaymentService.Tests.Integration;

public class SwedBankClientTests
{
    private readonly SwedBankSgwClient _client;

    public SwedBankClientTests()
    {
        var settings = TestConfig.Load();
        _client = new SwedBankSgwClient(
            settings.SwedBank,
            NullLogger<SwedBankSgwClient>.Instance);
    }

    [Fact]
    public async Task CommunicationTestSucceeds()
    {
        await _client.TestConnectionAsync();
        // no exception = 204 received
    }

    [Fact]
    public async Task CanRequestAccountStatement()
    {
        var to = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
        var from = to.AddDays(-7);

        var transactions = (await _client.GetIncomingTransactionsAsync(from, to)).ToList();

        Assert.NotNull(transactions);
        foreach (var tx in transactions)
        {
            Assert.False(string.IsNullOrEmpty(tx.TransactionId));
            Assert.True(tx.Amount > 0);
            Assert.False(string.IsNullOrEmpty(tx.Currency));
            Assert.Equal(PaymentSource.Swedbank, tx.Source);
        }
    }

    [Fact]
    public async Task OnlyReturnsCreditTransactions()
    {
        var to = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
        var from = to.AddDays(-30);

        var transactions = (await _client.GetIncomingTransactionsAsync(from, to)).ToList();

        foreach (var tx in transactions)
            Assert.True(tx.Amount > 0, $"Transaction {tx.TransactionId} has non-positive amount");
    }
}
