using Microsoft.AspNetCore.Identity;

namespace BillWatch.API.Data.Entities;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public DateTimeOffset CreatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public DateTimeOffset? LastLoginAtUtc { get; set; }

    public bool IsActive { get; set; } = true;
}