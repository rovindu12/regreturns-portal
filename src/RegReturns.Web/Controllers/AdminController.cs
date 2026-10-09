using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using RegReturns.Application.Authorization;
using RegReturns.Application.Demo;
using RegReturns.Application.Identity;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Common;
using RegReturns.Domain.Identity;
using RegReturns.Web.Models;
using RegReturns.Web.Models.Admin;
using RegReturns.Web.Navigation;

namespace RegReturns.Web.Controllers;

/// <summary>
/// The administration area (administrators with two-step verification): the landing page with the demo reset, and
/// the directory, where a person's portal access can be disabled or re-enabled (ADR 0031). Templates have their own
/// controller.
/// </summary>
[Route(PortalAreas.AdminRoute)]
[Authorize(Policy = Policies.AdminManage)]
public sealed class AdminController : Controller
{
    /// <summary>Shows the administration tools, the demo's resets in demo mode, and who is signed in.</summary>
    /// <param name="demo">The reset history query.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The landing page.</returns>
    [HttpGet]
    public async Task<IActionResult> IndexAsync(
        [FromServices] IQueryHandler<GetDemoStatus, DemoStatus> demo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(demo);
        var status = await demo.HandleAsync(new GetDemoStatus(), cancellationToken);
        return View("Index", new AdminIndexViewModel(PortalAreas.Admin, SignedInUserViewModel.From(User), status));
    }

    /// <summary>Resets the demo: replaces the workload with the seed and keeps the directory and the audit trail.</summary>
    /// <param name="handler">The reset command.</param>
    /// <param name="cancellationToken">Cancels the reset; nothing changes then.</param>
    /// <returns>A redirect to the landing page with the outcome.</returns>
    [HttpPost("demo/reset")]
    public async Task<IActionResult> ResetDemoAsync(
        [FromServices] ICommandHandler<ResetDemo, Result<DemoResetReport>> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var result = await handler.HandleAsync(new ResetDemo(DemoResetTrigger.Manual), cancellationToken);
        if (result.IsFailure)
        {
            if (result.Error!.Is(DemoErrors.AdminsOnly))
            {
                return Forbid();
            }

            TempData.Error(result.Error.Message);
        }
        else
        {
            var report = result.Value;
            TempData.Success(
                $"The demo was reset: {report.Seeded.Submissions} returns seeded in {report.ElapsedMs / 1000.0:0.0} s (audit entry {report.AuditSequence}).");
        }

        return RedirectToAction("Index", null, null, "demo");
    }

    /// <summary>Shows the directory: people with their roles and access, and the registered API clients.</summary>
    /// <param name="handler">The directory query.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The directory page.</returns>
    [HttpGet("users")]
    public async Task<IActionResult> UsersAsync(
        [FromServices] IQueryHandler<GetUserDirectory, Result<UserDirectory>> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var directory = await handler.HandleAsync(new GetUserDirectory(), cancellationToken);
        return directory.IsSuccess ? View("Users", new UsersViewModel(directory.Value)) : Forbid();
    }

    /// <summary>Disables a person's portal access. Demo accounts and the caller's own account are refused.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="handler">The access command.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A redirect to the directory with the outcome.</returns>
    [HttpPost("users/{userId:guid}/disable")]
    public Task<IActionResult> DisableUserAsync(
        Guid userId, [FromServices] ICommandHandler<ChangeUserAccess, Result<UserStatus>> handler, CancellationToken cancellationToken) =>
        ChangeAccessAsync(new ChangeUserAccess(userId, Enable: false), handler, cancellationToken);

    /// <summary>Re-enables a person's portal access.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="handler">The access command.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A redirect to the directory with the outcome.</returns>
    [HttpPost("users/{userId:guid}/enable")]
    public Task<IActionResult> EnableUserAsync(
        Guid userId, [FromServices] ICommandHandler<ChangeUserAccess, Result<UserStatus>> handler, CancellationToken cancellationToken) =>
        ChangeAccessAsync(new ChangeUserAccess(userId, Enable: true), handler, cancellationToken);

    private async Task<IActionResult> ChangeAccessAsync(
        ChangeUserAccess command, ICommandHandler<ChangeUserAccess, Result<UserStatus>> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var result = await handler.HandleAsync(command, cancellationToken);
        if (result.IsFailure)
        {
            if (result.Error!.Is(UserAdministrationErrors.NotFound))
            {
                return NotFound();
            }

            if (result.Error.Is(UserAdministrationErrors.AdminsOnly))
            {
                return Forbid();
            }

            TempData.Error(result.Error.Message);
        }
        else
        {
            TempData.Success(result.Value == UserStatus.Active
                ? "Portal access re-enabled. The user can sign in again."
                : "Portal access disabled. The user can no longer sign in or act in the portal.");
        }

        return RedirectToAction("Users");
    }
}
