namespace BillWatch.API.Data.Entities;

public enum BankConnectionStatus
{
    Active,
    RequiresAttention,
    Disconnected
}

public sealed class BankConnectionEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public string InstitutionName { get; set; } = string.Empty;

    public string? PlaidInstitutionId { get; set; }

    public string? PlaidItemId { get; set; }

    public string? ProtectedPlaidAccessToken { get; set; }

    public string? TransactionsCursor { get; set; }

    public BankConnectionStatus Status { get; set; } =
        BankConnectionStatus.Active;

    public DateTimeOffset? LastSuccessfulSyncAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public ApplicationUser User { get; set; } = null!;
}