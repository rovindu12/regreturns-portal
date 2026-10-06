using System.Data;
using System.Diagnostics;
using System.Globalization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using RegReturns.Application.Diagnostics;
using RegReturns.Application.Migration;
using RegReturns.Domain.Common;
using RegReturns.Domain.Identity;
using RegReturns.Domain.Institutions;
using RegReturns.Domain.Migration;
using RegReturns.Domain.Obligations;
using RegReturns.Domain.Periods;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;
using RegReturns.Domain.Validation;
using RegReturns.Infrastructure.Persistence;

namespace RegReturns.Infrastructure.Legacy;

/// <summary>
/// Runs a legacy migration (ADR 0029). Reading and cleansing touch no database. Loading happens in one transaction:
/// every record is validated with the template's rules and becomes an approved, migrated return; the database's values
/// are then read back and reconciled against the source. Only a reconciled run that is not a dry run commits; any other
/// run rolls back and records just the run and its row errors.
/// </summary>
/// <param name="db">The database context.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class LegacyMigrator(RegReturnsDbContext db, TimeProvider timeProvider, ILogger<LegacyMigrator> logger)
    : ILegacyMigrator
{
    /// <summary>The note kept with each warning of a migrated return.</summary>
    internal const string WarningNote =
        "Accepted in the legacy returns system, which recorded no justification. Kept as found by the migration.";

    private const string DefaultSystemName = "the legacy returns system";

    /// <inheritdoc />
    public async Task<Result<LegacyMigrationResult>> RunAsync(LegacyMigrationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var activity = RegReturnsTelemetry.ActivitySource.StartActivity("migration.legacy");
        activity?.SetTag("regreturns.dry_run", request.DryRun);

        var source = await ReadSourceAsync(request, cancellationToken);
        if (source.IsFailure)
        {
            LogCannotStart(logger, source.Error!.Code, source.Error.Message);
            activity?.SetStatus(ActivityStatusCode.Error, source.Error.Code);
            return source.Error;
        }

        var (mapping, mappingSha256, tables) = source.Value;
        LogStarted(logger, tables.Count, request.SourceDirectory, request.DryRun);
        foreach (var table in tables)
        {
            LogFileRead(logger, table.FileName, table.Rows.Count, table.Sha256);
        }

        var reference = await ReferenceData.LoadAsync(db, cancellationToken);
        var reads = Cleanse(mapping, tables, reference);
        if (reads.IsFailure)
        {
            LogCannotStart(logger, reads.Error!.Code, reads.Error.Message);
            activity?.SetStatus(ActivityStatusCode.Error, reads.Error.Code);
            return reads.Error;
        }

        var strategy = db.Database.CreateExecutionStrategy();
        var outcome = await strategy.ExecuteAsync(
            (Migrator: this, Request: request, Reads: reads.Value, MappingSha256: mappingSha256, System: mapping.System),
            (_, state, ct) => state.Migrator.LoadAsync(state.Request, state.Reads, state.MappingSha256, state.System, ct),
            verifySucceeded: null,
            cancellationToken);

        if (!outcome.Committed)
        {
            // The load rolled back; the run and its row errors are still worth keeping.
            db.ChangeTracker.Clear();
            await db.MigrationRuns.AddAsync(outcome.Run, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        var result = outcome.ToResult();
        activity?.SetTag(RegReturnsTelemetry.OutcomeTag, result.Outcome.ToString());
        var totals = result.Totals;
        LogFinished(
            logger, result.RunId, result.Outcome, totals.MigratedReturns, totals.AlreadyMigratedReturns, totals.RejectedRows,
            totals.SupersededRows, totals.BlankRows, totals.RowsRead, result.Reconciliation.Mismatches, result.Committed);
        if (result.Outcome == MigrationOutcome.Mismatch)
        {
            LogMismatch(logger, result.RunId, result.Reconciliation.Mismatches, totals.IsBalanced);
        }

        return result;
    }

    private static async Task<Result<(LegacyMapping Mapping, string Sha256, List<LegacyTable> Tables)>> ReadSourceAsync(
        LegacyMigrationRequest request, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(request.SourceDirectory) || !File.Exists(request.MappingPath))
        {
            return MigrationErrors.SourceNotFound.WithMessage(
                $"The source folder '{request.SourceDirectory}' or the mapping file '{request.MappingPath}' does not exist.");
        }

        var parsed = LegacyMapping.Parse(await File.ReadAllBytesAsync(request.MappingPath, cancellationToken));
        if (parsed.IsFailure)
        {
            return parsed.Error!;
        }

        var (mapping, sha256) = parsed.Value;
        var present = Directory.EnumerateFiles(request.SourceDirectory, "*.csv", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToList();
        var mapped = mapping.Files.Select(f => f.File).ToHashSet(StringComparer.Ordinal);
        if (present.FirstOrDefault(f => !mapped.Contains(f)) is { } unmapped)
        {
            return MigrationErrors.SourceMismatch.WithMessage($"{unmapped} is in the source folder but not in the mapping.");
        }

        if (mapping.Files.FirstOrDefault(f => !present.Contains(f.File)) is { } missing)
        {
            return MigrationErrors.SourceMismatch.WithMessage($"{missing.File} is in the mapping but not in the source folder.");
        }

        var tables = new List<LegacyTable>();
        foreach (var name in mapping.Files.Select(f => f.File))
        {
            var path = Path.Combine(request.SourceDirectory, name);
            if (new FileInfo(path).Length > LegacyCsvReader.MaxFileBytes)
            {
                return MigrationErrors.FileUnreadable.WithMessage(
                    $"{name} is larger than {LegacyCsvReader.MaxFileBytes / (1024 * 1024)} MB.");
            }

            var table = LegacyCsvReader.Read(name, await File.ReadAllBytesAsync(path, cancellationToken));
            if (table.IsFailure)
            {
                return table.Error!;
            }

            tables.Add(table.Value);
        }

        return (mapping, sha256, tables);
    }

    private static Result<List<LegacyFileRead>> Cleanse(LegacyMapping mapping, List<LegacyTable> tables, ReferenceData reference)
    {
        var lookup = mapping.InstitutionLookup();
        var reads = new List<LegacyFileRead>();
        foreach (var (file, table) in mapping.Files.Zip(tables))
        {
            if (!reference.ReturnTypes.TryGetValue(file.ReturnType, out var returnType))
            {
                return MigrationErrors.MappingInvalid.WithMessage($"{file.File} maps to return type {file.ReturnType}, which is not in the portal.");
            }

            var known = reference.FieldCodes(returnType.Id);
            if (file.Fields.Values.FirstOrDefault(f => !known.Contains(f)) is { } unknown)
            {
                return MigrationErrors.MappingInvalid.WithMessage(
                    $"{file.File} maps a column to {unknown}, which is not a field of any published {file.ReturnType} template.");
            }

            var read = LegacyRowReader.Read(table, file, returnType.Frequency, mapping.Rules, lookup, reference.InstitutionCodes);
            if (read.IsFailure)
            {
                return read.Error!;
            }

            reads.Add(read.Value);
        }

        return reads;
    }

    private async Task<LoadOutcome> LoadAsync(
        LegacyMigrationRequest request, List<LegacyFileRead> reads, string mappingSha256, string? system, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var now = timeProvider.GetUtcNow();
        var run = MigrationRun.Start(request.SourceDirectory, mappingSha256, request.DryRun, now);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var state = await LoadState.CreateAsync(db, reads, cancellationToken);
        var migrator = await MigrationAccountAsync(cancellationToken);
        var loaded = new List<(LegacyRecord Record, string ReturnType, bool AlreadyMigrated)>();
        var loadErrors = new List<LegacyRowError>();

        var records = reads
            .SelectMany(r => r.Records.Select(record => (Record: record, r.Mapping.ReturnType)))
            .OrderBy(r => r.ReturnType, StringComparer.Ordinal)
            .ThenBy(r => r.Record.InstitutionCode, StringComparer.Ordinal)
            .ThenBy(r => r.Record.Period.Start);
        foreach (var (record, returnTypeCode) in records)
        {
            var step = Load(state, record, returnTypeCode, migrator, system ?? DefaultSystemName, run.Id, now);
            if (step.Errors.Count > 0)
            {
                loadErrors.AddRange(step.Errors);
            }
            else
            {
                loaded.Add((record, returnTypeCode, step.AlreadyMigrated));
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var source = loaded.Select(l => new ReconciledReturn(KeyOf(l.ReturnType, l.Record), l.Record.Values));
        var target = await TargetAsync(state, loaded.Select(l => (l.ReturnType, l.Record)), cancellationToken);
        var reconciliation = Reconciliation.Compare(source, target);

        var errors = reads.SelectMany(r => r.Errors).Concat(loadErrors)
            .OrderBy(e => e.FileName, StringComparer.Ordinal).ThenBy(e => e.LineNumber).ThenBy(e => e.Kind).ToList();
        var files = reads.Select(r => Summarise(r, errors, loaded)).ToList();
        var totals = Sum(files.Select(f => f.Totals));
        foreach (var read in reads)
        {
            run.AddFile(read.Table.FileName, read.Mapping.ReturnType, read.Table.Sha256, read.Table.Rows.Count);
        }

        foreach (var error in errors)
        {
            run.AddError(MigrationRowError.Create(
                error.FileName, error.LineNumber, error.Kind, error.Code, error.Message, error.Field, error.Value));
        }

        run.Finish(totals, reconciliation.Mismatches, timeProvider.GetUtcNow());
        var commit = !request.DryRun && run.Outcome == MigrationOutcome.Reconciled;
        if (commit)
        {
            await db.MigrationRuns.AddAsync(run, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await transaction.RollbackAsync(cancellationToken);
        }

        return new LoadOutcome(run, commit, totals, files, errors, reconciliation);
    }

    private static LoadStep Load(
        LoadState state, LegacyRecord record, string returnTypeCode, Actor migrator, string system, Guid runId, DateTimeOffset now)
    {
        var returnType = state.ReturnTypes[returnTypeCode];
        var institution = state.Institutions[record.InstitutionCode];
        var template = TemplateVersion.SelectFor(state.Templates[returnType.Id], record.Period);
        if (template is null)
        {
            return LoadStep.Rejected(Error(record, MigrationErrors.NoTemplate,
                $"No published {returnTypeCode} template applies to {record.Period.Label}."));
        }

        var key = (institution.Id, returnType.Id, record.Period);
        var obligation = state.Obligations.GetValueOrDefault(key);
        if (obligation is not null && state.LiveSources.TryGetValue(obligation.Id, out var liveSource))
        {
            return liveSource == SubmissionSource.Migration
                ? LoadStep.AlreadyMigratedStep
                : LoadStep.Rejected(Error(record, MigrationErrors.AlreadyFiled,
                    $"The portal already holds a {liveSource} return for {record.InstitutionCode} {returnTypeCode} {record.Period.Label}."));
        }

        var values = record.Values.ToDictionary(v => v.Key, v => LegacyCleansing.Format(v.Value), StringComparer.Ordinal);
        var findings = ValidationEngine.Validate(template, values, state.PriorValues(institution.Id, returnType.Id, record.Period));
        var failed = findings.Where(f => f.Severity == Severity.Error).ToList();
        if (failed.Count > 0)
        {
            return LoadStep.Rejected([.. failed.Select(f => Error(
                record, MigrationErrors.Validation, $"{f.RuleCode}: {f.Message}", record.Columns.GetValueOrDefault(f.FieldCode, f.FieldCode),
                values.GetValueOrDefault(f.FieldCode)))]);
        }

        var target = obligation ?? ReturnObligation.Create(institution.Id, returnType, record.Period);
        var filing = new MigratedFiling(
            record.SubmittedAt,
            record.ApprovedAt,
            string.Create(CultureInfo.InvariantCulture, $"Migrated from {system} ({record.FileName}, line {record.LineNumber}) by migration run {runId}."),
            WarningNote);
        var migrated = Submission.Migrate(target, template, values, findings, filing, migrator, now);
        if (migrated.IsFailure)
        {
            var error = migrated.Error!;
            var code = error.Code == SubmissionErrors.MigrationDates.Code ? MigrationErrors.InconsistentDates : MigrationErrors.Refused;
            return LoadStep.Rejected(Error(record, code, error.Message));
        }

        if (obligation is null)
        {
            state.AddObligation(key, target);
        }

        state.AddSubmission(target, migrated.Value);
        return LoadStep.Migrated;
    }

    private async Task<Actor> MigrationAccountAsync(CancellationToken cancellationToken)
    {
        var account = await db.Users.SingleOrDefaultAsync(u => u.UserName == AppUser.MigrationUserName, cancellationToken);
        if (account is null)
        {
            account = AppUser.ForMigration();
            await db.Users.AddAsync(account, cancellationToken);
        }

        return account.ToActor();
    }

    private async Task<List<ReconciledReturn>> TargetAsync(
        LoadState state, IEnumerable<(string ReturnType, LegacyRecord Record)> loaded, CancellationToken cancellationToken)
    {
        var keys = new Dictionary<Guid, ReconciliationKey>();
        foreach (var (returnType, record) in loaded)
        {
            var obligation = state.Obligations[(state.Institutions[record.InstitutionCode].Id, state.ReturnTypes[returnType].Id, record.Period)];
            keys[obligation.Id] = KeyOf(returnType, record);
        }

        var ids = keys.Keys.ToList();
        var stored = await db.Submissions.AsNoTracking()
            .Where(s => ids.Contains(s.ObligationId) && s.Source == SubmissionSource.Migration && s.Status == SubmissionStatus.Approved)
            .Select(s => new { s.ObligationId, Values = s.Values.Select(v => new { v.FieldCode, v.NumericValue }).ToList() })
            .ToListAsync(cancellationToken);
        return [.. stored.Select(s => new ReconciledReturn(
            keys[s.ObligationId],
            s.Values.ToDictionary(v => v.FieldCode, v => v.NumericValue, StringComparer.Ordinal)))];
    }

    private static LegacyFileSummary Summarise(
        LegacyFileRead read, List<LegacyRowError> errors, List<(LegacyRecord Record, string ReturnType, bool AlreadyMigrated)> loaded)
    {
        var name = read.Table.FileName;
        var fileErrors = errors.Where(e => e.FileName == name).ToList();
        var fileLoaded = loaded.Where(l => l.Record.FileName == name).ToList();
        var totals = new MigrationTotals(
            RowsRead: read.Table.Rows.Count,
            BlankRows: read.BlankRows,
            SupersededRows: fileErrors.Where(e => e.Kind == RowErrorKind.Superseded).Select(e => e.LineNumber).Distinct().Count(),
            RejectedRows: fileErrors.Where(e => e.Kind == RowErrorKind.Rejected).Select(e => e.LineNumber).Distinct().Count(),
            MigratedReturns: fileLoaded.Count(l => !l.AlreadyMigrated),
            AlreadyMigratedReturns: fileLoaded.Count(l => l.AlreadyMigrated));
        return new LegacyFileSummary(name, read.Mapping.ReturnType, read.Table.Sha256, totals);
    }

    private static MigrationTotals Sum(IEnumerable<MigrationTotals> totals) => totals.Aggregate(
        new MigrationTotals(0, 0, 0, 0, 0, 0),
        (sum, t) => new MigrationTotals(
            sum.RowsRead + t.RowsRead,
            sum.BlankRows + t.BlankRows,
            sum.SupersededRows + t.SupersededRows,
            sum.RejectedRows + t.RejectedRows,
            sum.MigratedReturns + t.MigratedReturns,
            sum.AlreadyMigratedReturns + t.AlreadyMigratedReturns));

    private static ReconciliationKey KeyOf(string returnType, LegacyRecord record) =>
        new(returnType, record.InstitutionCode, record.Period.Label);

    private static LegacyRowError Error(LegacyRecord record, string code, string message, string? field = null, string? value = null) =>
        new(record.FileName, record.LineNumber, RowErrorKind.Rejected, code, message, field, value);

    [LoggerMessage(EventId = 2101, Level = LogLevel.Information,
        Message = "Legacy migration started: {Files} file(s) from {Source}, dry run {DryRun}")]
    private static partial void LogStarted(ILogger logger, int files, string source, bool dryRun);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Information, Message = "Read {FileName}: {Rows} data row(s), SHA-256 {Sha256}")]
    private static partial void LogFileRead(ILogger logger, string fileName, int rows, string sha256);

    [LoggerMessage(EventId = 2103, Level = LogLevel.Information,
        Message = "Legacy migration run {RunId} {Outcome}: {Migrated} migrated, {AlreadyMigrated} already migrated, {Rejected} rejected, "
            + "{Superseded} superseded and {Blank} blank of {Rows} row(s); {Mismatches} reconciliation difference(s); committed {Committed}")]
    private static partial void LogFinished(
        ILogger logger, Guid runId, MigrationOutcome outcome, int migrated, int alreadyMigrated, int rejected, int superseded,
        int blank, int rows, int mismatches, bool committed);

    [LoggerMessage(EventId = 2104, Level = LogLevel.Warning,
        Message = "Legacy migration run {RunId} did not reconcile ({Mismatches} difference(s), rows balanced {Balanced}); nothing was committed")]
    private static partial void LogMismatch(ILogger logger, Guid runId, int mismatches, bool balanced);

    [LoggerMessage(EventId = 2105, Level = LogLevel.Error, Message = "Legacy migration could not start: {ErrorCode} {Reason}")]
    private static partial void LogCannotStart(ILogger logger, string errorCode, string reason);

    private sealed record LoadStep(IReadOnlyList<LegacyRowError> Errors, bool AlreadyMigrated)
    {
        public static LoadStep Migrated { get; } = new([], false);

        public static LoadStep AlreadyMigratedStep { get; } = new([], true);

        public static LoadStep Rejected(params LegacyRowError[] errors) => new(errors, false);
    }

    private sealed record LoadOutcome(
        MigrationRun Run,
        bool Committed,
        MigrationTotals Totals,
        IReadOnlyList<LegacyFileSummary> Files,
        IReadOnlyList<LegacyRowError> Errors,
        Reconciliation Reconciliation)
    {
        public LegacyMigrationResult ToResult() =>
            new(Run.Id, Run.IsDryRun, Committed, Run.Outcome!.Value, Totals, Files, Errors, Reconciliation);
    }

    /// <summary>The portal's banks, return types and published templates, which reading and loading check against.</summary>
    private sealed class ReferenceData
    {
        private readonly Dictionary<Guid, HashSet<string>> _fieldCodes;

        private ReferenceData(
            Dictionary<string, Institution> institutions, Dictionary<string, ReturnType> returnTypes, Dictionary<Guid, HashSet<string>> fieldCodes)
        {
            Institutions = institutions;
            ReturnTypes = returnTypes;
            InstitutionCodes = institutions.Keys.ToHashSet(StringComparer.Ordinal);
            _fieldCodes = fieldCodes;
        }

        public Dictionary<string, Institution> Institutions { get; }

        public Dictionary<string, ReturnType> ReturnTypes { get; }

        public HashSet<string> InstitutionCodes { get; }

        public static async Task<ReferenceData> LoadAsync(RegReturnsDbContext db, CancellationToken cancellationToken)
        {
            var institutions = await db.Institutions.AsNoTracking().ToDictionaryAsync(i => i.Code, StringComparer.Ordinal, cancellationToken);
            var returnTypes = await db.ReturnTypes.AsNoTracking().ToDictionaryAsync(r => r.Code, StringComparer.Ordinal, cancellationToken);
            var fields = await db.TemplateVersions.AsNoTracking()
                .Where(v => v.Status == TemplateStatus.Published)
                .SelectMany(v => v.Fields.Select(f => new { v.ReturnTypeId, f.Code }))
                .ToListAsync(cancellationToken);
            var fieldCodes = fields.GroupBy(f => f.ReturnTypeId)
                .ToDictionary(g => g.Key, g => g.Select(f => f.Code).ToHashSet(StringComparer.Ordinal));
            return new ReferenceData(institutions, returnTypes, fieldCodes);
        }

        public HashSet<string> FieldCodes(Guid returnTypeId) =>
            _fieldCodes.TryGetValue(returnTypeId, out var codes) ? codes : [];
    }

    /// <summary>What the load needs from the database, tracked in the load's transaction, and the returns it adds.</summary>
    private sealed class LoadState
    {
        private readonly RegReturnsDbContext _db;
        private readonly Dictionary<(Guid Institution, Guid ReturnType, ReportingPeriod Period), IReadOnlyDictionary<string, decimal>> _approved;

        private LoadState(RegReturnsDbContext db)
        {
            _db = db;
            _approved = [];
        }

        public Dictionary<string, Institution> Institutions { get; private init; } = [];

        public Dictionary<string, ReturnType> ReturnTypes { get; private init; } = [];

        public Dictionary<Guid, List<TemplateVersion>> Templates { get; private init; } = [];

        public Dictionary<(Guid Institution, Guid ReturnType, ReportingPeriod Period), ReturnObligation> Obligations { get; private init; } = [];

        public Dictionary<Guid, SubmissionSource> LiveSources { get; private init; } = [];

        public static async Task<LoadState> CreateAsync(RegReturnsDbContext db, List<LegacyFileRead> reads, CancellationToken cancellationToken)
        {
            var bankCodes = reads.SelectMany(r => r.Records).Select(r => r.InstitutionCode).Distinct().ToList();
            var typeCodes = reads.Select(r => r.Mapping.ReturnType).Distinct().ToList();
            var institutions = await db.Institutions.Where(i => bankCodes.Contains(i.Code)).ToDictionaryAsync(i => i.Code, StringComparer.Ordinal, cancellationToken);
            var returnTypes = await db.ReturnTypes.Where(r => typeCodes.Contains(r.Code)).ToDictionaryAsync(r => r.Code, StringComparer.Ordinal, cancellationToken);
            var institutionIds = institutions.Values.Select(i => i.Id).ToList();
            var typeIds = returnTypes.Values.Select(r => r.Id).ToList();

            var templates = await db.TemplateVersions.AsNoTracking()
                .Include(v => v.Fields)
                .Include(v => v.Rules)
                .Where(v => typeIds.Contains(v.ReturnTypeId))
                .ToListAsync(cancellationToken);
            var obligations = await db.Obligations
                .Where(o => institutionIds.Contains(o.InstitutionId) && typeIds.Contains(o.ReturnTypeId))
                .ToListAsync(cancellationToken);
            var obligationIds = obligations.Select(o => o.Id).ToList();
            var live = await db.Submissions.AsNoTracking()
                .Where(s => obligationIds.Contains(s.ObligationId) && s.Status != SubmissionStatus.Rejected)
                .Select(s => new { s.ObligationId, s.Source })
                .ToListAsync(cancellationToken);
            var approved = await db.Submissions.AsNoTracking()
                .Where(s => obligationIds.Contains(s.ObligationId) && s.Status == SubmissionStatus.Approved)
                .Select(s => new { s.ObligationId, Values = s.Values.Where(v => v.NumericValue != null).Select(v => new { v.FieldCode, v.NumericValue }).ToList() })
                .ToListAsync(cancellationToken);

            var state = new LoadState(db)
            {
                Institutions = institutions,
                ReturnTypes = returnTypes,
                Templates = typeIds.ToDictionary(id => id, id => templates.Where(t => t.ReturnTypeId == id).ToList()),
                Obligations = obligations.ToDictionary(o => (o.InstitutionId, o.ReturnTypeId, o.Period)),
                LiveSources = live.ToDictionary(s => s.ObligationId, s => s.Source),
            };
            var byId = obligations.ToDictionary(o => o.Id);
            foreach (var submission in approved)
            {
                var obligation = byId[submission.ObligationId];
                state._approved[(obligation.InstitutionId, obligation.ReturnTypeId, obligation.Period)] =
                    submission.Values.ToDictionary(v => v.FieldCode, v => v.NumericValue!.Value, StringComparer.Ordinal);
            }

            return state;
        }

        public Dictionary<VarianceBasis, IReadOnlyDictionary<string, decimal>> PriorValues(Guid institutionId, Guid returnTypeId, ReportingPeriod period)
        {
            var prior = new Dictionary<VarianceBasis, IReadOnlyDictionary<string, decimal>>();
            if (_approved.TryGetValue((institutionId, returnTypeId, period.Previous()), out var previous))
            {
                prior[VarianceBasis.PreviousPeriod] = previous;
            }

            if (_approved.TryGetValue((institutionId, returnTypeId, period.SamePeriodLastYear()), out var lastYear))
            {
                prior[VarianceBasis.SamePeriodLastYear] = lastYear;
            }

            return prior;
        }

        public void AddObligation((Guid Institution, Guid ReturnType, ReportingPeriod Period) key, ReturnObligation obligation)
        {
            Obligations[key] = obligation;
            _db.Obligations.Add(obligation);
        }

        public void AddSubmission(ReturnObligation obligation, Submission submission)
        {
            LiveSources[obligation.Id] = SubmissionSource.Migration;
            _approved[(obligation.InstitutionId, obligation.ReturnTypeId, obligation.Period)] = submission.Values
                .Where(v => v.NumericValue is not null)
                .ToDictionary(v => v.FieldCode, v => v.NumericValue!.Value, StringComparer.Ordinal);
            _db.Submissions.Add(submission);
        }
    }
}
