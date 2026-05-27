namespace PaymentService.Models;

public enum PaymentSource { Stripe, Swedbank }

public class NormalizedTransaction
{
    public PaymentSource Source { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public DateTime TransactionDate { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ExtractedReference { get; set; }
    public decimal? StripeFee { get; set; }
    public string? AccountName { get; set; }
}
