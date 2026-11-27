namespace DisputePortal.Api.Hosting;

/// <summary>
/// The identity provider settings, read once and checked before anything is built. Left unchecked, a wrong
/// value only shows when the first request arrives: the service is up and answers every call with a 500.
/// </summary>
internal sealed record AuthSettings(string? Authority, string? MetadataAddress, string? Audience, bool RequireHttpsMetadata)
{
    public const string Section = "Auth";

    public static AuthSettings Read(IConfiguration configuration)
    {
        var auth = configuration.GetSection(Section);
        return new AuthSettings(
            NullIfBlank(auth["Authority"]),
            NullIfBlank(auth["MetadataAddress"]),
            NullIfBlank(auth["Audience"]),
            auth.GetValue("RequireHttpsMetadata", true));
    }

    /// <summary>What is wrong with the settings, one message per setting. Empty when the application can start.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();

        if (Authority is null)
        {
            problems.Add(
                "Auth:Authority is not set. Set it to the public URL of the identity provider's realm, " +
                "for example https://login.example.com/realms/acme-bank.");
        }
        else
        {
            CheckUrl("Auth:Authority", Authority, problems);
        }

        if (MetadataAddress is not null)
        {
            CheckUrl("Auth:MetadataAddress", MetadataAddress, problems);
        }

        if (Audience is null)
        {
            problems.Add("Auth:Audience is not set. Set it to the audience that access tokens carry, for example dispute-portal-api.");
        }

        return problems;
    }

    private void CheckUrl(string setting, string value, List<string> problems)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            problems.Add($"{setting} is '{value}', which is not an absolute http or https URL.");
        }
        else if (RequireHttpsMetadata && url.Scheme != Uri.UriSchemeHttps)
        {
            problems.Add(
                $"{setting} is '{value}', which is not https, while Auth:RequireHttpsMetadata is true. " +
                "Use an https URL, or set Auth:RequireHttpsMetadata to false for local HTTP only.");
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
