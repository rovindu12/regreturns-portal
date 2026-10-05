using RegReturns.Infrastructure.Files;

namespace RegReturns.UnitTests.Infrastructure.Files;

public sealed class CsvFormulaGuardTests
{
    [Theory]
    [InlineData("=1+1")]
    [InlineData("'=1+1")]
    [InlineData("''=1+1")]
    [InlineData("'")]
    [InlineData("''")]
    [InlineData("'-12.5")]
    [InlineData("-12.5")]
    [InlineData("-")]
    [InlineData("@")]
    [InlineData("plain")]
    [InlineData("")]
    public void Unprotect_reverses_protect(string text)
    {
        CsvFormulaGuard.Unprotect(CsvFormulaGuard.Protect(text)).ShouldBe(text);
    }
}
