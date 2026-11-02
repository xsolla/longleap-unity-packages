# Xsolla ID account block

A reusable widget: in the top-right corner it shows either a sign-in button in
the Xsolla brand style or the logo with the avatar — and on click expands a card
with the name, username and "sign out".

## The "signed in" state: two halves

```
SignedIn            vertical column; it sets the width of both halves
├── Pill            collapsed: logo + avatar, the whole row is the expand button
└── Expanded        expanded: name, username, "sign out". Off by default
```

Expansion lives in the widget's own `_expanded` field and is NOT persisted: it is
a response to a click, not a setting. It is collapsed in four places — on a
second click, on sign-out, when the object is disabled, and in the preview. The
visibility condition is extracted into `XsollaIdAccountWidgetLogic.ShowsExpandedPanel`,
because a forgotten collapse only shows up on the next sign-in, with the previous
player's name on screen.

**Clicking outside the card does not close it — this is a decision, not an
omission.** A full-screen catcher would collapse the card "the usual way", but
the widget sits on top of the main menu and would swallow clicks on it — and this
block deliberately never touches modality (see the comment on the
`XsollaIdAccountWidget` class).

**The column sets the width of the halves** (`m_ChildForceExpandWidth` on
`SignedIn`), and `Pill` and `Expanded` deliberately have no horizontal
`ContentSizeFitter` of their own: it runs after the parent's layout and gives the
half back its own width. With it, the expanded card, being narrower than the
pill, ended up centered instead of matching it.

The name on the card is the `name` claim, the username is the `username` claim,
and one must not be substituted for the other: for an account without its own
username Xsolla ID issues a generated `id<digits>`. Both arrive in
`XsollaIdSession.FullName` and `.UserName`. If there is no `name` claim, the name
falls back to `DisplayName`; if there is no `username`, the line is hidden
entirely rather than left empty.

It is compiled into the `LongLeap.XsollaId.UI` assembly together with the prefab
and sprites, so it moves to another project as a whole, along with the package.

## Setup

1. Put `XsollaIdAccountWidget.prefab` on a canvas (or nest it in your own screen).
   The root anchors are a point in the top-right corner with a 24 offset — works
   at any aspect ratio.
2. Give it the services:

```csharp
widget.Bind(auth, avatars);   // IXsollaIdAuth, IXsollaIdAvatarLoader

// The third parameter controls the Backpack button (shown by default).
widget.Bind(auth, avatars, showBackPackLink: false);
```

`Bind` can be called at any time — before the object is first enabled, and
repeatedly. Until it is called the widget shows "checking", not the guest state:
a player we know nothing about yet must not be declared a guest.

The widget looks NOTHING up by itself. No `FindObjectOfType`, no singletons, no
service locator: otherwise the package would know about a specific game. Same
approach as `LongLeap.Settings.UI.SettingsToggleRow.Bind`.

**Sign-in unavailable — the widget hides entirely.** Hidden, not greyed out: on
GamePix this is required by T&C 3.3. It is enough to pass stubs to `Bind`
(`UnavailableXsollaIdAuth`, `UnavailableXsollaIdAvatarLoader`) — no `#if` is
needed in game code.

**The Backpack button is an exception, and you need to know about it.**
`BackPackBtn` is a child of the prefab ROOT, next to the three states, not inside
`SignedIn`: state switching does not affect it, and the root is never disabled.
So its visibility is controlled only by the `showBackPackLink` flag — if you don't
pass `false`, it stays visible even when sign-in is unavailable.

## Localization keys

The widget resolves them via `Loc.Resolve` and keeps the names as constants in
`XsollaIdAccountWidgetLogic`. The consuming project must define all ten:

`btn_xsolla_sign_in`, `btn_xsolla_signing_in`, `btn_log_out`,
`xsolla_checking`, `xsolla_name_unknown`,
`xsolla_err_popup_blocked`, `xsolla_err_network`, `xsolla_err_timeout`,
`xsolla_err_failed`, `xsolla_err_unavailable`.

The sign-in button label is restricted by the Xsolla guidelines: only
"Sign in with Xsolla", "Continue with Xsolla" and "Sign in" are allowed. They may
be translated, but the meaning must not change — the player must understand they
are signing in THROUGH Xsolla, not creating a separate account.

## Palette

The colors were picked from the official Xsolla app (dark theme, cyan accent) —
not eyeballed, so don't change them "because it looks similar".

| Role | Color | Where |
|---|---|---|
| Base background | `#141D1F` | not drawn by the widget; the screen background under it |
| Card | `#1B2628` | the "checking" plate, the pill and the expanded card |
| Nested surface | `#243335` | the "sign out" button, pill hover |
| Primary button fill | `#135D6C` | sign-in button, `normal` |
| Accent outline | `#31CAE8` | 1 pt outline of the sign-in button |
| Brand cyan | `#80EAFF` | sign-in button label, logo |
| Muted text | `#BABFC0` | "checking", "sign out" |

The corner radius in the reference is ~3 pt at a button height of ~37 pt, so
`round_rect_4` (4 pt at a height of 40) fits as is — no need to touch
`pixelsPerUnitMultiplier`.

Two layout details are non-obvious and are done this way on purpose:

- **Button colors live in `Button.m_Colors`, while their `Image.color` is white.**
  ColorTint is multiplied by the graphic's color, so a tinted image PLUS a tinted
  `normalColor` square the brightness and give an almost black button. The single
  source of truth is the `ColorBlock`; hover and pressed then stay on-palette too.
- **The sign-in button's outline is its own `Image`, and the fill is the `Fill`
  child.** `Image` has no outline, so the outer rectangle is painted cyan, and on
  top of it sits `Fill` with the same sprite, stretched with a 1 pt inset on all
  sides. `Fill` is the button's `targetGraphic` — state tinting goes to the fill,
  and the outline stays steady. `Fill` has `LayoutElement.ignoreLayout`, otherwise
  `HorizontalLayoutGroup` would count it as the button's content.

## Checking the layout in the editor

Real sign-in is never available in the editor (`XsollaIdWebBrowser.IsSupported`
lives under `#if UNITY_WEBGL && !UNITY_EDITOR`). So the component has a context
menu: gear → `Preview: signed in`, `signed in (expanded)`,
`signed in (expanded, no claims)`, `signed out`, `checking`,
`error (popup blocked)`, `hidden`. The fake sign-in lives in `FakeXsollaIdAuth`;
the whole file is under `#if UNITY_EDITOR` and does not get into the build.

## Debug panel

`XsollaIdDebugPanel` (assembly `LongLeap.XsollaId.UI`, the whole file under
`XSOLLA_ID_DEBUG`) is an on-screen panel with the service state, the four name
claims and a button per entry point. It lives in the package and knows nothing
about the game: the service arrives through a provider.

```csharp
XsollaIdDebugPanel.Spawn(() => MyServices.Instance?.Account);
```

A provider rather than the service itself: the panel is created on
`AfterSceneLoad`, while the bootstrap builds the service later in its chain, so a
value captured once would stay null forever. A repeated `Spawn` does not create a
second instance; it replaces the provider on the live one.

The game keeps only an installer — the few lines that know where exactly that
project keeps the service.

The panel takes claims from `XsollaIdDebugClaims` — a snapshot of the last parsed
userinfo. `?` means userinfo did not happen in this run (a restored session with
`ProfileFetched` does not request it), `-` means the claim is absent from the
response.

The expanded card has separate items because expansion lives in a widget field,
not in the service: in edit mode button clicks do not work, and otherwise that
half of the layout could not be seen at all. The `no claims` variant is an
account without the `name` and `username` claims: the name must take the
fallback path, and the username line must disappear. That is exactly where the
layout breaks if empty values are not hidden.

---

# Sprites

## Sprites/round_rect_4.png — present

A white 32×32 square with an 8px radius cut into the **alpha**. White plus alpha
means `Image.color` multiplication gives the exact guideline color, so one sprite
serves all four button states.

Import: Sprite (Single), `spritePixelsPerUnit = 200`, `spriteBorder = 8,8,8,8`,
`alphaIsTransparency`, no mipmaps, RGBA32 uncompressed (DXT/ASTC block artifacts
on a rounded 32px edge are visible to the eye).

`8 × (100 / 200) = ` **exactly 4 canvas units = 4 pt**, as in the guideline, while
the texture is drawn at 2× and stays sharp if the widget is scaled up.
The `Image` uses `type = Sliced`.

## Sprites/avatar_placeholder.png — present

A 128×128 placeholder shown whenever there is no profile picture: the account
has no `picture` claim, the CDN did not serve it, or CORS blocked the request.

## Sprites/xsolla_logo.png — PRESENT, but NOT an official export

175x39, solid `#80EAFF` with clean alpha, aspect ratio **4.4872**. The `Logo` node
is enabled both on the sign-in button and in the collapsed pill — there are two
instances, because one `Image` cannot be in two places.

**The mark was taken from a screenshot of the official Xsolla app, not exported
from Figma.** It is the official mark and the official color, but a raster
reconstructed from an already downscaled render: the alpha was computed by
reverse matting (`observed = fg*a + bg*(1-a)` with known `fg = #80EAFF` and
`bg = #141D1F`), so there is no background in the corners and no fringe on the
edges. At a height of 20 pt the source is more than enough (39 px ~ 2x), but
**for the portal review it is better to substitute the real SVG**: upscaling this
raster above 20 pt is noticeable.

### What to replace it with

Source — Figma `Xsolla-ID-playground`, file `M9kM5gdXjwFaqL2oBJHUuA`, node
**`826:9812`** (`Xsolla logos`, a 122x32 instance containing a single
118.946x25.684 vector, already in `#80EAFF`).

After replacing it, check three things:

1. **Alpha.** The instance is rendered on the dark brand background, and the
   export may come with an opaque `#141D1F` fill. Make sure the corners are
   transparent — otherwise it is a rectangle, not a logo.
2. **Cropping.** The instance is 122x32, and the mark itself is
   118.946x25.684 — ~3px of padding around it. If the export kept that padding,
   then at a *sprite* height of 20 pt the *mark* itself will be
   20 x 25.684/32 = **16.05 pt**, while the guideline requires 20 pt.
3. **Width.** Read `sprite.rect` and set `preferredWidth =
   preferredHeight x rect.width / rect.height` on BOTH `Logo` nodes. It is
   currently 20 x 89.74 from the actual 4.4872 ratio of the current raster; the
   vector's ratio is 4.6310, so the number will need adjusting.

### Import

Sprite (2D and UI), Single, `spritePixelsPerUnit = 100`, **no border** (the logo
is never Sliced), `alphaIsTransparency`, no mipmaps, Bilinear, Clamp,
`maxTextureSize = 512`, for WebGL — RGBA32 uncompressed (the thin diagonals of
the mark are exactly where DXT ringing shows).

### Rules that must not be broken

- `Image.preserveAspect = true` on the `Logo` node. This is the most important
  flag in the prefab: it satisfies the "aspect ratio must be preserved"
  requirement even if the numbers in `LayoutElement` turn out wrong.
- `Image.color = #FFFFFF` (neutral multiplication) in all states: the asset's own
  `#80EAFF` is drawn. The guidelines forbid recoloring the logo.
- Do not crop, replace or stretch the logo.

Separately: the **disabled** state from the guideline (icon `#999`) is
deliberately not used in this layout — the branded button is never disabled, see
the comments in `XsollaIdAccountWidget`. Getting `#999` from `#80EAFF` by
multiplication is impossible (the red channel would need a 1.195 multiplier); that
would require a second, monochrome master of the mark.
