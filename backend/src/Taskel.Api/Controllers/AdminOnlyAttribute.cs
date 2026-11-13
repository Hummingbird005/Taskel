using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Taskel.Api.Models;

namespace Taskel.Api.Controllers;

/// <summary>
/// Lets only administrators run the action. The role is read from the database (through
/// <see cref="CurrentUser"/>) on each request, so a withdrawn role takes effect at once.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AdminOnlyAttribute(string refusal = "Only administrators can do this.") : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var currentUser = context.HttpContext.RequestServices.GetRequiredService<CurrentUser>();
        var me = await currentUser.GetAsync(context.HttpContext.User, context.HttpContext.RequestAborted);
        if (me is null)
        {
            context.Result = new UnauthorizedResult();
            return;
        }
        if (me.Value.Role != UserRole.Admin)
        {
            context.Result = new ObjectResult(new ProblemDetails { Status = StatusCodes.Status403Forbidden, Title = refusal })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }
        await next();
    }
}
