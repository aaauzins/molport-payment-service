namespace PaymentService.Models;

public enum MatchOutcome
{
    Matched,
    NoMatch,
    AmountMismatch,
    CurrencyMismatch,
    AlreadyProcessed
}

public class MatchResult
{
    public MatchOutcome Outcome { get; set; }
    public required NormalizedTransaction Transaction { get; set; }
    public OpenInvoice? Invoice { get; set; }
    public string? Details { get; set; }
}
