using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace RegReturns.Web.Models;

/// <summary>One-off messages shown on the next page after a redirect (post-redirect-get), kept in TempData.</summary>
public static class Flash
{
    /// <summary>The TempData key for a success message.</summary>
    public const string SuccessKey = "Flash.Success";

    /// <summary>The TempData key for an error message.</summary>
    public const string ErrorKey = "Flash.Error";

    /// <summary>Shows a success message on the next page.</summary>
    /// <param name="tempData">The TempData dictionary.</param>
    /// <param name="message">The message.</param>
    public static void Success(this ITempDataDictionary tempData, string message)
    {
        ArgumentNullException.ThrowIfNull(tempData);
        tempData[SuccessKey] = message;
    }

    /// <summary>Shows an error message on the next page.</summary>
    /// <param name="tempData">The TempData dictionary.</param>
    /// <param name="message">The message.</param>
    public static void Error(this ITempDataDictionary tempData, string message)
    {
        ArgumentNullException.ThrowIfNull(tempData);
        tempData[ErrorKey] = message;
    }
}
