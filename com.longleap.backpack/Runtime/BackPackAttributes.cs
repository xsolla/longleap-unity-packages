using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace LongLeap.Backpack
{
    // One value of a Backpack attribute: { "external_id": "cash_10", "value": { "en": "cash_10" } }.
    //
    // ExternalId is the id the game works with; Value is the text for display
    // (see BackPackAttributes.Localized).
    public sealed class BackPackAttributeValue
    {
        public string ExternalId { get; }
        public string Value { get; }

        public BackPackAttributeValue(string externalId, string value)
        {
            ExternalId = externalId;
            Value = value;
        }

        public override string ToString() => ExternalId;
    }

    // One Backpack attribute of an item — an element of attributes / custom_attributes:
    //
    //   { "external_id": "reward",
    //     "name": { "en": "Reward" },
    //     "values": [ { "external_id": "cash_10", "value": { "en": "cash_10" } } ] }
    //
    // The game looks attributes up by ExternalId; Name is only for display.
    public sealed class BackPackAttribute
    {
        public string ExternalId { get; }
        public string Name { get; }

        // Never null; only values that have an id.
        public IReadOnlyList<BackPackAttributeValue> Values { get; }

        public BackPackAttribute(string externalId, string name, IReadOnlyList<BackPackAttributeValue> values)
        {
            ExternalId = externalId;
            Name = name;
            Values = values ?? Array.Empty<BackPackAttributeValue>();
        }

        public override string ToString() => $"{ExternalId}=[{string.Join(", ", Values)}]";
    }

    // Parses an item's attributes / custom_attributes and looks them up.
    //
    // The server hands attributes over exactly as Backpack stores them and does not parse
    // them, so the parsing is tolerant: an entry of the wrong shape is skipped rather than
    // failing the whole backpack reply.
    //
    // Attributes are taken as they are: the package knows no attribute ids and gives them
    // no meaning — that is the client's business.
    public static class BackPackAttributes
    {
        // The language taken from localized texts ({ "en": "Reward" }).
        public const string Language = "en";

        public static readonly IReadOnlyList<BackPackAttribute> None = Array.Empty<BackPackAttribute>();

        public static IReadOnlyList<BackPackAttribute> Parse(JToken token)
        {
            if (!(token is JArray list) || list.Count == 0)
                return None;

            var attributes = new List<BackPackAttribute>(list.Count);

            foreach (var entry in list)
            {
                if (!(entry is JObject attribute))
                    continue;

                var id = Id(attribute["external_id"]);
                if (id == null)
                    continue;

                attributes.Add(new BackPackAttribute(id, Localized(attribute["name"]), ParseValues(attribute["values"])));
            }

            return attributes;
        }

        // The attribute with this external_id (ignoring case), or null.
        public static BackPackAttribute Find(this IReadOnlyList<BackPackAttribute> attributes, string externalId)
        {
            if (attributes == null)
                return null;

            foreach (var attribute in attributes)
            {
                if (string.Equals(attribute.ExternalId, externalId, StringComparison.OrdinalIgnoreCase))
                    return attribute;
            }

            return null;
        }

        // The value ids of the attribute with this external_id; empty if there is no such attribute.
        public static string[] ValueIds(this IReadOnlyList<BackPackAttribute> attributes, string externalId)
        {
            var attribute = attributes.Find(externalId);
            if (attribute == null || attribute.Values.Count == 0)
                return Array.Empty<string>();

            var ids = new string[attribute.Values.Count];
            for (var i = 0; i < ids.Length; i++)
                ids[i] = attribute.Values[i].ExternalId;

            return ids;
        }

        // A localized text: { "en": "Reward" } → Language, else the first text there is.
        // A plain string is taken as is.
        public static string Localized(JToken token)
        {
            switch (token)
            {
                case JObject texts:
                    var preferred = Text(texts[Language]);
                    if (preferred != null)
                        return preferred;

                    foreach (var property in texts.Properties())
                    {
                        var text = Text(property.Value);
                        if (text != null)
                            return text;
                    }

                    return null;

                default:
                    return Text(token);
            }
        }

        // A value without an id is useless to the game and is dropped; a plain string
        // ("cash_10") is both the id and the text.
        private static IReadOnlyList<BackPackAttributeValue> ParseValues(JToken token)
        {
            if (!(token is JArray list) || list.Count == 0)
                return Array.Empty<BackPackAttributeValue>();

            var values = new List<BackPackAttributeValue>(list.Count);

            foreach (var entry in list)
            {
                switch (entry)
                {
                    case JObject value:
                        var id = Id(value["external_id"]);
                        if (id != null)
                            values.Add(new BackPackAttributeValue(id, Localized(value["value"]) ?? id));
                        break;

                    case JValue _:
                        var plain = Id(entry);
                        if (plain != null)
                            values.Add(new BackPackAttributeValue(plain, plain));
                        break;
                }
            }

            return values;
        }

        private static string Id(JToken token)
        {
            var text = Text(token)?.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        // Only a string is text: a number or bool where an id was expected is a data error.
        private static string Text(JToken token) =>
            token != null && token.Type == JTokenType.String ? (string)token : null;
    }
}
