using System.Net;
using System.Text.RegularExpressions;

namespace RegReturns.IntegrationTests.Web;

/// <summary>
/// Posts portal forms the way a browser does: GET the page that holds the form, take its antiforgery request token and
/// post it with the form. The client's cookie container carries the antiforgery cookie, the session and TempData, so
/// use one client for the whole flow, with <see cref="PortalHost.BaseAddress"/> because those cookies are Secure.
/// </summary>
internal static partial class PortalForms
{
    /// <summary>The form field that carries the antiforgery request token.</summary>
    public const string TokenField = "__RequestVerificationToken";

    /// <summary>GETs a page, which must answer 200, and returns its HTML.</summary>
    public static async Task<string> GetHtmlAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"GET {path}");
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>GETs a page, which must answer 200, and returns its HTML with entities decoded, for text assertions.</summary>
    public static async Task<string> GetTextAsync(HttpClient client, string path) =>
        WebUtility.HtmlDecode(await GetHtmlAsync(client, path));

    /// <summary>Returns the antiforgery request token of the first form on a page.</summary>
    public static string TokenFrom(string html)
    {
        var match = TokenPattern().Match(html);
        match.Success.ShouldBeTrue("The page has no form with an antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    /// <summary>Posts a form to <paramref name="action"/> with the antiforgery token of the page at <paramref name="page"/>.</summary>
    public static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string page, string action, params (string Name, string Value)[] fields)
    {
        var token = TokenFrom(await GetHtmlAsync(client, page));
        using var content = new FormUrlEncodedContent(
            [.. fields.Select(f => new KeyValuePair<string, string>(f.Name, f.Value)), new(TokenField, token)]);
        return await client.PostAsync(new Uri(action, UriKind.Relative), content, TestContext.Current.CancellationToken);
    }

    /// <summary>Checks a POST answered with a redirect and returns the target path, without any fragment.</summary>
    public static string RedirectPath(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location.ShouldNotBeNull().OriginalString;
        var fragment = location.IndexOf('#', StringComparison.Ordinal);
        return fragment < 0 ? location : location[..fragment];
    }

    /// <summary>Follows a POST's redirect (Post-Redirect-Get) and returns the target page's decoded text.</summary>
    public static Task<string> FollowAsync(HttpClient client, HttpResponseMessage response) =>
        GetTextAsync(client, RedirectPath(response));

    [GeneratedRegex("name=\"" + TokenField + "\" type=\"hidden\" value=\"([^\"]+)\"", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TokenPattern();
}
