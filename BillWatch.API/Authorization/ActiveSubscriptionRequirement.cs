using System.Security.Claims;
using BillWatch.API.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Authorization;

public sealed class ActiveSubscriptionRequirement
    : IAuthorizationRequirement;

public sealed class ActiveSubscriptionAuthorizationHandler(
    BillWatchDbContext dbContext,
    TimeProvider timeProvider,
    IConfiguration configuration,
    SubscriptionAuthorizationTelemetry telemetry)
    : AuthorizationHandler<ActiveSubscriptionRequirement>
{
    public ActiveSubscriptionAuthorizationHandler(
        BillWatchDbContext dbContext,
        TimeProvider timeProvider)
        : this(
            dbContext,
            timeProvider,
            new ConfigurationBuilder().Build(),
            new SubscriptionAuthorizationTelemetry())
    {
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActiveSubscriptionRequirement requirement)
    {
        if (IsSubscriptionAccessExempt(context.Resource))
        {
            context.Succeed(requirement);
            return;
        }

        var userIdValue =
            context.User.FindFirstValue(
                ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdValue, out var userId))
        {
            telemetry.RecordDenial("missing_user");
            return;
        }

        var nowUtc = timeProvider.GetUtcNow();

        if (!await IsTargetedForEnforcementAsync(
                userId,
                nowUtc))
        {
            context.Succeed(requirement);
            return;
        }

        var hasActiveEntitlement =
            await dbContext.Set<Data.Entities.SubscriptionEntitlementEntity>()
                .AsNoTracking()
                .AnyAsync(
                    entitlement =>
                        entitlement.UserId == userId &&
                        !entitlement.IsRevoked &&
                        entitlement.StartsAtUtc <= nowUtc &&
                        (entitlement.EndsAtUtc == null ||
                         entitlement.EndsAtUtc > nowUtc));

        if (hasActiveEntitlement)
        {
            context.Succeed(requirement);
            return;
        }

        telemetry.RecordDenial("inactive_subscription");
    }

    private async Task<bool> IsTargetedForEnforcementAsync(
        Guid userId,
        DateTimeOffset nowUtc)
    {
        var cohort = configuration["Subscription:EnforcementCohort"];
        if (string.IsNullOrWhiteSpace(cohort) ||
            string.Equals(cohort, "All", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var programs = string.Equals(
                cohort,
                "InternalTester",
                StringComparison.OrdinalIgnoreCase)
            ? new[] { Data.Entities.UserProgramType.InternalTester }
            : string.Equals(
                cohort,
                "BetaTester",
                StringComparison.OrdinalIgnoreCase)
                ? new[]
                {
                    Data.Entities.UserProgramType.InternalTester,
                    Data.Entities.UserProgramType.BetaTester
                }
                : null;

        if (programs is null)
        {
            return true;
        }

        return await dbContext.UserProgramMemberships
            .AsNoTracking()
            .AnyAsync(
                membership => membership.UserId == userId &&
                    programs.Contains(membership.Program) &&
                    membership.IsActive &&
                    membership.StartsAtUtc <= nowUtc &&
                    (membership.EndsAtUtc == null ||
                     membership.EndsAtUtc > nowUtc));
    }

    private static bool IsSubscriptionAccessExempt(object? resource)
    {
        var endpoint = resource switch
        {
            HttpContext httpContext => httpContext.GetEndpoint(),
            AuthorizationFilterContext filterContext =>
                filterContext.HttpContext.GetEndpoint(),
            Endpoint directEndpoint => directEndpoint,
            _ => null
        };

        return endpoint?.Metadata
            .GetMetadata<SubscriptionAccessExemptAttribute>()
            is not null;
    }
}
