using System.Data;
using BillWatch.API.Authorization;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BillWatch.API.Services.Admin;

public sealed class AdminUserManagementService(
    BillWatchDbContext dbContext,
    TimeProvider timeProvider)
{
    public async Task<AdminUserMutationResult> AssignRoleAsync(
        Guid actorUserId,
        Guid targetUserId,
        string roleName,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == targetUserId ||
            !BillWatchRoles.IsStaffRole(roleName) ||
            roleName == BillWatchRoles.Owner)
        {
            return AdminUserMutationResult.Forbidden;
        }

        return await InTransactionAsync(
            async () =>
            {
                var state = await LoadManagementStateAsync(
                    actorUserId,
                    targetUserId,
                    cancellationToken);

                if (state is null ||
                    !BillWatchRoleHierarchy.CanManageUser(
                        state.ActorHighestRole!,
                        state.TargetHighestRole) ||
                    !BillWatchRoleHierarchy.CanAssignRole(
                        state.ActorHighestRole!,
                        roleName))
                {
                    return AdminUserMutationResult.Forbidden;
                }

                var role = await dbContext.Roles.SingleOrDefaultAsync(
                    candidate => candidate.NormalizedName == roleName.ToUpper(),
                    cancellationToken);

                if (role is null)
                {
                    return AdminUserMutationResult.NotFound;
                }

                var exists = await dbContext.UserRoles.AnyAsync(
                    userRole => userRole.UserId == targetUserId &&
                        userRole.RoleId == role.Id,
                    cancellationToken);

                if (!exists)
                {
                    dbContext.UserRoles.Add(
                        new IdentityUserRole<Guid>
                        {
                            UserId = targetUserId,
                            RoleId = role.Id
                        });
                    AddAudit(
                        actorUserId,
                        targetUserId,
                        "StaffRoleAssigned",
                        role.Id);
                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                return AdminUserMutationResult.Success;
            },
            cancellationToken);
    }

    public async Task<AdminUserMutationResult> RemoveRoleAsync(
        Guid actorUserId,
        Guid targetUserId,
        string roleName,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == targetUserId ||
            !BillWatchRoles.IsStaffRole(roleName) ||
            roleName == BillWatchRoles.Owner)
        {
            return AdminUserMutationResult.Forbidden;
        }

        return await InTransactionAsync(
            async () =>
            {
                var state = await LoadManagementStateAsync(
                    actorUserId,
                    targetUserId,
                    cancellationToken);

                if (state is null ||
                    !BillWatchRoleHierarchy.CanManageUser(
                        state.ActorHighestRole!,
                        state.TargetHighestRole))
                {
                    return AdminUserMutationResult.Forbidden;
                }

                var role = await dbContext.Roles.SingleOrDefaultAsync(
                    candidate => candidate.NormalizedName == roleName.ToUpper(),
                    cancellationToken);

                if (role is null)
                {
                    return AdminUserMutationResult.NotFound;
                }

                var userRole = await dbContext.UserRoles.SingleOrDefaultAsync(
                    candidate => candidate.UserId == targetUserId &&
                        candidate.RoleId == role.Id,
                    cancellationToken);

                if (userRole is null)
                {
                    return AdminUserMutationResult.NotFound;
                }

                dbContext.UserRoles.Remove(userRole);
                AddAudit(
                    actorUserId,
                    targetUserId,
                    "StaffRoleRemoved",
                    role.Id);
                await dbContext.SaveChangesAsync(cancellationToken);
                return AdminUserMutationResult.Success;
            },
            cancellationToken);
    }

    public async Task<AdminUserMutationResult> GrantEntitlementAsync(
        Guid actorUserId,
        Guid targetUserId,
        BillWatchSubscriptionTier tier,
        int? durationDays,
        bool grantsLifetimeAccess,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(tier) ||
            grantsLifetimeAccess == durationDays.HasValue ||
            durationDays is <= 0 or > 3650)
        {
            return AdminUserMutationResult.Invalid;
        }

        var state = await LoadManagementStateAsync(
            actorUserId,
            targetUserId,
            cancellationToken);

        if (state is null ||
            !BillWatchRoleHierarchy.CanManageUser(
                state.ActorHighestRole!,
                state.TargetHighestRole))
        {
            return AdminUserMutationResult.Forbidden;
        }

        var nowUtc = timeProvider.GetUtcNow();
        var entitlement = new SubscriptionEntitlementEntity
        {
            UserId = targetUserId,
            Tier = tier,
            Source = SubscriptionEntitlementSource.Complimentary,
            StartsAtUtc = nowUtc,
            EndsAtUtc = grantsLifetimeAccess
                ? null
                : nowUtc.AddDays(durationDays!.Value),
            GrantedByUserId = actorUserId,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        dbContext.SubscriptionEntitlements.Add(entitlement);
        AddAudit(
            actorUserId,
            targetUserId,
            "SubscriptionEntitlementGranted",
            entitlement.Id);
        await dbContext.SaveChangesAsync(cancellationToken);
        return AdminUserMutationResult.SuccessWithId(entitlement.Id);
    }

    public async Task<AdminUserMutationResult> RevokeEntitlementAsync(
        Guid actorUserId,
        Guid targetUserId,
        Guid entitlementId,
        CancellationToken cancellationToken = default)
    {
        var state = await LoadManagementStateAsync(
            actorUserId,
            targetUserId,
            cancellationToken);

        if (state is null ||
            !BillWatchRoleHierarchy.CanManageUser(
                state.ActorHighestRole!,
                state.TargetHighestRole))
        {
            return AdminUserMutationResult.Forbidden;
        }

        var entitlement = await dbContext.SubscriptionEntitlements
            .SingleOrDefaultAsync(
                candidate => candidate.Id == entitlementId &&
                    candidate.UserId == targetUserId,
                cancellationToken);

        if (entitlement is null)
        {
            return AdminUserMutationResult.NotFound;
        }

        if (!entitlement.IsRevoked)
        {
            var nowUtc = timeProvider.GetUtcNow();
            entitlement.IsRevoked = true;
            entitlement.RevokedAtUtc = nowUtc;
            entitlement.UpdatedAtUtc = nowUtc;
            AddAudit(
                actorUserId,
                targetUserId,
                "SubscriptionEntitlementRevoked",
                entitlement.Id);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return AdminUserMutationResult.Success;
    }

    public async Task<AdminUserMutationResult> SetProgramMembershipAsync(
        Guid actorUserId,
        Guid targetUserId,
        UserProgramType program,
        bool isActive,
        DateTimeOffset? endsAtUtc,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = timeProvider.GetUtcNow();
        if (!Enum.IsDefined(program) ||
            endsAtUtc <= nowUtc)
        {
            return AdminUserMutationResult.Invalid;
        }

        var state = await LoadManagementStateAsync(
            actorUserId,
            targetUserId,
            cancellationToken);

        if (state is null ||
            !BillWatchRoleHierarchy.CanManageUser(
                state.ActorHighestRole!,
                state.TargetHighestRole))
        {
            return AdminUserMutationResult.Forbidden;
        }

        var membership = await dbContext.UserProgramMemberships
            .SingleOrDefaultAsync(
                candidate => candidate.UserId == targetUserId &&
                    candidate.Program == program,
                cancellationToken);

        if (membership is null)
        {
            membership = new UserProgramMembershipEntity
            {
                UserId = targetUserId,
                Program = program,
                StartsAtUtc = nowUtc,
                EndsAtUtc = endsAtUtc,
                IsActive = isActive,
                GrantedByUserId = actorUserId,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            };
            dbContext.UserProgramMemberships.Add(membership);
        }
        else
        {
            membership.IsActive = isActive;
            membership.EndsAtUtc = endsAtUtc;
            membership.GrantedByUserId = actorUserId;
            membership.UpdatedAtUtc = nowUtc;
        }

        AddAudit(
            actorUserId,
            targetUserId,
            isActive
                ? "UserProgramMembershipEnabled"
                : "UserProgramMembershipDisabled",
            membership.Id);
        await dbContext.SaveChangesAsync(cancellationToken);
        return AdminUserMutationResult.SuccessWithId(membership.Id);
    }

    private async Task<ManagementState?> LoadManagementStateAsync(
        Guid actorUserId,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.Users.AnyAsync(
                user => user.Id == targetUserId,
                cancellationToken))
        {
            return null;
        }

        var assignments = await (
            from userRole in dbContext.UserRoles
            join role in dbContext.Roles on userRole.RoleId equals role.Id
            where userRole.UserId == actorUserId ||
                userRole.UserId == targetUserId
            select new { userRole.UserId, role.Name })
            .ToListAsync(cancellationToken);

        var actorHighest = assignments
            .Where(item => item.UserId == actorUserId)
            .Select(item => item.Name)
            .OrderByDescending(BillWatchRoleHierarchy.GetRank)
            .FirstOrDefault();

        if (!BillWatchRoles.IsStaffRole(actorHighest))
        {
            return null;
        }

        var targetHighest = assignments
            .Where(item => item.UserId == targetUserId)
            .Select(item => item.Name)
            .OrderByDescending(BillWatchRoleHierarchy.GetRank)
            .FirstOrDefault();

        return new ManagementState(actorHighest, targetHighest);
    }

    private void AddAudit(
        Guid actorUserId,
        Guid targetUserId,
        string action,
        Guid subjectId)
    {
        dbContext.AdminAuditLogs.Add(
            new AdminAuditLogEntity
            {
                ActorUserId = actorUserId,
                TargetUserId = targetUserId,
                Action = action,
                SubjectType = "UserAdministration",
                SubjectId = subjectId,
                CreatedAtUtc = timeProvider.GetUtcNow()
            });
    }

    private async Task<AdminUserMutationResult> InTransactionAsync(
        Func<Task<AdminUserMutationResult>> action,
        CancellationToken cancellationToken)
    {
        IDbContextTransaction? transaction = null;

        try
        {
            if (dbContext.Database.IsRelational())
            {
                transaction = await dbContext.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
            }

            var result = await action();
            if (transaction is not null && result.Succeeded)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return result;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private sealed record ManagementState(
        string? ActorHighestRole,
        string? TargetHighestRole);
}

public sealed record AdminUserMutationResult(
    bool Succeeded,
    string Code,
    Guid? ResourceId = null)
{
    public static AdminUserMutationResult Success { get; } =
        new(true, "success");
    public static AdminUserMutationResult Forbidden { get; } =
        new(false, "forbidden");
    public static AdminUserMutationResult NotFound { get; } =
        new(false, "not_found");
    public static AdminUserMutationResult Invalid { get; } =
        new(false, "invalid");
    public static AdminUserMutationResult SuccessWithId(Guid id) =>
        new(true, "success", id);
}
