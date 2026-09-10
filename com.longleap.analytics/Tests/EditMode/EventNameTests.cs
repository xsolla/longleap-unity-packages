using LongLeap.Analytics;
using NUnit.Framework;
using UnityEngine.TestTools;

public class EventNameTests
{
    [SetUp]
    public void SetUp()
    {
        LogAssert.ignoreFailingMessages = false;
    }

    [Test]
    public void Constructor_ValidName_StoresValue()
    {
        var name = new EventName("hero_selected");
        Assert.AreEqual("hero_selected", name.Value);
    }

    [Test]
    public void Constructor_ValidNameSingleChar_StoresValue()
    {
        var name = new EventName("a");
        Assert.AreEqual("a", name.Value);
    }

    [Test]
    public void Constructor_ValidNameMaxLength_StoresValue()
    {
        var name = new EventName(new string('a', 40));
        Assert.AreEqual(40, name.Value.Length);
    }

    [Test]
    public void Constructor_NameTooLong_UsesInvalidSentinel()
    {
        LogAssert.ignoreFailingMessages = true;
        var name = new EventName(new string('a', 41));
        Assert.AreEqual("invalid_event", name.Value);
    }

    [Test]
    public void Constructor_EmptyString_UsesInvalidSentinel()
    {
        LogAssert.ignoreFailingMessages = true;
        var name = new EventName("");
        Assert.AreEqual("invalid_event", name.Value);
    }

    [Test]
    public void Constructor_NullString_UsesInvalidSentinel()
    {
        LogAssert.ignoreFailingMessages = true;
        var name = new EventName(null);
        Assert.AreEqual("invalid_event", name.Value);
    }

    [Test]
    public void Constructor_CyrillicChars_UsesInvalidSentinel()
    {
        LogAssert.ignoreFailingMessages = true;
        var name = new EventName("событие");
        Assert.AreEqual("invalid_event", name.Value);
    }

    [Test]
    public void Constructor_Spaces_UsesInvalidSentinel()
    {
        LogAssert.ignoreFailingMessages = true;
        var name = new EventName("hero selected");
        Assert.AreEqual("invalid_event", name.Value);
    }

    [Test]
    public void Constructor_DigitFirst_UsesInvalidSentinel()
    {
        LogAssert.ignoreFailingMessages = true;
        var name = new EventName("1hero");
        Assert.AreEqual("invalid_event", name.Value);
    }

    [Test]
    public void Constructor_UnderscoreFirst_UsesInvalidSentinel()
    {
        LogAssert.ignoreFailingMessages = true;
        var name = new EventName("_hero");
        Assert.AreEqual("invalid_event", name.Value);
    }

    [Test]
    public void Constructor_Hyphen_UsesInvalidSentinel()
    {
        LogAssert.ignoreFailingMessages = true;
        var name = new EventName("hero-selected");
        Assert.AreEqual("invalid_event", name.Value);
    }

    [Test]
    public void Equals_SameValue_ReturnsTrue()
    {
        var a = new EventName("test_event");
        var b = new EventName("test_event");
        Assert.IsTrue(a.Equals(b));
        Assert.IsTrue(a == b);
    }

    [Test]
    public void Equals_DifferentValue_ReturnsFalse()
    {
        var a = new EventName("event_a");
        var b = new EventName("event_b");
        Assert.IsFalse(a.Equals(b));
        Assert.IsFalse(a == b);
    }

    [Test]
    public void Equals_CaseSensitive_ReturnsFalse()
    {
        var a = new EventName("test_event");
        var b = new EventName("Test_Event");
        Assert.IsFalse(a.Equals(b));
    }

    [Test]
    public void GetHashCode_SameValue_Matches()
    {
        var a = new EventName("test_event");
        var b = new EventName("test_event");
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }

    [Test]
    public void ToString_ReturnsValue()
    {
        var name = new EventName("test_event");
        Assert.AreEqual("test_event", name.ToString());
    }

    [Test]
    public void ToString_Invalid_ReturnsSentinel()
    {
        LogAssert.ignoreFailingMessages = true;
        var name = new EventName("");
        Assert.AreEqual("invalid_event", name.ToString());
    }
}
