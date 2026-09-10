using System.Collections.Generic;
using System.Threading.Tasks;

namespace LongLeap.Core.CloudSaves
{
    // Значение вместе с меткой времени — то же, что лежит в конверте.
    //
    // 0 в метке означает «время неизвестно» (значение записано до появления
    // конвертов). Показывать такую метку как дату нельзя.
    public readonly struct CloudSaveEntry
    {
        public string Value { get; }
        public long TimestampUnixSeconds { get; }

        public CloudSaveEntry(string value, long timestampUnixSeconds)
        {
            Value = value;
            TimestampUnixSeconds = timestampUnixSeconds;
        }
    }

    // Чтение сейва целиком, одним снимком.
    //
    // Отдельно от ICloudSaveService намеренно: тот отдаёт значения по одному, и
    // сводке сейва пришлось бы перечислять сотни ключей (у статов ключ на каждый
    // лист). А ещё интерфейс реализует только CloudSaveService — навешивать
    // массовое чтение на заглушки и тестовые подделки незачем.
    public interface ICloudSaveBulkAccess
    {
        // Снимок того, что сервис считает текущим состоянием.
        //
        // Ничего не пишет и в сеть не ходит: это чтение памяти под тем же
        // замком, что GetStringAsync. Нужен, чтобы показать игроку содержимое
        // сейва, НЕ применяя его и ничего не решая за него.
        Task<IReadOnlyDictionary<string, CloudSaveEntry>> SnapshotAsync();
    }
}
