namespace PaymentService.Models;

public class OpenInvoice
{
    public long InvoiceId { get; set; }
    public long OrderId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public long CurrencyId { get; set; }
}
