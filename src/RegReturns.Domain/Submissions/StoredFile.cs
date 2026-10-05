using System.Security.Cryptography;
using System.Text;

using RegReturns.Domain.Auditing;
using RegReturns.Domain.Common;
using RegReturns.Domain.Identity;

namespace RegReturns.Domain.Submissions;

/// <summary>
/// An uploaded return file, kept in the database as evidence of what the bank sent. It is never written to disk and
/// never served back as-is (plan §3); the content type comes from the file's signature, not from the browser.
/// </summary>
public sealed class StoredFile : Entity
{
    /// <summary>The largest file accepted, in bytes (5 MB).</summary>
    public const int MaxSizeBytes = 5 * 1024 * 1024;

    /// <summary>Maximum length of the stored file name.</summary>
    public const int FileNameMaxLength = 200;

    /// <summary>Maximum length of a content type.</summary>
    public const int ContentTypeMaxLength = 100;

    /// <summary>Length of the hex SHA-256 digest.</summary>
    public const int Sha256Length = 64;

    private const string FallbackName = "upload";

    private static readonly char[] PathSeparators = ['/', '\\'];

    private StoredFile()
    {
        FileName = string.Empty;
        ContentType = string.Empty;
        Sha256 = string.Empty;
        Content = [];
    }

    /// <summary>Gets the submission the file was uploaded to.</summary>
    public Guid SubmissionId { get; private set; }

    /// <summary>Gets the submission revision the file was uploaded for.</summary>
    public int Revision { get; private set; }

    /// <summary>Gets the original file name, sanitised; for display only.</summary>
    public string FileName { get; private set; }

    /// <summary>Gets the content type determined from the file's signature.</summary>
    public string ContentType { get; private set; }

    /// <summary>Gets the size in bytes.</summary>
    public int SizeBytes { get; private set; }

    /// <summary>Gets the lower-case hex SHA-256 of the content.</summary>
    public string Sha256 { get; private set; }

    /// <summary>Gets the content. Not audited: the size and SHA-256 identify it in the audit trail.</summary>
    [NotAudited]
    public byte[] Content { get; private set; }

    /// <summary>Gets the maker who uploaded the file.</summary>
    public Guid UploadedByUserId { get; private set; }

    /// <summary>Gets when the file was uploaded.</summary>
    public DateTimeOffset UploadedAt { get; private set; }

    /// <summary>Records an uploaded file against the current revision of a submission.</summary>
    /// <param name="submission">The submission the file's values were loaded into.</param>
    /// <param name="fileName">The name the browser sent; it is sanitised.</param>
    /// <param name="contentType">The content type from the signature check.</param>
    /// <param name="content">The file content (at most <see cref="MaxSizeBytes"/>).</param>
    /// <param name="uploader">The maker who uploaded it.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The stored file.</returns>
    public static StoredFile Create(
        Submission submission, string? fileName, string contentType, byte[] content, Actor uploader, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(uploader);
        if (content.Length is 0 or > MaxSizeBytes)
        {
            throw new DomainException($"A stored file must be between 1 byte and {MaxSizeBytes} bytes.");
        }

        return new StoredFile
        {
            SubmissionId = submission.Id,
            Revision = submission.Revision,
            FileName = SanitizeFileName(fileName),
            ContentType = Guard.NotBlank(contentType, ContentTypeMaxLength),
            SizeBytes = content.Length,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)),
            Content = content,
            UploadedByUserId = uploader.UserId,
            UploadedAt = now,
        };
    }

    /// <summary>
    /// Reduces a browser-supplied file name to a safe display name: no directories, only letters, digits, space,
    /// <c>.</c>, <c>-</c> and <c>_</c>, at most <see cref="FileNameMaxLength"/> characters, never empty or hidden.
    /// </summary>
    /// <param name="fileName">The name as sent.</param>
    /// <returns>The safe name.</returns>
    public static string SanitizeFileName(string? fileName)
    {
        // Browsers on Windows may send a full path; keep only the last segment whatever the separator.
        var segments = (fileName ?? string.Empty).Split(PathSeparators);
        var name = segments[^1];
        var safe = new StringBuilder(name.Length);
        foreach (var c in name.Normalize(NormalizationForm.FormC))
        {
            safe.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or ' ' ? c : '_');
        }

        var result = safe.ToString().Trim().TrimStart('.');
        if (result.Length > FileNameMaxLength)
        {
            var extension = Path.GetExtension(result);
            result = extension.Length is > 0 and <= 10
                ? result[..(FileNameMaxLength - extension.Length)] + extension
                : result[..FileNameMaxLength];
        }

        return result.Length == 0 ? FallbackName : result;
    }
}
