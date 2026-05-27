using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentService.Clients;
using PaymentService.DB;
using PaymentService.Services;

namespace PaymentService.Workers;

public class SwedBankWorker : BackgroundService
{
    private readonly SwedBankSgwClient _sgw;
    private readonly PaymentMatchingService _matcher;
    private readonly OracleRepository _repo;
    private readonly ILogger<SwedBankWorker> _logger;
    private readonly TimeSpan _pollInterval;

    private const string Source = "SWEDBANK";

    public SwedBankWorker(SwedBankSgwClient sgw, PaymentMatchingService matcher,
        OracleRepository repo, IOptions<AppSettings> settings, ILogger<SwedBankWorker> logger)
    {
        _sgw = sgw;
        _matcher = matcher;
        _repo = repo;
        _logger = logger;
        _pollInterval = TimeSpan.FromHours(settings.Value.SwedBankPollIntervalHours);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SwedBankWorker started, polling every {Interval}", _pollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SwedBankWorker cycle failed");
            }

            await Task.Delay(_pollInterval, stoppingToken);
        }
    }

    private async Task RunCycleAsync()
    {
        var lastSync = await _repo.GetLastSyncDateAsync(Source);
        var from = DateOnly.FromDateTime(lastSync ?? DateTime.UtcNow.AddDays(-7));
        var to = DateOnly.FromDateTime(DateTime.Today);

        _logger.LogInformation("SwedBank: fetching transactions {From} → {To}", from, to);
        var transactions = await _sgw.GetIncomingTransactionsAsync(from, to);
        await _matcher.ProcessTransactionsAsync(transactions);
        await _repo.UpsertSyncStateAsync(Source, DateTime.UtcNow);
    }
}
