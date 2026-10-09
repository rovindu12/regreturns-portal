using RegReturns.Infrastructure.Persistence.Seeding;

namespace RegReturns.Infrastructure.Legacy;

/// <summary>The sample legacy exports and mapping in <c>samples/legacy</c> (ADR 0029), for the migrator's <c>legacy-samples</c> verb.</summary>
public static class LegacySamples
{
    /// <summary>Generates the sample files: the same bytes on every call.</summary>
    /// <returns>File names and contents.</returns>
    public static IReadOnlyList<(string Name, byte[] Content)> Generate() => LegacySampleGenerator.Generate();
}
