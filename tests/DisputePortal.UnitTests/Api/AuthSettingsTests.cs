using DisputePortal.Api.Hosting;
using Microsoft.Extensions.Configuration;

namespace DisputePortal.UnitTests.Api;

public class AuthSettingsTests
{
    private const string HttpsAuthority = "https://login.example.com/realms/acme-bank";
    private const string HttpAuthority = "http://localhost:8080/realms/acme-bank";
    private const string Audience = "dispute-portal-api";

    [Fact]
    public void An_https_authority_with_an_audience_has_no_problems()
    {
        new AuthSettings(HttpsAuthority, null, Audience, RequireHttpsMetadata: true).Problems().ShouldBeEmpty();
    }

    [Fact]
    public void An_http_authority_and_metadata_address_are_accepted_when_https_is_not_required()
    {
        var settings = new AuthSettings(
            HttpAuthority, "http://keycloak:8080/realms/acme-bank/.well-known/openid-configuration", Audience, RequireHttpsMetadata: false);

        settings.Problems().ShouldBeEmpty();
    }

    [Fact]
    public void A_missing_authority_is_a_problem()
    {
        var problem = new AuthSettings(null, null, Audience, RequireHttpsMetadata: true).Problems().ShouldHaveSingleItem();

        problem.ShouldStartWith("Auth:Authority is not set.");
    }

    [Theory]
    [InlineData("login.example.com/realms/acme-bank")]
    [InlineData("/realms/acme-bank")]
    [InlineData("ftp://login.example.com/realms/acme-bank")]
    public void An_authority_that_is_not_an_absolute_web_address_is_a_problem(string authority)
    {
        var problem = new AuthSettings(authority, null, Audience, RequireHttpsMetadata: false).Problems().ShouldHaveSingleItem();

        problem.ShouldBe($"Auth:Authority is '{authority}', which is not an absolute http or https URL.");
    }

    [Fact]
    public void A_missing_audience_is_a_problem()
    {
        var problem = new AuthSettings(HttpsAuthority, null, null, RequireHttpsMetadata: true).Problems().ShouldHaveSingleItem();

        problem.ShouldStartWith("Auth:Audience is not set.");
    }

    [Fact]
    public void An_http_authority_is_a_problem_when_https_is_required()
    {
        var problem = new AuthSettings(HttpAuthority, null, Audience, RequireHttpsMetadata: true).Problems().ShouldHaveSingleItem();

        problem.ShouldStartWith($"Auth:Authority is '{HttpAuthority}', which is not https, while Auth:RequireHttpsMetadata is true.");
        problem.ShouldContain("set Auth:RequireHttpsMetadata to false for local HTTP only");
    }

    [Fact]
    public void An_http_metadata_address_is_a_problem_when_https_is_required()
    {
        const string metadataAddress = "http://keycloak:8080/realms/acme-bank/.well-known/openid-configuration";

        var problem = new AuthSettings(HttpsAuthority, metadataAddress, Audience, RequireHttpsMetadata: true).Problems().ShouldHaveSingleItem();

        problem.ShouldStartWith($"Auth:MetadataAddress is '{metadataAddress}', which is not https,");
    }

    [Fact]
    public void A_metadata_address_that_is_not_an_absolute_web_address_is_a_problem()
    {
        var problem = new AuthSettings(HttpsAuthority, "keycloak:8080", Audience, RequireHttpsMetadata: false).Problems().ShouldHaveSingleItem();

        problem.ShouldStartWith("Auth:MetadataAddress is 'keycloak:8080'");
    }

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        new AuthSettings(null, null, null, RequireHttpsMetadata: true).Problems().Count.ShouldBe(2);
    }

    [Fact]
    public void Blank_settings_are_read_as_missing_and_https_is_required_unless_switched_off()
    {
        var settings = AuthSettings.Read(Configuration(new()
        {
            ["Auth:Authority"] = " ",
            ["Auth:MetadataAddress"] = "",
            ["Auth:Audience"] = Audience,
        }));

        settings.ShouldBe(new AuthSettings(null, null, Audience, RequireHttpsMetadata: true));
    }

    [Fact]
    public void Settings_are_read_from_the_Auth_section()
    {
        var settings = AuthSettings.Read(Configuration(new()
        {
            ["Auth:Authority"] = HttpAuthority,
            ["Auth:MetadataAddress"] = "http://keycloak:8080/metadata",
            ["Auth:Audience"] = Audience,
            ["Auth:RequireHttpsMetadata"] = "false",
        }));

        settings.ShouldBe(new AuthSettings(HttpAuthority, "http://keycloak:8080/metadata", Audience, RequireHttpsMetadata: false));
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
