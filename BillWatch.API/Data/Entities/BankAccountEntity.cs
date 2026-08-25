namespace BillWatch.API.Data.Entities;

public enum BankAccountType
{
    Unknown,
    Checking,
    Savings,
    CreditCard,
    Loan,
    Other
}

public sealed class BankAccountEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public Guid BankConnectionId { get; set; }

    public string PlaidAccountId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? OfficialName { get; set; }

    public string? Mask { get; set; }

    public BankAccountType AccountType { get; set; } =
        BankAccountType.Unknown;

    public string? AccountSubtype { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public ApplicationUser User { get; set; } = null!;

    public BankConnectionEntity BankConnection { get; set; } = null!;
}