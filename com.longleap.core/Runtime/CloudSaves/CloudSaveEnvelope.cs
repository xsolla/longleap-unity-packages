using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LongLeap.Core.CloudSaves
{
    // Format of one cloud entry: {"v": value, "ts": timestamp}.
    //
    // Lives outside CloudSaveService because it has more than one reader: the
    // service itself and anything that summarises another save (e.g. a
    // "which save do you want?" window). There must be exactly one parser —
    // two would drift apart, and the summary would show something other than
    // what is actually in the save.
    public static class CloudSaveEnvelope
    {
        public static string Serialize(string value, long timestampUnixSeconds) =>
            JsonConvert.SerializeObject(new { v = value, ts = timestampUnixSeconds });

        // Parses an entry. Never throws.
        //
        // Do NOT change the behaviour on malformed input: a value that does not
        // parse as an envelope is returned AS IS with timestamp 0. That is what
        // lets saves written before the envelope existed still be read.
        // Timestamp 0 means "time unknown" and must not be shown as 1970.
        public static (string value, long timestampUnixSeconds) Parse(string raw)
        {
            try
            {
                var obj = JObject.Parse(raw);
                var value = (string)obj["v"];
                var ts = (long?)obj["ts"] ?? 0;
                return (value, ts);
            }
            catch
            {
                return (raw, 0);
            }
        }
    }
}
