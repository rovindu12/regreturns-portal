using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using RegReturns.Application.Authorization;
using RegReturns.Application.Messaging;
using RegReturns.Application.Templates;
using RegReturns.Domain.Common;
using RegReturns.Domain.Templates;
using RegReturns.Web.Models.Templates;
using RegReturns.Web.Navigation;

namespace RegReturns.Web.Controllers;

/// <summary>
/// Return template administration: the catalogue, draft versions (fields, rules and effective date), publishing,
/// retiring and deleting drafts. Every change is a POST that redirects back to a page (Post-Redirect-Get) with a
/// notice in TempData. The domain refuses changes to anything but a draft, so published versions stay as they are.
/// </summary>
/// <param name="timeProvider">The clock, for the default effective date of a new draft.</param>
[Route(RoutePrefix)]
[Authorize(Policy = Policies.AdminManage)]
public sealed class TemplatesController(TimeProvider timeProvider) : Controller
{
    /// <summary>Route of the template catalogue, inside the administration area (no leading slash).</summary>
    public const string RoutePrefix = PortalAreas.AdminRoute + "/templates";

    private const string IndexAction = "Index";
    private const string VersionAction = "Version";
    private const string EditFieldAction = "EditField";
    private const string EffectiveDateName = "effective-from date";

    /// <summary>Lists every return type with its template versions. Routed as <c>Index</c>.</summary>
    /// <param name="catalogue">The catalogue query.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The catalogue page.</returns>
    [HttpGet]
    public async Task<IActionResult> IndexAsync(
        [FromServices] IQueryHandler<GetTemplateCatalogue, IReadOnlyList<ReturnTypeTemplates>> catalogue,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        var returnTypes = await catalogue.HandleAsync(new GetTemplateCatalogue(), cancellationToken);
        return View(new TemplateCatalogueViewModel(returnTypes, FirstOfNextMonth(), TempData.TakeNotice()));
    }

    /// <summary>
    /// Starts a draft version of a return type, copied from the latest published version or from the one given.
    /// Routed as <c>StartDraft</c>.
    /// </summary>
    /// <param name="returnTypeId">The return type.</param>
    /// <param name="effectiveFrom">The first reporting-period start date, as <c>yyyy-MM-dd</c>.</param>
    /// <param name="copyFromVersionId">The version to copy, or none for the latest published one.</param>
    /// <param name="handler">The command handler.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A redirect to the new draft, or back to the form with the reason it was refused.</returns>
    [HttpPost("drafts")]
    public async Task<IActionResult> StartDraftAsync(
        [FromForm] Guid returnTypeId,
        [FromForm] string? effectiveFrom,
        [FromForm] Guid? copyFromVersionId,
        [FromServices] ICommandHandler<StartTemplateDraft, Result<Guid>> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);

        // A refusal goes back to the form it came from: the published version's page, or the catalogue.
        RedirectToActionResult Back(Error error) => copyFromVersionId is { } source
            ? Refused(source, TemplateSections.NewDraft, error)
            : RefusedAtCatalogue(error);

        if (!ModelState.IsValid)
        {
            return Back(FormErrors.InvalidInput);
        }

        var date = FormInput.Date(effectiveFrom, EffectiveDateName);
        if (date.IsFailure)
        {
            return Back(date.Error!);
        }

        var started = await handler.HandleAsync(new StartTemplateDraft(returnTypeId, date.Value, copyFromVersionId), cancellationToken);
        return started.IsSuccess
            ? Done(started.Value, null, "Draft started. Change its fields and rules below, then publish it.")
            : Back(started.Error!);
    }

    /// <summary>Shows a template version: editable when it is a draft, read-only otherwise. Routed as <c>Version</c>.</summary>
    /// <param name="id">The version id.</param>
    /// <param name="query">The version query.</param>
    /// <param name="catalogue">The catalogue query, to find the return type's draft from a published version.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The version page, or 404.</returns>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> VersionAsync(
        Guid id,
        [FromServices] IQueryHandler<GetTemplateVersion, TemplateVersionDetails?> query,
        [FromServices] IQueryHandler<GetTemplateCatalogue, IReadOnlyList<ReturnTypeTemplates>> catalogue,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(catalogue);
        var template = await query.HandleAsync(new GetTemplateVersion(id), cancellationToken);
        if (template is null)
        {
            return NotFound();
        }

        TemplateVersionSummary? draft = null;
        if (template.Status == TemplateStatus.Published)
        {
            var returnTypes = await catalogue.HandleAsync(new GetTemplateCatalogue(template.ReturnTypeCode), cancellationToken);
            draft = returnTypes.SingleOrDefault()?.Draft;
        }

        return View(new TemplateVersionViewModel(
            template,
            draft,
            FirstOfNextMonth(),
            TempData.TakeFieldForm() ?? FieldForm.Empty(),
            TempData.TakeRuleForm() ?? RuleForm.Empty(),
            TempData.TakeNotice()));
    }

    /// <summary>Changes the date a draft applies from. Routed as <c>ChangeEffectiveDate</c>.</summary>
    /// <param name="id">The draft version id.</param>
    /// <param name="effectiveFrom">The new date, as <c>yyyy-MM-dd</c>.</param>
    /// <param name="handler">The command handler.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A redirect to the version page.</returns>
    [HttpPost("{id:guid}/effective-date")]
    public async Task<IActionResult> ChangeEffectiveDateAsync(
        Guid id,
        [FromForm] string? effectiveFrom,
        [FromServices] ICommandHandler<ChangeTemplateEffectiveDate, Result> handler,
        CancellationToken cancellationToken)
    {
        var date = FormInput.Date(effectiveFrom, EffectiveDateName);
        return date.IsFailure
            ? Refused(id, TemplateSections.EffectiveDate, date.Error!)
            : await ChangeAsync(
                handler, new ChangeTemplateEffectiveDate(id, date.Value), id, TemplateSections.EffectiveDate,
                $"The draft now applies from {TemplateDisplay.Date(date.Value)}.", cancellationToken);
    }

    /// <summary>Adds a field to the end of a draft. Routed as <c>AddField</c>.</summary>
    /// <param name="id">The draft version id.</param>
    /// <param name="form">The add-field form.</param>
    /// <param name="handler">The command handler.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A redirect to the version page; a refused form comes back as typed.</returns>
    [HttpPost("{id:guid}/fields")]
    public async Task<IActionResult> AddFieldAsync(
        Guid id,
        [FromForm] FieldForm form,
        [FromServices] ICommandHandler<AddTemplateField, Result> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(handler);
        var definition = form.ToDefinition();
        var result = definition.IsSuccess
            ? await handler.HandleAsync(new AddTemplateField(id, definition.Value), cancellationToken)
            : definition;
        if (result.IsFailure)
        {
            TempData.SetFieldForm(form);
            return Refused(id, TemplateSections.AddField, result.Error!);
        }

        return Done(id, TemplateSections.AddField, $"Field {definition.Value.Code} added at the end of the list.");
    }

    /// <summary>Shows the form to change a draft field. Routed as <c>EditField</c>.</summary>
    /// <param name="id">The draft version id.</param>
    /// <param name="code">The field code.</param>
    /// <param name="query">The version query.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The edit page, a redirect when the version is not a draft, or 404.</returns>
    [HttpGet("{id:guid}/fields/{code}")]
    public async Task<IActionResult> EditFieldAsync(
        Guid id,
        string code,
        [FromServices] IQueryHandler<GetTemplateVersion, TemplateVersionDetails?> query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var template = await query.HandleAsync(new GetTemplateVersion(id), cancellationToken);
        var field = template?.Fields.FirstOrDefault(f => string.Equals(f.Definition.Code, code, StringComparison.Ordinal));
        if (template is null || field is null)
        {
            return NotFound();
        }

        if (!template.IsDraft)
        {
            return Refused(id, null, TemplateErrors.NotDraft);
        }

        return View(new EditFieldViewModel(
            template, field, TempData.TakeFieldForm() ?? FieldForm.From(field.Definition), TempData.TakeNotice()));
    }

    /// <summary>Saves a draft field's label, section, type, unit and decimal places. Routed as <c>UpdateField</c>.</summary>
    /// <param name="id">The draft version id.</param>
    /// <param name="code">The field code, which cannot change.</param>
    /// <param name="form">The edit-field form.</param>
    /// <param name="handler">The command handler.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A redirect to the version page, or back to the edit page with the reason it was refused.</returns>
    [HttpPost("{id:guid}/fields/{code}")]
    public async Task<IActionResult> UpdateFieldAsync(
        Guid id,
        string code,
        [FromForm] FieldForm form,
        [FromServices] ICommandHandler<UpdateTemplateField, Result> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(handler);
        form.Code = code;
        var definition = form.ToDefinition();
        var result = definition.IsSuccess
            ? await handler.HandleAsync(new UpdateTemplateField(id, definition.Value), cancellationToken)
            : definition;
        if (result.IsSuccess)
        {
            return Done(id, TemplateSections.Fields, $"Field {code} saved.");
        }

        // The edit page cannot show a field that is gone or a version that is no longer a draft.
        if (result.Error!.Code == TemplateErrors.NotDraft.Code || result.Error.Code == TemplateErrors.UnknownField.Code)
        {
            return Refused(id, TemplateSections.Fields, result.Error);
        }

        TempData.SetFieldForm(form);
        TempData.SetNotice(new TemplateNotice(true, result.Error.Message, null));
        return RedirectToAction(EditFieldAction, new { id, code });
    }

    /// <summary>Moves a draft field one place up or down. Routed as <c>MoveField</c>.</summary>
    /// <param name="id">The draft version id.</param>
    /// <param name="code">The field code.</param>
    /// <param name="offset">Negative to move up, positive to move down.</param>
    /// <param name="handler">The command handler.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A redirect to the fields table.</returns>
    [HttpPost("{id:guid}/fields/{code}/move")]
    public async Task<IActionResult> MoveFieldAsync(
        Guid id,
        string code,
        [FromForm] int offset,
        [FromServices] ICommandHandler<MoveTemplateField, Result> handler,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || offset == 0)
        {
            return Refused(id, TemplateSections.Fields, FormErrors.InvalidInput.WithMessage("Choose to move the field up or down."));
        }

        var direction = offset < 0 ? "up" : "down";
        return await ChangeAsync(
            handler, new MoveTemplateField(id, code, Math.Sign(offset)), id, TemplateSections.Fields,
            $"Field {code} moved {direction}.", cancellationToken);
    }

    /// <summary>Removes a draft field that no rule uses. Routed as <c>RemoveField</c>.</summary>
    /// <param name="id">The draft version id.</param>
    /// <param name="code">The field code.</param>
    /// <param name="handler">The command handler.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A redirect to the fields table.</returns>
    [HttpPost("{id:guid}/fields/{code}/remove")]
    public Task<IActionResult> RemoveFieldAsync(
        Guid id,
        string code,
        [FromServices] ICommandHandler<RemoveTemplateField, Result> handler,
        CancellationToken cancellationToken) =>
        ChangeAsync(handler, new RemoveTemplateField(id, code), id, TemplateSections.Fields, $"Field {code} removed.", cancellationToken);

    /// <summary>Adds a validation rule to a draft. Routed as <c>AddRule</c>.</summary>
    /// <param name="id">The draft version id.</param>
    /// <param name="form">The add-rule form.</param>
    /// <param name="handler">The command handler.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A redirect to the version page; a refused form comes back as typed.</returns>
    [HttpPost("{id:guid}/rules")]
    public async Task<IActionResult> AddRuleAsync(
        Guid id,
        [FromForm] RuleForm form,
        [FromServices] ICommandHandler<AddValidationRule, Result> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(handler);
        var definition = form.ToDefinition();
        var result = definition.IsSuccess
            ? await handler.HandleAsync(new AddValidationRule(id, definition.Value), cancellationToken)
            : definition;
        if (result.IsFailure)
        {
            TempData.SetRuleForm(form);
            return Refused(id, TemplateSections.AddRule, result.Error!);
        }

        return Done(id, TemplateSections.AddRule, $"Rule {definition.Value.Code} added.");
    }

    /// <summary>Switches a draft's rule on or off. Routed as <c>SetRuleActive</c>.</summary>
    /// <param name="id">The draft version id.</param>
    /// <param name="code">The rule code.</param>
    /// <param name="active">Whether the rule is evaluated.</param>
    /// <param name="handler">The command handler.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A redirect to the rules table.</returns>
    [HttpPost("{id:guid}/rules/{code}/active")]
    public async Task<IActionResult> SetRuleActiveAsync(
        Guid id,
        string code,
        [FromForm] bool active,
        [FromServices] ICommandHandler<SetValidationRuleActive, Result> handler,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Refused(id, TemplateSections.Rules, FormErrors.InvalidInput.WithMessage("Choose to switch the rule on or off."));
        }

        var outcome = active ? $"Rule {code} is active." : $"Rule {code} is inactive: it is kept but not evaluated.";
        return await ChangeAsync(
            handler, new SetValidationRuleActive(id, code, active), id, TemplateSections.Rules, outcome, cancellationToken);
    }

    /// <summary>Removes a rule from a draft. Routed as <c>RemoveRule</c>.</summary>
    /// <param name="id">The draft version id.</param>
    /// <param name="code">The rule code.</param>
    /// <param name="handler">The command handler.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A redirect to the rules table.</returns>
    [HttpPost("{id:guid}/rules/{code}/remove")]
    public Task<IActionResult> RemoveRuleAsync(
        Guid id,
        string code,
        [FromServices] ICommandHandler<RemoveValidationRule, Result> handler,
        CancellationToken cancellationToken) =>
        ChangeAsync(handler, new RemoveValidationRule(id, code), id, TemplateSections.Rules, $"Rule {code} removed.", cancellationToken);

    /// <summary>Publishes a draft once the administrator has confirmed it. Routed as <c>Publish</c>.</summary>
    /// <param name="id">The draft version id.</param>
    /// <param name="confirm">Whether the confirmation box was ticked.</param>
    /// <param name="handler">The command handler.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A redirect to the version page.</returns>
    [HttpPost("{id:guid}/publish")]
    public async Task<IActionResult> PublishAsync(
        Guid id,
        [FromForm] bool confirm,
        [FromServices] ICommandHandler<PublishTemplateVersion, Result> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!ModelState.IsValid || !confirm)
        {
            return Refused(id, TemplateSections.Publish, FormErrors.NotConfirmed);
        }

        var result = await handler.HandleAsync(new PublishTemplateVersion(id), cancellationToken);
        return result.IsSuccess
            ? Done(id, null, "Version published. Banks file against it from its effective date, and it can no longer be changed.")
            : Refused(id, TemplateSections.Publish, result.Error!);
    }

    /// <summary>Retires a published version once the administrator has confirmed it. Routed as <c>Retire</c>.</summary>
    /// <param name="id">The published version id.</param>
    /// <param name="confirm">Whether the confirmation box was ticked.</param>
    /// <param name="handler">The command handler.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A redirect to the version page.</returns>
    [HttpPost("{id:guid}/retire")]
    public async Task<IActionResult> RetireAsync(
        Guid id,
        [FromForm] bool confirm,
        [FromServices] ICommandHandler<RetireTemplateVersion, Result> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!ModelState.IsValid || !confirm)
        {
            return Refused(id, TemplateSections.Retire, FormErrors.NotConfirmed);
        }

        var result = await handler.HandleAsync(new RetireTemplateVersion(id), cancellationToken);
        return result.IsSuccess
            ? Done(id, null, "Version retired. No new return is filed against it; returns already captured with it keep it.")
            : Refused(id, TemplateSections.Retire, result.Error!);
    }

    /// <summary>Deletes a draft once the administrator has confirmed it. Routed as <c>DeleteDraft</c>.</summary>
    /// <param name="id">The draft version id.</param>
    /// <param name="confirm">Whether the confirmation box was ticked.</param>
    /// <param name="handler">The command handler.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A redirect to the catalogue, or back to the version page with the reason it was refused.</returns>
    [HttpPost("{id:guid}/delete")]
    public async Task<IActionResult> DeleteDraftAsync(
        Guid id,
        [FromForm] bool confirm,
        [FromServices] ICommandHandler<DeleteTemplateDraft, Result> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!ModelState.IsValid || !confirm)
        {
            return Refused(id, TemplateSections.DeleteDraft, FormErrors.NotConfirmed);
        }

        var result = await handler.HandleAsync(new DeleteTemplateDraft(id), cancellationToken);
        if (result.IsFailure)
        {
            return Refused(id, TemplateSections.DeleteDraft, result.Error!);
        }

        TempData.SetNotice(new TemplateNotice(false, "Draft deleted.", null));
        return RedirectToAction(IndexAction);
    }

    private async Task<IActionResult> ChangeAsync<TCommand>(
        ICommandHandler<TCommand, Result> handler, TCommand command, Guid id, string section, string outcome, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var result = await handler.HandleAsync(command, cancellationToken);
        return result.IsSuccess ? Done(id, section, outcome) : Refused(id, section, result.Error!);
    }

    private RedirectToActionResult Done(Guid id, string? section, string outcome)
    {
        TempData.SetNotice(new TemplateNotice(false, outcome, section));
        return RedirectToAction(VersionAction, null, new { id }, section);
    }

    private RedirectToActionResult Refused(Guid id, string? section, Error error)
    {
        if (error.Code == TemplateErrors.NotFound.Code)
        {
            return RefusedAtCatalogue(error);
        }

        TempData.SetNotice(new TemplateNotice(true, error.Message, section));
        return RedirectToAction(VersionAction, null, new { id }, section);
    }

    private RedirectToActionResult RefusedAtCatalogue(Error error)
    {
        TempData.SetNotice(new TemplateNotice(true, error.Message, null));
        return RedirectToAction(IndexAction);
    }

    private DateOnly FirstOfNextMonth()
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        return new DateOnly(today.Year, today.Month, 1).AddMonths(1);
    }
}
