using Microsoft.Extensions.Logging.Abstractions;
using PaymentService.Clients;
using PaymentService.Models;

namespace PaymentService.Tests.Integration;

public class StripeClientTests
{
    private readonly AppSettings _settings;

    public StripeClientTests()
    {
        _settings = TestConfig.Load();
    }

    [Fact]
    public async Task CanConnectAndListRecentCharges_AllAccounts()
    {
        foreach (var account in _settings.StripeAccounts)
        {
            var client = new StripeApiClient(account.Name, account.ApiKey, NullLogger<StripeApiClient>.Instance);
            var since = DateTime.UtcNow.AddDays(-30);

            var transactions = (await client.GetSucceededChargesSinceAsync(since)).ToList();

            Assert.NotNull(transactions);
            foreach (var tx in transactions)
            {
                Assert.False(string.IsNullOrEmpty(tx.TransactionId),
                    $"[{account.Name}] TransactionId is empty");
                Assert.True(tx.Amount > 0,
                    $"[{account.Name}] Amount is 0 for {tx.TransactionId}");
                Assert.False(string.IsNullOrEmpty(tx.Currency),
                    $"[{account.Name}] Currency is empty for {tx.TransactionId}");
            }
        }
    }

    [Fact]
    public async Task ChargesHaveExpectedFields_AllAccounts()
    {
        foreach (var account in _settings.StripeAccounts)
        {
            var client = new StripeApiClient(account.Name, account.ApiKey, NullLogger<StripeApiClient>.Instance);
            var transactions = (await client.GetSucceededChargesSinceAsync(DateTime.UtcNow.AddDays(-90))).ToList();

            if (transactions.Count == 0) continue;

            var sample = transactions.First();
            Assert.StartsWith("ch_", sample.TransactionId);
            Assert.Equal(PaymentSource.Stripe, sample.Source);
            Assert.Contains(sample.Currency.ToUpperInvariant(), new[] { "USD", "EUR", "GBP", "CHF" });
        }
    }
}
