extern alias IamBootstrapTool;

using System.Buffers.Text;
using System.Globalization;
using System.Text;

using IamBootstrapTool::RegReturns.IamBootstrap;
using IamBootstrapTool::RegReturns.IamBootstrap.Wso2;

namespace RegReturns.UnitTests.IamBootstrap;

public sealed class Wso2IdsTests
{
    [Theory]
    [InlineData("http://wso2.org/claims", "aHR0cDovL3dzbzIub3JnL2NsYWltcw")]
    [InlineData("http://wso2.org/oidc/claim", "aHR0cDovL3dzbzIub3JnL29pZGMvY2xhaW0")]
    [InlineData("http://wso2.org/claims/institution_id", "aHR0cDovL3dzbzIub3JnL2NsYWltcy9pbnN0aXR1dGlvbl9pZA")]
    public void ForUri_is_unpadded_base64_of_the_uri(string uri, string expected)
    {
        Wso2Ids.ForUri(uri).ShouldBe(expected);
    }

    [Theory]
    [InlineData("?>>", "Pz4-")]
    [InlineData("???", "Pz8_")]
    public void ForUri_uses_the_url_safe_alphabet(string uri, string expected)
    {
        Wso2Ids.ForUri(uri).ShouldBe(expected);
    }

    [Fact]
    public void ForUri_matches_standard_base64url_for_the_scim_custom_schema()
    {
        Wso2Ids.ForUri(Wso2Ids.ScimCustomUserSchema)
            .ShouldBe(Base64Url.EncodeToString(Encoding.UTF8.GetBytes(Wso2Ids.ScimCustomUserSchema)));
    }

    [Fact]
    public void Filter_escapes_characters_that_would_break_the_query()
    {
        Wso2Ids.Filter("a b+c&d=e#f").ShouldBe("a%20b%2Bc%26d%3De%23f");
    }

    [Fact]
    public void Filter_escapes_a_uri_value()
    {
        Wso2Ids.Filter("https://api.regreturns").ShouldBe("https%3A%2F%2Fapi.regreturns");
    }

    [Fact]
    public void Bank_application_name_uses_the_upper_case_code()
    {
        IamNames.BankApp("hlb").ShouldBe("RegReturns Bank HLB");
    }

    [Fact]
    public void Bank_client_id_uses_the_lower_case_code()
    {
        IamNames.BankClientId("HLB").ShouldBe("regreturns-bank-hlb");
    }

    [Fact]
    public void Bank_names_do_not_depend_on_the_culture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");

            IamNames.BankClientId("ISB").ShouldBe("regreturns-bank-isb");
            IamNames.BankApp("isb").ShouldBe("RegReturns Bank ISB");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
