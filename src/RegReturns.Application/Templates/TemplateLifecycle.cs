using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Common;
using RegReturns.Domain.Templates;

namespace RegReturns.Application.Templates;

/// <summary>
/// Starts a new draft version of a return type's template, copied from an existing version (by default the latest
/// published one) or empty when the return type has none. A return type has at most one draft at a time.
/// </summary>
/// <param name="ReturnTypeId">The return type.</param>
/// <param name="EffectiveFrom">First reporting-period start date the new version will apply to.</param>
/// <param name="CopyFromVersionId">The version to copy, or <see langword="null"/> for the latest published one.</param>
public sealed record StartTemplateDraft(Guid ReturnTypeId, DateOnly EffectiveFrom, Guid? CopyFromVersionId = null);

/// <summary>Handles <see cref="StartTemplateDraft"/>.</summary>
/// <param name="db">The unit of work.</param>
/// <param name="logger">The logger.</param>
public sealed class StartTemplateDraftHandler(IAppDbContext db, ILogger<StartTemplateDraftHandler> logger)
    : ICommandHandler<StartTemplateDraft, Result<Guid>>
{
    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(StartTemplateDraft command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await db.ReturnTypes.AnyAsync(r => r.Id == command.ReturnTypeId, cancellationToken))
        {
            return TemplateErrors.NotFound;
        }

        var versions = await db.TemplateVersions
            .Where(v => v.ReturnTypeId == command.ReturnTypeId)
            .Select(v => new { v.Id, v.Version, v.Status })
            .ToListAsync(cancellationToken);
        if (versions.Exists(v => v.Status == TemplateStatus.Draft))
        {
            return TemplateErrors.DraftExists;
        }

        var sourceId = command.CopyFromVersionId
            ?? versions.Where(v => v.Status == TemplateStatus.Published).MaxBy(v => v.Version)?.Id;
        if (command.CopyFromVersionId is { } requested && !versions.Exists(v => v.Id == requested))
        {
            return TemplateErrors.NotFound;
        }

        var number = versions.Count == 0 ? 1 : versions.Max(v => v.Version) + 1;
        TemplateVersion draft;
        if (sourceId is { } id)
        {
            var source = await TemplateEditHandler<StartTemplateDraft>.LoadAsync(db, id, cancellationToken);
            draft = source!.CopyAsDraft(number, command.EffectiveFrom);
        }
        else
        {
            draft = TemplateVersion.CreateDraft(command.ReturnTypeId, number, command.EffectiveFrom);
        }

        await db.TemplateVersions.AddAsync(draft, cancellationToken);
        var saved = await db.SaveOrConflictAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error!;
        }

        TemplateLog.DraftStarted(logger, draft.Id, command.ReturnTypeId, number);
        return draft.Id;
    }
}

/// <summary>Deletes a draft version that was never published.</summary>
/// <param name="VersionId">The draft version id.</param>
public sealed record DeleteTemplateDraft(Guid VersionId);

/// <summary>Handles <see cref="DeleteTemplateDraft"/>.</summary>
/// <param name="db">The unit of work.</param>
/// <param name="logger">The logger.</param>
public sealed class DeleteTemplateDraftHandler(IAppDbContext db, ILogger<DeleteTemplateDraftHandler> logger)
    : ICommandHandler<DeleteTemplateDraft, Result>
{
    /// <inheritdoc />
    public async Task<Result> HandleAsync(DeleteTemplateDraft command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var version = await TemplateEditHandler<DeleteTemplateDraft>.LoadAsync(db, command.VersionId, cancellationToken);
        if (version is null)
        {
            return TemplateErrors.NotFound;
        }

        if (version.Status != TemplateStatus.Draft)
        {
            return TemplateErrors.NotDraft;
        }

        db.TemplateVersions.Remove(version);
        var saved = await db.SaveOrConflictAsync(cancellationToken);
        if (saved.IsSuccess)
        {
            TemplateLog.DraftDeleted(logger, version.Id, version.ReturnTypeId, version.Version);
        }

        return saved;
    }
}

/// <summary>Publishes a draft so banks file against it for periods from its effective date.</summary>
/// <param name="VersionId">The draft version id.</param>
public sealed record PublishTemplateVersion(Guid VersionId);

/// <summary>Handles <see cref="PublishTemplateVersion"/>.</summary>
/// <param name="db">The unit of work.</param>
/// <param name="logger">The logger.</param>
public sealed class PublishTemplateVersionHandler(IAppDbContext db, ILogger<PublishTemplateVersionHandler> logger)
    : TemplateEditHandler<PublishTemplateVersion>(db)
{
    /// <inheritdoc />
    protected override Guid VersionIdOf(PublishTemplateVersion command) => command.VersionId;

    /// <inheritdoc />
    protected override Result Apply(TemplateVersion version, PublishTemplateVersion command)
    {
        var result = version.Publish();
        if (result.IsSuccess)
        {
            TemplateLog.Published(logger, version.Id, version.ReturnTypeId, version.Version, version.EffectiveFrom);
        }

        return result;
    }
}

/// <summary>Retires a published version so no new return files against it.</summary>
/// <param name="VersionId">The published version id.</param>
public sealed record RetireTemplateVersion(Guid VersionId);

/// <summary>Handles <see cref="RetireTemplateVersion"/>.</summary>
/// <param name="db">The unit of work.</param>
/// <param name="logger">The logger.</param>
public sealed class RetireTemplateVersionHandler(IAppDbContext db, ILogger<RetireTemplateVersionHandler> logger)
    : TemplateEditHandler<RetireTemplateVersion>(db)
{
    /// <inheritdoc />
    protected override Guid VersionIdOf(RetireTemplateVersion command) => command.VersionId;

    /// <inheritdoc />
    protected override Result Apply(TemplateVersion version, RetireTemplateVersion command)
    {
        var result = version.Retire();
        if (result.IsSuccess)
        {
            TemplateLog.Retired(logger, version.Id, version.ReturnTypeId, version.Version);
        }

        return result;
    }
}

/// <summary>Log events for template administration (50xx).</summary>
internal static partial class TemplateLog
{
    [LoggerMessage(EventId = 5001, Level = LogLevel.Information,
        Message = "Template draft {TemplateVersionId} (version {Version}) started for return type {ReturnTypeId}")]
    public static partial void DraftStarted(ILogger logger, Guid templateVersionId, Guid returnTypeId, int version);

    [LoggerMessage(EventId = 5002, Level = LogLevel.Information,
        Message = "Template draft {TemplateVersionId} (version {Version}) of return type {ReturnTypeId} deleted")]
    public static partial void DraftDeleted(ILogger logger, Guid templateVersionId, Guid returnTypeId, int version);

    [LoggerMessage(EventId = 5003, Level = LogLevel.Information,
        Message = "Template version {TemplateVersionId} (version {Version}) of return type {ReturnTypeId} published, effective from {EffectiveFrom}")]
    public static partial void Published(ILogger logger, Guid templateVersionId, Guid returnTypeId, int version, DateOnly effectiveFrom);

    [LoggerMessage(EventId = 5004, Level = LogLevel.Information,
        Message = "Template version {TemplateVersionId} (version {Version}) of return type {ReturnTypeId} retired")]
    public static partial void Retired(ILogger logger, Guid templateVersionId, Guid returnTypeId, int version);
}
