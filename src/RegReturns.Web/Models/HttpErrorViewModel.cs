namespace RegReturns.Web.Models;

/// <summary>Data for the page shown when a request ends with an error status and no content of its own.</summary>
/// <param name="StatusCode">The HTTP status code of the original request.</param>
/// <param name="ErrorReference">The W3C trace id of the request, which support can search for in the logs.</param>
public sealed record HttpErrorViewModel(int StatusCode, string ErrorReference)
{
    /// <summary>Gets the page's heading for the status code.</summary>
    public string Title => StatusCode switch
    {
        404 => "Page not found",
        400 => "The request could not be read",
        405 => "This page cannot be used that way",
        429 => "Too many requests",
        >= 500 => "Something went wrong",
        _ => "The request was not completed",
    };

    /// <summary>Gets the explanation shown under the heading.</summary>
    public string Message => StatusCode switch
    {
        404 => "The page does not exist, or it is not one your account can see. Check the address, or start again from the home page.",
        429 => "Please wait a moment and try again.",
        >= 500 => "We could not complete your request. Please try again in a moment.",
        _ => "Go back and try again, or start again from the home page.",
    };
}
