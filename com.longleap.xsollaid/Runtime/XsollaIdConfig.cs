using System;
using System.Collections.Generic;

namespace LongLeap.XsollaId
{
    public enum XsollaIdEnvironment
    {
        Stage,
        Production
    }

    // OAuth client registration for one environment.
    //
    // Stage and Production are DIFFERENT registrations, each with its own
    // clientId and list of redirect uris. That is why they are stored
    // separately instead of pairing one clientId with different hosts.
    public sealed class XsollaIdClientRegistration
    {
        // E.g. https://id.xsolla.com — without a trailing slash.
        public string IssuerBaseUrl { get; }

        // Public client (there is no secret in the build, and there cannot be).
        public string ClientId { get; }

        // Must match the registered value CHARACTER FOR CHARACTER: scheme,
        // host, port, path. Any mismatch is rejected by the server.
        public string RedirectUri { get; }

        public XsollaIdClientRegistration(string issuerBaseUrl, string clientId, string redirectUri)
        {
            IssuerBaseUrl = issuerBaseUrl?.TrimEnd('/');
            ClientId = clientId;
            RedirectUri = redirectUri;
        }

        public bool IsValid =>
            !string.IsNullOrEmpty(IssuerBaseUrl) &&
            !string.IsNullOrEmpty(ClientId) &&
            !string.IsNullOrEmpty(RedirectUri);

        public string DiscoveryUrl => $"{IssuerBaseUrl}/.well-known/openid-configuration";
    }

    public sealed class XsollaIdConfig
    {
        public const string DefaultStageIssuer = "https://id-staging.xsolla.com";
        public const string DefaultProductionIssuer = "https://id.xsolla.com";

        // openid is required; profile/email are needed so the UI has something
        // to show besides the opaque sub.
        //
        // offline is what saves the player from signing in again after a page
        // reload: this scope is what makes the server issue a refresh token.
        //
        // NOTE: this is NOT offline_access. Production discovery advertises
        // both (scopes_supported: offline_access, offline, openid, profile,
        // email, phone), but per the Xsolla ID integration design doc, this
        // Hydra instance issues refresh tokens for offline specifically.
        // Moreover, if the client is registered for offline, requesting
        // offline_access may be rejected as invalid_scope — breaking sign-in
        // entirely, not just refresh.
        public const string DefaultScopes = "openid offline email profile";

        public XsollaIdEnvironment Environment { get; }
        public XsollaIdClientRegistration Stage { get; }
        public XsollaIdClientRegistration Production { get; }
        public string Scopes { get; }

        // Origins from which it makes sense to open sign-in.
        //
        // Separate from the redirect uri because the two need not match: in a
        // portal build the portal (e.g. GamePix) serves the game from its own
        // origin, while the callback page may live on our domain. An empty list
        // means no check.
        public IReadOnlyList<string> AllowedGameOrigins { get; }

        public XsollaIdConfig(
            XsollaIdEnvironment environment,
            XsollaIdClientRegistration stage,
            XsollaIdClientRegistration production,
            string scopes = DefaultScopes,
            IReadOnlyList<string> allowedGameOrigins = null)
        {
            Environment = environment;
            Stage = stage;
            Production = production;
            Scopes = string.IsNullOrEmpty(scopes) ? DefaultScopes : scopes;
            AllowedGameOrigins = allowedGameOrigins ?? Array.Empty<string>();
        }

        public XsollaIdClientRegistration Active =>
            Environment == XsollaIdEnvironment.Production ? Production : Stage;

        public bool IsValid => Active != null && Active.IsValid;

        // A copy with the redirect_uri of the ACTIVE registration replaced.
        //
        // Needed because for self-hosted builds the callback page address is
        // computed at runtime (see XsollaIdRedirect) rather than stored in the
        // asset: a single field for several hosts would mean editing the asset
        // before every build.
        //
        // A copy rather than an in-place write: the config is immutable and is
        // read from two places (the authorize url and the code exchange) — a
        // replacement underneath them could diverge from what was already sent
        // to the server.
        //
        // An empty redirectUri returns the config unchanged: "could not compute"
        // means "keep the asset value", not "erase it".
        public XsollaIdConfig WithRedirectUri(string redirectUri)
        {
            if (string.IsNullOrEmpty(redirectUri) || Active == null)
                return this;

            var replaced = new XsollaIdClientRegistration(
                Active.IssuerBaseUrl, Active.ClientId, redirectUri);

            return Environment == XsollaIdEnvironment.Production
                ? new XsollaIdConfig(Environment, Stage, replaced, Scopes, AllowedGameOrigins)
                : new XsollaIdConfig(Environment, replaced, Production, Scopes, AllowedGameOrigins);
        }
    }
}
