using A2.Server.Common;
using A2.Server.Models;
using A2.Server.Repositories;

namespace A2.Server.Services;

public class CallerOrganizationService(
    IHttpContextAccessor httpContextAccessor,
    IOrganizationUserRepository organizationUserRepository,
    IOrganizationRepository organizationRepository
) : ICallerOrganizationService
{
    private const string CacheKey = "CallerOrganization";

    public async Task<Organization> GetCallerOrganizationAsync()
    {
        var httpContext = httpContextAccessor.HttpContext!;

        // Check if the Organization is already cached in HttpContext.Items
        if (httpContext.Items.TryGetValue(CacheKey, out var cachedOrganization))
            return (Organization)cachedOrganization!;

        var userId = httpContext.User.GetUserId();
        var memberships = await organizationUserRepository.ListByUserAsync(
            userId
        );
        if (memberships.Count == 0)
            throw new InvalidOperationException(
                "User has no organization memberships"
            );

        var organizationId = memberships[0].OrganizationId;
        var organization = await organizationRepository.GetByIdAsync(
            organizationId
        );

        if (organization == null)
            throw new InvalidOperationException(
                $"Organization {organizationId} not found"
            );

        // Cache the resolved Organization in HttpContext.Items for subsequent calls in the same request
        httpContext.Items[CacheKey] = organization;

        return organization;
    }
}