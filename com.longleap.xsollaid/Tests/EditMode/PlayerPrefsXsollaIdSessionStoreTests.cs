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

        private static XsollaIdSession FullSession() => new XsollaIdSession(
            new XsollaUserId("sub-1"), "Player One",
            new AccessToken("access-1"), new RefreshToken("refresh-1"),
            expiresAtUnixMs: 1_700_000_000_000,
            picture: AvatarUrl.TryParse("https://cdn.xsolla.com/a.png"),
            profileFetched: true,
            userName: "id12345678",
            fullName: "Editor User");

        // Exactly the JSON written by the first shipped version: no picture,
        // profileFetched, userName or fullName keys.
        private const string OldBlob =
            "{\"subject\":\"sub-1\",\"displayName\":\"Player One\",\"access\":\"access-1\"," +
            "\"refresh\":\"refresh-1\",\"expiresAtUnixMs\":1700000000000}";

        // Every field must survive a page reload. userName and fullName matter as
        // much as the tokens: after a restore userinfo is not called again
        // (ProfileFetched), so there would be nowhere to get them back.
        [Test]
        public void SaveThenLoad_RoundTripsEveryField()
        {
            _store.Save(FullSession());

            XsollaIdSession loaded = _store.Load();

            Assert.IsNotNull(loaded);
            Assert.AreEqual("sub-1", loaded.Subject.Value);
            Assert.AreEqual("Player One", loaded.DisplayName);
            Assert.AreEqual("access-1", loaded.Access.Value);
            Assert.AreEqual("refresh-1", loaded.Refresh.Value);
            Assert.AreEqual(1_700_000_000_000, loaded.ExpiresAtUnixMs);
            Assert.AreEqual("https://cdn.xsolla.com/a.png", loaded.Picture.Value);
            Assert.IsTrue(loaded.ProfileFetched);
            Assert.AreEqual("id12345678", loaded.UserName);
            Assert.AreEqual("Editor User", loaded.FullName);
        }

        [Test]
        public void Load_ReturnsNullWhenNothingStored()
        {
            Assert.IsNull(_store.Load());
        }

        // The key migration test: the old blob is already on players' devices,
        // and a game update must not cost them their session.
        [Test]
        public void Load_OldBlob_KeepsTheSessionAndAsksForTheProfileOnce()
        {
            _prefs.SetString(StoreKey, OldBlob);

            XsollaIdSession loaded = _store.Load();

            Assert.IsNotNull(loaded, "an old blob must load, not be discarded");
            Assert.AreEqual("Player One", loaded.DisplayName);
            Assert.AreEqual("access-1", loaded.Access.Value);
            Assert.AreEqual("refresh-1", loaded.Refresh.Value);
            Assert.IsFalse(loaded.Picture.IsValid);
            Assert.IsNull(loaded.UserName);
            Assert.IsNull(loaded.FullName);

            // A missing key means "profile not fetched yet": that is what makes
            // XsollaIdAuth fetch the profile exactly once.
            Assert.IsFalse(loaded.ProfileFetched);
            Assert.IsTrue(_prefs.HasKey(StoreKey), "the blob must not be cleared by the upgrade");
        }

        // What makes the migration work: an old blob is detected by the ABSENCE of
        // the key. If keys were written only for non-empty values, an old blob
        // would look like a new one without a picture, and the profile request
        // would repeat forever.
        [Test]
        public void Save_AlwaysWritesPictureAndProfileFetchedKeys()
        {
            _store.Save(Session());

            string written = _prefs.GetString(StoreKey);

            StringAssert.Contains("\"picture\"", written);
            StringAssert.Contains("\"profileFetched\"", written);
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
            // Without a refresh token there is nothing to restore; silently treat
            // it as no session.
            _prefs.SetString(StoreKey, "{\"subject\":\"sub-1\",\"access\":\"a\"}");

            Assert.IsNull(_store.Load());
            Assert.IsFalse(_prefs.HasKey(StoreKey));
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

            // Already fetched and there was no picture: no reason to ask again.
            Assert.IsTrue(loaded.ProfileFetched);
        }

        // The address is re-validated on read: the blob may have been written by a
        // build with different rules, and in WebGL it lives in IndexedDB, which the
        // player can edit by hand.
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

        // Regression: a field of the wrong JSON type used to throw inside Load,
        // whose catch clears the key — one garbage field cost the whole session.
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

            // 0 means "expired long ago": the session goes to refresh instead of
            // disappearing.
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
            // The anonymous Firebase identity owns the cloud saves; if it were
            // overwritten, a player signing out would get someone else's empty
            // progress instead of their own guest one.
            _prefs.SetString(FirebaseAuthKey, "firebase-blob");

            _store.Save(Session());
            _store.Load();
            _store.Clear();

            Assert.AreEqual("firebase-blob", _prefs.GetString(FirebaseAuthKey));
        }
    }
}
