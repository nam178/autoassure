using A2.Server.Common;
using A2.Server.Repositories;

namespace A2.Server.Services;

public class CallerOrganizationService(
    IHttpContextAccessor httpContextAccessor,
    IOrganizationUserRepository organizationUserRepository
) : ICallerOrganizationService
{
    public async Task<Guid> GetOrganizationIdAsync()
    {
        var userId = httpContextAccessor.HttpContext!.User.GetUserId();
        var memberships = await organizationUserRepository.ListByUserAsync(userId);
        return memberships[0].OrganizationId;
    }
}
