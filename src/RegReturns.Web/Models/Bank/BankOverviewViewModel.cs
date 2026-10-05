using RegReturns.Application.Returns;

namespace RegReturns.Web.Models.Bank;

/// <summary>The bank returns overview page.</summary>
/// <param name="Overview">The bank and its obligations, or <see langword="null"/> when they could not be loaded.</param>
/// <param name="Problem">Why they could not be loaded.</param>
/// <param name="CanPrepare">Whether the caller may start and upload returns (a maker).</param>
public sealed record BankOverviewViewModel(BankReturnsOverview? Overview, string? Problem, bool CanPrepare);
