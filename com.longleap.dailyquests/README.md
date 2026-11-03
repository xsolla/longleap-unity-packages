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
| `settings.uniqueCategories` | нет (false) | У квестов набора нет общих категорий |
| `settings.hardestLast` | нет (false) | Набор идёт от лёгкого к трудному; без `slotDifficulties` — ровно один квест самой высокой сложности |
| `settings.slotDifficulties` | нет ([]) | Сложность каждого места в наборе, например `[1, 2, 3]` |
| `quests[].id` | да | Уникальный id; по нему же ключ локализации в игре |
| `quests[].weight` | нет (1) | Вес при выборе. 0 — квест выключен |
| `quests[].categories` | нет ([]) | Категории для `uniqueCategories`. Квест без категорий ни с кем не конфликтует |
| `quests[].difficulty` | нет (0) | Сложность для `slotDifficulties` и `hardestLast` |
| `objectives[].statPath` | да | Путь стата, как в ачивках (`grind`, `kill:*:grunt`) |
| `objectives[].statTarget` | да | Сколько набрать, > 0 |
| `objectives[].mode` | нет (`delta`) | `delta` — прирост с момента выдачи; `absolute` — значение стата как есть |
| `objectives[].id` | нет (`o0`, `o1`…) | Нужен, только если целей несколько и хочется стабильных id |
| `rewards[].key` / `amount` | да / > 0 | Что дать. Ключ толкует игра (в Climber — id валюты) |

`delta` рассчитан на накопительные статы (счётчики). Для Max-статов (`score_best`) он означает «побей свой рекорд на N», что обычно не то, что нужно, — для «набери 10k за заезд» заведите стат, который игра обнуляет в начале заезда, и используйте `absolute`.

Конфиг проверяется `DailyQuestValidator` при старте: ошибки и предупреждения уходят в лог, а квест с ошибкой просто не попадает в пул. Опечатка в `statPath` (стат не объявлен) — предупреждение: такой квест никогда не выполнится.

## Правила набора

Без `uniqueCategories`, `hardestLast` и `slotDifficulties` набор — просто взвешенная выборка из пула. С любым из них — выборка по правилам, и набор упорядочен по сложности, от лёгкого к трудному.

- **`slotDifficulties`** задаёт сложность каждого места. Мест меньше, чем `dailyCount`, — остальные любой сложности (при `hardestLast` — легче самого трудного квеста пула). Мест больше — берутся самые сложные. Работает и без двух других флагов.
- **`hardestLast`** без слотов: в наборе ровно один квест самой высокой сложности пула, остальные легче.
- **`uniqueCategories`**: никакие два квеста набора не делят категорию.

Если полный набор по всем правилам не собрать (не хватает квестов нужной сложности или категорий), правила ослабляются по очереди:

1. без `uniqueCategories` — от сложности зависят награды, она важнее разнообразия;
2. без сложности (`slotDifficulties` и `hardestLast`), но с `uniqueCategories`;
3. без правил вообще.

Игрок всегда получает столько квестов, сколько позволяет пул, — пустого или урезанного набора из-за правил не бывает. Перебор вариантов ограничен (`DailyQuestSelector.SearchBudget`), так что большой пул с невыполнимыми правилами не подвесит игру.

Валидатор предупреждает, если правила не выполнить на этом пуле: слот хочет сложность, которой нет ни у одного квеста; слотов больше, чем `dailyCount`; полный набор по всем правилам не собрать.

## Public API

| Тип | Что это |
|---|---|
| `IDailyQuestService` / `DailyQuestService` | Сервис. `GetActive()`, `GetStatus(id)`, `ClaimAsync(id)`, `Tick()`, `ClaimableCount`, `TimeUntilReset`, событие `Changed` |
| `IDailyQuestDebug` | Отладка: `ForceComplete`, `SkipToNextPeriod`, `ResetState`. Тот же `DailyQuestService`, но отдельный интерфейс — игровой UI его не видит |
| `IDailyQuestProvider` | Откуда конфиг. Готовые: `ResourcesDailyQuestProvider`, `JsonDailyQuestProvider` |
| `IDailyQuestStateStore` | Где состояние. `LoadAsync` возвращает `DailyQuestStateLoadResult`. Готовый: `JsonDailyQuestStateStore(IPersistentStorage)` |
| `DailyQuestStateLoadResult` | Результат загрузки: `Loaded` (+ `State`), `Empty`, `Unreadable`, `Unavailable` |
| `IDailyQuestGameBridge` | `OnQuestsRotated`, `OnQuestCompleted`, `ApplyRewards` — игра начисляет награду здесь. Обязателен |
| `QuestStatus` | Снимок квеста для UI: `Definition`, `State` (`InProgress`/`Completed`/`Claimed`), `ObjectiveProgresses`, `TotalProgress`, `CanClaim` |
| `QuestId`, `QuestObjectiveId`, `QuestRewardKey` | Newtype-ы с `None` |
| `DailyQuestJson` | Разбор JSON → `DailyQuestConfig` |
| `DailyQuestValidator` | Проверка конфига |
| `DailyQuestSelector` | Взвешенный детерминированный выбор набора по правилам |

## Инициализация

```csharp
var service = new DailyQuestService(
    provider: new ResourcesDailyQuestProvider(),             // Resources/DailyQuests/daily_quests.json
    store:    new JsonDailyQuestStateStore(persistentStorage),
    bridge:   myBridge,                                       // обязателен: сюда приходит ApplyRewards
    stats:    statSystem);                                    // optional, без него прогресса нет
await service.InitializeAsync();

services.Register<IDailyQuestService>(service);
services.Register<IDailyQuestDebug>(service);                 // для dev-tools
```

`ResourcesDailyQuestProvider` читает `TextAsset` через `Resources.Load` — JSON внутри билда, без HTTP, поэтому работает и на порталах, которые перепаковывают билд (GamePix). Читает один раз и кеширует задачу: его можно создать и вызвать `LoadAsync()` в начале загрузки (на главном потоке), а сервису отдать позже — он получит готовый результат. Нет файла или битый JSON — пустой пул и ошибка в лог.

Своё хранилище возвращает `DailyQuestStateLoadResult`: сохранения нет — `Empty()`, не читается — `Unreadable()`, хранилище не ответило — `Unavailable()`. Разбор строки готов: `JsonDailyQuestStateStore.Parse(json)`.

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

`GetActive()` возвращает тот же массив и те же `QuestStatus`, пока ничего не поменялось, — их не нужно копировать, но и менять нельзя. Сравнение по ссылке (`ReferenceEquals(old, status)`) — дешёвый способ понять, что строку перерисовывать не надо.

## Как это устроено

- **Смена периода ленивая.** Таймера нет: каждый `GetActive`/`ClaimAsync`/`Tick`/изменение стата проверяет, не наступил ли новый период. Открытый UI квестов зовёт `Tick()` из `Update`, чтобы набор сменился у игрока на глазах. Перевод часов назад набор не меняет.
- **Сохраняется не прогресс, а точка отсчёта.** При выдаче квеста запоминается текущее значение стата (`baseline`), прогресс = стат − baseline. Поэтому прогресс переживает перезапуск и ничего не пишется на каждый гринд — запись идёт только при выдаче, выполнении и заборе.
- **Стат откатился — точка отсчёта опускается.** Если `delta`-стат стал меньше baseline (сейв статов старше сейва квестов, статы сбросили), baseline становится текущим значением и сохраняется. Иначе прогресс стоял бы на нуле, пока стат не догонит старую отметку. Набранное до отката не возвращается.
- **Выполнение защёлкивается — каждая цель отдельно.** Набранная цель остаётся набранной, даже если стат потом уменьшился; квест становится `Completed`, когда набраны все. Поэтому «10k за заезд + 30 грайндов» засчитается, даже если грайнды добраны в другом заезде.
- **Забор безопасен к падению.** Сначала квест помечается забранным, `ClaimAsync` дожидается записи и только потом зовёт `ApplyRewards`: в худшем случае игрок теряет одну награду, но не получает её дважды. Запись не удалась — награды нет, `false`, можно нажать ещё раз. Мост обязателен — без него награду было бы некуда начислить.
- **Записи идут по очереди.** Следующее сохранение ждёт предыдущее, так что старое состояние не ляжет поверх нового.
- **Набор детерминирован.** Сид — начало периода; квесты прошлого набора берутся только если без них набор не собрать.
- **Конфиг поменялся посреди периода.** Убранные квесты выбывают из набора, новые цели считаются с момента загрузки, а если `dailyCount` вырос — набор добирается по тем же правилам: оставшиеся квесты держат свои слоты и категории.
- **Хранилище не ответило** (`Unavailable`) — сервис выключен до перезапуска, чтобы не выдать забранное ещё раз.
- **Сохранение не читается** (`Unreadable`: битое или от более новой версии) — до ближайшего сброса квестов нет: что сегодня уже забрано, неизвестно, а заново выданный набор совпал бы со старым.
- **Изменение стата дешёвое.** Сверяются только квесты, которые ждут этот стат; без изменений ничего не выделяется. `Changed` приходит, только если сдвинулся видимый прогресс или состояние; `QuestStatus` пересобирается лениво — при чтении. `GetActive()` заодно сверяет прогресс со статами, так что и стат, изменённый без события, виден сразу.

Внутри: `ResetSchedule` (когда начинается период), `ProgressEvaluator` (baseline, прогресс, защёлки, откат) и `DailyQuestSelector` (выбор набора) — без состояния сервиса, каждый тестируется отдельно; `DailyQuestService` их связывает.
