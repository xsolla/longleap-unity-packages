# Вход через Xsolla ID

OAuth2/OIDC authorization code + PKCE. Пакет **самодостаточен**: ничего
копировать в `Assets/` проекта не нужно — ни скриптов, ни плагинов, ни
страницы обратного вызова.

## Что лежит внутри

| Где | Что |
|---|---|
| `Runtime/` | контракты и оркестрация, без единого обращения к сети |
| `Runtime.Web/` | HTTP, окно браузера и `XsollaId.jslib`. Собирается только под дефайном `XSOLLA_ID` |
| `Runtime.UI/` | виджет блока аккаунта |
| `UI/` | префаб виджета и спрайты (см. `UI/README.md`) |
| `Editor/` | меню `Xsolla ID` и `xid-callback.html` |

**`XsollaId.jslib` лежит РЯДОМ с `Runtime.Web/*.cs`, и это не случайность.**
Половина этой связки — `[DllImport("__Internal")]` в `XsollaIdWebBrowser` и
`XsollaIdWebGLImageDecoder`, вторая — семь функций в jslib. Когда файл жил в
`Assets/Plugins/WebGL/` игры, пакет компилировался, а сборка падала на линковке:

```
wasm-ld: error: undefined symbol: OpenXsollaIdPopup
wasm-ld: error: undefined symbol: XsollaIdBeginImageDecode
...
```

Ошибка приезжает в самом конце WebGL-сборки и ничем не намекает, что забыли
скопировать файл. Держать обе половины в одном месте — единственный способ,
при котором подключение пакета не требует помнить об этом.

Имя объекта-приёмника (`XsollaIdCallbacks`) продублировано в jslib и в
`XsollaIdCallbackReceiver.ReceiverName`. Менять только вместе.

## Подключение

1. **Пакет.** Положить в `Packages/` (embedded) либо прописать в `manifest.json`.
   Зависимости: `com.longleap.core`, `com.longleap.localization`,
   `com.unity.nuget.newtonsoft-json`, `com.unity.textmeshpro`.

2. **Дефайн.** Меню `Xsolla ID / Required / Sign-in Enabled (XSOLLA_ID)`.
   Без него сборки `LongLeap.XsollaId.Web` нет вовсе и ни одного запроса на
   `id.xsolla.com` не уходит. Дефайн ставится для WebGL.

3. **Конфиг.** `Assets / Create / LongLeap Services / Xsolla ID Config`,
   положить в `Assets/Resources/Backend/XsollaIdConfig.asset`, заполнить
   `clientId` и `redirectUri` активной среды. Среда переключается тем же меню.

   Пустой `clientId` — это НЕ ошибка сборки: вход отвечает `Unavailable`, а
   виджет прячется целиком. Если блок аккаунта «не появился», смотреть сюда
   первым делом; фабрика пишет в консоль, чего именно не хватает.

4. **Виджет.** `UI/XsollaIdAccountWidget.prefab` на канвас. Подробности
   верстки, палитры и превью в редакторе — в `UI/README.md`.

5. **Сервисы виджету.** Он сам ничего не ищет:

   ```csharp
   var auth = LongLeap.XsollaId.Web.XsollaIdFactory.Create(config, prefs);
   var avatars = LongLeap.XsollaId.Web.XsollaIdFactory.CreateAvatarLoader();
   widget.Bind(auth, avatars);
   ```

   Искать виджет нужно с `FindObjectsInactive.Include`, если он лежит в
   выключенном экране.

6. **Ключи локализации.** Десять штук, список в `UI/README.md`. Без сервиса
   локализации `Loc.Resolve` возвращает сам ключ, и на кнопке будет написано
   `btn_xsolla_sign_in`.

## Чего делать НЕ нужно

- Копировать `XsollaId.jslib` в `Assets/Plugins/WebGL/` — он уже в пакете, и
  вторая копия даст два определения одних и тех же символов.
- Класть `xid-callback.html` рядом со сборкой руками:
  `XsollaIdCallbackPagePostprocessor` кладёт её в корень WebGL-сборки сам,
  когда дефайн включён, и убирает, когда выключен.
- Создавать объект `XsollaIdCallbacks` на сцене: его поднимает
  `XsollaIdCallbackReceiver.Instance`.

## Известное ограничение

Префаб виджета ссылается на конкретный шрифт TMP. В чужом проекте этой ссылки
нет, и подписи молча остаются без шрифта — переопределяйте `m_fontAsset` на
экземпляре префаба своим шрифтом.
