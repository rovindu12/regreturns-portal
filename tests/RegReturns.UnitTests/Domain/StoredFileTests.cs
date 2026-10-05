using RegReturns.Domain.Common;
using RegReturns.Domain.Submissions;
using RegReturns.UnitTests.TestSupport;

namespace RegReturns.UnitTests.Domain;

public sealed class StoredFileTests
{
    private readonly DomainFixture _f = new();

    [Fact]
    public void A_stored_file_records_its_hash_size_revision_and_uploader()
    {
        var submission = _f.ValidatedDraft();
        byte[] content = [.. "ASSETS,1000\n"u8];

        var file = StoredFile.Create(submission, "march.csv", "text/csv", content, _f.Maker, DomainFixture.Now);

        file.SubmissionId.ShouldBe(submission.Id);
        file.Revision.ShouldBe(1);
        file.SizeBytes.ShouldBe(content.Length);
        file.Sha256.ShouldBe(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content)));
        file.Sha256.Length.ShouldBe(StoredFile.Sha256Length);
        file.UploadedByUserId.ShouldBe(_f.Maker.UserId);
        file.UploadedAt.ShouldBe(DomainFixture.Now);
    }

    [Theory]
    [InlineData("march.csv", "march.csv")]
    [InlineData(@"C:\Users\maker\Desktop\march.xlsx", "march.xlsx")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("rëturn <2026>.csv", "r_turn _2026_.csv")]
    [InlineData("..hidden.csv", "hidden.csv")]
    [InlineData("", "upload")]
    [InlineData(null, "upload")]
    [InlineData("/", "upload")]
    [InlineData("...", "upload")]
    public void File_names_are_reduced_to_a_safe_display_name(string? name, string expected)
    {
        StoredFile.SanitizeFileName(name).ShouldBe(expected);
    }

    [Fact]
    public void Long_file_names_keep_their_extension()
    {
        var name = StoredFile.SanitizeFileName(new string('a', 300) + ".xlsx");

        name.Length.ShouldBe(StoredFile.FileNameMaxLength);
        name.ShouldEndWith(".xlsx");
    }

    [Fact]
    public void Empty_and_oversized_content_is_refused()
    {
        var submission = _f.ValidatedDraft();

        Should.Throw<DomainException>(() => StoredFile.Create(submission, "a.csv", "text/csv", [], _f.Maker, DomainFixture.Now));
        Should.Throw<DomainException>(() =>
            StoredFile.Create(submission, "a.csv", "text/csv", new byte[StoredFile.MaxSizeBytes + 1], _f.Maker, DomainFixture.Now));
    }
}
