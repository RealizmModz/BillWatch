using Microsoft.AspNetCore.Identity;

namespace BillWatch.API.Data.Entities;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public DateTimeOffset CreatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public DateTimeOffset? LastLoginAtUtc { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<SubscriptionEntitlementEntity> SubscriptionEntitlements
    {
        get;
        set;
    } = [];

    public ICollection<SubscriptionEntitlementEntity> GrantedSubscriptionEntitlements
    {
        get;
        set;
    } = [];

    public ICollection<UserProgramMembershipEntity> ProgramMemberships
    {
        get;
        set;
    } = [];

    public ICollection<UserProgramMembershipEntity> GrantedProgramMemberships
    {
        get;
        set;
    } = [];

    public ICollection<SubscriptionAccessKeyEntity> CreatedSubscriptionAccessKeys
    {
        get;
        set;
    } = [];

    public ICollection<SubscriptionAccessKeyRedemptionEntity> SubscriptionAccessKeyRedemptions
    {
        get;
        set;
    } = [];

    public ICollection<AdminAuditLogEntity> AdminAuditActions
    {
        get;
        set;
    } = [];

    public ICollection<AdminAuditLogEntity> AdminAuditTargets
    {
        get;
        set;
    } = [];
}