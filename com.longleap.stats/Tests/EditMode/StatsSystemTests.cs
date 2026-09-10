using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Stats;
using NUnit.Framework;

[TestFixture]
public class StatsSystemTests
{
    private StatsSystem _stats;

    [SetUp]
    public void Setup()
    {
        _stats = new StatsSystem();
    }

    private void DefineTree()
    {
        _stats.Define("kill", StatType.Accumulate);
        _stats.Define("kill:monk", StatType.Accumulate);
        _stats.Define("kill:monk:grunt", StatType.Value);
        _stats.Define("kill:monk:brute", StatType.Value);
        _stats.Define("kill:ninja", StatType.Accumulate);
        _stats.Define("kill:ninja:grunt", StatType.Value);
        _stats.Define("damage", StatType.Accumulate);
        _stats.Define("damage:monk", StatType.Value);
        _stats.Define("bighit", StatType.Max);
        _stats.Define("bighit:monk", StatType.MaxValue);
        _stats.Define("level", StatType.Max);
        _stats.Define("level:monk", StatType.MaxValue);
    }

    [Test]
    public void Define_NewPath_StoresZero()
    {
        _stats.Define("test", StatType.Value);
        Assert.AreEqual(0, _stats.Get("test"));
    }

    [Test]
    public void Define_Duplicate_DoesNotThrow()
    {
        _stats.Define("test", StatType.Value);
        Assert.DoesNotThrow(() => _stats.Define("test", StatType.Value));
    }

    [Test]
    public void Define_LeafWithoutParent_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _stats.Define("kill:monk:grunt", StatType.Value));
    }

    [Test]
    public void Set_ValueType_Replaces()
    {
        DefineTree();
        _stats.Set("kill:monk:grunt", 5);
        Assert.AreEqual(5, _stats.Get("kill:monk:grunt"));
    }

    [Test]
    public void Set_MaxValueType_TakesMax()
    {
        DefineTree();
        _stats.Set("bighit:monk", 100);
        _stats.Set("bighit:monk", 50);
        Assert.AreEqual(100, _stats.Get("bighit:monk"));
    }

    [Test]
    public void Set_OnUndefined_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _stats.Set("nonexistent", 5));
    }

    [Test]
    public void Set_OnNonLeaf_Throws()
    {
        DefineTree();
        Assert.Throws<InvalidOperationException>(() =>
            _stats.Set("kill", 5));
    }

    [Test]
    public void Get_Undefined_ReturnsZero()
    {
        Assert.AreEqual(0, _stats.Get("nonexistent"));
    }

    [Test]
    public void Cascade_Accumulate_SumsChildren()
    {
        DefineTree();
        _stats.Set("kill:monk:grunt", 3);
        _stats.Set("kill:monk:brute", 7);
        Assert.AreEqual(10, _stats.Get("kill:monk"));
        Assert.AreEqual(10, _stats.Get("kill"));
    }

    [Test]
    public void Cascade_Max_TakesMaxOfChildren()
    {
        DefineTree();
        _stats.Set("level:monk", 5);
        Assert.AreEqual(5, _stats.Get("level"));
        _stats.Set("level:monk", 10);
        Assert.AreEqual(10, _stats.Get("level"));
    }

    [Test]
    public void Cascade_MultipleChildren_MaxAcrossHeroes()
    {
        _stats.Define("bighit", StatType.Max);
        _stats.Define("bighit:monk", StatType.MaxValue);
        _stats.Define("bighit:ninja", StatType.MaxValue);
        _stats.Set("bighit:monk", 50);
        _stats.Set("bighit:ninja", 100);
        Assert.AreEqual(100, _stats.Get("bighit"));
    }

    [Test]
    public void Subscribe_ExactPattern_FiresOnChange()
    {
        DefineTree();
        long? fired = null;
        _stats.Subscribe("kill:monk", (path, value) => fired = value);
        _stats.Set("kill:monk:grunt", 5);
        Assert.AreEqual(5, fired);
    }

    [Test]
    public void Subscribe_WildcardPattern_FiresOnMatch()
    {
        DefineTree();
        long? fired = null;
        _stats.Subscribe("kill:*:grunt", (path, value) => fired = value);
        _stats.Set("kill:monk:grunt", 5);
        Assert.AreEqual(5, fired);
    }

    [Test]
    public void Subscribe_Wildcard_DoesNotFireOnNoMatch()
    {
        DefineTree();
        bool fired = false;
        _stats.Subscribe("kill:*:grunt", (path, value) => fired = true);
        _stats.Set("kill:monk:brute", 5);
        Assert.IsFalse(fired);
    }

    [Test]
    public void Subscribe_CallbackReceivesCorrectPathAndValue()
    {
        DefineTree();
        StatPath? calledPath = null;
        long calledValue = 0;
        _stats.Subscribe("kill:monk:grunt", (path, value) =>
        {
            calledPath = path;
            calledValue = value;
        });
        _stats.Set("kill:monk:grunt", 42);
        Assert.AreEqual("kill:monk:grunt", calledPath?.Value);
        Assert.AreEqual(42, calledValue);
    }

    [Test]
    public void Subscribe_MultipleSubscribers_AllFire()
    {
        DefineTree();
        int count = 0;
        _stats.Subscribe("kill:monk:grunt", (p, v) => count++);
        _stats.Subscribe("kill:monk:grunt", (p, v) => count++);
        _stats.Set("kill:monk:grunt", 1);
        Assert.AreEqual(2, count);
    }

    [Test]
    public void Unsubscribe_RemovesListener()
    {
        DefineTree();
        int count = 0;
        Action<StatPath, long> handler = (p, v) => count++;
        _stats.Subscribe("kill:monk:grunt", handler);
        _stats.Unsubscribe("kill:monk:grunt", handler);
        _stats.Set("kill:monk:grunt", 1);
        Assert.AreEqual(0, count);
    }

    [Test]
    public void GetAggregate_ExactPath_ReturnsNodeValue()
    {
        DefineTree();
        _stats.Set("kill:monk:grunt", 5);
        _stats.Set("kill:monk:brute", 3);
        Assert.AreEqual(8, _stats.GetAggregate("kill:monk"));
    }

    [Test]
    public void GetAggregate_Wildcard_Accumulates()
    {
        DefineTree();
        _stats.Set("kill:monk:grunt", 5);
        _stats.Set("kill:ninja:grunt", 3);
        Assert.AreEqual(5, _stats.GetAggregate("*:monk:grunt"));
    }

    [Test]
    public void GetAggregate_WildcardMax()
    {
        _stats.Define("bighit", StatType.Max);
        _stats.Define("bighit:monk", StatType.MaxValue);
        _stats.Define("bighit:ninja", StatType.MaxValue);
        _stats.Set("bighit:monk", 100);
        _stats.Set("bighit:ninja", 50);
        Assert.AreEqual(100, _stats.GetAggregate("bighit:*"));
    }

    [Test]
    public void GetAggregate_UndefinedPattern_ReturnsZero()
    {
        Assert.AreEqual(0, _stats.GetAggregate("nonexistent:*"));
    }

    private sealed class MockPersistence : IStatPersistence
    {
        public readonly Dictionary<string, long> Saved = new();

        public Task SaveAsync(StatPath path, long value)
        {
            Saved[path.Value] = value;
            return Task.CompletedTask;
        }

        public Task<StatValue> LoadAsync(StatPath path)
        {
            if (Saved.TryGetValue(path.Value, out var value))
                return Task.FromResult(StatValue.Of(value));
            return Task.FromResult(StatValue.None);
        }
    }

    [Test]
    public async Task SaveAsync_OnlyDirtyLeavesAreSaved()
    {
        DefineTree();
        _stats.Set("kill:monk:grunt", 3);
        _stats.Set("kill:monk:brute", 7);

        var mock = new MockPersistence();
        await _stats.SaveAsync(mock);

        Assert.AreEqual(2, mock.Saved.Count);
        Assert.IsTrue(mock.Saved.ContainsKey("kill:monk:grunt"));
        Assert.AreEqual(3, mock.Saved["kill:monk:grunt"]);
        Assert.IsTrue(mock.Saved.ContainsKey("kill:monk:brute"));
        Assert.AreEqual(7, mock.Saved["kill:monk:brute"]);
    }

    [Test]
    public async Task SaveAsync_NonLeafNodesAreNotSaved()
    {
        DefineTree();
        _stats.Set("kill:monk:grunt", 5);

        var mock = new MockPersistence();
        await _stats.SaveAsync(mock);

        Assert.IsFalse(mock.Saved.ContainsKey("kill:monk"));
        Assert.IsFalse(mock.Saved.ContainsKey("kill"));
    }

    [Test]
    public async Task SaveAsync_DirtyClearedAfterSave()
    {
        DefineTree();
        _stats.Set("kill:monk:grunt", 1);

        await _stats.SaveAsync(new MockPersistence());
        await _stats.SaveAsync(new MockPersistence());

        var mock = new MockPersistence();
        await _stats.SaveAsync(mock);

        Assert.AreEqual(0, mock.Saved.Count);
    }

    [Test]
    public async Task LoadAsync_RestoresSavedValues()
    {
        DefineTree();
        var mock = new MockPersistence();
        mock.Saved["kill:monk:grunt"] = 5;
        mock.Saved["kill:monk:brute"] = 3;

        await _stats.LoadAsync(mock);

        Assert.AreEqual(5, _stats.Get("kill:monk:grunt"));
        Assert.AreEqual(3, _stats.Get("kill:monk:brute"));
    }

    [Test]
    public async Task LoadAsync_MissingStatsStayZero()
    {
        DefineTree();
        var mock = new MockPersistence();
        mock.Saved["kill:monk:grunt"] = 5;

        await _stats.LoadAsync(mock);

        Assert.AreEqual(5, _stats.Get("kill:monk:grunt"));
        Assert.AreEqual(0, _stats.Get("kill:monk:brute"));
    }

    [Test]
    public async Task LoadAsync_RecomputesAccumulateNodes()
    {
        DefineTree();
        var mock = new MockPersistence();
        mock.Saved["kill:monk:grunt"] = 3;
        mock.Saved["kill:monk:brute"] = 7;

        await _stats.LoadAsync(mock);

        Assert.AreEqual(10, _stats.Get("kill:monk"));
        Assert.AreEqual(10, _stats.Get("kill"));
    }

    [Test]
    public async Task LoadAsync_RecomputesMaxNodes()
    {
        DefineTree();
        var mock = new MockPersistence();
        mock.Saved["level:monk"] = 8;

        await _stats.LoadAsync(mock);

        Assert.AreEqual(8, _stats.Get("level"));
    }

    [Test]
    public async Task SaveLoad_Roundtrip()
    {
        DefineTree();
        _stats.Set("kill:monk:grunt", 3);
        _stats.Set("kill:monk:brute", 7);
        _stats.Set("level:monk", 5);

        var mock = new MockPersistence();
        await _stats.SaveAsync(mock);

        var fresh = new StatsSystem();
        fresh.Define("kill", StatType.Accumulate);
        fresh.Define("kill:monk", StatType.Accumulate);
        fresh.Define("kill:monk:grunt", StatType.Value);
        fresh.Define("kill:monk:brute", StatType.Value);
        fresh.Define("level", StatType.Max);
        fresh.Define("level:monk", StatType.MaxValue);

        await fresh.LoadAsync(mock);

        Assert.AreEqual(3, fresh.Get("kill:monk:grunt"));
        Assert.AreEqual(7, fresh.Get("kill:monk:brute"));
        Assert.AreEqual(10, fresh.Get("kill:monk"));
        Assert.AreEqual(5, fresh.Get("level:monk"));
        Assert.AreEqual(5, fresh.Get("level"));
    }

    [Test]
    public async Task FlushAsync_AutosaveStillWorksAfterFlush()
    {
        DefineTree();
        var mock = new MockPersistence();
        _stats.StartAutoSave(mock, TimeSpan.FromMilliseconds(20));

        _stats.Set("kill:monk:grunt", 1);
        await _stats.FlushAsync();

        Assert.AreEqual(1, mock.Saved["kill:monk:grunt"], "Flush should save immediately");

        await Task.Delay(100);

        _stats.Set("kill:monk:brute", 5);
        await Task.Delay(200);

        Assert.AreEqual(5, mock.Saved["kill:monk:brute"], "Autosave should still fire after FlushAsync");
    }
}
