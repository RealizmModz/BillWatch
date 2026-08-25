namespace BillWatch.API.Data.Entities;

public sealed class BillLineItemEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public Guid BillStatementId { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string? Category { get; set; }

    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public ApplicationUser User { get; set; } = null!;

    public BillStatementEntity BillStatement { get; set; } = null!;
}