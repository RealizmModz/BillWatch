namespace BillWatch.API.Data.Entities;

public enum PlaidLinkSessionStatus
{
    Pending,
    Completed,
    Exited,
    Expired,
    Failed
}

public sealed class PlaidLinkSessionEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public string ProtectedLinkToken { get; set; } = string.Empty;

    public PlaidLinkSessionStatus Status { get; set; } =
        PlaidLinkSessionStatus.Pending;

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public ApplicationUser User { get; set; } = null!;
}