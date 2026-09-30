using System;
using System.Threading.Tasks;
using LongLeap.Achievements;
using UnityEngine;

namespace LongLeap.Backpack
{
    // Обёртка над хранилищем ачивок: сначала событие на BackPack-сервер, и
    // только при удачном ответе — запись в хранилище.
    //
    // Почему обёртка, а не мост IAchievementGameBridge: мост зовут для
    // уведомлений, он ничего не может отменить — AchievementSystem пишет в
    // хранилище своим PersistUnlockAsync независимо от него. Точка, где
    // «сохранить» ещё можно НЕ сделать, ровно одна — MarkUnlockedAsync.
    //
    // Что происходит при отказе сервера: в памяти ачивка на эту сессию
    // остаётся открытой (AchievementSystem помечает её до обращения к
    // хранилищу — переиграть это нельзя, не переписав систему), но на диск и в
    // облако не уходит. В следующем запуске она снова закрыта, статы дадут ей
    // открыться заново, и событие уйдёт повторно — с тем же eventId, поэтому
    // награду сервер не выдаст дважды (BackPackService.StableEventId).
    public sealed class BackPackAchievementStateStore : IAchievementStateStore
    {
        private readonly IAchievementStateStore _inner;
        private readonly BackPackService _backPack;

        public BackPackAchievementStateStore(IAchievementStateStore inner, BackPackService backPack)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _backPack = backPack;
        }

        public Task<AchievementStatesSnapshot> LoadAsync(AchievementDefinition[] definitions) =>
            _inner.LoadAsync(definitions);

        public async Task MarkUnlockedAsync(AchievementId id, long timestamp)
        {
            // Гостю сохраняем как раньше, без сервера.
            //
            // Это НЕ уступка ради простоты: xsollaID до входа не существует
            // вовсе. Требуй мы удачного ответа и здесь, игрок без аккаунта
            // терял бы вообще все ачивки при перезапуске, а условия открытия
            // героев висели бы невыполненными. Награду он всё равно не
            // получает: события без xsollaID нет, значит сервер о ней не
            // знает, и после входа она не выдастся — если это нужно, событие
            // придётся досылать отдельно при SignedIn.
            if (_backPack == null || !_backPack.IsAvailable || !_backPack.HasXsollaId)
            {
                await _inner.MarkUnlockedAsync(id, timestamp);
                return;
            }

            BackPackReply reply = await _backPack.SendEventAsync(id.Value);

            if (!reply.Success)
            {
                // Warning, а не Error: отказ сервера — рабочая ситуация
                // (сервера нет на месте, клиент ему незнаком), и ачивка
                // вернётся сюда в следующей сессии.
                Debug.LogWarning(
                    $"BackPack: событие '{id.Value}' не принято ({reply.Error}) — ачивка не сохранена, попробуем в следующий раз.");
                return;
            }

            if (reply.RewardGranted)
                Debug.Log($"BackPack: за '{id.Value}' выдана награда '{reply.RewardId ?? "?"}'.");

            await _inner.MarkUnlockedAsync(id, timestamp);
        }

        // Сброс — только локальный. Сервер о нём не знает намеренно: выданные
        // награды отзывать нельзя, а eventId остаётся тем же, поэтому после
        // сброса повторные события просто отработают как дубли.
        public Task DeleteAllAsync() => _inner.DeleteAllAsync();
    }
}
