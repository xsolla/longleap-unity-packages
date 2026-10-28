# LongLeap Tools

Общие инструменты для всех игр. Сейчас: **глобальный профиль устройства** (рантайм), **кирпичи UI, собираемого кодом** (рантайм) и **WebGL page mode** (редактор).

## Runtime UI

`RuntimeUi` (assembly `LongLeap.Tools.UI`, namespace `LongLeap.Tools.UI`) — текст, фон, ползунок, кнопка, колонки и строки раскладки, плюс общая палитра и размеры. Ими собирают себя панель настроек (`com.longleap.settings`) и окна `com.longleap.ui`: один класс на всех, поэтому цвет, поправленный здесь, меняется во всех окнах сразу. Сборка отдельная, чтобы `LongLeap.Tools` (профиль устройства) не тянул за собой UGUI и TextMeshPro.

## Device profile

`DeviceInfo` (assembly `LongLeap.Tools`) — единственное место, где решается «что это за устройство». Остальной код не проверяет `Application.isMobilePlatform`, тачскрин или `UNITY_WEBGL` сам, а спрашивает `DeviceInfo`.

| Профиль (`DeviceProfile`) | Что это |
|---|---|
| `Desktop` | Нативная сборка для ПК (и редактор): клавиатура и мышь |
| `Mobile` | Нативная мобильная сборка / планшет: пальцы |
| `WebDesktop` | Браузер на ПК (WebGL) |
| `WebMobile` | Браузер на телефоне / планшете (WebGL) |

```csharp
using LongLeap.Tools;

DeviceProfile profile = DeviceInfo.Profile;   // выбор конфига: switch / словарь / поле ассета
if (DeviceInfo.IsMobile) { /* управление пальцами, нативное или в браузере */ }
if (DeviceInfo.IsWeb)    { /* браузер */ }
if (DeviceInfo.IsDesktop) { /* клавиатура и мышь */ }
```

Две оси: среда (`IsWeb`) и управление (`IsMobile`/`IsDesktop`). Мобильным считается мобильная платформа, телефон/планшет по `SystemInfo.deviceType` или устройство с тачскрином; в браузере `Keyboard` есть всегда, поэтому там признак один — тачскрин (ноутбук с тачскрином в браузере даст `WebMobile`).

Значение не кэшируется (тачскрин и клавиатура могут появиться после старта). Профиль, нужный надолго, читай один раз.

Это не то же самое, что игровая схема «чем нажали последним» (подсказки в мире) — её определяет игровой ввод.

### Проверить другой профиль

- Код / тесты: `DeviceInfo.Override = DeviceProfile.WebMobile;` (сбрасывается при входе в Play Mode).
- Редактор: меню **Tools ▸ Device Profile** (Auto / Desktop / Mobile / Web Desktop / Web Mobile). Выбор хранится в EditorPrefs и действует со следующего создания окон — перезапусти Play Mode.

## WebGL page mode (editor)

Меню **Tools ▸ WebGL Page Mode** выбирает, как страница с игрой ведёт себя в браузере; применяется постпроцессором к `index.html` следующего WebGL-билда:

- **Fixed Aspect** — пропорции берутся из размеров canvas в `index.html` (Player Settings), по краям поля.
- **Free Aspect** — canvas занимает всё окно браузера.

Ориентация игры — подменю **Tools ▸ WebGL Page Mode ▸ Orientation**:

- **Auto** (по умолчанию) — из WebGL Resolution в Player Settings: ширина < высоты — портретная, иначе альбомная.
- **Landscape** / **Portrait** — явно, независимо от разрешения.

В обоих режимах страницы добавляется заглушка «Rotate your device to …» под выбранную ориентацию и best-effort `screen.orientation.lock` в fullscreen. Заглушка срабатывает только на сенсорных устройствах (`hover: none` и `pointer: coarse`): окно браузера на ПК не повернуть, там игра просто показывается с полями.

Режим и ориентация хранятся в EditorPrefs (локально у разработчика, в репозиторий не попадают).

Класс `WebGlPagePostprocessor` не зависит от игры.

## Assemblies

| Assembly | Содержимое |
|---|---|
| `LongLeap.Tools` | `DeviceProfile`, `DeviceInfo` (зависит от Unity.InputSystem) |
| `LongLeap.Tools.Editor` | `WebGlPagePostprocessor`, меню `Tools/Device Profile` |
