using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using RegReturns.Domain.Common;

namespace RegReturns.Infrastructure.Persistence.Configurations;

/// <summary>Shared mapping conventions for entity configurations.</summary>
internal static class ConfigurationExtensions
{
    /// <summary>Name of the shadow concurrency token column.</summary>
    public const string RowVersion = "RowVersion";

    /// <summary>Maps the key (assigned by the domain, never by the database).</summary>
    public static void HasDomainKey<T>(this EntityTypeBuilder<T> builder)
        where T : Entity
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
    }

    /// <summary>Adds a SQL Server <c>rowversion</c> shadow property for optimistic concurrency.</summary>
    public static void HasRowVersion<T>(this EntityTypeBuilder<T> builder)
        where T : class =>
        builder.Property<byte[]>(RowVersion).IsRowVersion();
}
