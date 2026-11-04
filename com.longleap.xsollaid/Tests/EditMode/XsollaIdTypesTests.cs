using System;
using NUnit.Framework;

namespace LongLeap.XsollaId.Tests
{
    [TestFixture]
    public class XsollaIdTypesTests
    {
        [Test]
        public void AccessToken_ToString_DoesNotLeakValue()
        {
            const string secret = "eyJhbGciOiJSUzI1NiJ9.super-secret-payload.sig";

            var token = new AccessToken(secret);

            // A token in the log is a ready-made way into someone else's account.
            StringAssert.DoesNotContain(secret, token.ToString());
            Assert.AreEqual("AccessToken(valid)", token.ToString());
            Assert.AreEqual("AccessToken(none)", AccessToken.None.ToString());
        }

        [Test]
        public void RefreshToken_ToString_DoesNotLeakValue()
        {
            const string secret = "refresh-super-secret";

            StringAssert.DoesNotContain(secret, new RefreshToken(secret).ToString());
        }

        [Test]
        public void XsollaUserId_ToString_DoesNotLeakValue()
        {
            StringAssert.DoesNotContain("sub-42", new XsollaUserId("sub-42").ToString());
        }

        [Test]
        public void Newtypes_RejectNullAndEmpty()
        {
            // Unlike LongLeap.Core.Auth.IdToken, which silently returns None on null:
            // there it is easy to miss that the identity is missing.
            Assert.Throws<ArgumentException>(() => new AccessToken(null));
            Assert.Throws<ArgumentException>(() => new AccessToken(""));
            Assert.Throws<ArgumentException>(() => new RefreshToken(null));
            Assert.Throws<ArgumentException>(() => new XsollaUserId(""));
        }

        [Test]
        public void Newtypes_NoneIsNotValidAndEqualsDefault()
        {
            Assert.IsFalse(AccessToken.None.IsValid);
            Assert.IsFalse(RefreshToken.None.IsValid);
            Assert.IsFalse(XsollaUserId.None.IsValid);

            Assert.AreEqual(default(AccessToken), AccessToken.None);
        }

        [Test]
        public void Newtypes_EqualityIsOrdinal()
        {
            Assert.IsTrue(new XsollaUserId("abc") == new XsollaUserId("abc"));
            Assert.IsTrue(new XsollaUserId("abc") != new XsollaUserId("ABC"));
        }

        [Test]
        public void Session_IsExpired_RespectsMargin()
        {
            var session = new XsollaIdSession(
                new XsollaUserId("sub"), "Name",
                new AccessToken("a"), new RefreshToken("r"),
                expiresAtUnixMs: 1_000_000);

            // 5-minute margin: 6 minutes before expiry it is still valid, 4 minutes before it is not.
            Assert.IsFalse(session.IsExpired(1_000_000 - 360_000, 300_000));
            Assert.IsTrue(session.IsExpired(1_000_000 - 240_000, 300_000));
        }

        [Test]
        public void Session_WithoutAccessToken_IsAlwaysExpired()
        {
            var session = new XsollaIdSession(
                new XsollaUserId("sub"), null, AccessToken.None, new RefreshToken("r"), long.MaxValue);

            Assert.IsTrue(session.IsExpired(0, 0));
            Assert.IsTrue(session.CanRefresh);
        }

        [Test]
        public void Session_ToString_DoesNotLeakTokens()
        {
            var session = new XsollaIdSession(
                new XsollaUserId("sub"), "Name",
                new AccessToken("access-secret"), new RefreshToken("refresh-secret"), 0);

            string text = session.ToString();

            StringAssert.DoesNotContain("access-secret", text);
            StringAssert.DoesNotContain("refresh-secret", text);
        }

        [Test]
        public void Result_Failure_CarriesDistinguishableCodes()
        {
            var cancelled = XsollaIdResult.Cancelled() as XsollaIdResult.Failure;
            var unavailable = XsollaIdResult.Unavailable() as XsollaIdResult.Failure;

            Assert.AreEqual(XsollaIdErrorCode.Cancelled, cancelled.Code);
            Assert.AreEqual(XsollaIdErrorCode.Unavailable, unavailable.Code);
        }

        [Test]
        public void Config_ActiveFollowsEnvironment()
        {
            var config = TestConfig.Valid();
            Assert.AreEqual("client-stage", config.Active.ClientId);

            var prod = new XsollaIdConfig(
                XsollaIdEnvironment.Production,
                config.Stage,
                config.Production);

            Assert.AreEqual("client-prod", prod.Active.ClientId);
            Assert.IsTrue(prod.IsValid);
        }

        [Test]
        public void Config_IsInvalidWhenClientIdMissing()
        {
            var config = new XsollaIdConfig(
                XsollaIdEnvironment.Stage,
                new XsollaIdClientRegistration(XsollaIdConfig.DefaultStageIssuer, "", "http://localhost:8081/cb.html"),
                null);

            Assert.IsFalse(config.IsValid);
        }

        [Test]
        public void DefaultScopes_RequestOfflineForRefreshToken()
        {
            // offline, not offline_access: on this Hydra instance it is the scope
            // that issues a refresh token. Without it the player has to sign in again
            // after every reload.
            CollectionAssert.Contains(XsollaIdConfig.DefaultScopes.Split(' '), "offline");
            StringAssert.DoesNotContain("offline_access", XsollaIdConfig.DefaultScopes);
        }

        [Test]
        public void Registration_DiscoveryUrlTrimsTrailingSlash()
        {
            var registration = new XsollaIdClientRegistration(
                "https://id.xsolla.com/", "c", "https://example.com/cb.html");

            Assert.AreEqual("https://id.xsolla.com/.well-known/openid-configuration", registration.DiscoveryUrl);
        }
    }
}
