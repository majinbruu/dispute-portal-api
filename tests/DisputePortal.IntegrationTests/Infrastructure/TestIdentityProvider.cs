using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DisputePortal.IntegrationTests.Infrastructure;

/// <summary>
/// Stands in for Keycloak. It owns a signing key, issues access tokens shaped like Keycloak's, and answers the two
/// documents the API fetches from its identity provider: the OpenID Connect discovery document and the key set.
/// The API's own token validation is left exactly as it runs in production; only the network is replaced.
/// </summary>
internal sealed class TestIdentityProvider : IDisposable
{
    public const string Issuer = "https://identity.test/realms/acme-bank";
    public const string Audience = "dispute-portal-api";

    private const string KeySetAddress = Issuer + "/protocol/openid-connect/certs";

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RSA _strangerRsa = RSA.Create(2048);

    private SigningCredentials Credentials => Sign(_rsa, "test-key");

    /// <summary>A valid access token for a user with the given realm roles.</summary>
    public string TokenFor(Guid subject, params string[] roles) => IssueToken(subject.ToString(), roles);

    /// <summary>Issues a token. Every argument left out takes the value a valid token has, so a test changes one thing at a time.</summary>
    public string IssueToken(
        string subject,
        string[] roles,
        string issuer = Issuer,
        string audience = Audience,
        TimeSpan? expiresIn = null,
        bool signedByStranger = false)
    {
        // Token lifetimes are checked against the real clock, not the application's TimeProvider.
        var expires = DateTime.UtcNow.Add(expiresIn ?? TimeSpan.FromMinutes(5));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = expires.AddMinutes(-10),
            NotBefore = expires.AddMinutes(-10),
            Expires = expires,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = subject,
                ["preferred_username"] = "test-user",
                ["roles"] = roles,
            },
            SigningCredentials = signedByStranger ? Sign(_strangerRsa, "stranger-key") : Credentials,
        });
    }

    /// <summary>Answers the API's back-channel requests to the identity provider. Each application instance gets its own.</summary>
    public HttpMessageHandler CreateBackchannel() => new Backchannel(_rsa.ExportParameters(includePrivateParameters: false));

    public void Dispose()
    {
        _rsa.Dispose();
        _strangerRsa.Dispose();
    }

    private static SigningCredentials Sign(RSA rsa, string keyId) =>
        new(new RsaSecurityKey(rsa) { KeyId = keyId }, SecurityAlgorithms.RsaSha256);

    private sealed class Backchannel(RSAParameters publicKey) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            object? document = request.RequestUri?.AbsoluteUri switch
            {
                Issuer + "/.well-known/openid-configuration" => new { issuer = Issuer, jwks_uri = KeySetAddress },
                KeySetAddress => new
                {
                    keys = new[]
                    {
                        new
                        {
                            kty = "RSA",
                            use = "sig",
                            alg = SecurityAlgorithms.RsaSha256,
                            kid = "test-key",
                            n = Base64UrlEncoder.Encode(publicKey.Modulus),
                            e = Base64UrlEncoder.Encode(publicKey.Exponent),
                        },
                    },
                },
                _ => null,
            };

            return Task.FromResult(document is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(document) });
        }
    }
}
