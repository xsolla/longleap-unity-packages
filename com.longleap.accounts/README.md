# LongLeap Accounts

Startup account flow: the player picks **Xsolla ID** or **guest** before the save
service starts, and guest progress is merged into the account on sign-in.

| Type | Role |
|---|---|
| `AccountGate` | Waits for the silent session restore; shows the startup screen only when there is no session; reads the account save. Returns `AccountGateResult` (account id or guest). |
| `IAccountScreen` | The startup screen, drawn by the game. Invoke `SignInClicked` synchronously from `onClick` (browser popup rules). |
| `SaveSwitch` | Run before `CloudSaveService.InitializeAsync`. Clears another owner's local save, uploads a local save the server lost, merges guest progress into the account. |
| `SaveMergeRules` | Per-key merge table: glob pattern → `Max`, `Min`, `Or`, `Union`, `PreferAccount`, `PreferLocal` or a custom function. The only game-specific part. |

## Using it in a game

```csharp
// 1. Before the save service: who plays.
var scope = new XsollaCloudSaveScope(baseUrl, gameId, prefs, savesPath: savesPath);
var gate = new AccountGate(auth, scope, scope.TryPeekAccountAsync, () => MyStartupScreen.Create());
AccountGateResult who = await gate.RunAsync();

// 2. The provider follows the answer and never changes while the game runs.
ICloudSaveProvider provider = who.IsGuest
    ? new NullCloudSaveProvider()
    : new XsollaCloudSaveProvider(baseUrl, gameId, who.AccountId, savesPath: savesPath);
var saves = new CloudSaveService(provider, prefs);

// 3. Bring the local save in line with the owner, then initialize.
var rules = new SaveMergeRules(SaveMergeRule.PreferAccount)
    .Add("stat_*", SaveMergeRule.Max)
    .Add("ach_*_unlocked", SaveMergeRule.Or);
await new SaveSwitch(saves, saves, scope, rules, saves.ResetLocalCacheAsync)
    .RunAtStartupAsync(who.AccountId, who.AccountEntries);
await saves.InitializeAsync();
```

An account change (sign-in or sign-out during the game) restarts the game, so
the gate and the switch run again. All waits are frame-based (`Task.Yield`), so
everything works under WebGL.
