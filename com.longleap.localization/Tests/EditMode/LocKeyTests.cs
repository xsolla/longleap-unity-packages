using LongLeap.Localization;
using NUnit.Framework;

public class LocKeyTests
{
    [Test]
    public void LocKey_ImplicitString_ReturnsValue()
    {
        LocKey key = new LocKey("test_key");
        string value = key;

        Assert.AreEqual("test_key", value);
    }

    [Test]
    public void LocKey_ImplicitFromString_SetsValue()
    {
        LocKey key = (LocKey)"test_key";

        Assert.AreEqual("test_key", (string)key);
    }

    [Test]
    public void LocKey_IsEmpty_NullValue_ReturnsTrue()
    {
        LocKey key = new LocKey(null);

        Assert.IsTrue(key.IsEmpty);
    }

    [Test]
    public void LocKey_IsEmpty_Default_ReturnsTrue()
    {
        LocKey key = default(LocKey);

        Assert.IsTrue(key.IsEmpty);
    }

    [Test]
    public void LocKey_IsEmpty_NonEmpty_ReturnsFalse()
    {
        LocKey key = new LocKey("test");

        Assert.IsFalse(key.IsEmpty);
    }

    [Test]
    public void LocKey_Equals_SameValue_ReturnsTrue()
    {
        LocKey a = new LocKey("key");
        LocKey b = new LocKey("key");

        Assert.AreEqual(a, b);
    }

    [Test]
    public void LocKey_Equals_DifferentValue_ReturnsFalse()
    {
        LocKey a = new LocKey("key_a");
        LocKey b = new LocKey("key_b");

        Assert.AreNotEqual(a, b);
    }

    [Test]
    public void LocKey_EqOperator_SameValue_ReturnsTrue()
    {
        LocKey a = "test";
        LocKey b = "test";

        Assert.IsTrue(a == b);
    }

    [Test]
    public void Loc_Resolve_NullInstance_ReturnsKey()
    {
        string result = Loc.Resolve("my_key");

        Assert.AreEqual("my_key", result);
    }

    [Test]
    public void Loc_ResolveFormat_NullInstance_ReturnsKey()
    {
        string result = Loc.ResolveFormat("format_{0}", 42);

        Assert.AreEqual("format_42", result);
    }

    [Test]
    public void Loc_Resolve_NullKey_ReturnsEmpty()
    {
        string result = Loc.Resolve(null);

        Assert.AreEqual(string.Empty, result);
    }
}
