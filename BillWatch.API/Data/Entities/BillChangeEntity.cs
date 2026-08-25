namespace BillWatch.API.Data.Entities;

public enum BillChangeType
{
    Unknown,
    TotalIncrease,
    TotalDecrease,
    LineItemAdded,
    LineItemRemoved,
    LineItemIncrease,
    LineItemDecrease
}

public enum BillChangeConfidence
{
    Unknown,
    Possible,
    StrongInference,
    Confirmed
}

public sealed class BillChangeEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public Guid BillStreamId { get; set; }

    public Guid? PreviousStatementId { get; set; }

    public Guid CurrentStatementId { get; set; }

    public BillChangeType ChangeType { get; set; } =
        BillChangeType.Unknown;

    public BillChangeConfidence Confidence { get; set; } =
        BillChangeConfidence.Unknown;

    public string Description { get; set; } = string.Empty;

    public decimal PreviousAmount { get; set; }

    public decimal CurrentAmount { get; set; }

    public decimal AmountDifference { get; set; }

    public decimal AnnualizedImpact { get; set; }

    public bool IsAcknowledged { get; set; }

    public DateTimeOffset DetectedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public DateTimeOffset CreatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public ApplicationUser User { get; set; } = null!;

    public BillStreamEntity BillStream { get; set; } = null!;

    public BillStatementEntity? PreviousStatement { get; set; }

    public BillStatementEntity CurrentStatement { get; set; } = null!;
}