using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using RegReturns.Application.Abstractions;
using RegReturns.Infrastructure.Persistence.Seeding;

namespace RegReturns.Infrastructure.Persistence;

/// <summary>Applies migrations and loads the demo data set.</summary>
/// <param name="context">The database context.</param>
/// <param name="timeProvider">The clock used to date the demo history.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class DatabaseInitializer(
    RegReturnsDbContext context,
    TimeProvider timeProvider,
    ILogger<DatabaseInitializer> logger) : IDatabaseInitializer
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> MigrateAsync(CancellationToken cancellationToken)
    {
        var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
        {
            LogNoPendingMigrations(logger);
            return [];
        }

        LogApplyingMigrations(logger, pending.Count, pending);
        await context.Database.MigrateAsync(cancellationToken);
        return pending;
    }

    /// <inheritdoc />
    public async Task<bool> SeedAsync(CancellationToken cancellationToken)
    {
        if (await context.Institutions.AnyAsync(cancellationToken))
        {
            LogSeedSkipped(logger);
            return false;
        }

        var data = new DemoDataBuilder(timeProvider.GetUtcNow()).Build();

        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(
            (context, data),
            async (_, state, ct) =>
            {
                state.context.ChangeTracker.Clear();
                await state.context.AddRangeAsync(state.data.Institutions, ct);
                await state.context.AddRangeAsync(state.data.Users, ct);
                await state.context.AddRangeAsync(state.data.ReturnTypes, ct);
                await state.context.AddRangeAsync(state.data.Templates, ct);
                await state.context.AddRangeAsync(state.data.Obligations, ct);
                await state.context.AddRangeAsync(state.data.Submissions, ct);
                return await state.context.SaveChangesAsync(ct);
            },
            verifySucceeded: null,
            cancellationToken);

        LogSeeded(logger, data.Institutions.Count, data.Obligations.Count, data.Submissions.Count);
        return true;
    }

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Database schema is up to date")]
    private static partial void LogNoPendingMigrations(ILogger logger);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Applying {Count} migration(s): {Migrations}")]
    private static partial void LogApplyingMigrations(ILogger logger, int count, IReadOnlyList<string> migrations);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "Demo data already present; seeding skipped")]
    private static partial void LogSeedSkipped(ILogger logger);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Information,
        Message = "Seeded {Institutions} institutions, {Obligations} obligations and {Submissions} submissions")]
    private static partial void LogSeeded(ILogger logger, int institutions, int obligations, int submissions);
}
