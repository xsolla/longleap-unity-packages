// XsollaIdAuthJS exists only with GAMEPIX_JS, and so do its tests.
#if GAMEPIX_JS
using System;
using System.Text;
using LongLeap.GamePix;
using LongLeap.XsollaId;
using NUnit.Framework;

// Mapping of a GamePix.user.* answer to a session and a sign-in result. The SDK
// itself is not available in the Editor, so the pure part is tested —
// ToResult and ReadSubject.
public sealed class XsollaIdAuthJSTests
{
    // Unsigned JWT: the client does not check the signature, only the payload matters.
    private static string Jwt(string payloadJson)
    {
        static string B64Url(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return B64Url("{\"alg\":\"none\"}") + "." + B64Url(payloadJson) + ".sig";
    }

    private static GamePixAuthPayload SignedIn(string token = null) => new GamePixAuthPayload
    {
        Success = true,
        Code = 200,
        Id = "gpx-42",
        Name = "Full Name",
        Nickname = "Nick",
        Username = "user1",
        Avatar = "https://cdn.xsolla.net/a.webp",
        Token = token ?? Jwt("{\"sub\":\"xsolla-sub-1\"}"),
        ExpiresAt = 1_800_000_000_000
    };

    [Test]
    public void Success_BuildsSessionWithJwtSubject()
    {
        var result = XsollaIdAuthJS.ToResult(SignedIn(), login: false) as XsollaIdResult.Success;

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Session.Subject.Value, Is.EqualTo("xsolla-sub-1"));
        Assert.That(result.Session.DisplayName, Is.EqualTo("Nick"));
        Assert.That(result.Session.UserName, Is.EqualTo("user1"));
        Assert.That(result.Session.FullName, Is.EqualTo("Full Name"));
        Assert.That(result.Session.Picture.IsValid, Is.True);
        Assert.That(result.Session.Access.IsValid, Is.True);
        Assert.That(result.Session.CanRefresh, Is.False);
        Assert.That(result.Session.ExpiresAtUnixMs, Is.EqualTo(1_800_000_000_000));
    }

    [Test]
    public void Success_UnparsableToken_FallsBackToUserId()
    {
        var result = XsollaIdAuthJS.ToResult(SignedIn(token: "not-a-jwt"), login: false) as XsollaIdResult.Success;

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Session.Subject.Value, Is.EqualTo("gpx-42"));
    }

    [Test]
    public void Success_NoNickname_FallsBackToUsernameThenName()
    {
        GamePixAuthPayload payload = SignedIn();
        payload.Nickname = "";

        var result = (XsollaIdResult.Success)XsollaIdAuthJS.ToResult(payload, login: false);
        Assert.That(result.Session.DisplayName, Is.EqualTo("user1"));

        payload.Username = "";
        result = (XsollaIdResult.Success)XsollaIdAuthJS.ToResult(payload, login: false);
        Assert.That(result.Session.DisplayName, Is.EqualTo("Full Name"));
        Assert.That(result.Session.UserName, Is.Null);
    }

    [Test]
    public void Success_WithoutAnyId_IsFailure()
    {
        GamePixAuthPayload payload = SignedIn(token: "");
        payload.Id = "";

        Assert.That(XsollaIdAuthJS.ToResult(payload, login: false), Is.InstanceOf<XsollaIdResult.Failure>());
    }

    [TestCase(401, false, XsollaIdErrorCode.SessionExpired)]
    [TestCase(401, true, XsollaIdErrorCode.Cancelled)]
    [TestCase(404, false, XsollaIdErrorCode.Unavailable)]
    [TestCase(408, true, XsollaIdErrorCode.Timeout)]
    [TestCase(0, false, XsollaIdErrorCode.Network)]
    public void FailureCodes_MapToErrorCodes(int code, bool login, XsollaIdErrorCode expected)
    {
        var payload = new GamePixAuthPayload { Success = false, Code = code };

        var failure = XsollaIdAuthJS.ToResult(payload, login) as XsollaIdResult.Failure;

        Assert.That(failure, Is.Not.Null);
        Assert.That(failure.Code, Is.EqualTo(expected));
    }

    [Test]
    public void Parse_ReadsJsFields()
    {
        GamePixAuthPayload p = GamePixAuthPayload.Parse(
            "{\"requestId\":7,\"success\":true,\"code\":200,\"id\":\"u\",\"token\":\"t\",\"expiresAt\":123}");

        Assert.That(p.RequestId, Is.EqualTo(7));
        Assert.That(p.Success, Is.True);
        Assert.That(p.Code, Is.EqualTo(200));
        Assert.That(p.ExpiresAt, Is.EqualTo(123));
    }

    [Test]
    public void Payload_ToString_DoesNotLeakToken()
    {
        Assert.That(SignedIn(token: "secret.token.value").ToString(), Does.Not.Contain("secret"));
    }
}
#endif
