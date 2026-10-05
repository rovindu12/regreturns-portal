namespace RegReturns.Application.Abstractions;

/// <summary>The signed-in caller as the host sees it (the portal reads its session cookie).</summary>
public interface ICurrentUser
{
    /// <summary>Gets the caller's WSO2 subject id (<c>sub</c>), or <see langword="null"/> when nobody is signed in.</summary>
    string? SubjectId { get; }
}
