using Microsoft.EntityFrameworkCore;

using RegReturns.Infrastructure.Demo;
using RegReturns.UnitTests.Auditing;

namespace RegReturns.UnitTests.Infrastructure.Demo;

public sealed class DemoResetTablesTests
{
    [Fact]
    public void Every_table_is_either_reset_or_kept_and_none_is_both()
    {
        using var db = DataChangeCollectorTests.OfflineContext();
        var modelTables = db.Model.GetEntityTypes()
            .Where(e => e.GetTableName() is not null)
            .Select(e => $"{e.GetSchema()}.{e.GetTableName()}")
            .Distinct()
            .Order(StringComparer.Ordinal);

        var listed = DemoResetService.WorkloadTables.Concat(DemoResetService.KeptTables).Select(t => $"{t.Schema}.{t.Table}").ToList();

        listed.ShouldBeUnique();
        listed.Order(StringComparer.Ordinal).ShouldBe(modelTables);
    }

    [Fact]
    public void Children_are_deleted_before_the_tables_they_reference()
    {
        using var db = DataChangeCollectorTests.OfflineContext();
        var order = DemoResetService.WorkloadTables.Select(t => $"{t.Schema}.{t.Table}").ToList();

        foreach (var foreignKey in db.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
        {
            var child = Name(foreignKey.DeclaringEntityType);
            var parent = Name(foreignKey.PrincipalEntityType);
            if (child == parent || !order.Contains(parent))
            {
                continue;
            }

            // A kept table must not point at the workload, or the reset could not delete it.
            order.ShouldContain(child, $"{child} is kept but references {parent}");
            order.IndexOf(child).ShouldBeLessThan(order.IndexOf(parent), $"{child} references {parent}");
        }
    }

    private static string Name(Microsoft.EntityFrameworkCore.Metadata.IEntityType type) => $"{type.GetSchema()}.{type.GetTableName()}";
}
