namespace BillWatch.API.Data.Entities;

public sealed class BillStatementEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public Guid BillStreamId { get; set; }

    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    public DateOnly? StatementDate { get; set; }

    public DateOnly? DueDate { get; set; }

    public decimal TotalAmount { get; set; }

    public string CurrencyCode { get; set; } = "USD";

    public string? ProviderStatementId { get; set; }

    public DateTimeOffset RetrievedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public DateTimeOffset CreatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public ApplicationUser User { get; set; } = null!;

    public BillStreamEntity BillStream { get; set; } = null!;
}