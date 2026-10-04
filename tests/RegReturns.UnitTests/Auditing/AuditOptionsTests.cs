using System.ComponentModel.DataAnnotations;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using RegReturns.Infrastructure;
using RegReturns.Infrastructure.Auditing;

namespace RegReturns.UnitTests.Auditing;

public sealed class AuditOptionsTests
{
    [Fact]
    public void Key_of_thirty_two_bytes_is_valid()
    {
        Validate(new AuditOptions { HmacKey = Convert.ToBase64String(new byte[32]) }).ShouldBeEmpty();
    }

    [Fact]
    public void Key_longer_than_thirty_two_bytes_is_valid()
    {
        Validate(new AuditOptions { HmacKey = Convert.ToBase64String(new byte[64]) }).ShouldBeEmpty();
    }

    [Fact]
    public void Key_shorter_than_thirty_two_bytes_is_rejected()
    {
        var results = Validate(new AuditOptions { HmacKey = Convert.ToBase64String(new byte[31]) });

        results.ShouldHaveSingleItem().MemberNames.ShouldBe([nameof(AuditOptions.HmacKey)]);
    }

    [Fact]
    public void Key_that_is_not_base64_is_rejected()
    {
        var results = Validate(new AuditOptions { HmacKey = "%%%%-this-is-not-base64-although-it-is-long-%%%%" });

        results.ShouldHaveSingleItem().ErrorMessage.ShouldNotBeNull().ShouldContain("Audit:HmacKey");
    }

    [Fact]
    public void Missing_key_is_rejected()
    {
        Validate(new AuditOptions()).ShouldNotBeEmpty();
    }

    [Fact]
    public void Key_bytes_are_the_decoded_base64()
    {
        var bytes = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

        new AuditOptions { HmacKey = Convert.ToBase64String(bytes) }.GetKeyBytes().ShouldBe(bytes);
    }

    [Fact]
    public void Audit_trail_registration_refuses_a_short_key()
    {
        using var provider = BuildAuditServices(Convert.ToBase64String(new byte[16]));

        var options = provider.GetRequiredService<IOptions<AuditOptions>>();

        Should.Throw<OptionsValidationException>(() => options.Value);
    }

    [Fact]
    public void Audit_trail_registration_accepts_a_valid_key()
    {
        using var provider = BuildAuditServices(AuditTestData.Key);

        provider.GetRequiredService<IOptions<AuditOptions>>().Value.HmacKey.ShouldBe(AuditTestData.Key);
    }

    private static List<ValidationResult> Validate(AuditOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    private static ServiceProvider BuildAuditServices(string key)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Audit:HmacKey"] = key })
            .Build();
        return new ServiceCollection().AddAuditTrail(configuration).BuildServiceProvider();
    }
}
