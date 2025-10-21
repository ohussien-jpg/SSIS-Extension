using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SalesforceExtension
{
    /// <summary>
    /// Represents the configuration required to authenticate against Salesforce's OAuth 2.0 endpoint.
    /// </summary>
    public sealed class SalesforceConnectionOptions
    {
        public string AuthEndpoint { get; set; } = "https://login.salesforce.com";
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string SecurityToken { get; set; } = string.Empty;
        public bool UseSandbox { get; set; }
        public TimeSpan TokenSkew { get; set; } = TimeSpan.FromMinutes(5);

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(ClientId))
                throw new InvalidOperationException("Salesforce ClientId must be provided.");
            if (string.IsNullOrWhiteSpace(ClientSecret))
                throw new InvalidOperationException("Salesforce ClientSecret must be provided.");
            if (string.IsNullOrWhiteSpace(Username))
                throw new InvalidOperationException("Salesforce Username must be provided.");
            if (string.IsNullOrWhiteSpace(Password))
                throw new InvalidOperationException("Salesforce Password must be provided.");
        }
    }

    /// <summary>
    /// Handles acquiring and caching OAuth tokens for Salesforce REST calls.
    /// </summary>
    public sealed class SalesforceConnectionManager : IDisposable
    {
        private readonly SalesforceConnectionOptions _options;
        private readonly HttpClient _httpClient;
        private TokenResponse? _token;

        public SalesforceConnectionManager(SalesforceConnectionOptions options, HttpMessageHandler? handler = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _options.Validate();
            _httpClient = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        }

        public string InstanceUrl => _token?.InstanceUrl ?? string.Empty;
        public string AccessToken => _token?.AccessToken ?? string.Empty;

        public async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
        {
            if (_token != null && !_token.IsExpired(_options.TokenSkew))
            {
                return;
            }

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["username"] = _options.Username,
                ["password"] = _options.Password + _options.SecurityToken
            });

            var domain = _options.UseSandbox ? "https://test.salesforce.com" : _options.AuthEndpoint;
            var response = await _httpClient.PostAsync(new Uri(new Uri(domain), "/services/oauth2/token"), content, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var payload = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            _token = TokenResponse.Parse(payload);
        }

        public async Task<JObject> GetAsync(Uri requestUri, CancellationToken cancellationToken)
        {
            if (requestUri is null)
            {
                throw new ArgumentNullException(nameof(requestUri));
            }

            await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AccessToken);

            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return JObject.Parse(json);
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }

        private sealed class TokenResponse
        {
            public string AccessToken { get; }
            public string InstanceUrl { get; }
            public DateTimeOffset ExpiresAt { get; }

            private TokenResponse(string accessToken, string instanceUrl, DateTimeOffset expiresAt)
            {
                AccessToken = accessToken;
                InstanceUrl = instanceUrl;
                ExpiresAt = expiresAt;
            }

            public static TokenResponse Parse(JObject payload)
            {
                if (payload is null)
                {
                    throw new ArgumentNullException(nameof(payload));
                }

                var accessToken = payload.Value<string>("access_token") ?? throw new InvalidOperationException("OAuth response missing access_token");
                var instanceUrl = payload.Value<string>("instance_url") ?? throw new InvalidOperationException("OAuth response missing instance_url");
                var issuedAtString = payload.Value<string>("issued_at") ?? throw new InvalidOperationException("OAuth response missing issued_at");

                if (!long.TryParse(issuedAtString, NumberStyles.Integer, CultureInfo.InvariantCulture, out var issuedAtUnix))
                {
                    throw new InvalidOperationException("OAuth response contains invalid issued_at value");
                }

                var issuedAt = DateTimeOffset.FromUnixTimeMilliseconds(issuedAtUnix);
                var expiresIn = payload.Value<int?>("expires_in") ?? 3600;
                var expiresAt = issuedAt.AddSeconds(expiresIn);

                return new TokenResponse(accessToken, instanceUrl, expiresAt);
            }

            public bool IsExpired(TimeSpan skew)
            {
                return DateTimeOffset.UtcNow >= ExpiresAt.Subtract(skew);
            }
        }
    }
}
