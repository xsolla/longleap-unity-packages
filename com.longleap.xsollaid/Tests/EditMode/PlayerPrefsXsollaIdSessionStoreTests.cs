using NUnit.Framework;

namespace LongLeap.XsollaId.Tests
{
    [TestFixture]
    public class PlayerPrefsXsollaIdSessionStoreTests
    {
        private const string FirebaseAuthKey = "longleap_auth";
        private const string StoreKey = "longleap_xsollaid";

        private FakePlayerPrefs _prefs;
        private PlayerPrefsXsollaIdSessionStore _store;

        [SetUp]
        public void SetUp()
        {
            _prefs = new FakePlayerPrefs();
            _store = new PlayerPrefsXsollaIdSessionStore(_prefs);
        }

        private static XsollaIdSession Session() => new XsollaIdSession(
            new XsollaUserId("sub-1"), "Player One",
            new AccessToken("access-1"), new RefreshToken("refresh-1"),
            expiresAtUnixMs: 1_700_000_000_000);

        private static XsollaIdSession SessionWithPicture() => new XsollaIdSession(
            new XsollaUserId("sub-1"), "Player One",
            new AccessToken("access-1"), new RefreshToken("refresh-1"),
            expiresAtUnixMs: 1_700_000_000_000,
            picture: AvatarUrl.TryParse("https://cdn.xsolla.com/a.png"),
            profileFetched: true);

        // Ровно тот JSON, который пишет отгруженная игрокам версия: без ключей
        // picture и profileFetched.
        private const string OldBlob =
            "{\"subject\":\"sub-1\",\"displayName\":\"Player One\",\"access\":\"access-1\"," +
            "\"refresh\":\"refresh-1\",\"expiresAtUnixMs\":1700000000000}";

        private static XsollaIdSession SessionWithClaims() => new XsollaIdSession(
            new XsollaUserId("sub-1"), "Player One",
            new AccessToken("access-1"), new RefreshToken("refresh-1"),
            expiresAtUnixMs: 1_700_000_000_000,
            picture: AvatarUrl.None,
            profileFetched: true,
            userName: "id19334455",
            fullName: "Anton Smirnov");

        // Раскрытая карточка показывает username и имя из профиля по отдельности,
        // поэтому оба обязаны переживать перезагрузку страницы: userinfo после
        // восстановления сессии НЕ случается (ProfileFetched), и взять их
        // заново будет негде.
        [Test]
        public void SaveThenLoad_RoundTripsUserNameAndFullName()
        {
            _store.Save(SessionWithClaims());

            XsollaIdSession loaded = _store.Load();

            Assert.AreEqual("id19334455", loaded.UserName);
            Assert.AreEqual("Anton Smirnov", loaded.FullName);
        }

        // Блоб, записанный до появления этих двух ключей, обязан читаться и
        // отдавать null'ы, а не терять сессию целиком: у игрока, который просто
        // обновил игру, вход не должен разваливаться.
        [Test]
        public void Load_BlobWithoutTheNewClaims_KeepsSessionAndLeavesThemNull()
        {
            _prefs.SetString(StoreKey, OldBlob);

            XsollaIdSession loaded = _store.Load();

            Assert.IsNotNull(loaded, "старый блоб не должен обнулять сессию");
            Assert.AreEqual("Player One", loaded.DisplayName);
            Assert.IsNull(loaded.UserName);
            Assert.IsNull(loaded.FullName);
        }

        [Test]
        public void SaveThenLoad_RoundTrips()
        {
            _store.Save(Session());

            XsollaIdSession loaded = _store.Load();

            Assert.IsNotNull(loaded);
            Assert.AreEqual("sub-1", loaded.Subject.Value);
            Assert.AreEqual("Player One", loaded.DisplayName);
            Assert.AreEqual("access-1", loaded.Access.Value);
            Assert.AreEqual("refresh-1", loaded.Refresh.Value);
            Assert.AreEqual(1_700_000_000_000, loaded.ExpiresAtUnixMs);
        }

        [Test]
        public void Load_ReturnsNullWhenNothingStored()
        {
            Assert.IsNull(_store.Load());
        }

        [Test]
        public void Load_CorruptJson_ReturnsNullAndClearsKey()
        {
            _prefs.SetString(StoreKey, "{ this is not json");

            Assert.IsNull(_store.Load());
            Assert.IsFalse(_prefs.HasKey(StoreKey), "a corrupted blob must be deleted, not re-read on every launch");
        }

        [Test]
        public void Load_WithoutRefreshToken_ReturnsNull()
        {
            // Без refresh token'а восстанавливать нечего — молча считаем, что
            // сессии нет.
            _prefs.SetString(StoreKey, "{\"subject\":\"sub-1\",\"access\":\"a\"}");

            Assert.IsNull(_store.Load());
            Assert.IsFalse(_prefs.HasKey(StoreKey));
        }

        [Test]
        public void SaveThenLoad_RoundTripsPictureAndFlag()
        {
            _store.Save(SessionWithPicture());

            XsollaIdSession loaded = _store.Load();

            Assert.IsNotNull(loaded);
            Assert.AreEqual("https://cdn.xsolla.com/a.png", loaded.Picture.Value);
            Assert.IsTrue(loaded.ProfileFetched);
        }

        // ГЛАВНЫЙ тест миграции. Блоб уже лежит у игроков, и обновление игры не
        // имеет права стоить им сессии.
        [Test]
        public void Load_OldBlobWithoutPictureKey_KeepsSessionAndAsksForRefetch()
        {
            _prefs.SetString(StoreKey, OldBlob);

            XsollaIdSession loaded = _store.Load();

            Assert.IsNotNull(loaded, "an old blob must load, not be discarded");
            Assert.AreEqual("Player One", loaded.DisplayName);
            Assert.AreEqual("access-1", loaded.Access.Value);
            Assert.AreEqual("refresh-1", loaded.Refresh.Value);
            Assert.IsFalse(loaded.Picture.IsValid);

            // Отсутствие ключа = «профиль не спрашивали». Именно это заставляет
            // XsollaIdAuth сходить за картинкой ровно один раз.
            Assert.IsFalse(loaded.ProfileFetched);
            Assert.IsTrue(_prefs.HasKey(StoreKey), "the blob must not be cleared by the upgrade");
        }

        // Линчпин миграции: признак «блоб старый» — это ОТСУТСТВИЕ ключа. Если
        // писать ключи только при непустом значении, признак перестанет
        // отличать старый блоб от нового без картинки, и догоняющий запрос
        // будет повторяться вечно.
        [Test]
        public void Save_AlwaysWritesPictureAndProfileFetchedKeys()
        {
            _store.Save(Session());

            string written = _prefs.GetString(StoreKey);

            StringAssert.Contains("\"picture\"", written);
            StringAssert.Contains("\"profileFetched\"", written);
        }

        [Test]
        public void Load_NullPicture_IsTreatedAsNoPictureButKeepsTheFlag()
        {
            _prefs.SetString(
                StoreKey,
                "{\"subject\":\"sub-1\",\"access\":\"access-1\",\"refresh\":\"refresh-1\"," +
                "\"picture\":null,\"profileFetched\":true}");

            XsollaIdSession loaded = _store.Load();

            Assert.IsNotNull(loaded);
            Assert.IsFalse(loaded.Picture.IsValid);

            // Спрашивали и картинки не оказалось — второй раз ходить незачем.
            Assert.IsTrue(loaded.ProfileFetched);
        }

        // Адрес перепроверяется на чтении: блоб мог записать билд с другими
        // правилами, а в WebGL это IndexedDB, куда игрок способен залезть руками.
        [Test]
        public void Load_GarbagePicture_DropsOnlyThePicture()
        {
            _prefs.SetString(
                StoreKey,
                "{\"subject\":\"sub-1\",\"access\":\"access-1\",\"refresh\":\"refresh-1\"," +
                "\"picture\":\"javascript:alert(1)\",\"profileFetched\":true}");

            XsollaIdSession loaded = _store.Load();

            Assert.IsNotNull(loaded);
            Assert.IsFalse(loaded.Picture.IsValid);
            Assert.AreEqual("refresh-1", loaded.Refresh.Value);
        }

        [Test]
        public void Load_NonBooleanProfileFetched_DoesNotLoseTheSession()
        {
            _prefs.SetString(
                StoreKey,
                "{\"subject\":\"sub-1\",\"access\":\"access-1\",\"refresh\":\"refresh-1\"," +
                "\"profileFetched\":\"yes\"}");

            XsollaIdSession loaded = _store.Load();

            Assert.IsNotNull(loaded);
            Assert.IsFalse(loaded.ProfileFetched, "an unreadable flag falls back to \"not asked\"");
        }

        // Регрессия. Раньше поля читались через (string)o[key], и приведение
        // поля-объекта бросало InvalidCastException — а его ловит catch в Load,
        // который ЧИСТИТ КЛЮЧ. То есть одно мусорное поле стоило игроку всей
        // сессии, хотя subject и refresh были на месте.
        [Test]
        public void Load_NonStringDisplayName_KeepsTheSession()
        {
            _prefs.SetString(
                StoreKey,
                "{\"subject\":\"sub-1\",\"displayName\":{\"first\":\"A\"}," +
                "\"access\":\"access-1\",\"refresh\":\"refresh-1\",\"expiresAtUnixMs\":1700000000000}");

            XsollaIdSession loaded = _store.Load();

            Assert.IsNotNull(loaded, "junk in one optional field must not cost the player the session");
            Assert.AreEqual("sub-1", loaded.Subject.Value);
            Assert.AreEqual("refresh-1", loaded.Refresh.Value);
            Assert.IsNull(loaded.DisplayName, "only the unreadable field is dropped");
            Assert.IsTrue(_prefs.HasKey(StoreKey), "the blob must survive an unreadable optional field");
        }

        [Test]
        public void Load_NonNumericExpiry_KeepsTheSession()
        {
            _prefs.SetString(
                StoreKey,
                "{\"subject\":\"sub-1\",\"access\":\"access-1\",\"refresh\":\"refresh-1\"," +
                "\"expiresAtUnixMs\":\"whenever\"}");

            XsollaIdSession loaded = _store.Load();

            Assert.IsNotNull(loaded);

            // 0 значит «истёк давно» — сессия честно пойдёт обновляться, а не
            // исчезнет.
            Assert.AreEqual(0L, loaded.ExpiresAtUnixMs);
        }

        [Test]
        public void Clear_RemovesSession()
        {
            _store.Save(Session());
            _store.Clear();

            Assert.IsNull(_store.Load());
        }

        [Test]
        public void Save_Null_ClearsInsteadOfWritingGarbage()
        {
            _store.Save(Session());
            _store.Save(null);

            Assert.IsFalse(_prefs.HasKey(StoreKey));
        }

        [Test]
        public void NeverTouchesFirebaseAuthKey()
        {
            // Анонимная личность Firebase держит облачные сохранения; если её
            // затереть, игрок при выходе из аккаунта получит чужой пустой
            // прогресс вместо своего гостевого.
            _prefs.SetString(FirebaseAuthKey, "firebase-blob");

            _store.Save(Session());
            _store.Load();
            _store.Clear();

            Assert.AreEqual("firebase-blob", _prefs.GetString(FirebaseAuthKey));
        }
    }
}
