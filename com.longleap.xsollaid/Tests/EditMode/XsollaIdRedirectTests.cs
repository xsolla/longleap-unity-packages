using NUnit.Framework;

namespace LongLeap.XsollaId.Tests
{
    // Вычисление redirect_uri из адреса самой игры.
    //
    // Проверяется тестами, потому что ошибка здесь не видна нигде, кроме живого
    // входа: сервер сверяет redirect_uri посимвольно и отказывает уже ПОСЛЕ
    // открытия окна.
    [TestFixture]
    public class XsollaIdRedirectTests
    {
        // Три адреса, ради которых вычисление и появилось: под каждый пришлось
        // бы править ассет перед сборкой.
        [TestCase("http://127.0.0.1:8081/index.html", "http://127.0.0.1:8081/xid-callback.html")]
        [TestCase("https://blood-red-snow.pages.dev/index.html", "https://blood-red-snow.pages.dev/xid-callback.html")]
        [TestCase("https://bloodredsnow.hel1.your-objectstorage.com/index.html",
            "https://bloodredsnow.hel1.your-objectstorage.com/xid-callback.html")]
        public void Derives_CallbackNextToTheGame(string pageUrl, string expected)
        {
            Assert.AreEqual(expected, XsollaIdRedirect.TryDerive(pageUrl, out string problem));

            // На успехе жаловаться не на что — иначе лог сообщал бы о проблеме
            // там, где всё получилось.
            Assert.IsNull(problem);
        }

        // Сборку могут отдавать из подпапки — страница обратного вызова лежит
        // там же, а не в корне домена.
        [Test]
        public void Derives_FromTheDirectory_NotTheDomainRoot()
        {
            Assert.AreEqual(
                "https://host.example/game/xid-callback.html",
                XsollaIdRedirect.TryDerive("https://host.example/game/index.html", out _));
        }

        // Адрес без имени файла: каталог — он сам.
        [Test]
        public void Derives_WhenThePathIsADirectory()
        {
            Assert.AreEqual(
                "https://host.example/game/xid-callback.html",
                XsollaIdRedirect.TryDerive("https://host.example/game/", out _));
        }

        // Query и fragment в redirect_uri попадать не имеют права: сервер
        // сверяет строку посимвольно.
        [Test]
        public void Derives_WithoutQueryOrFragment()
        {
            Assert.AreEqual(
                "http://127.0.0.1:8081/xid-callback.html",
                XsollaIdRedirect.TryDerive("http://127.0.0.1:8081/index.html?sid=42#anchor", out _));
        }

        // Нестандартный порт — часть origin'а, и потерять его нельзя.
        [Test]
        public void Derives_KeepsTheNonDefaultPort()
        {
            StringAssert.StartsWith(
                "http://127.0.0.1:8081/", XsollaIdRedirect.TryDerive("http://127.0.0.1:8081/", out _));
        }

        // Редактор и standalone: адреса страницы нет. null означает «возьмите
        // значение из ассета», и это не сбой.
        [TestCase("")]
        [TestCase(null)]
        [TestCase("not a url")]
        [TestCase("file:///C:/build/index.html")]
        public void ReturnsNull_WhenNothingCanBeDerived(string pageUrl)
        {
            Assert.IsNull(XsollaIdRedirect.TryDerive(pageUrl, out _));
        }

        // Причина обязана быть у КАЖДОЙ неудачи: она уходит в лог, и без неё
        // «взяли адрес из ассета» не отличить от «вычислили».
        [TestCase("")]
        [TestCase(null)]
        [TestCase("not a url")]
        [TestCase("file:///C:/build/index.html")]
        public void ExplainsEveryFailure(string pageUrl)
        {
            XsollaIdRedirect.TryDerive(pageUrl, out string problem);

            Assert.IsFalse(string.IsNullOrWhiteSpace(problem), "a failure with no explanation would be silent in the log");
        }

        // Разные неудачи объясняются по-разному: пустой адрес в редакторе —
        // норма, а file:// и мусор в строке — нет, и путать их в логе нельзя.
        [Test]
        public void ExplainsFailures_Distinctly()
        {
            XsollaIdRedirect.TryDerive("", out string empty);
            XsollaIdRedirect.TryDerive("not a url", out string garbage);
            XsollaIdRedirect.TryDerive("file:///C:/build/index.html", out string scheme);

            StringAssert.Contains("editor", empty);
            StringAssert.Contains("not an absolute URL", garbage);
            StringAssert.Contains("file", scheme);

            // Неразобранный адрес попадает в лог целиком: без него неясно, что
            // именно приехало из браузера.
            StringAssert.Contains("not a url", garbage);
        }

        // ---- подмена в конфиге ----

        [Test]
        public void WithRedirectUri_ReplacesTheActiveRegistrationOnly()
        {
            var config = new XsollaIdConfig(
                XsollaIdEnvironment.Production,
                new XsollaIdClientRegistration("https://id-staging.xsolla.com", "stage-id", "https://stage/cb"),
                new XsollaIdClientRegistration("https://id.xsolla.com", "prod-id", "https://prod/cb"));

            XsollaIdConfig replaced = config.WithRedirectUri("http://127.0.0.1:8081/xid-callback.html");

            Assert.AreEqual("http://127.0.0.1:8081/xid-callback.html", replaced.Active.RedirectUri);

            // Вторая регистрация не тронута: переключение среды из меню не
            // должно унести с собой чужой адрес.
            Assert.AreEqual("https://stage/cb", replaced.Stage.RedirectUri);

            // clientId и issuer активной регистрации переезжают как есть.
            Assert.AreEqual("prod-id", replaced.Active.ClientId);
            Assert.AreEqual("https://id.xsolla.com", replaced.Active.IssuerBaseUrl);

            // Исходный конфиг неизменяем.
            Assert.AreEqual("https://prod/cb", config.Active.RedirectUri);
        }

        [Test]
        public void WithRedirectUri_OnStageEnvironment_ReplacesStage()
        {
            var config = new XsollaIdConfig(
                XsollaIdEnvironment.Stage,
                new XsollaIdClientRegistration("https://id-staging.xsolla.com", "stage-id", "https://stage/cb"),
                new XsollaIdClientRegistration("https://id.xsolla.com", "prod-id", "https://prod/cb"));

            XsollaIdConfig replaced = config.WithRedirectUri("https://derived/xid-callback.html");

            Assert.AreEqual("https://derived/xid-callback.html", replaced.Stage.RedirectUri);
            Assert.AreEqual("https://prod/cb", replaced.Production.RedirectUri);
        }

        // Ничего не вычислилось — конфиг остаётся как есть. Стереть адрес было
        // бы хуже: вход стал бы недоступен вместо «работает по значению из ассета».
        [TestCase("")]
        [TestCase(null)]
        public void WithRedirectUri_KeepsTheAssetValue_WhenNothingDerived(string derived)
        {
            var config = new XsollaIdConfig(
                XsollaIdEnvironment.Production,
                new XsollaIdClientRegistration("https://id-staging.xsolla.com", "stage-id", "https://stage/cb"),
                new XsollaIdClientRegistration("https://id.xsolla.com", "prod-id", "https://prod/cb"));

            Assert.AreEqual("https://prod/cb", config.WithRedirectUri(derived).Active.RedirectUri);
        }

        // Вычисленный адрес обязан быть тем же файлом, который постпроцесс
        // кладёт в сборку: разъехавшись, они дали бы 404 в окне входа.
        [Test]
        public void CallbackFileName_MatchesWhatIsDerived()
        {
            StringAssert.EndsWith(
                XsollaIdRedirect.CallbackFileName,
                XsollaIdRedirect.TryDerive("https://host.example/index.html", out _));
        }
    }
}
