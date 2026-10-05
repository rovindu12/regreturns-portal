using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using RegReturns.Application.Authorization;
using RegReturns.Application.Messaging;
using RegReturns.Application.Returns;
using RegReturns.Domain.Common;
using RegReturns.Domain.Submissions;
using RegReturns.Web.Models;
using RegReturns.Web.Models.Bank;
using RegReturns.Web.Models.Returns;
using RegReturns.Web.Navigation;

namespace RegReturns.Web.Controllers;

/// <summary>
/// The bank area (plan §6.1): the bank's filing obligations, the return entry form with drafts and validation, returns
/// as files (template download and upload) and submission by a checker. Use cases scope every read and write to the
/// caller's bank.
/// </summary>
/// <param name="authorization">Checks what the caller may do, to show only the actions they can take.</param>
[Route(PortalAreas.BankRoute)]
[Authorize(Policy = Policies.BankAccess)]
public sealed class BankController(IAuthorizationService authorization) : Controller
{
    /// <summary>
    /// The largest upload request: the file limit plus room for the multipart framing and the antiforgery token. Files
    /// between the two limits get a friendly message; anything larger is cut off by the server.
    /// </summary>
    private const int UploadRequestLimit = StoredFile.MaxSizeBytes + (1024 * 1024);

    /// <summary>Stands in for a missing edit counter: it never matches, so such a post is treated as an edit conflict.</summary>
    private const int MissingEditVersion = -1;

    /// <summary>Shows the bank's obligations, most urgent first.</summary>
    /// <param name="handler">The overview query.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The overview page.</returns>
    [HttpGet]
    public async Task<IActionResult> IndexAsync(
        [FromServices] IQueryHandler<GetBankReturns, Result<BankReturnsOverview>> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var overview = await handler.HandleAsync(new GetBankReturns(), cancellationToken);
        var canPrepare = await CanPrepareAsync();
        return View(overview.IsSuccess
            ? new BankOverviewViewModel(overview.Value, null, canPrepare)
            : new BankOverviewViewModel(null, overview.Error!.Message, canPrepare));
    }

    /// <summary>Starts a draft return for an obligation, or opens the one already started.</summary>
    /// <param name="obligationId">The obligation id.</param>
    /// <param name="handler">The start-draft command.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A redirect to the return.</returns>
    [HttpPost("obligations/{obligationId:guid}/start")]
    [Authorize(Policy = Policies.BankPrepareReturn)]
    public async Task<IActionResult> StartAsync(
        Guid obligationId,
        [FromServices] ICommandHandler<StartReturnDraft, Result<Guid>> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var result = await handler.HandleAsync(new StartReturnDraft(obligationId), cancellationToken);
        if (result.IsFailure)
        {
            return FailRedirect(result.Error!);
        }

        return RedirectToAction("Return", new { submissionId = result.Value });
    }

    /// <summary>Shows a return as an entry form, with its findings and uploads.</summary>
    /// <param name="submissionId">The submission id.</param>
    /// <param name="handler">The form query.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The return page.</returns>
    [HttpGet("returns/{submissionId:guid}")]
    public async Task<IActionResult> ReturnAsync(
        Guid submissionId,
        [FromServices] IQueryHandler<GetReturnForm, Result<ReturnForm>> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var form = await handler.HandleAsync(new GetReturnForm(submissionId), cancellationToken);
        return form.IsFailure ? FailRedirect(form.Error!) : View("Return", new ReturnFormViewModel(form.Value));
    }

    /// <summary>Saves the entered values as a draft and validates them.</summary>
    /// <param name="submissionId">The submission id.</param>
    /// <param name="input">The posted values and the edit counter the page was rendered with.</param>
    /// <param name="handler">The save command.</param>
    /// <param name="formHandler">The form query, to show the page again when the save fails.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A redirect to the return, or the page again with the posted values and the reason.</returns>
    [HttpPost("returns/{submissionId:guid}")]
    [Authorize(Policy = Policies.BankPrepareReturn)]
    public async Task<IActionResult> SaveAsync(
        Guid submissionId,
        ReturnValuesInput input,
        [FromServices] ICommandHandler<SaveReturnValues, Result<ValidationOutcome>> handler,
        [FromServices] IQueryHandler<GetReturnForm, Result<ReturnForm>> formHandler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(formHandler);

        var result = await handler.HandleAsync(new SaveReturnValues(submissionId, input.EditVersion ?? MissingEditVersion, input.Values), cancellationToken);
        if (result.IsSuccess)
        {
            TempData.Success($"Draft saved. {ReturnDisplay.Describe(result.Value)}");
            return RedirectToAction("Return", new { submissionId });
        }

        var form = await formHandler.HandleAsync(new GetReturnForm(submissionId), cancellationToken);
        if (form.IsFailure)
        {
            return FailRedirect(form.Error!);
        }

        // After a conflict the page carries the latest edit counter, so saving again deliberately replaces the other change.
        var conflict = result.Error!.Is(SubmissionErrors.EditConflict);
        return View("Return", new ReturnFormViewModel(form.Value)
        {
            EditVersion = conflict ? form.Value.EditVersion : input.EditVersion ?? form.Value.EditVersion,
            PostedValues = input.Values,
            SaveError = result.Error.Message,
            IsEditConflict = conflict,
        });
    }

    /// <summary>Validates the saved values again (for example after a template or prior-period change).</summary>
    /// <param name="submissionId">The submission id.</param>
    /// <param name="handler">The validate command.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A redirect to the return.</returns>
    [HttpPost("returns/{submissionId:guid}/validate")]
    public async Task<IActionResult> ValidateAsync(
        Guid submissionId,
        [FromServices] ICommandHandler<ValidateReturn, Result<ValidationOutcome>> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var result = await handler.HandleAsync(new ValidateReturn(submissionId), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.Is(SubmissionErrors.NotFound) ? NotFound() : FailRedirectToReturn(submissionId, result.Error);
        }

        TempData.Success(ReturnDisplay.Describe(result.Value));
        return RedirectToAction("Return", new { submissionId });
    }

    /// <summary>Records why a warning is acceptable.</summary>
    /// <param name="submissionId">The submission id.</param>
    /// <param name="findingId">The warning's finding id.</param>
    /// <param name="justification">The justification.</param>
    /// <param name="handler">The justify command.</param>
    /// <param name="formHandler">The form query, to show the page again when the justification is refused.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A redirect to the return, or the page again with the posted text and the reason.</returns>
    [HttpPost("returns/{submissionId:guid}/findings/{findingId:guid}/justify")]
    public async Task<IActionResult> JustifyAsync(
        Guid submissionId,
        Guid findingId,
        [FromForm] string? justification,
        [FromServices] ICommandHandler<JustifyReturnWarning, Result> handler,
        [FromServices] IQueryHandler<GetReturnForm, Result<ReturnForm>> formHandler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(formHandler);

        var result = await handler.HandleAsync(new JustifyReturnWarning(submissionId, findingId, justification ?? string.Empty), cancellationToken);
        if (result.IsSuccess)
        {
            TempData.Success("Justification saved.");
            return RedirectToAction("Return", null, new { submissionId }, "findings");
        }

        if (result.Error!.Is(SubmissionErrors.NotFound))
        {
            return NotFound();
        }

        var form = await formHandler.HandleAsync(new GetReturnForm(submissionId), cancellationToken);
        if (form.IsFailure)
        {
            return FailRedirect(form.Error!);
        }

        return View("Return", new ReturnFormViewModel(form.Value)
        {
            JustifyFindingId = findingId,
            JustifyText = justification,
            JustifyError = result.Error.Message,
        });
    }

    /// <summary>Submits the return to the Bank of Valoria (checkers only; the domain refuses the preparer and last editor).</summary>
    /// <param name="submissionId">The submission id.</param>
    /// <param name="comment">The checker's comment.</param>
    /// <param name="handler">The workflow command.</param>
    /// <param name="formHandler">The form query, to show the page again when the submission is refused.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A redirect to the return, or the page again with the comment and the reason.</returns>
    [HttpPost("returns/{submissionId:guid}/submit")]
    [Authorize(Policy = Policies.BankSubmitReturn)]
    public async Task<IActionResult> SubmitAsync(
        Guid submissionId,
        [FromForm] string? comment,
        [FromServices] ICommandHandler<TransitionReturn, Result<TransitionOutcome>> handler,
        [FromServices] IQueryHandler<GetReturnForm, Result<ReturnForm>> formHandler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(formHandler);

        var result = await handler.HandleAsync(new TransitionReturn(submissionId, WorkflowAction.Submit, comment), cancellationToken);
        if (result.IsSuccess)
        {
            TempData.Success(result.Value.IsLate
                ? "Return submitted to the Bank of Valoria. It arrived after the due date, so it is marked late."
                : "Return submitted to the Bank of Valoria.");
            return RedirectToAction("Return", new { submissionId });
        }

        if (result.Error!.Is(SubmissionErrors.NotFound))
        {
            return NotFound();
        }

        var form = await formHandler.HandleAsync(new GetReturnForm(submissionId), cancellationToken);
        if (form.IsFailure)
        {
            return FailRedirect(form.Error!);
        }

        return View("Return", new ReturnFormViewModel(form.Value) { SubmitComment = comment, SubmitError = result.Error.Message });
    }

    /// <summary>Downloads an obligation's return as a file to fill in, with any values already entered.</summary>
    /// <param name="obligationId">The obligation id.</param>
    /// <param name="format"><c>xlsx</c> (the default) or <c>csv</c>.</param>
    /// <param name="handler">The download query.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The file.</returns>
    [HttpGet("obligations/{obligationId:guid}/download")]
    public async Task<IActionResult> DownloadAsync(
        Guid obligationId,
        [FromQuery] string? format,
        [FromServices] IQueryHandler<DownloadReturnFile, Result<FileDownload>> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var fileFormat = string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase) ? ReturnFileFormat.Csv : ReturnFileFormat.Xlsx;
        var result = await handler.HandleAsync(new DownloadReturnFile(obligationId, fileFormat), cancellationToken);
        if (result.IsFailure)
        {
            return FailRedirect(result.Error!);
        }

        Response.Headers.XContentTypeOptions = "nosniff";
        return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }

    /// <summary>Shows the upload page of an obligation.</summary>
    /// <param name="obligationId">The obligation id.</param>
    /// <param name="handler">The obligation query.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The upload page.</returns>
    [HttpGet("obligations/{obligationId:guid}/upload")]
    [Authorize(Policy = Policies.BankPrepareReturn)]
    public async Task<IActionResult> UploadAsync(
        Guid obligationId,
        [FromServices] IQueryHandler<GetBankObligation, Result<BankReturnRow>> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var obligation = await handler.HandleAsync(new GetBankObligation(obligationId), cancellationToken);
        return obligation.IsFailure ? FailRedirect(obligation.Error!) : View("Upload", new UploadViewModel(obligation.Value));
    }

    /// <summary>Loads an uploaded <c>.xlsx</c> or <c>.csv</c> file into the obligation's return and validates it.</summary>
    /// <param name="obligationId">The obligation id.</param>
    /// <param name="file">The uploaded file.</param>
    /// <param name="handler">The upload command.</param>
    /// <param name="obligationHandler">The obligation query, to show the page again when the file is refused.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A redirect to the return, or the upload page with the reason.</returns>
    [HttpPost("obligations/{obligationId:guid}/upload")]
    [Authorize(Policy = Policies.BankPrepareReturn)]
    [RequestSizeLimit(UploadRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadRequestLimit)]
    public async Task<IActionResult> UploadAsync(
        Guid obligationId,
        IFormFile? file,
        [FromServices] ICommandHandler<UploadReturnFile, Result<UploadOutcome>> handler,
        [FromServices] IQueryHandler<GetBankObligation, Result<BankReturnRow>> obligationHandler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(obligationHandler);

        Result<UploadOutcome> result = file switch
        {
            null or { Length: 0 } => UploadErrors.Empty.WithMessage("Choose a file to upload."),
            { Length: > StoredFile.MaxSizeBytes } => UploadErrors.TooLarge,
            _ => await handler.HandleAsync(
                new UploadReturnFile(obligationId, file.FileName, await ReadAsync(file, cancellationToken)), cancellationToken),
        };
        if (result.IsSuccess)
        {
            var outcome = result.Value;
            TempData.Success(
                $"Loaded {outcome.FieldsLoaded} of {ReturnDisplay.Count(outcome.FieldsInTemplate, "field")} from the file. " +
                ReturnDisplay.Describe(outcome.Validation));
            return RedirectToAction("Return", new { submissionId = outcome.SubmissionId });
        }

        if (result.Error!.Is(SubmissionErrors.NotFound))
        {
            return NotFound();
        }

        var obligation = await obligationHandler.HandleAsync(new GetBankObligation(obligationId), cancellationToken);
        return obligation.IsFailure
            ? FailRedirect(obligation.Error!)
            : View("Upload", new UploadViewModel(obligation.Value, result.Error.Message));
    }

    private static async Task<byte[]> ReadAsync(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream((int)file.Length);
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private async Task<bool> CanPrepareAsync() =>
        (await authorization.AuthorizeAsync(User, Policies.BankPrepareReturn)).Succeeded;

    // A return or obligation of another bank is reported as missing, never as forbidden, so ids reveal nothing.
    private IActionResult FailRedirect(Error error)
    {
        if (error.Is(SubmissionErrors.NotFound))
        {
            return NotFound();
        }

        TempData.Error(error.Message);
        return RedirectToAction("Index");
    }

    private RedirectToActionResult FailRedirectToReturn(Guid submissionId, Error error)
    {
        TempData.Error(error.Message);
        return RedirectToAction("Return", new { submissionId });
    }
}
