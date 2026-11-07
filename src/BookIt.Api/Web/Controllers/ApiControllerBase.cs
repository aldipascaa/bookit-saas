using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace BookIt.Api.Web.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
