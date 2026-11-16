using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace LongLeap.XsollaId.Web.Tests
{
    [TestFixture]
    public class XsollaIdHttpTests
    {
        // ---- EncodeForm ----

        [Test]
        public void EncodeForm_JoinsPairsInOrder()
        {
            string form = XsollaIdHttp.EncodeForm(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = "c1"
            });

            Assert.AreEqual("grant_type=refresh_token&client_id=c1", form);
        }

        // An unset optional field must not go out as "key=".
        [Test]
        public void EncodeForm_SkipsEmptyValues()
        {
            string form = XsollaIdHttp.EncodeForm(new Dictionary<string, string>
            {
                ["a"] = "1",
                ["empty"] = "",
                ["missing"] = null,
                ["b"] = "2"
            });

            Assert.AreEqual("a=1&b=2", form);
        }

        [Test]
        public void EncodeForm_EscapesKeysAndValues()
        {
            string form = XsollaIdHttp.EncodeForm(new Dictionary<string, string>
            {
                ["redirect uri"] = "http://h/cb?x=1&y=2"
            });

            Assert.AreEqual("redirect%20uri=http%3A%2F%2Fh%2Fcb%3Fx%3D1%26y%3D2", form);
        }

        [Test]
        public void EncodeForm_EmptyForm_IsEmptyString()
        {
            Assert.AreEqual("", XsollaIdHttp.EncodeForm(new Dictionary<string, string>()));
        }

        // ---- Sanitize ----

        [TestCase("https://id.xsolla.com/cb?code=secret&state=s", "https://id.xsolla.com/cb?…")]
        [TestCase("https://id.xsolla.com/api/oauth2/token", "https://id.xsolla.com/api/oauth2/token")]
        [TestCase("", "")]
        [TestCase(null, null)]
        public void Sanitize_StripsTheQuery(string url, string expected)
        {
            Assert.AreEqual(expected, XsollaIdHttp.Sanitize(url));
        }

        // ---- handler injection ----

        private static (XsollaIdHttp http, List<HttpCall> calls) Recording(HttpReply reply)
        {
            var calls = new List<HttpCall>();

            return (new XsollaIdHttp(call =>
            {
                calls.Add(call);
                return Task.FromResult(reply);
            }), calls);
        }

        [Test]
        public async Task Get_RoutesThroughTheHandler()
        {
            var (http, calls) = Recording(HttpReply.Ok("body"));

            HttpReply reply = await http.GetAsync("https://h/userinfo", "token-1");

            Assert.AreEqual("body", reply.Body);
            Assert.AreEqual(1, calls.Count);
            Assert.AreEqual("GET", calls[0].Method);
            Assert.AreEqual("https://h/userinfo", calls[0].Url);
            Assert.AreEqual("token-1", calls[0].BearerToken);
            Assert.IsNull(calls[0].Body);
            Assert.AreEqual(0, calls[0].TimeoutSeconds, "0 means the shared default");
        }

        [Test]
        public async Task PostForm_SendsAnEncodedForm()
        {
            var (http, calls) = Recording(HttpReply.Ok(""));

            await http.PostFormAsync("https://h/token", new Dictionary<string, string> { ["a"] = "1 2" });

            Assert.AreEqual("POST", calls[0].Method);
            Assert.AreEqual("https://h/token", calls[0].Url);
            Assert.AreEqual("a=1%202", calls[0].Body);
            Assert.AreEqual("application/x-www-form-urlencoded", calls[0].ContentType);
            Assert.IsNull(calls[0].BearerToken);
        }

        [Test]
        public async Task Request_PassesTheCallThroughAsIs()
        {
            var (http, calls) = Recording(HttpReply.Status(204));

            HttpReply reply = await http.RequestAsync(
                new HttpCall("PUT", "https://h/saves", "{}", "application/json", "t", 60));

            Assert.AreEqual(204, reply.StatusCode);
            Assert.AreEqual("PUT", calls[0].Method);
            Assert.AreEqual("{}", calls[0].Body);
            Assert.AreEqual("application/json", calls[0].ContentType);
            Assert.AreEqual("t", calls[0].BearerToken);
            Assert.AreEqual(60, calls[0].TimeoutSeconds);
        }

        // ---- HttpReply ----

        [TestCase(200, true)]
        [TestCase(204, true)]
        [TestCase(299, true)]
        [TestCase(300, false)]
        [TestCase(404, false)]
        public void Reply_IsSuccessOnlyFor2xx(int status, bool expected)
        {
            Assert.AreEqual(expected, HttpReply.Status(status).IsSuccess);
        }

        [Test]
        public void Reply_TransportError_IsNeverSuccess()
        {
            HttpReply reply = HttpReply.Transport("dns");

            Assert.IsTrue(reply.IsTransportError);
            Assert.IsFalse(reply.IsSuccess);
            Assert.AreEqual(0, reply.StatusCode);
        }
    }
}
