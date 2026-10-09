using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Auditing;
using RegReturns.Application.Idempotency;
using RegReturns.Domain.Auditing;
using RegReturns.Domain.Identity;
using RegReturns.Domain.Insights;
using RegReturns.Domain.Institutions;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;
using RegReturns.Infrastructure.Auditing;
using RegReturns.Infrastructure.Idempotency;
using RegReturns.Infrastructure.Persistence;
using RegReturns.UnitTests.TestSupport;

namespace RegReturns.UnitTests.Auditing;

public sealed class DataChangeCollectorTests : IDisposable
{
    private readonly DomainFixture _world = new();
    private readonly RegReturnsDbContext _db = OfflineContext();

    [Fact]
    public void Nothing_is_collected_when_nothing_changed()
    {
        _db.Attach(_world.Bank);

        Collect().ShouldBeEmpty();
    }

    [Fact]
    public void Created_aggregate_is_one_created_entry_named_by_its_root()
    {
        var draft = Submission.CreateDraft(_world.Obligation, _world.Template, _world.Maker, SubmissionSource.Web, DomainFixture.Now).Value;
        draft.SetValues(_world.Template, DomainFixture.ValidValues(), _world.Maker, DomainFixture.Now).IsSuccess.ShouldBeTrue();
        _db.Add(draft);

        var change = Collect().ShouldHaveSingleItem();

        change.Action.ShouldBe(AuditAction.Created);
        change.EntityType.ShouldBe(nameof(Submission));
        change.EntityId.ShouldBe(draft.Id.ToString());
        change.Details.ShouldBeNull();
    }

    [Fact]
    public void Created_aggregate_lists_the_root_first_then_its_children_by_path()
    {
        var draft = Submission.CreateDraft(_world.Obligation, _world.Template, _world.Maker, SubmissionSource.Web, DomainFixture.Now).Value;
        draft.SetValues(_world.Template, DomainFixture.ValidValues(), _world.Maker, DomainFixture.Now).IsSuccess.ShouldBeTrue();
        _db.Add(draft);

        var items = Items(Collect().ShouldHaveSingleItem());

        items.Select(i => i.Item).ShouldBe(
            [nameof(Submission), $"Events[{draft.Events.Single().Id}]", "Values[ASSETS]", "Values[RATIO]"]);
        items.ShouldAllBe(i => i.Change == AuditChanges.Added);
    }

    [Fact]
    public void Added_child_records_its_values_without_its_keys()
    {
        var draft = Submission.CreateDraft(_world.Obligation, _world.Template, _world.Maker, SubmissionSource.Web, DomainFixture.Now).Value;
        draft.SetValues(_world.Template, DomainFixture.ValidValues(assets: "1000.00"), _world.Maker, DomainFixture.Now).IsSuccess.ShouldBeTrue();
        _db.Add(draft);

        var assets = Items(Collect().Single()).Single(i => i.Item == "Values[ASSETS]");

        assets.Values.ShouldBe(
        [
            new AuditValueChange(nameof(SubmissionValue.NumericValue), null, "1000"),
            new AuditValueChange(nameof(SubmissionValue.RawValue), null, "1000.00"),
        ]);
    }

    [Fact]
    public void Created_root_records_its_values_but_not_its_id()
    {
        _db.Add(_world.Bank);

        var root = Items(Collect().Single()).ShouldHaveSingleItem();

        root.Values.Select(v => v.Property).ShouldBe(
            [nameof(Institution.Code), nameof(Institution.IsActive), nameof(Institution.LicenceCategory), nameof(Institution.Name)]);
    }

    [Fact]
    public void Change_document_is_compact_and_pinned()
    {
        // Pinned on purpose: entries are hashed over this text, and readers rely on its shape (ADR 0024).
        _db.Add(_world.Bank);

        Collect().Single().Changes.ShouldBe(
            "[{\"item\":\"Institution\",\"change\":\"Added\",\"values\":{" +
            "\"Code\":{\"after\":\"TST\"},\"IsActive\":{\"after\":true},\"LicenceCategory\":{\"after\":\"Commercial\"}," +
            "\"Name\":{\"after\":\"Test Bank PLC\"}}}]");
    }

    [Fact]
    public void Collecting_the_same_changes_twice_gives_the_same_document()
    {
        _db.Add(_world.ValidatedDraft());

        Collect().Single().Changes.ShouldBe(Collect().Single().Changes);
    }

    [Fact]
    public void Changed_value_records_before_and_after_under_its_root()
    {
        var draft = _world.ValidatedDraft();
        _db.Attach(draft);

        draft.SetValues(_world.Template, DomainFixture.ValidValues(assets: "1200.50"), _world.Maker, DomainFixture.Now.AddHours(1))
            .IsSuccess.ShouldBeTrue();
        var change = Collect().ShouldHaveSingleItem();

        change.Action.ShouldBe(AuditAction.Updated);
        change.EntityId.ShouldBe(draft.Id.ToString());
        var assets = Items(change).Single(i => i.Item == "Values[ASSETS]");
        assets.Change.ShouldBe(AuditChanges.Modified);
        assets.Values.ShouldContain(new AuditValueChange(nameof(SubmissionValue.RawValue), "1000.00", "1200.50"));
        assets.Values.ShouldContain(new AuditValueChange(nameof(SubmissionValue.NumericValue), "1000", "1200.5"));
    }

    [Fact]
    public void Modified_root_records_only_the_values_that_changed()
    {
        var draft = _world.ValidatedDraft();
        _db.Attach(draft);

        draft.SetValues(_world.Template, DomainFixture.ValidValues(assets: "1200.50"), _world.Maker, DomainFixture.Now.AddHours(1))
            .IsSuccess.ShouldBeTrue();
        var root = Items(Collect().Single())[0];

        root.Item.ShouldBe(nameof(Submission));
        root.Values.Select(v => v.Property).ShouldBe([nameof(Submission.EditVersion), nameof(Submission.LastEditedAt)]);
        root.Values[0].ShouldBe(new AuditValueChange(nameof(Submission.EditVersion), "1", "2"));
    }

    [Fact]
    public void Status_change_is_a_state_changed_entry_with_a_summary()
    {
        var draft = _world.ValidatedDraft();
        _db.Attach(_world.Obligation);
        _db.Attach(draft);

        draft.Submit(_world.Checker, _world.Obligation, "Checked.", DomainFixture.Now).IsSuccess.ShouldBeTrue();
        var submission = Collect().Single(c => c.EntityType == nameof(Submission));

        submission.Action.ShouldBe(AuditAction.StateChanged);
        submission.Details.ShouldBe("Status changed from Draft to Submitted.");
        Items(submission).ShouldContain(i => i.Item.StartsWith("Events[", StringComparison.Ordinal) && i.Change == AuditChanges.Added);
    }

    [Fact]
    public void Each_changed_aggregate_gets_its_own_entry_in_type_order()
    {
        var draft = _world.ValidatedDraft();
        _db.Attach(_world.Obligation);
        _db.Attach(draft);

        draft.Submit(_world.Checker, _world.Obligation, "Checked.", DomainFixture.Now).IsSuccess.ShouldBeTrue();

        Collect().Select(c => (c.EntityType, c.Action)).ShouldBe(
            [("ReturnObligation", AuditAction.StateChanged), (nameof(Submission), AuditAction.StateChanged)]);
    }

    [Fact]
    public void Changed_child_whose_root_is_not_loaded_is_grouped_under_the_root_id()
    {
        var draft = _world.ValidatedDraft();
        using (var loader = OfflineContext())
        {
            // Tracking the whole aggregate once fills in the child's foreign key, as loading it from the database would.
            loader.Attach(draft);
        }

        _db.Attach(draft.FindValue("RATIO")!);

        draft.SetValues(_world.Template, DomainFixture.ValidValues(ratio: "16.00"), _world.Maker, DomainFixture.Now).IsSuccess.ShouldBeTrue();
        var change = Collect().ShouldHaveSingleItem();

        (change.Action, change.EntityType, change.EntityId).ShouldBe((AuditAction.Updated, nameof(Submission), draft.Id.ToString()));
        Items(change).ShouldHaveSingleItem().Item.ShouldBe("Values[RATIO]");
    }

    [Fact]
    public void Deleted_aggregate_records_the_values_it_had()
    {
        _db.Attach(_world.Bank);

        _db.Remove(_world.Bank);
        var change = Collect().ShouldHaveSingleItem();

        change.Action.ShouldBe(AuditAction.Deleted);
        var root = Items(change).ShouldHaveSingleItem();
        root.Change.ShouldBe(AuditChanges.Deleted);
        root.Values.ShouldContain(new AuditValueChange(nameof(Institution.Code), "TST", null));
    }

    [Fact]
    public void Removed_child_rows_are_recorded_as_deleted()
    {
        var draft = _world.ValidatedDraft();
        draft.RecordValidation([_world.WarningFinding()]).IsSuccess.ShouldBeTrue();
        _db.Attach(draft);
        var finding = draft.Findings.Single();

        draft.RecordValidation([]).IsSuccess.ShouldBeTrue();
        var items = Items(Collect().Single());

        items.ShouldContain(i => i.Item == $"Findings[{finding.Id}]" && i.Change == AuditChanges.Deleted);
    }

    [Fact]
    public void Template_fields_and_rules_are_identified_by_their_codes()
    {
        _db.Add(_world.Template);

        var items = Items(Collect().ShouldHaveSingleItem()).Select(i => i.Item).ToList();

        items.ShouldContain("Fields[ASSETS]");
        items.ShouldContain("Rules[RATIO_MIN]");
    }

    [Fact]
    public void Complex_properties_are_recorded_by_their_dotted_names()
    {
        _db.Add(_world.Obligation);

        var root = Items(Collect().Single()).Single();

        root.Values.ShouldContain(new AuditValueChange("Period.Year", null, "2026"));
        root.Values.ShouldContain(new AuditValueChange("Period.Number", null, "2"));
        root.Values.ShouldContain(new AuditValueChange("Period.Frequency", null, "Monthly"));
    }

    [Fact]
    public void Primitive_collections_are_recorded_as_lists()
    {
        _db.Add(AppUser.Create("maker", "Maker", "maker@test.example", _world.Bank.Id, [Role.BankMaker]).Value);

        Items(Collect().Single()).Single().Values.ShouldContain(new AuditValueChange(nameof(AppUser.Roles), null, nameof(Role.BankMaker)));
    }

    [Fact]
    public void Not_audited_properties_are_left_out_of_a_created_entry()
    {
        _db.Add(AppUser.Create("maker", "Maker", "maker@test.example", _world.Bank.Id, [Role.BankMaker]).Value);

        var values = Items(Collect().Single()).Single().Values;

        values.ShouldNotContain(v => v.Property == nameof(AppUser.Email));
        values.ShouldContain(v => v.Property == nameof(AppUser.UserName));
    }

    [Fact]
    public void A_change_to_not_audited_properties_alone_writes_no_entry()
    {
        var user = AppUser.Create("maker", "Maker", "maker@test.example", _world.Bank.Id, [Role.BankMaker]).Value;
        _db.Attach(user);

        user.SyncFromDirectory("Maker", "new.address@test.example", _world.Bank.Id, [Role.BankMaker]).IsSuccess.ShouldBeTrue();

        _db.ChangeTracker.HasChanges().ShouldBeTrue();
        Collect().ShouldBeEmpty();
    }

    [Fact]
    public void Stored_file_content_is_not_recorded_but_its_digest_is()
    {
        var draft = _world.ValidatedDraft();
        var file = StoredFile.Create(draft, "return.csv", "text/csv", [1, 2, 3], _world.Maker, DomainFixture.Now);
        _db.Add(file);

        var change = Collect().ShouldHaveSingleItem();

        change.EntityType.ShouldBe(nameof(StoredFile));
        var values = Items(change).Single().Values;
        values.ShouldNotContain(v => v.Property == nameof(StoredFile.Content));
        values.ShouldContain(new AuditValueChange(nameof(StoredFile.Sha256), null, file.Sha256));
    }

    [Fact]
    public void Row_versions_are_never_recorded()
    {
        _db.Attach(_world.Bank);

        _world.Bank.Rename("Renamed Bank PLC");
        var root = Items(Collect().Single()).Single();

        root.Values.ShouldBe([new AuditValueChange(nameof(Institution.Name), "Test Bank PLC", "Renamed Bank PLC")]);
    }

    [Fact]
    public void Audit_entries_are_never_audited()
    {
        _db.AuditEntries.Add(AuditEntry.Create(DomainFixture.Now, AuditAction.SignIn, ActorType.User, "user-1"));

        Collect().ShouldBeEmpty();
    }

    [Fact]
    public void Entity_types_marked_not_audited_are_never_audited()
    {
        _db.Add(IdempotencyRecord.Claim(
            new IdempotentRequest("client", "key", new string('a', 64)), DomainFixture.Now, TimeSpan.FromMinutes(1), TimeSpan.FromHours(1)));

        Collect().ShouldBeEmpty();
    }

    [Fact]
    public void Insights_are_never_audited()
    {
        _db.Add(ReturnInsight.Record(
            Guid.CreateVersion7(), 1, Guid.CreateVersion7(), InsightProvider.RuleBased, "rules", null, "{}", "{}", 1, 1, DomainFixture.Now));

        Collect().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(typeof(SubmissionValue), nameof(SubmissionValue.FieldCode))]
    [InlineData(typeof(TemplateField), nameof(TemplateField.Code))]
    [InlineData(typeof(ValidationRule), nameof(ValidationRule.Code))]
    [InlineData(typeof(ValidationFinding), null)]
    [InlineData(typeof(WorkflowEvent), null)]
    public void Child_rows_have_a_readable_key_only_when_a_unique_index_gives_one(Type childType, string? expected)
    {
        var type = _db.Model.FindEntityType(childType)!;

        DataChangeCollector.ReadableKey(type, DataChangeCollector.ParentKey(type)!)?.Name.ShouldBe(expected);
    }

    [Theory]
    [InlineData(typeof(Submission))]
    [InlineData(typeof(StoredFile))]
    [InlineData(typeof(TemplateVersion))]
    [InlineData(typeof(AppUser))]
    public void Types_not_reached_through_a_collection_are_their_own_roots(Type rootType)
    {
        DataChangeCollector.ParentKey(_db.Model.FindEntityType(rootType)!).ShouldBeNull();
    }

    public void Dispose() => _db.Dispose();

    internal static RegReturnsDbContext OfflineContext() =>
        new(new DbContextOptionsBuilder<RegReturnsDbContext>()
            .UseSqlServer("Server=unit-tests-never-connect;Database=RegReturns;Integrated Security=true")
            .Options);

    private static IReadOnlyList<AuditChangeItem> Items(DataChange change)
    {
        var items = AuditChanges.Parse(change.Changes);
        items.ShouldNotBeNull();
        using var document = JsonDocument.Parse(change.Changes);
        document.RootElement.ValueKind.ShouldBe(JsonValueKind.Array);
        return items;
    }

    private IReadOnlyList<DataChange> Collect()
    {
        _db.ChangeTracker.DetectChanges();
        return DataChangeCollector.Collect(_db.ChangeTracker);
    }
}
