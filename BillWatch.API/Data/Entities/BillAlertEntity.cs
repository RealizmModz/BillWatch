namespace BillWatch.API.Data.Entities;

public enum BillAlertType
{
    Unknown,
    BillIncrease,
    BillDecrease,
    NewFee,
    RemovedDiscount,
    PaymentDue,
    ConnectionIssue
}

public enum BillAlertSeverity
{
    Info,
    Warning,
    Critical
}

public sealed class BillAlertEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public Guid? BillStreamId { get; set; }

    public Guid? BillChangeId { get; set; }

    public BillAlertType AlertType { get; set; } =
        BillAlertType.Unknown;

    public BillAlertSeverity Severity { get; set; } =
        BillAlertSeverity.Info;

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public bool IsRead { get; set; }

    public bool IsDismissed { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public ApplicationUser User { get; set; } = null!;

    public BillStreamEntity? BillStream { get; set; }

    public BillChangeEntity? BillChange { get; set; }
}