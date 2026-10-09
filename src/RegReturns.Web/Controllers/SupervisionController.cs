using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using RegReturns.Application.Authorization;
using RegReturns.Application.Insights;
using RegReturns.Application.Messaging;
using RegReturns.Application.Returns;
using RegReturns.Application.Supervision;
using RegReturns.Domain.Common;
using RegReturns.Domain.Submissions;
using RegReturns.Web.Models;
using RegReturns.Web.Models.Supervision;
using RegReturns.Web.Navigation;

namespace RegReturns.Web.Controllers;

/// <summary>
/// The supervision area (plan §6.2): the reviewers' and approvers' worklist and the review page of a submitted return,
/// where a reviewer picks it up, a reviewer or approver sends it back for correction, and an approver who did not
/// review it approves or rejects it. Approving and rejecting need the approval policy (TOTP when MFA is enforced).
/// </summary>
/// <param name="authorization">Checks the approval policy, to offer the decision only to callers who pass it.</param>
[Route(PortalAreas.SupervisionRoute)]
[Authorize(Policy = Policies.SupervisionAccess)]
public sealed class SupervisionController(IAuthorizationService authorization) : Controller
{
    /// <summary>Shows the worklist.</summary>
    /// <param name="filter">The bank, return type and late filters from the query string.</param>
    /// <param name="handler">The worklist query.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The worklist page.</returns>
    [HttpGet]
    public async Task<IActionResult> IndexAsync(
        [FromQuery] WorklistFilterInput filter,
        [FromServices] IQueryHandler<GetSupervisionWorklist, Result<SupervisionWorklist>> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(handler);
        var worklist = await handler.HandleAsync(
            new GetSupervisionWorklist(filter.Institution, filter.ReturnType, filter.LateOnly), cancellationToken);
        return View(worklist.IsSuccess
            ? new WorklistViewModel(worklist.Value, null, filter)
            : new WorklistViewModel(null, worklist.Error!.Message, filter));
    }

    /// <summary>Shows a submitted return with its values, findings, justifications, uploads and history.</summary>
    /// <param name="submissionId">The submission id.</param>
    /// <param name="handler">The return query.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The review page.</returns>
    [HttpGet("returns/{submissionId:guid}")]
    public async Task<IActionResult> ReturnAsync(
        Guid submissionId,
        [FromServices] IQueryHandler<GetReturnForm, Result<ReturnForm>> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var form = await handler.HandleAsync(new GetReturnForm(submissionId), cancellationToken);
        return form.IsFailure ? Fail(form.Error!) : View("Return", new SupervisionReturnViewModel(form.Value, await CanDecideAsync()));
    }

    /// <summary>Picks a submitted return up for review, making the caller its reviewer.</summary>
    /// <param name="submissionId">The submission id.</param>
    /// <param name="handler">The workflow command.</param>
    /// <param name="formHandler">The return query, to show the page again when the step is refused.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A redirect to the return, or the page again with the reason.</returns>
    [HttpPost("returns/{submissionId:guid}/start-review")]
    [Authorize(Policy = Policies.SupervisionReview)]
    public Task<IActionResult> StartReviewAsync(
        Guid submissionId,
        [FromServices] ICommandHandler<TransitionReturn, Result<TransitionOutcome>> handler,
        [FromServices] IQueryHandler<GetReturnForm, Result<ReturnForm>> formHandler,
        CancellationToken cancellationToken) =>
        TransitionAsync(
            new TransitionReturn(submissionId, WorkflowAction.StartReview), "You are now reviewing this return.", handler, formHandler, cancellationToken);

    /// <summary>Sends a return under review back to its bank for correction.</summary>
    /// <param name="submissionId">The submission id.</param>
    /// <param name="comment">What the bank must correct.</param>
    /// <param name="handler">The workflow command.</param>
    /// <param name="formHandler">The return query, to show the page again when the step is refused.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A redirect to the return, or the page again with the comment and the reason.</returns>
    [HttpPost("returns/{submissionId:guid}/return-for-correction")]
    public Task<IActionResult> ReturnForCorrectionAsync(
        Guid submissionId,
        [FromForm] string? comment,
        [FromServices] ICommandHandler<TransitionReturn, Result<TransitionOutcome>> handler,
        [FromServices] IQueryHandler<GetReturnForm, Result<ReturnForm>> formHandler,
        CancellationToken cancellationToken) =>
        TransitionAsync(
            new TransitionReturn(submissionId, WorkflowAction.ReturnForCorrection, comment),
            "Return sent back to the bank for correction.",
            handler,
            formHandler,
            cancellationToken);

    /// <summary>Approves a return under review, which fulfils the bank's obligation.</summary>
    /// <param name="submissionId">The submission id.</param>
    /// <param name="comment">The approver's comment.</param>
    /// <param name="handler">The workflow command.</param>
    /// <param name="formHandler">The return query, to show the page again when the step is refused.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A redirect to the return, or the page again with the comment and the reason.</returns>
    [HttpPost("returns/{submissionId:guid}/approve")]
    [Authorize(Policy = Policies.SupervisionApprove)]
    public Task<IActionResult> ApproveAsync(
        Guid submissionId,
        [FromForm] string? comment,
        [FromServices] ICommandHandler<TransitionReturn, Result<TransitionOutcome>> handler,
        [FromServices] IQueryHandler<GetReturnForm, Result<ReturnForm>> formHandler,
        CancellationToken cancellationToken) =>
        TransitionAsync(
            new TransitionReturn(submissionId, WorkflowAction.Approve, comment), "Return approved.", handler, formHandler, cancellationToken);

    /// <summary>Rejects a return under review; the bank's obligation opens again so it can file a new return.</summary>
    /// <param name="submissionId">The submission id.</param>
    /// <param name="comment">The reason for rejecting it.</param>
    /// <param name="handler">The workflow command.</param>
    /// <param name="formHandler">The return query, to show the page again when the step is refused.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A redirect to the return, or the page again with the comment and the reason.</returns>
    [HttpPost("returns/{submissionId:guid}/reject")]
    [Authorize(Policy = Policies.SupervisionApprove)]
    public Task<IActionResult> RejectAsync(
        Guid submissionId,
        [FromForm] string? comment,
        [FromServices] ICommandHandler<TransitionReturn, Result<TransitionOutcome>> handler,
        [FromServices] IQueryHandler<GetReturnForm, Result<ReturnForm>> formHandler,
        CancellationToken cancellationToken) =>
        TransitionAsync(
            new TransitionReturn(submissionId, WorkflowAction.Reject, comment),
            "Return rejected. The bank can file the return again.",
            handler,
            formHandler,
            cancellationToken);

    /// <summary>
    /// Generates an advisory insight on the return's current revision (ADR 0030), or shows the last one again when
    /// nothing has changed. Never changes the return or its workflow.
    /// </summary>
    /// <param name="submissionId">The submission id.</param>
    /// <param name="handler">The insight command.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A redirect to the insight on the review page.</returns>
    [HttpPost("returns/{submissionId:guid}/insight")]
    public async Task<IActionResult> GenerateInsightAsync(
        Guid submissionId,
        [FromServices] ICommandHandler<GenerateReturnInsight, Result<InsightGeneration>> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var result = await handler.HandleAsync(new GenerateReturnInsight(submissionId), cancellationToken);
        if (result.IsFailure)
        {
            if (result.Error!.Is(SubmissionErrors.NotFound))
            {
                return NotFound();
            }

            if (result.Error.Is(InsightErrors.SupervisorsOnly))
            {
                return Forbid();
            }

            TempData.Error(result.Error.Message);
        }
        else if (result.Value.Reused)
        {
            TempData.Success("Nothing has changed since the last insight, so it is shown again.");
        }
        else if (result.Value.Insight.FallbackReason is { } reason)
        {
            TempData.Success($"Insight generated by fixed rules. {InsightErrors.Describe(reason)}");
        }
        else
        {
            TempData.Success("Insight generated.");
        }

        return RedirectToAction("Return", null, new { submissionId }, "insight");
    }

    private async Task<IActionResult> TransitionAsync(
        TransitionReturn command,
        string success,
        ICommandHandler<TransitionReturn, Result<TransitionOutcome>> handler,
        IQueryHandler<GetReturnForm, Result<ReturnForm>> formHandler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(formHandler);

        var result = await handler.HandleAsync(command, cancellationToken);
        if (result.IsSuccess)
        {
            TempData.Success(success);
            return RedirectToAction("Return", new { submissionId = command.SubmissionId });
        }

        if (result.Error!.Is(SubmissionErrors.NotFound))
        {
            return NotFound();
        }

        var form = await formHandler.HandleAsync(new GetReturnForm(command.SubmissionId), cancellationToken);
        if (form.IsFailure)
        {
            return Fail(form.Error!);
        }

        return View("Return", new SupervisionReturnViewModel(form.Value, await CanDecideAsync())
        {
            FailedAction = command.Action,
            Comment = command.Comment,
            Error = result.Error.Message,
        });
    }

    private IActionResult Fail(Error error)
    {
        if (error.Is(SubmissionErrors.NotFound))
        {
            return NotFound();
        }

        TempData.Error(error.Message);
        return RedirectToAction("Index");
    }

    private async Task<bool> CanDecideAsync() =>
        (await authorization.AuthorizeAsync(User, Policies.SupervisionApprove)).Succeeded;
}
