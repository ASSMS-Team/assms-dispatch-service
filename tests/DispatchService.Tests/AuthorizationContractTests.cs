using DispatchService.Controllers;
using DispatchService.Security;
using Microsoft.AspNetCore.Authorization;

namespace DispatchService.Tests;

public sealed class AuthorizationContractTests
{
    [Fact]
    public void TechnicianEndpoints_RequireDispatcherOrManagerRole()
    {
        var authorization = typeof(TechniciansController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal(StaffRoles.TechnicianManagement, authorization.Roles);
    }
}
