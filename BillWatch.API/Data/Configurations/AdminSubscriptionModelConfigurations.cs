using BillWatch.API.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillWatch.API.Data.Configurations;

internal sealed class SubscriptionEntitlementEntityConfiguration
    : IEntityTypeConfiguration<SubscriptionEntitlementEntity>
{
    public void Configure(
        EntityTypeBuilder<SubscriptionEntitlementEntity> entity)
    {
        entity.ToTable(
            "SubscriptionEntitlements",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_SubscriptionEntitlements_Period",
                    "\"EndsAtUtc\" IS NULL OR \"EndsAtUtc\" > \"StartsAtUtc\"");

                table.HasCheckConstraint(
                    "CK_SubscriptionEntitlements_Revocation",
                    "(\"IsRevoked\" = FALSE AND \"RevokedAtUtc\" IS NULL) OR (\"IsRevoked\" = TRUE AND \"RevokedAtUtc\" IS NOT NULL)");
            });

        entity.HasKey(entitlement => entitlement.Id);

        entity.HasAlternateKey(entitlement => new
        {
            entitlement.Id,
            entitlement.UserId
        });

        entity.Property(entitlement => entitlement.Tier)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        entity.Property(entitlement => entitlement.Source)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        entity.Property(entitlement => entitlement.StartsAtUtc)
            .IsRequired();

        entity.Property(entitlement => entitlement.IsRevoked)
            .IsRequired();

        entity.Property(entitlement => entitlement.CreatedAtUtc)
            .IsRequired();

        entity.Property(entitlement => entitlement.UpdatedAtUtc)
            .IsRequired();

        entity.HasIndex(entitlement => entitlement.UserId);

        entity.HasIndex(entitlement => new
        {
            entitlement.UserId,
            entitlement.IsRevoked,
            entitlement.StartsAtUtc,
            entitlement.EndsAtUtc
        });

        entity.HasOne(entitlement => entitlement.User)
            .WithMany(user => user.SubscriptionEntitlements)
            .HasForeignKey(entitlement => entitlement.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(entitlement => entitlement.GrantedByUser)
            .WithMany(user => user.GrantedSubscriptionEntitlements)
            .HasForeignKey(entitlement => entitlement.GrantedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class UserProgramMembershipEntityConfiguration
    : IEntityTypeConfiguration<UserProgramMembershipEntity>
{
    public void Configure(
        EntityTypeBuilder<UserProgramMembershipEntity> entity)
    {
        entity.ToTable(
            "UserProgramMemberships",
            table =>
                table.HasCheckConstraint(
                    "CK_UserProgramMemberships_Period",
                    "\"EndsAtUtc\" IS NULL OR \"EndsAtUtc\" > \"StartsAtUtc\""));

        entity.HasKey(membership => membership.Id);

        entity.HasAlternateKey(membership => new
        {
            membership.Id,
            membership.UserId
        });

        entity.Property(membership => membership.Program)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        entity.Property(membership => membership.StartsAtUtc)
            .IsRequired();

        entity.Property(membership => membership.IsActive)
            .IsRequired();

        entity.Property(membership => membership.CreatedAtUtc)
            .IsRequired();

        entity.Property(membership => membership.UpdatedAtUtc)
            .IsRequired();

        entity.HasIndex(membership => new
            {
                membership.UserId,
                membership.Program
            })
            .IsUnique();

        entity.HasIndex(membership => new
        {
            membership.UserId,
            membership.IsActive,
            membership.EndsAtUtc
        });

        entity.HasOne(membership => membership.User)
            .WithMany(user => user.ProgramMemberships)
            .HasForeignKey(membership => membership.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(membership => membership.GrantedByUser)
            .WithMany(user => user.GrantedProgramMemberships)
            .HasForeignKey(membership => membership.GrantedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class SubscriptionAccessKeyEntityConfiguration
    : IEntityTypeConfiguration<SubscriptionAccessKeyEntity>
{
    public void Configure(
        EntityTypeBuilder<SubscriptionAccessKeyEntity> entity)
    {
        entity.ToTable(
            "SubscriptionAccessKeys",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_SubscriptionAccessKeys_GrantDuration",
                    "(\"GrantsLifetimeAccess\" = TRUE AND \"DurationDays\" IS NULL) OR (\"GrantsLifetimeAccess\" = FALSE AND \"DurationDays\" IS NOT NULL AND \"DurationDays\" > 0)");

                table.HasCheckConstraint(
                    "CK_SubscriptionAccessKeys_Redemptions",
                    "\"MaxRedemptions\" > 0 AND \"RedemptionCount\" >= 0 AND \"RedemptionCount\" <= \"MaxRedemptions\"");

                table.HasCheckConstraint(
                    "CK_SubscriptionAccessKeys_Revocation",
                    "(\"IsRevoked\" = FALSE AND \"RevokedAtUtc\" IS NULL) OR (\"IsRevoked\" = TRUE AND \"RevokedAtUtc\" IS NOT NULL)");
            });

        entity.HasKey(accessKey => accessKey.Id);

        entity.Property(accessKey => accessKey.KeyHash)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();

        entity.Property(accessKey => accessKey.DisplayPrefix)
            .HasMaxLength(16)
            .IsRequired();

        entity.Property(accessKey => accessKey.Purpose)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        entity.Property(accessKey => accessKey.Tier)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        entity.Property(accessKey => accessKey.GrantsLifetimeAccess)
            .IsRequired();

        entity.Property(accessKey => accessKey.MaxRedemptions)
            .IsRequired();

        entity.Property(accessKey => accessKey.RedemptionCount)
            .IsRequired();

        entity.Property(accessKey => accessKey.IsRevoked)
            .IsRequired();

        entity.Property(accessKey => accessKey.CreatedAtUtc)
            .IsRequired();

        entity.HasIndex(accessKey => accessKey.KeyHash)
            .IsUnique();

        entity.HasIndex(accessKey => new
        {
            accessKey.IsRevoked,
            accessKey.ExpiresAtUtc
        });

        entity.HasIndex(accessKey => accessKey.CreatedByUserId);

        entity.HasOne(accessKey => accessKey.CreatedByUser)
            .WithMany(user => user.CreatedSubscriptionAccessKeys)
            .HasForeignKey(accessKey => accessKey.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SubscriptionAccessKeyRedemptionEntityConfiguration
    : IEntityTypeConfiguration<SubscriptionAccessKeyRedemptionEntity>
{
    public void Configure(
        EntityTypeBuilder<SubscriptionAccessKeyRedemptionEntity> entity)
    {
        entity.ToTable("SubscriptionAccessKeyRedemptions");

        entity.HasKey(redemption => redemption.Id);

        entity.Property(redemption => redemption.RedeemedAtUtc)
            .IsRequired();

        entity.HasIndex(redemption => new
            {
                redemption.AccessKeyId,
                redemption.UserId
            })
            .IsUnique();

        entity.HasIndex(redemption => redemption.UserId);

        entity.HasIndex(redemption => redemption.EntitlementId);

        entity.HasOne(redemption => redemption.AccessKey)
            .WithMany(accessKey => accessKey.Redemptions)
            .HasForeignKey(redemption => redemption.AccessKeyId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(redemption => redemption.User)
            .WithMany(user => user.SubscriptionAccessKeyRedemptions)
            .HasForeignKey(redemption => redemption.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(redemption => redemption.Entitlement)
            .WithMany(entitlement => entitlement.AccessKeyRedemptions)
            .HasForeignKey(redemption => new
            {
                redemption.EntitlementId,
                redemption.UserId
            })
            .HasPrincipalKey(entitlement => new
            {
                entitlement.Id,
                entitlement.UserId
            })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AdminAuditLogEntityConfiguration
    : IEntityTypeConfiguration<AdminAuditLogEntity>
{
    public void Configure(
        EntityTypeBuilder<AdminAuditLogEntity> entity)
    {
        entity.ToTable("AdminAuditLogs");

        entity.HasKey(audit => audit.Id);

        entity.Property(audit => audit.Action)
            .HasMaxLength(100)
            .IsRequired();

        entity.Property(audit => audit.SubjectType)
            .HasMaxLength(100)
            .IsRequired();

        entity.Property(audit => audit.CreatedAtUtc)
            .IsRequired();

        entity.HasIndex(audit => audit.ActorUserId);

        entity.HasIndex(audit => audit.TargetUserId);

        entity.HasIndex(audit => new
        {
            audit.SubjectType,
            audit.SubjectId
        });

        entity.HasIndex(audit => audit.CreatedAtUtc);

        entity.HasOne(audit => audit.ActorUser)
            .WithMany(user => user.AdminAuditActions)
            .HasForeignKey(audit => audit.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(audit => audit.TargetUser)
            .WithMany(user => user.AdminAuditTargets)
            .HasForeignKey(audit => audit.TargetUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
