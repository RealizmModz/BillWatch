namespace BillWatch.API.Data.Entities;

public sealed class BankTransactionEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public Guid BankAccountId { get; set; }

    public Guid? BillStreamId { get; set; }

    public string PlaidTransactionId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? MerchantName { get; set; }

    public decimal Amount { get; set; }

    public string? IsoCurrencyCode { get; set; }

    public DateOnly PostedDate { get; set; }

    public DateOnly? AuthorizedDate { get; set; }

    public bool IsPending { get; set; }

    public bool IsRemoved { get; set; }

    public string? CategoryPrimary { get; set; }

    public string? CategoryDetailed { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public ApplicationUser User { get; set; } = null!;

    public BankAccountEntity BankAccount { get; set; } = null!;

    public BillStreamEntity? BillStream { get; set; }
}