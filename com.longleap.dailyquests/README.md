# LongLeap Daily Quests

Ежедневные квесты по образцу `com.longleap.achievements`: квесты описаны в JSON, прогресс выводится из `IStatSystem`, состояние хранится через `IDailyQuestStateStore`. Отличия от ачивок:

- квесты **выдаются набором на период** (от сброса до сброса по UTC) из пула, а не висят все сразу;
- прогресс по умолчанию считается **от момента выдачи** (`Delta`), а не за всю жизнь;
- награду **забирает игрок** (`ClaimAsync`) — сама она не начисляется.

## Добавить квест

Дописать объект в JSON — и всё. Код трогать не нужно.

```json
{
  "settings": { "dailyCount": 3, "resetHourUtc": 0 },
  "quests": [
    {
      "id": "grind_50",
      "weight": 1,
      "objectives": [ { "statPath": "grind", "statTarget": 50 } ],
      "rewards":    [ { "key": "soft", "amount": 5 } ]
    }
  ]
}
```

| Поле | Обязательное | Что значит |
|---|---|---|
| `settings.dailyCount` | нет (3) | Сколько квестов в наборе |
| `settings.resetHourUtc` | нет (0) | Час по UTC, когда набор меняется |
| `quests[].id` | да | Уникальный id; по нему же ключ локализации в игре |
| `quests[].weight` | нет (1) | Вес при выборе. 0 — квест выключен |
| `objectives[].statPath` | да | Путь стата, как в ачивках (`grind`, `kill:*:grunt`) |
| `objectives[].statTarget` | да | Сколько набрать, > 0 |
| `objectives[].mode` | нет (`delta`) | `delta` — прирост с момента выдачи; `absolute` — значение стата как есть |
| `objectives[].id` | нет (`o0`, `o1`…) | Нужен, только если целей несколько и хочется стабильных id |
| `rewards[].key` / `amount` | да / > 0 | Что дать. Ключ толкует игра (в Darkslide — id валюты) |

`delta` рассчитан на накопительные статы (счётчики). Для Max-статов (`score_best`) он означает «побей свой рекорд на N», что обычно не то, что нужно, — для «набери 10k за заезд» заведите стат, который игра обнуляет в начале заезда, и используйте `absolute`.

Конфиг проверяется `DailyQuestValidator` при старте: ошибки и предупреждения уходят в лог, а квест с ошибкой просто не попадает в пул. Опечатка в `statPath` (стат не объявлен) — предупреждение: такой квест никогда не выполнится.

## Public API

| Тип | Что это |
|---|---|
| `IDailyQuestService` / `DailyQuestService` | Сервис. `GetActive()`, `GetStatus(id)`, `ClaimAsync(id)`, `Tick()`, `ClaimableCount`, `TimeUntilReset`, событие `Changed`, отладка: `ForceComplete`, `DebugSkipToNextResetWindow`, `Reset` |
| `IDailyQuestProvider` | Откуда конфиг. Готовые: `ResourcesDailyQuestProvider`, `JsonDailyQuestProvider` |
| `IDailyQuestStateStore` | Где состояние. Готовый: `JsonDailyQuestStateStore(IPersistentStorage)` |
| `IDailyQuestGameBridge` | `OnQuestsRotated`, `OnQuestCompleted`, `ApplyRewards` — игра начисляет награду здесь |
| `QuestStatus` | Снимок квеста для UI: `Definition`, `State` (`InProgress`/`Completed`/`Claimed`), `ObjectiveProgresses`, `TotalProgress`, `CanClaim` |
| `QuestId`, `QuestObjectiveId`, `QuestRewardKey` | Newtype-ы с `None` |
| `DailyQuestJson` | Разбор JSON → `DailyQuestConfig` |
| `DailyQuestValidator` | Проверка конфига |
| `DailyQuestSelector` | Взвешенный детерминированный выбор набора |

## Инициализация

```csharp
var service = new DailyQuestService(
    provider: new ResourcesDailyQuestProvider(),             // Resources/DailyQuests/daily_quests.json
    store:    new JsonDailyQuestStateStore(persistentStorage),
    bridge:   myBridge,                                       // optional
    stats:    statSystem);                                    // optional, без него прогресса нет
await service.InitializeAsync();
```

`ResourcesDailyQuestProvider` читает `TextAsset` через `Resources.Load` — JSON внутри билда, без HTTP, поэтому работает и на порталах, которые перепаковывают билд (GamePix). Читает один раз и кеширует задачу: его можно создать и вызвать `LoadAsync()` в начале загрузки (на главном потоке), а сервису отдать позже — он получит готовый результат. Нет файла или битый JSON — пустой пул и ошибка в лог.

## Использование в UI

```csharp
service.Changed += Redraw;

void Redraw()
{
    foreach (var quest in service.GetActive())
    {
        var p = quest.TotalProgress;          // p.Current / p.Target, p.Ratio
        claimButton.interactable = quest.CanClaim;
    }
    timerLabel.text = service.TimeUntilReset.ToString(@"hh\:mm");
}

void Update() => service.Tick();   // наступил сброс — придёт новый набор и Changed

async void OnClaimClicked(QuestId id) => await service.ClaimAsync(id);   // награда придёт в bridge.ApplyRewards
```

## Как это устроено

- **Смена периода ленивая.** Таймера нет: каждый `GetActive`/`ClaimAsync`/`Tick`/изменение стата проверяет, не наступил ли новый период. Открытый UI квестов зовёт `Tick()` из `Update`, чтобы набор сменился у игрока на глазах. Перевод часов назад набор не меняет.
- **Сохраняется не прогресс, а точка отсчёта.** При выдаче квеста запоминается текущее значение стата (`baseline`), прогресс = стат − baseline. Поэтому прогресс переживает перезапуск и ничего не пишется на каждый гринд — запись идёт только при выдаче, выполнении и заборе.
- **Выполнение защёлкивается — каждая цель отдельно.** Набранная цель остаётся набранной, даже если стат потом уменьшился; квест становится `Completed`, когда набраны все. Поэтому «10k за заезд + 30 грайндов» засчитается, даже если грайнды добраны в другом заезде.
- **Забор безопасен к падению.** Сначала квест помечается забранным, `ClaimAsync` дожидается записи и только потом зовёт `ApplyRewards`: в худшем случае игрок теряет одну награду, но не получает её дважды. Запись не удалась — награды нет, `false`, можно нажать ещё раз.
- **Записи идут по очереди.** Следующее сохранение ждёт предыдущее, так что старое состояние не ляжет поверх нового.
- **Набор детерминирован.** Сид — начало периода; квесты прошлого набора берутся только если без них набор не собрать.
- **Конфиг поменялся посреди периода.** Убранные квесты выбывают из набора, новые цели считаются с момента загрузки, а если `dailyCount` вырос — набор добирается.
- **Хранилище не ответило** (`LoadAsync` вернул `null`) — сервис выключен до перезапуска, чтобы не выдать забранное ещё раз.
- **Сохранение не читается** (битое или от более новой версии) — до ближайшего сброса квестов нет: что сегодня уже забрано, неизвестно, а заново выданный набор совпал бы со старым.
