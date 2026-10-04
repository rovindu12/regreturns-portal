namespace RegReturns.Application.Abstractions;

/// <summary>Applies schema migrations and loads demo data. Used by the migrator tool and integration tests.</summary>
public interface IDatabaseInitializer
{
    /// <summary>Applies any pending schema migrations.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The names of the migrations that were applied.</returns>
    Task<IReadOnlyList<string>> MigrateAsync(CancellationToken cancellationToken);

    /// <summary>Loads the demo data set if the database has none. Safe to run repeatedly.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="true"/> if data was loaded; <see langword="false"/> if it was already present.</returns>
    Task<bool> SeedAsync(CancellationToken cancellationToken);
}
