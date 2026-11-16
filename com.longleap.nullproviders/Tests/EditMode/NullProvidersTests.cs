using System.Threading.Tasks;
using LongLeap.Core.Ads;
using LongLeap.Core.Auth;
using LongLeap.Core.CloudSaves;
using NUnit.Framework;

namespace LongLeap.Null.Tests
{
    // One test per provider: every call completes synchronously and returns
    // the documented default.
    [TestFixture]
    public class NullProvidersTests
    {
        [Test]
        public void Auth_LoginSucceedsWithoutToken()
        {
            AssertDone(new NullAuthProvider().LoginAsync(), out AuthResult result);

            var success = result as AuthResult.Success;
            Assert.IsNotNull(success);
            Assert.IsFalse(success.Token.IsValid);
            Assert.AreEqual(NullAuthProvider.DefaultUserId, success.Uid.Value);

            AssertDone(new NullAuthProvider("device-42").LoginAsync(), out AuthResult custom);
            Assert.AreEqual("device-42", ((AuthResult.Success)custom).Uid.Value);

            Assert.Throws<System.ArgumentNullException>(() => new NullAuthProvider(null));
            Assert.Throws<System.ArgumentException>(() => new NullAuthProvider(""));
        }

        [Test]
        public void CloudSave_KeepsNothingAndIsNeverAccountScoped()
        {
            var provider = new NullCloudSaveProvider();
            var key = new CloudSaveKey("k");

            Assert.IsTrue(provider.SetStringAsync(key, "v").IsCompleted);
            AssertDone(provider.GetStringAsync(key), out string value);
            AssertDone(provider.HasKeyAsync(key), out bool hasKey);
            AssertDone(provider.LoadAllAsync(), out var all);
            Assert.IsNull(value);
            Assert.IsFalse(hasKey);
            Assert.AreEqual(0, all.Count);
            Assert.IsTrue(provider.DeleteKeyAsync(key).IsCompleted);
            Assert.IsTrue(provider.DeleteAllAsync().IsCompleted);

            provider.UseAccount("acc");
            Assert.IsFalse(provider.IsAccountScoped);
            Assert.AreEqual("", provider.AccountId);
            AssertDone(provider.PeekAccountAsync("acc"), out var peek);
            Assert.AreEqual(0, peek.Count);
        }

        [Test]
        public void Ads_UnavailableAndCompletesImmediately()
        {
            var provider = new NullAdsProvider();

            Assert.IsFalse(provider.IsAvailable);
            Assert.IsFalse(provider.IsRewardedReady);
            Assert.IsFalse(provider.IsAdShowing);

            int calls = 0;
            AdResult rewarded = default, interstitial = default;
            provider.ShowRewarded(r => { rewarded = r; calls++; });
            provider.ShowInterstitial(r => { interstitial = r; calls++; });

            Assert.AreEqual(2, calls, "onComplete must be called exactly once per request, synchronously");
            Assert.AreEqual(AdResult.Unavailable, rewarded);
            Assert.AreEqual(AdResult.Unavailable, interstitial);
            Assert.DoesNotThrow(() => provider.ReportScore(1));
            Assert.DoesNotThrow(provider.Tick);
        }

        private static void AssertDone<T>(Task<T> task, out T result)
        {
            Assert.IsTrue(task.IsCompleted, "null providers must complete synchronously");
            result = task.Result;
        }
    }
}
