using Microsoft.AspNetCore.Mvc;

using RegReturns.Application.Insights;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Common;
using RegReturns.Web.Models.Supervision;

namespace RegReturns.Web.ViewComponents;

/// <summary>
/// The advisory insight panel of the review page (ADR 0030): the latest insight of the return's current revision and
/// the button that generates one. Renders nothing for callers who may not use insights.
/// </summary>
/// <param name="handler">The insight panel query.</param>
public sealed class ReturnInsightViewComponent(IQueryHandler<GetReturnInsight, Result<ReturnInsightPanel>> handler) : ViewComponent
{
    /// <summary>Renders the panel.</summary>
    /// <param name="submissionId">The return.</param>
    /// <returns>The panel, or nothing.</returns>
    public async Task<IViewComponentResult> InvokeAsync(Guid submissionId)
    {
        var panel = await handler.HandleAsync(new GetReturnInsight(submissionId), HttpContext.RequestAborted);
        return panel.IsSuccess
            ? View(new ReturnInsightViewModel(submissionId, panel.Value))
            : Content(string.Empty);
    }
}
