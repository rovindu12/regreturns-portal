extern alias IamBootstrapTool;

using System.Text.Json.Nodes;

using IamBootstrapTool::RegReturns.IamBootstrap.Wso2;

namespace RegReturns.UnitTests.IamBootstrap;

public sealed class Wso2ApplicationsContainsTests
{
    [Fact]
    public void Object_contains_a_subset_of_its_properties()
    {
        Contains("""{"a":1,"b":"x","extra":true}""", """{"a":1,"b":"x"}""").ShouldBeTrue();
    }

    [Fact]
    public void Missing_property_is_not_contained()
    {
        Contains("""{"a":1}""", """{"a":1,"b":"x"}""").ShouldBeFalse();
    }

    [Fact]
    public void Different_value_is_not_contained()
    {
        Contains("""{"a":1}""", """{"a":2}""").ShouldBeFalse();
    }

    [Fact]
    public void Strings_are_compared_case_sensitively()
    {
        Contains("""{"type":"jwt"}""", """{"type":"JWT"}""").ShouldBeFalse();
    }

    [Fact]
    public void Nested_objects_are_compared_as_subsets()
    {
        Contains("""{"pkce":{"mandatory":true,"supportPlainTransformAlgorithm":false,"added":1}}""", """{"pkce":{"mandatory":true}}""")
            .ShouldBeTrue();
    }

    [Fact]
    public void Nested_difference_is_found()
    {
        Contains("""{"pkce":{"mandatory":false}}""", """{"pkce":{"mandatory":true}}""").ShouldBeFalse();
    }

    [Fact]
    public void Arrays_in_another_order_match()
    {
        Contains("""["refresh_token","authorization_code"]""", """["authorization_code","refresh_token"]""").ShouldBeTrue();
    }

    [Fact]
    public void Arrays_of_different_length_do_not_match()
    {
        Contains("""["authorization_code","refresh_token"]""", """["authorization_code"]""").ShouldBeFalse();
    }

    [Fact]
    public void Arrays_are_compared_as_multisets()
    {
        Contains("""["a","b"]""", """["a","a"]""").ShouldBeFalse();
    }

    [Fact]
    public void Array_items_are_matched_as_subsets_in_any_order()
    {
        Contains(
            """[{"id":2,"options":[{"idp":"LOCAL","authenticator":"totp"}]},{"id":1,"options":[{"idp":"LOCAL","authenticator":"BasicAuthenticator"}]}]""",
            """[{"id":1,"options":[{"authenticator":"BasicAuthenticator"}]},{"id":2,"options":[{"authenticator":"totp"}]}]""")
            .ShouldBeTrue();
    }

    [Fact]
    public void Array_items_find_their_match_even_when_an_earlier_item_matches_several()
    {
        // {"a":1} also fits the first actual item; a greedy match would take it and leave {"a":1,"b":2} unmatched.
        Contains("""[{"a":1,"b":2},{"a":1,"c":3}]""", """[{"a":1},{"a":1,"b":2}]""").ShouldBeTrue();
    }

    [Fact]
    public void Empty_arrays_match()
    {
        Contains("""{"roles":[]}""", """{"roles":[]}""").ShouldBeTrue();
    }

    [Fact]
    public void Expected_null_matches_a_missing_property()
    {
        Contains("""{"a":1}""", """{"b":null}""").ShouldBeTrue();
    }

    [Fact]
    public void Expected_null_does_not_match_a_value()
    {
        Contains("""{"b":"x"}""", """{"b":null}""").ShouldBeFalse();
    }

    [Fact]
    public void Object_is_not_contained_in_an_array()
    {
        Contains("""[{"a":1}]""", """{"a":1}""").ShouldBeFalse();
    }

    [Fact]
    public void Built_numbers_and_booleans_match_parsed_ones()
    {
        var expected = new JsonObject { ["userAccessTokenExpiryInSeconds"] = 300, ["publicClient"] = false };

        Wso2Applications.Contains(JsonNode.Parse("""{"userAccessTokenExpiryInSeconds":300,"publicClient":false}"""), expected)
            .ShouldBeTrue();
    }

    [Fact]
    public void Number_and_string_with_the_same_digits_do_not_match()
    {
        Contains("""{"a":"300"}""", """{"a":300}""").ShouldBeFalse();
    }

    private static bool Contains(string actual, string expected) =>
        Wso2Applications.Contains(JsonNode.Parse(actual), JsonNode.Parse(expected));
}
