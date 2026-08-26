using BillWatch.Core.Models;

namespace BillWatch.API.Data.Entities;

public enum BillStreamSource
{
    Unknown,
    Manual,
    AutomaticDiscovery
}

public sealed class BillStreamEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public string ProviderName { get; set; } = string.Empty;

    public BillCategory Category { get; set; } = BillCategory.Unknown;

    public BillStreamSource Source { get; set; } =
        BillStreamSource.Unknown;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public ApplicationUser User { get; set; } = null!;
}