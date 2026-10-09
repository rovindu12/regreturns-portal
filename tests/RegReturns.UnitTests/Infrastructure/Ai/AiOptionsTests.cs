using System.ComponentModel.DataAnnotations;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using RegReturns.Application.Insights;
using RegReturns.Domain.Insights;
using RegReturns.Infrastructure;
using RegReturns.Infrastructure.Ai;

namespace RegReturns.UnitTests.Infrastructure.Ai;

public sealed class AiOptionsTests
{
    [Fact]
    public void Defaults_are_valid_and_ask_the_default_model_at_medium_effort()
    {
        var options = new AiOptions();

        Validate(options).ShouldBeEmpty();
        (options.Provider, options.Anthropic.Model, options.Anthropic.Effort).ShouldBe((InsightProvider.Anthropic, "claude-opus-5-5", "medium"));
        options.Anthropic.ApiKey.ShouldBeNull();
    }

    [Theory]
    [InlineData("low")]
    [InlineData("xhigh")]
    [InlineData("max")]
    public void Known_effort_levels_are_valid(string effort)
    {
        var options = new AiOptions();
        options.Anthropic.Effort = effort;

        Validate(options).ShouldBeEmpty();
    }

    [Fact]
    public void An_unknown_effort_level_is_rejected()
    {
        var options = new AiOptions();
        options.Anthropic.Effort = "extreme";

        Validate(options).ShouldHaveSingleItem().ErrorMessage.ShouldNotBeNull().ShouldContain("Ai:Anthropic");
    }

    [Theory]
    [InlineData("http://proxy.example/")]
    [InlineData("ftp://proxy.example/")]
    public void Base_url_must_use_https(string url)
    {
        var options = new AiOptions();
        options.Anthropic.BaseUrl = new Uri(url);

        Validate(options).ShouldHaveSingleItem().MemberNames.ShouldBe(["Anthropic.BaseUrl"]);
    }

    [Theory]
    [InlineData("Claude Opus")]
    [InlineData("")]
    public void Model_must_be_a_model_id(string model)
    {
        var options = new AiOptions();
        options.Anthropic.Model = model;

        Validate(options).ShouldNotBeEmpty();
    }

    [Fact]
    public void Limits_outside_their_ranges_are_rejected()
    {
        var options = new AiOptions();
        options.Anthropic.TimeoutSeconds = 1;
        options.Anthropic.MaxRetries = 9;
        options.Anthropic.MaxOutputTokens = 100;

        Validate(options).Count.ShouldBe(3);
    }

    [Fact]
    public void An_unknown_provider_is_rejected()
    {
        Validate(new AiOptions { Provider = (InsightProvider)99 }).ShouldHaveSingleItem().MemberNames.ShouldBe(["Provider"]);
    }

    [Fact]
    public void The_anthropic_provider_is_registered_by_default()
    {
        using var provider = Services(new Dictionary<string, string?>());

        provider.GetRequiredService<IInsightNarrator>().ShouldBeOfType<AnthropicInsightNarrator>();
    }

    [Fact]
    public void The_rule_based_provider_sends_nothing_out()
    {
        using var provider = Services(new Dictionary<string, string?> { ["Ai:Provider"] = "RuleBased", ["Ai:Anthropic:ApiKey"] = "sk-test" });

        provider.GetRequiredService<IInsightNarrator>().ShouldBeOfType<RuleBasedNarrator>();
    }

    [Fact]
    public void Invalid_settings_stop_the_host_from_starting()
    {
        using var provider = Services(new Dictionary<string, string?> { ["Ai:Anthropic:Effort"] = "extreme" });

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<AiOptions>>().Value);
    }

    private static List<ValidationResult> Validate(AiOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    private static ServiceProvider Services(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddLogging().AddInsights(configuration).BuildServiceProvider();
    }
}
