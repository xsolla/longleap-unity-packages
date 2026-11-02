# Sign-in with Xsolla ID

OAuth2/OIDC authorization code + PKCE. The package is **self-contained**: nothing
needs to be copied into the project's `Assets/` — no scripts, no plugins, no
callback page.

## What's inside

| Where | What |
|---|---|
| `Runtime/` | contracts and orchestration, without a single network call |
| `Runtime.Web/` | HTTP, the browser window and `XsollaId.jslib`. Compiled only under the `XSOLLA_ID` define |
| `Runtime.UI/` | the account block widget |
| `UI/` | the widget prefab and sprites (see `UI/README.md`) |
| `Editor/` | the `Xsolla ID` menu and `xid-callback.html` |

**`XsollaId.jslib` sits NEXT TO `Runtime.Web/*.cs` on purpose.**
One half of the bridge is `[DllImport("__Internal")]` in `XsollaIdWebBrowser` and
`XsollaIdWebGLImageDecoder`, the other is seven functions in the jslib. When the
file lived in the game's `Assets/Plugins/WebGL/`, the package compiled but the
build failed at link time:

```
wasm-ld: error: undefined symbol: OpenXsollaIdPopup
wasm-ld: error: undefined symbol: XsollaIdBeginImageDecode
...
```

The error shows up at the very end of a WebGL build and gives no hint that a
file was not copied. Keeping both halves in one place is the only way to make
adding the package not depend on remembering this.

The receiver object name (`XsollaIdCallbacks`) is duplicated in the jslib and in
`XsollaIdCallbackReceiver.ReceiverName`. Change them only together.

## Setup

1. **Package.** Put it in `Packages/` (embedded) or reference it in `manifest.json`.
   Dependencies: `com.longleap.core`, `com.longleap.localization`,
   `com.unity.nuget.newtonsoft-json`, `com.unity.textmeshpro`.

2. **Define.** Menu `Xsolla ID / Required / Sign-in Enabled (XSOLLA_ID)`.
   Without it the `LongLeap.XsollaId.Web` assembly does not exist at all, and not
   a single request goes to `id.xsolla.com`. The define is set for WebGL.

3. **Config.** `Assets / Create / LongLeap Services / Xsolla ID Config`,
   place it at `Assets/Resources/Backend/XsollaIdConfig.asset`, fill in
   `clientId` and `redirectUri` for the active environment. The environment is
   switched from the same menu.

   An empty `clientId` is NOT a build error: sign-in reports `Unavailable` and
   the widget hides entirely. If the account block "did not appear", look here
   first; the factory logs exactly what is missing.

4. **Widget.** Put `UI/XsollaIdAccountWidget.prefab` on a canvas. Layout,
   palette and editor preview details are in `UI/README.md`.

5. **Services for the widget.** It does not look anything up by itself:

   ```csharp
   var auth = LongLeap.XsollaId.Web.XsollaIdFactory.Create(config, prefs);
   var avatars = LongLeap.XsollaId.Web.XsollaIdFactory.CreateAvatarLoader();
   widget.Bind(auth, avatars);
   ```

   Find the widget with `FindObjectsInactive.Include` if it lives on a disabled
   screen.

6. **Localization keys.** Ten of them, listed in `UI/README.md`. Without a
   localization service `Loc.Resolve` returns the key itself, and the button will
   read `btn_xsolla_sign_in`.

## What NOT to do

- Copy `XsollaId.jslib` into `Assets/Plugins/WebGL/` — it is already in the
  package, and a second copy gives two definitions of the same symbols.
- Place `xid-callback.html` next to the build by hand:
  `XsollaIdCallbackPagePostprocessor` puts it into the root of the WebGL build
  itself when the define is on, and removes it when it is off.
- Create a `XsollaIdCallbacks` object in the scene: it is created by
  `XsollaIdCallbackReceiver.Instance`.

## Font

The widget prefab ships without a font, so its labels use the project's TMP
default font (`TMP Settings` → Default Font Asset). To use another font, set
**Font Override** on the widget instance: it is applied to every label in the
widget, including those in states that are currently hidden. Per-label overrides
on the prefab instance keep working as well.
