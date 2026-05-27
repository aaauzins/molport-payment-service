using Microsoft.Extensions.Logging;
using PaymentService.DB;

namespace PaymentService.Services;

public class CurrencyCache
{
    private readonly OracleRepository _repo;
    private readonly ILogger<CurrencyCache> _logger;

    private volatile Dictionary<string, long> _map = Hardcoded;
    private DateTime _lastRefresh = DateTime.MinValue;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    // Fallback in case DB is unreachable — update if currencies are ever added
    public static readonly Dictionary<string, long> Hardcoded = new(StringComparer.OrdinalIgnoreCase)
    {
        { "LVL", 1 },
        { "USD", 2 },
        { "EUR", 3 },
        { "GBP", 7007 },
        { "CHF", 10324 }
    };

    public CurrencyCache(OracleRepository repo, ILogger<CurrencyCache> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<Dictionary<string, long>> GetMapAsync()
    {
        await RefreshIfStaleAsync();
        return _map;
    }

    private async Task RefreshIfStaleAsync()
    {
        if (DateTime.UtcNow - _lastRefresh < TimeSpan.FromDays(1))
            return;

        await _refreshLock.WaitAsync();
        try
        {
            if (DateTime.UtcNow - _lastRefresh < TimeSpan.FromDays(1))
                return;

            var fresh = await _repo.GetCurrencyMapAsync();
            if (fresh.Count > 0)
            {
                _map = fresh;
                _lastRefresh = DateTime.UtcNow;
                _logger.LogDebug("Currency map refreshed from DB: {Count} entries", fresh.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh currency map from DB, using cached values");
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
