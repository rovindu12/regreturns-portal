namespace RegReturns.Web.Models;

/// <summary>Data for the error page.</summary>
/// <param name="ErrorReference">The W3C trace id of the failed request, which support can search for in the logs.</param>
public sealed record ErrorViewModel(string ErrorReference);
