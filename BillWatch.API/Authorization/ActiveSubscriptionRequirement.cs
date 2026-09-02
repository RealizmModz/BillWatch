using System.Security.Claims;
using BillWatch.API.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Authorization;

public sealed class ActiveSubscriptionRequirement
    : IAuthorizationRequirement;

public sealed class ActiveSubscriptionAuthorizationHandler(
    BillWatchDbContext dbContext,
    TimeProvider timeProvider)
    : AuthorizationHandler<ActiveSubscriptionRequirement>
{
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
            return;
        }

        var nowUtc = timeProvider.GetUtcNow();

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
        }
    }

    private static bool IsSubscriptionAccessExempt(object? resource)
    {
        var endpoint = resource switch
        {
            HttpContext httpContext => httpContext.GetEndpoint(),
            Endpoint directEndpoint => directEndpoint,
            _ => null
        };

        return endpoint?.Metadata
            .GetMetadata<SubscriptionAccessExemptAttribute>()
            is not null;
    }
}
