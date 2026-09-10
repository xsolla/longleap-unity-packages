using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LongLeap.Core.CloudSaves
{
    // Формат одной записи в облаке: {"v": значение, "ts": метка времени}.
    //
    // Вынесен из CloudSaveService, потому что читателей стало двое: сам сервис и
    // сводка чужого сейва, которую строят для окна выбора. Парсер обязан быть
    // один — иначе две реализации разойдутся, и игрок увидит в карточке не то,
    // что на самом деле лежит в сейве.
    public static class CloudSaveEnvelope
    {
        public static string Serialize(string value, long timestampUnixSeconds) =>
            JsonConvert.SerializeObject(new { v = value, ts = timestampUnixSeconds });

        // Разобрать запись. Не бросает никогда.
        //
        // НЕ ТРОГАТЬ поведение на мусоре: значение, которое не разбирается как
        // конверт, возвращается КАК ЕСТЬ с меткой 0. Именно это позволяет читать
        // сейвы, записанные до появления конверта. Метка 0 означает «время
        // неизвестно» и показывать её как 1970 год нельзя.
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
