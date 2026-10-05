using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Common;
using RegReturns.Domain.Templates;

namespace RegReturns.Application.Templates;

/// <summary>
/// Base for commands that change one template version: loads it with its fields and rules, applies the domain
/// operation and saves. The domain refuses anything but a draft, so published versions stay immutable.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <param name="db">The unit of work.</param>
public abstract class TemplateEditHandler<TCommand>(IAppDbContext db) : ICommandHandler<TCommand, Result>
{
    /// <inheritdoc />
    public async Task<Result> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var version = await LoadAsync(db, VersionIdOf(command), cancellationToken);
        if (version is null)
        {
            return TemplateErrors.NotFound;
        }

        var result = Apply(version, command);
        return result.IsFailure ? result : await db.SaveOrConflictAsync(cancellationToken);
    }

    /// <summary>Loads a version with its fields and rules, tracked for changes.</summary>
    /// <param name="db">The unit of work.</param>
    /// <param name="id">The version id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The version, or <see langword="null"/>.</returns>
    internal static Task<TemplateVersion?> LoadAsync(IAppDbContext db, Guid id, CancellationToken cancellationToken) =>
        db.TemplateVersions
            .Include(v => v.Fields)
            .Include(v => v.Rules)
            .SingleOrDefaultAsync(v => v.Id == id, cancellationToken);

    /// <summary>Returns the id of the version the command changes.</summary>
    /// <param name="command">The command.</param>
    /// <returns>The version id.</returns>
    protected abstract Guid VersionIdOf(TCommand command);

    /// <summary>Applies the command to the loaded version.</summary>
    /// <param name="version">The version.</param>
    /// <param name="command">The command.</param>
    /// <returns>The domain result.</returns>
    protected abstract Result Apply(TemplateVersion version, TCommand command);
}

/// <summary>Adds a field to the end of a draft.</summary>
/// <param name="VersionId">The draft version id.</param>
/// <param name="Field">The field.</param>
public sealed record AddTemplateField(Guid VersionId, FieldDefinition Field);

/// <summary>Handles <see cref="AddTemplateField"/>.</summary>
/// <param name="db">The unit of work.</param>
public sealed class AddTemplateFieldHandler(IAppDbContext db) : TemplateEditHandler<AddTemplateField>(db)
{
    /// <inheritdoc />
    protected override Guid VersionIdOf(AddTemplateField command) => command.VersionId;

    /// <inheritdoc />
    protected override Result Apply(TemplateVersion version, AddTemplateField command) => version.AddField(command.Field);
}

/// <summary>Changes a draft field's label, section, type, unit or precision; the code names the field.</summary>
/// <param name="VersionId">The draft version id.</param>
/// <param name="Field">The field's new definition.</param>
public sealed record UpdateTemplateField(Guid VersionId, FieldDefinition Field);

/// <summary>Handles <see cref="UpdateTemplateField"/>.</summary>
/// <param name="db">The unit of work.</param>
public sealed class UpdateTemplateFieldHandler(IAppDbContext db) : TemplateEditHandler<UpdateTemplateField>(db)
{
    /// <inheritdoc />
    protected override Guid VersionIdOf(UpdateTemplateField command) => command.VersionId;

    /// <inheritdoc />
    protected override Result Apply(TemplateVersion version, UpdateTemplateField command) => version.UpdateField(command.Field);
}

/// <summary>Removes a field no rule uses from a draft.</summary>
/// <param name="VersionId">The draft version id.</param>
/// <param name="FieldCode">The field code.</param>
public sealed record RemoveTemplateField(Guid VersionId, string FieldCode);

/// <summary>Handles <see cref="RemoveTemplateField"/>.</summary>
/// <param name="db">The unit of work.</param>
public sealed class RemoveTemplateFieldHandler(IAppDbContext db) : TemplateEditHandler<RemoveTemplateField>(db)
{
    /// <inheritdoc />
    protected override Guid VersionIdOf(RemoveTemplateField command) => command.VersionId;

    /// <inheritdoc />
    protected override Result Apply(TemplateVersion version, RemoveTemplateField command) => version.RemoveField(command.FieldCode);
}

/// <summary>Moves a draft field up (negative offset) or down (positive offset).</summary>
/// <param name="VersionId">The draft version id.</param>
/// <param name="FieldCode">The field code.</param>
/// <param name="Offset">Positions to move.</param>
public sealed record MoveTemplateField(Guid VersionId, string FieldCode, int Offset);

/// <summary>Handles <see cref="MoveTemplateField"/>.</summary>
/// <param name="db">The unit of work.</param>
public sealed class MoveTemplateFieldHandler(IAppDbContext db) : TemplateEditHandler<MoveTemplateField>(db)
{
    /// <inheritdoc />
    protected override Guid VersionIdOf(MoveTemplateField command) => command.VersionId;

    /// <inheritdoc />
    protected override Result Apply(TemplateVersion version, MoveTemplateField command) =>
        version.MoveField(command.FieldCode, command.Offset);
}

/// <summary>Adds a validation rule to a draft.</summary>
/// <param name="VersionId">The draft version id.</param>
/// <param name="Rule">The rule definition.</param>
public sealed record AddValidationRule(Guid VersionId, RuleDefinition Rule);

/// <summary>Handles <see cref="AddValidationRule"/>.</summary>
/// <param name="db">The unit of work.</param>
public sealed class AddValidationRuleHandler(IAppDbContext db) : TemplateEditHandler<AddValidationRule>(db)
{
    /// <inheritdoc />
    protected override Guid VersionIdOf(AddValidationRule command) => command.VersionId;

    /// <inheritdoc />
    protected override Result Apply(TemplateVersion version, AddValidationRule command)
    {
        var rule = ValidationRule.Create(command.Rule);
        return rule.IsSuccess ? version.AddRule(rule.Value) : rule;
    }
}

/// <summary>Removes a validation rule from a draft.</summary>
/// <param name="VersionId">The draft version id.</param>
/// <param name="RuleCode">The rule code.</param>
public sealed record RemoveValidationRule(Guid VersionId, string RuleCode);

/// <summary>Handles <see cref="RemoveValidationRule"/>.</summary>
/// <param name="db">The unit of work.</param>
public sealed class RemoveValidationRuleHandler(IAppDbContext db) : TemplateEditHandler<RemoveValidationRule>(db)
{
    /// <inheritdoc />
    protected override Guid VersionIdOf(RemoveValidationRule command) => command.VersionId;

    /// <inheritdoc />
    protected override Result Apply(TemplateVersion version, RemoveValidationRule command) => version.RemoveRule(command.RuleCode);
}

/// <summary>Switches a draft's rule on or off.</summary>
/// <param name="VersionId">The draft version id.</param>
/// <param name="RuleCode">The rule code.</param>
/// <param name="Active">Whether the rule is evaluated.</param>
public sealed record SetValidationRuleActive(Guid VersionId, string RuleCode, bool Active);

/// <summary>Handles <see cref="SetValidationRuleActive"/>.</summary>
/// <param name="db">The unit of work.</param>
public sealed class SetValidationRuleActiveHandler(IAppDbContext db) : TemplateEditHandler<SetValidationRuleActive>(db)
{
    /// <inheritdoc />
    protected override Guid VersionIdOf(SetValidationRuleActive command) => command.VersionId;

    /// <inheritdoc />
    protected override Result Apply(TemplateVersion version, SetValidationRuleActive command) =>
        version.SetRuleActive(command.RuleCode, command.Active);
}

/// <summary>Changes the first reporting-period start date a draft applies to.</summary>
/// <param name="VersionId">The draft version id.</param>
/// <param name="EffectiveFrom">The new date.</param>
public sealed record ChangeTemplateEffectiveDate(Guid VersionId, DateOnly EffectiveFrom);

/// <summary>Handles <see cref="ChangeTemplateEffectiveDate"/>.</summary>
/// <param name="db">The unit of work.</param>
public sealed class ChangeTemplateEffectiveDateHandler(IAppDbContext db) : TemplateEditHandler<ChangeTemplateEffectiveDate>(db)
{
    /// <inheritdoc />
    protected override Guid VersionIdOf(ChangeTemplateEffectiveDate command) => command.VersionId;

    /// <inheritdoc />
    protected override Result Apply(TemplateVersion version, ChangeTemplateEffectiveDate command) =>
        version.ChangeEffectiveFrom(command.EffectiveFrom);
}
