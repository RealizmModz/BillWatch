using BillWatch.API.Data.Configurations;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Data.Entities;

[EntityTypeConfiguration(
    typeof(AdminAuditLogEntityConfiguration))]
public sealed class AdminAuditLogEntity
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public Guid ActorUserId { get; set; }

    public Guid? TargetUserId { get; set; }

    public string Action { get; set; } =
        string.Empty;

    public string SubjectType { get; set; } =
        string.Empty;

    public Guid? SubjectId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public ApplicationUser ActorUser { get; set; } =
        null!;

    public ApplicationUser? TargetUser { get; set; }
}
