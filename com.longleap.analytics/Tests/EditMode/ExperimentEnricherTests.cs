using System;
using System.Collections.Generic;
using LongLeap.Core;
using NUnit.Framework;

namespace LongLeap.Analytics.Tests
{
    public class ExperimentEnricherTests
    {
        [Test]
        public void Constructor_NullProvider_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ExperimentEnricher(null));
        }

        [Test]
        public void Enrich_AddsOneParamPerExperiment()
        {
            var provider = new FakeExperimentProvider
            {
                ActiveExperiments = new List<AbExperimentInfo>
                {
                    new AbExperimentInfo("12", 1),
                    new AbExperimentInfo("price_test", 2),
                },
            };
            var envelope = Envelopes.Make();

            new ExperimentEnricher(provider).Enrich(envelope);

            Assert.AreEqual(1L, envelope.Params["exp_12"]);
            Assert.AreEqual(2L, envelope.Params["exp_price_test"]);
        }

        [Test]
        public void Enrich_NoExperiments_AddsNothing()
        {
            var envelope = Envelopes.Make();

            new ExperimentEnricher(new FakeExperimentProvider()).Enrich(envelope);

            Assert.IsEmpty(envelope.Params);
        }

        [Test]
        public void Enrich_NullExperimentList_AddsNothing()
        {
            var envelope = Envelopes.Make();

            new ExperimentEnricher(new FakeExperimentProvider { ActiveExperiments = null }).Enrich(envelope);

            Assert.IsEmpty(envelope.Params);
        }

        [Test]
        public void Enrich_EmptyId_Skipped()
        {
            var provider = new FakeExperimentProvider
            {
                ActiveExperiments = new List<AbExperimentInfo> { new AbExperimentInfo("", 1) },
            };
            var envelope = Envelopes.Make();

            new ExperimentEnricher(provider).Enrich(envelope);

            Assert.IsEmpty(envelope.Params);
        }

        [TestCase("exp-01", "exp_exp_01")]
        [TestCase("a.b c", "exp_a_b_c")]
        [TestCase("тест", "exp_____")]
        public void ToParamName_ReplacesUnsafeCharacters(string id, string expected)
        {
            Assert.AreEqual(expected, ExperimentEnricher.ToParamName(id));
        }

        [Test]
        public void ToParamName_CutsTo40Characters()
        {
            var name = ExperimentEnricher.ToParamName(new string('a', 60));

            Assert.AreEqual(Ga4Rules.MaxNameLength, name.Length);
            StringAssert.StartsWith(ExperimentEnricher.ParamPrefix, name);
        }

        [Test]
        public void ToParamName_NullOrEmpty_ReturnsNull()
        {
            Assert.IsNull(ExperimentEnricher.ToParamName(null));
            Assert.IsNull(ExperimentEnricher.ToParamName(string.Empty));
        }

        [Test]
        public void Enrich_FullEvent_SkipsTagsThatDoNotFit()
        {
            var parameters = new (string, object)[Ga4Rules.MaxCustomParams];
            for (var i = 0; i < parameters.Length; i++)
                parameters[i] = ($"key{i}", i);
            var envelope = Envelopes.Make(parameters: parameters);
            var experiments = new List<AbExperimentInfo>();
            for (var i = 0; i < Ga4Rules.ReservedParamSlots + 2; i++)
                experiments.Add(new AbExperimentInfo($"e{i}", i));

            new ExperimentEnricher(new FakeExperimentProvider { ActiveExperiments = experiments }).Enrich(envelope);

            Assert.AreEqual(Ga4Rules.MaxParamsPerEvent, envelope.Params.Count);
        }
    }
}
