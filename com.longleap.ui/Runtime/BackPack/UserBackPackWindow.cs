using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LongLeap.Backpack;
using LongLeap.XsollaId;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LongLeap.UI
{
    /// <summary>
    /// The player's backpack: shows what the player has on the server — before a run.
    ///
    /// Contents come from the BackPack server (<see cref="IBackPackService.GetRewardsAsync"/>,
    /// GET {baseUrl}/rewards?xsollaId=…&amp;gameId=…) and are reloaded on every
    /// open: the backpack also changes outside the game (the server grants achievement rewards).
    /// The service is provided by game loading, as for all other windows —
    /// the window itself knows nothing about the service container.
    ///
    /// Entries are a grid of cards (<see cref="BackPackItemView"/>): image, title,
    /// description, rewards in a column (icon and what the player gets) and an "activate" button.
    ///
    /// What an item grants is decided by the game, not the server: the server attaches
    /// attributes to the item as they are, the game splits them into categories
    /// (<see cref="IBackPackItemCategorizer"/>) and the window takes reward keys from them
    /// (<see cref="IBackPackItemCategories.Rewards"/>); a key is a product id
    /// (<see cref="IBackPackProduct.Id"/>, catalog — <see cref="IBackPackProductCatalog"/>).
    /// Activating an item means granting these products to the player.
    ///
    /// Activation takes two steps, and both are load-bearing:
    /// <list type="number">
    /// <item>"Activate" in the window only SELECTS the item — nothing is sent
    /// to the server. Whether a product is applicable can only be asked of an ongoing run
    /// (<see cref="IBackPackProduct.CanApply"/>), and before the start there is none, and the start itself
    /// would reset whatever was granted. If, however, the
    /// window is opened mid-run, a product that is not applicable right now
    /// cannot be selected.</item>
    /// <item>Once there is somewhere to grant the product (right after the run starts), whoever opened the window calls
    /// <see cref="ActivateSelectedAsync"/>: for each selected item
    /// we check whether the product is applicable; an inapplicable one is NOT spent — the item stays
    /// in the backpack. An applicable one is deleted on the server, and the product is granted ONLY after
    /// a confirmed deletion: if it fails, nothing is granted and the item stays.</item>
    /// </list>
    /// An item cannot be selected if the game doesn't know its reward (no product with that id),
    /// if its product doesn't fit the moment the window was opened for
    /// (<see cref="IBackPackProduct.IsUsableIn"/>), or beyond how much of the product can be held at once
    /// (<see cref="IBackPackProduct.Max"/>).
    ///
    /// Only a signed-in player has a backpack, so the window has two panels, and
    /// exactly one is always on screen: no xsollaID — the sign-in panel (a sign-in prompt and
    /// a Xsolla ID sign-in button, see <see cref="BindAuth"/>), otherwise — the items
    /// panel. After sign-in the window switches to items by itself and reloads the
    /// backpack. The BackPack site button (the full backpack there, across all games) is visible
    /// in both cases.
    ///
    /// The server connection is checked not by the window but by whoever opens it
    /// (<see cref="CheckConnectionAsync"/>): without a connection there is no point opening the backpack.
    ///
    /// The opener first passes via <see cref="Prepare"/> the moment at which products will
    /// be granted, and what to do on "Continue" and on "back". For example, the main menu
    /// on the "play" button: <see cref="ProductUseContext.RunStart"/>, "Continue"
    /// starts the run, "back" returns to the menu. The window itself knows nothing
    /// about runs — so any other window can open it in front of itself.
    /// </summary>
    public sealed class UserBackPackWindow : MenuPageWindow
    {
        [Header("Panels")]

        [Tooltip("Items panel: card grid and status line. Visible to a signed-in player.")]
        [SerializeField] private GameObject _itemsPanel;

        [Tooltip("Sign-in panel: sign-in prompt and sign-in button. Visible while the player has no xsollaID.")]
        [SerializeField] private GameObject _signInPanel;

        [Header("Item list")]

        [Tooltip("Item card template (usually a separate prefab). If it lives in the scene, keep it " +
                 "disabled: an enabled one stays on screen as an extra card.")]
        [SerializeField] private BackPackItemView _itemTemplate;

        [Tooltip("Where to put cards (object with a GridLayoutGroup). Empty — the template's parent.")]
        [SerializeField] private Transform _itemsRoot;

        [Header("Status")]

        [Tooltip("Line shown instead of the list: loading, empty, error. Optional.")]
        [SerializeField] private TMP_Text _statusLabel;

        [SerializeField] private string _loadingText = "LOADING...";

        [Tooltip("Backpack is empty — it has no items compatible with this game.")]
        [SerializeField] private string _emptyText = "NO COMPATIBLE ITEMS";

        [SerializeField] private string _errorText = "COULDN'T LOAD YOUR BACKPACK";

        [Header("Xsolla ID sign-in")]

        [Tooltip("Text on the sign-in panel: sign-in prompt, 'checking account', failure reason.")]
        [SerializeField] private TMP_Text _signInMessage;

        [Tooltip("'Sign in' button. Visible only to a guest and only when sign-in is available at all.")]
        [SerializeField] private Button _signInButton;

        [Tooltip("Sign-in button label: changes to 'signing in...' while the sign-in window is open. Optional.")]
        [SerializeField] private TMP_Text _signInLabel;

        [SerializeField] private string _signInText = "SIGN IN";
        [SerializeField] private string _signingInText = "SIGNING IN...";
        [SerializeField] private string _signedOutText = "SIGN IN WITH XSOLLA TO SEE YOUR BACKPACK";
        [SerializeField] private string _checkingText = "CHECKING YOUR ACCOUNT...";
        [SerializeField] private string _signInUnavailableText = "SIGN-IN IS UNAVAILABLE";
        [SerializeField] private string _signInFailedText = "SIGN-IN FAILED. PLEASE TRY AGAIN";
        [SerializeField] private string _popupBlockedText = "POP-UP BLOCKED. ALLOW POP-UPS AND TRY AGAIN";

        [Tooltip("How long to wait for the previous session to restore before showing the guest state.")]
        [Min(1f)] [SerializeField] private float _readyTimeoutSeconds = 12f;

        [Header("BackPack site")]

        [Tooltip("'Open BackPack' button: the site with the player's backpack.")]
        [SerializeField] private Button _openBackPackButton;

        [SerializeField] private string _backPackUrl = "https://backpack.xsolla.com/";

        [Header("Continue")]

        [Tooltip("'Continue' button: hands control back to whoever opened the window (see Prepare).")]
        [SerializeField] private Button _continueButton;

        private readonly List<BackPackItemView> _itemViews = new List<BackPackItemView>();

        /// <summary>Items selected for the run, in selection order — they are activated in that order too.</summary>
        private readonly List<BackPackRewardItem> _selected = new List<BackPackRewardItem>();

        /// <summary>For one item's products, so a list isn't allocated on every check.</summary>
        private readonly List<IBackPackProduct> _productsScratch = new List<IBackPackProduct>();

        /// <summary>For one card's reward rows — the card consumes them immediately.</summary>
        private readonly List<BackPackRewardEntry> _rewardsScratch = new List<BackPackRewardEntry>();

        private readonly RemoteSpriteCache _icons = new RemoteSpriteCache();

        /// <summary>
        /// Provided by game loading — the window itself knows nothing about the service container. Until then
        /// (and if not provided) — <see cref="NullBackPackService"/>: BackPack disabled.
        /// </summary>
        private IBackPackService _backPack = NullBackPackService.Instance;

        /// <summary>Xsolla ID sign-in. Provided by loading, like <see cref="_backPack"/>.</summary>
        private IXsollaIdAuth _auth;

        /// <summary>Splits items into categories (reward keys among them). Provided by loading.</summary>
        private IBackPackItemCategorizer _categorizer;

        /// <summary>
        /// Categories per item instance: the window asks for reward keys on every redraw, while an item's
        /// attributes don't change. Items arrive as new objects on every reload, so stale entries go with them.
        /// </summary>
        private ConditionalWeakTable<BackPackRewardItem, IBackPackItemCategories> _categories =
            new ConditionalWeakTable<BackPackRewardItem, IBackPackItemCategories>();

        private static readonly string[] NoKeys = Array.Empty<string>();

        /// <summary>The item's categories, or null if there is no categorizer.</summary>
        public IBackPackItemCategories CategoriesOf(BackPackRewardItem item)
        {
            if (item == null || _categorizer == null) return null;

            if (!_categories.TryGetValue(item, out var categories))
            {
                categories = _categorizer.Categorize(item);
                if (categories == null) return null;
                _categories.Add(item, categories);
            }

            return categories;
        }

        /// <summary>The item's reward keys (<see cref="IBackPackItemCategories.Rewards"/>); never null.</summary>
        private IReadOnlyList<string> RewardKeysOf(BackPackRewardItem item) =>
            CategoriesOf(item)?.Rewards ?? NoKeys;

        /// <summary>Products — their ids are the reward keys. Provided by loading.</summary>
        private IBackPackProductCatalog _catalog;

        /// <summary>How an activated item is spent. Provided by loading; null — deleted on the server.</summary>
        private IBackPackItemSpender _spender;

        /// <summary>Whether a run is in progress when the product is granted. Provided by loading.</summary>
        private Func<bool> _isRunActive;

        /// <summary>The sign-in window is open and waiting for the player.</summary>
        private Task<XsollaIdResult> _signInTask;

        /// <summary>What to tell the guest about the last failed sign-in attempt. Null — nothing.</summary>
        private string _signInError;

        /// <summary>Waiting for the previous session to restore: IXsollaIdAuth has no event for that.</summary>
        private Coroutine _readyWatch;
        private bool _readyTimedOut;

        /// <summary>What to do on "Continue" — set by the opening window in <see cref="Prepare"/>.</summary>
        private Func<Task> _onContinue;

        /// <summary>Where to go on "back". Not set — to the main menu, like any menu page.</summary>
        private Func<Task> _onBack;

        /// <summary>
        /// The moment at which the selected products will be granted — set by the opening window in
        /// <see cref="Prepare"/>. Until then None: nothing can be selected.
        /// </summary>
        private ProductUseContext _context = ProductUseContext.None;

        /// <summary>
        /// List load number. A reply to a stale request (the window was closed and
        /// reopened meanwhile) is discarded by it, otherwise the old list would land on top of the new one.
        /// </summary>
        private int _loadVersion;

        /// <summary>
        /// Cancellation of the ongoing list load: a new load, closing or destroying the window
        /// abort the request instead of waiting for a reply that would be thrown away anyway.
        /// </summary>
        private CancellationTokenSource _loadCancellation;

        private bool _warnedNoService;

        /// <summary>Items selected for the run — activated in <see cref="ActivateSelectedAsync"/>.</summary>
        public IReadOnlyList<BackPackRewardItem> SelectedItems => _selected;

        /// <summary>An item was activated: the server confirmed the deletion and its products were granted.</summary>
        public event Action<BackPackRewardItem> ItemActivated;

        /// <summary>
        /// Give the window the BackPack server service. Called by loading; without it (or with
        /// a disabled one) the window shows an error instead of the list, and "Continue" works as usual.
        /// </summary>
        public void BindBackPack(IBackPackService backPack)
        {
            _backPack = backPack ?? NullBackPackService.Instance;

            if (IsOpen)
                Forget(ReloadAsync());
        }

        /// <summary>
        /// Give the window Xsolla ID sign-in. Called by loading; without it there is no
        /// sign-in button, and the guest simply sees that sign-in is unavailable.
        ///
        /// We subscribe to sign-in and sign-out immediately and permanently: the window object lives for the whole
        /// game (see <see cref="WindowBase"/>), and for a player who just signed in an open window
        /// must reload the backpack right away, without waiting to be reopened.
        /// </summary>
        public void BindAuth(IXsollaIdAuth auth)
        {
            UnsubscribeAuth();

            _auth = auth;

            if (_auth != null)
            {
                _auth.SignedIn += OnSignedIn;
                _auth.SignedOut += OnSignedOut;
            }

            RefreshSignInButton();

            if (IsOpen)
                Forget(ReloadAsync());
        }

        /// <summary>
        /// Set what the window's buttons do. Called by the opening window before
        /// <c>OpenAsync</c>. The window closes itself — after the passed action
        /// has run, so no empty screen flashes in between.
        /// </summary>
        /// <param name="context">
        /// The moment at which the selected products will be granted: items whose product is
        /// not marked for this moment (<see cref="IBackPackProduct.IsUsableIn"/>) cannot be selected.
        /// </param>
        /// <param name="onContinue">"Continue": e.g. start the run.</param>
        /// <param name="onBack">"Back": e.g. reopen level selection. Null — to the main menu.</param>
        public void Prepare(ProductUseContext context, Func<Task> onContinue, Func<Task> onBack)
        {
            // The selection was made for a different moment — it may be inapplicable in the new one.
            if (context != _context)
                _selected.Clear();

            _context = context;
            _onContinue = onContinue;
            _onBack = onBack;

            UpdateContinueButton();
            RefreshItemStates();
        }

        /// <summary>
        /// Whether the BackPack server is reachable. Called by whoever is about to open
        /// the window: without a connection there is nothing to show, and the player is better let
        /// straight through. BackPack disabled — no connection either.
        /// </summary>
        public Task<BackPackConnectionResult> CheckConnectionAsync(CancellationToken cancellationToken = default) =>
            _backPack.CheckConnectionAsync(cancellationToken);

        /// <summary>
        /// Give the window the item categorizer (it yields each item's reward keys), the product catalog
        /// (their ids are the reward keys) and a way to tell whether the run to grant them into is
        /// in progress. Called by loading.
        /// </summary>
        /// <summary>
        /// Give the window the way activated items are spent. Called by loading; without it (null)
        /// every item is deleted on the server.
        /// </summary>
        public void BindSpender(IBackPackItemSpender spender) => _spender = spender;

        public void BindRewards(IBackPackItemCategorizer categorizer, IBackPackProductCatalog catalog, Func<bool> isRunActive)
        {
            if (!ReferenceEquals(categorizer, _categorizer))
                _categories = new ConditionalWeakTable<BackPackRewardItem, IBackPackItemCategories>();

            _categorizer = categorizer;
            _catalog = catalog;
            _isRunActive = isRunActive;

            RefreshItemRewards();
            RefreshItemStates();
        }

        /// <summary>
        /// Activate the selected items. Called by whoever opened the window while the run
        /// is in progress: RIGHT AFTER it starts (before the start there is no run to check
        /// applicability against, and the start itself would reset whatever was granted).
        /// Returns how many items were activated.
        ///
        /// For each item, one at a time (a granted shield changes the applicability
        /// of the next one):
        /// <list type="number">
        /// <item>product is currently inapplicable — the item is NOT spent, it stays in the backpack;</item>
        /// <item>delete the item on the server — by instance id: by sku the server would wipe
        /// all items of that type;</item>
        /// <item>grant the product only if the server CONFIRMED deletion of exactly this
        /// instance (outcome "item_deleted" in the report, not just 200). No connection,
        /// error, timeout — nothing is granted, the item stays.</item>
        /// </list>
        /// The selection is taken from the window right away: the same item won't
        /// go to the server a second time, even if the window is opened while requests are in flight.
        /// </summary>
        public async Task<int> ActivateSelectedAsync()
        {
            if (_selected.Count == 0) return 0;

            var items = _selected.ToArray();
            _selected.Clear();
            RefreshItemStates();

            if (_categorizer == null || _catalog == null || _isRunActive == null || !_backPack.IsAvailable)
            {
                Debug.LogError(
                    $"[BackPack] {items.Length} selected items can't be activated: the window wasn't given " +
                    "the item categorizer, product catalog or run state (BindRewards), or BackPack is disabled. Items stay in the backpack.", this);
                return 0;
            }

            var activated = 0;
            var products = new List<IBackPackProduct>();

            foreach (var item in items)
            {
                if (!TryResolveProducts(item, products))
                {
                    Debug.LogWarning(
                        $"[BackPack] item '{item.Sku}': rewards [{Describe(RewardKeysOf(item))}] are not among " +
                        "the catalog products — not spending it, the item stays in the backpack.", this);
                    continue;
                }

                // Check BEFORE deleting: an inapplicable product is no reason to spend the item.
                if (!CanApplyAll(products, out var blocked))
                {
                    Debug.LogWarning(
                        $"[BackPack] item '{item.Sku}': product '{blocked.Id}' is currently inapplicable — " +
                        "not spending it, the item stays in the backpack.", this);
                    continue;
                }

                // No cancellation: an aborted DELETE may already have deleted the item on the server,
                // and then the player would be left with neither the item nor the product.
                var deleted = await SpendAsync(item);

                if (this == null) return activated;

                if (!deleted.IsDeleted)
                {
                    // "Already gone" is not our spend: the item was spent elsewhere
                    // (another tab, another device), and granting a product for it would be giving it away.
                    Debug.LogWarning(
                        deleted.Outcome == BackPackDeleteResult.OutcomeAlreadyGone
                            ? $"[BackPack] item '{item.Sku}' ({item.Id}) is already spent — not granting the product."
                            : $"[BackPack] item '{item.Sku}' ({item.Id}) was not spent ({deleted.Error}) — " +
                              "not granting the product, the item stays in the backpack.",
                        this);
                    continue;
                }

                // Definitely deleted — grant. Applicability is not rechecked: the item is already
                // spent, and the player can't be denied its product over a fraction of a second of reply time.
                if (!_isRunActive())
                {
                    Debug.LogError(
                        $"[BackPack] item '{item.Sku}' was deleted, but the run ended before " +
                        "the server replied — nowhere to grant the product.", this);
                    continue;
                }

                foreach (var product in products)
                    product.Apply();

                activated++;
                Debug.Log(
                    $"[BackPack] item '{item.Sku}' activated: spent, granted " +
                    $"[{Describe(RewardKeysOf(item))}].", this);
                ItemActivated?.Invoke(item);
            }

            return activated;
        }

        /// <summary>
        /// Spend an activated item: the game's way (<see cref="BindSpender"/>), otherwise delete it on the server.
        /// A failing spender spends nothing: an exception must not turn into a granted product.
        /// </summary>
        private async Task<BackPackDeleteResult> SpendAsync(BackPackRewardItem item)
        {
            if (_spender == null)
                return await _backPack.DeleteItemAsync(item.Id);

            try
            {
                return await _spender.SpendAsync(item, _backPack);
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
                return BackPackDeleteResult.Failed($"spender: {e.Message}");
            }
        }

        public override async Task OpenAsync()
        {
            await base.OpenAsync();

            // Don't await: the window appears immediately with the "loading" line; the list is drawn when the reply arrives.
            Forget(ReloadAsync());
        }

        public override Task CloseAsync()
        {
            CancelLoad();
            return base.CloseAsync();
        }

        /// <summary>"Back" leads to where we came from (see <see cref="Prepare"/>), not straight to the menu.</summary>
        public override async Task BackAsync()
        {
            if (_onBack == null)
            {
                await base.BackAsync();
                return;
            }

            await _onBack();
            await CloseSelfAsync();
        }

        protected override void Awake()
        {
            base.Awake();

            if (_continueButton != null)
                _continueButton.onClick.AddListener(OnContinue);

            if (_signInButton != null)
                _signInButton.onClick.AddListener(OnSignIn);

            if (_openBackPackButton != null)
                _openBackPackButton.onClick.AddListener(OnOpenBackPack);

            ResolveTemplate();
            UpdateContinueButton();
            RefreshSignInButton();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            if (_continueButton != null)
                _continueButton.onClick.RemoveListener(OnContinue);

            if (_signInButton != null)
                _signInButton.onClick.RemoveListener(OnSignIn);

            if (_openBackPackButton != null)
                _openBackPackButton.onClick.RemoveListener(OnOpenBackPack);

            CancelLoad();
            UnsubscribeAuth();
            _icons.Clear();
        }

        // =========================================================
        // LIST
        // =========================================================

        /// <summary>Reload the backpack from the server and rebuild the cards.</summary>
        private async Task ReloadAsync()
        {
            var version = ++_loadVersion;

            CancelLoad();

            ClearItems();
            RefreshSignInButton();

            // A disabled BackPack is no reason to ask the guest to sign in: even after sign-in
            // there would be nothing to show.
            if (!_backPack.IsAvailable)
            {
                if (!_warnedNoService)
                {
                    _warnedNoService = true;
                    Debug.LogWarning(
                        $"[BackPack] backpack is disabled ({_backPack}): config not filled in or BackPackTask " +
                        "didn't run — nothing to show.", this);
                }

                ShowPanel(signedIn: true);
                ShowStatus(_errorText);
                return;
            }

            // A guest is not a failure: only a signed-in player has a backpack. Instead of
            // items — the sign-in panel.
            if (!_backPack.HasXsollaId)
            {
                // The selection belonged to the player who signed out — the next one doesn't inherit it.
                _selected.Clear();

                ShowPanel(signedIn: false);
                ShowSignInMessage(GuestStatus());
                StartReadyWatch();
                return;
            }

            ShowPanel(signedIn: true);
            ShowStatus(_loadingText);

            _loadCancellation = new CancellationTokenSource();

            BackPackRewardsReply reply;

            try
            {
                reply = await _backPack.GetRewardsAsync(_loadCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                // The load was aborted on purpose (new load, closing) — nothing to report.
                return;
            }

            // While the request was in flight the window was closed, reopened or destroyed — nobody needs this reply anymore.
            if (this == null || version != _loadVersion || !IsOpen) return;

            if (!reply.Success)
            {
                // No list on screen — so nothing for the player to activate either: an unseen
                // selection sent to the server after the start would come as a surprise to them.
                _selected.Clear();

                Debug.LogWarning($"[BackPack] backpack failed to load: {reply}", this);
                ShowStatus(_errorText);
                return;
            }

            // Only items the game wants shown; the selection is kept against the same list,
            // so a hidden item can't stay selected and get activated unseen.
            var items = Shown(reply.Items);

            KeepSelectionOf(items);
            BuildItems(items);
            RefreshItemRewards();
            RefreshItemStates();
            ShowStatus(_itemViews.Count == 0 ? _emptyText : null);
        }

        /// <summary>
        /// Items that become cards — which ones is decided by the game
        /// (<see cref="IBackPackItemCategorizer.SelectShown"/>). Skipped items stay in the backpack.
        /// Without a categorizer every item is shown (none can be activated then anyway).
        /// </summary>
        private IReadOnlyList<BackPackRewardItem> Shown(IReadOnlyList<BackPackRewardItem> items)
        {
            if (_categorizer == null) return items;

            var selected = _categorizer.SelectShown(items);
            var result = new List<BackPackRewardItem>(selected?.Count ?? 0);

            if (selected != null)
            {
                foreach (var item in selected)
                {
                    if (item != null)
                        result.Add(item);
                }
            }

            if (result.Count < items.Count)
                Debug.Log($"[BackPack] {items.Count - result.Count} items skipped by the game — not shown in the backpack.", this);

            return result;
        }

        private void CancelLoad()
        {
            if (_loadCancellation == null) return;

            _loadCancellation.Cancel();
            _loadCancellation.Dispose();
            _loadCancellation = null;
        }

        private void BuildItems(IReadOnlyList<BackPackRewardItem> items)
        {
            var template = ResolveTemplate();
            if (template == null) return;

            var parent = _itemsRoot != null ? _itemsRoot : template.transform.parent;

            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(item.Sku))
                {
                    Debug.LogError($"[BackPack] item '{item.Id}' has no sku in the server reply — " +
                                   "it can't be activated and won't be added to the list.", this);
                    continue;
                }

                var view = Instantiate(template, parent);
                view.gameObject.name = item.Sku;
                view.gameObject.SetActive(true);
                view.Show(item);

                if (view.ActivateButton != null)
                    view.ActivateButton.onClick.AddListener(() => OnActivate(view));
                else
                    Debug.LogError($"[BackPack] card '{item.Sku}' has no 'activate' button — " +
                                   "the item can't be activated.", this);

                _itemViews.Add(view);
                Forget(LoadIconAsync(view, item.IconUrl));
            }
        }

        private async Task LoadIconAsync(BackPackItemView view, string url)
        {
            var sprite = await _icons.GetAsync(url);

            // The list may have been rebuilt during the download — the card is gone.
            if (view != null && sprite != null)
                view.SetIcon(sprite);
        }

        private void ClearItems()
        {
            foreach (var view in _itemViews)
            {
                if (view != null)
                    Destroy(view.gameObject);
            }

            _itemViews.Clear();
        }

        /// <summary>
        /// The card template. In the scene it is disabled: if enabled it would remain on screen
        /// as an extra card. A prefab template (not in the scene) is left alone — it's an asset.
        /// </summary>
        private BackPackItemView ResolveTemplate()
        {
            if (_itemTemplate == null)
            {
                Debug.LogError(
                    "[BackPack] the backpack has no card template (Item Template) — " +
                    "the list will stay empty.", this);
                return null;
            }

            if (_itemTemplate.gameObject.scene.IsValid() && _itemTemplate.gameObject.activeSelf)
                _itemTemplate.gameObject.SetActive(false);

            return _itemTemplate;
        }

        /// <summary>Exactly one panel on screen: items for a signed-in player, sign-in for a guest.</summary>
        private void ShowPanel(bool signedIn)
        {
            if (_itemsPanel != null)
                _itemsPanel.SetActive(signedIn);

            if (_signInPanel != null)
                _signInPanel.SetActive(!signedIn);
        }

        private void ShowSignInMessage(string text)
        {
            if (_signInMessage != null)
                _signInMessage.text = text ?? "";
        }

        /// <summary>Status line instead of the list. Empty — the line is hidden and the list is visible.</summary>
        private void ShowStatus(string text)
        {
            if (_statusLabel == null) return;

            var visible = !string.IsNullOrEmpty(text);
            _statusLabel.gameObject.SetActive(visible);

            if (visible)
                _statusLabel.text = text;
        }

        // =========================================================
        // SELECTION FOR THE RUN
        // =========================================================

        /// <summary>"Activate" — select the item for the run or deselect it.</summary>
        private void OnActivate(BackPackItemView view)
        {
            var item = view != null ? view.Item : null;
            if (item == null) return;

            var index = IndexOfSelected(item);

            if (index >= 0)
                _selected.RemoveAt(index);
            else if (StateOf(item) == BackPackItemState.Available)
                _selected.Add(item);

            RefreshItemStates();
        }

        /// <summary>Redraw all cards: selecting one changes the availability of others (product limit).</summary>
        private void RefreshItemStates()
        {
            foreach (var view in _itemViews)
            {
                if (view != null && view.Item != null)
                    view.SetState(StateOf(view.Item));
            }
        }

        /// <summary>
        /// Show on the cards what the player gets on activation. Icons and labels
        /// are known only to the product catalog, which may arrive after the list — so this is also called
        /// from <see cref="BindRewards"/>.
        /// </summary>
        private void RefreshItemRewards()
        {
            foreach (var view in _itemViews)
            {
                if (view != null && view.Item != null)
                    view.SetRewards(RewardEntries(view.Item));
            }
        }

        /// <summary>
        /// The item's rewards as rows for the card: the product's rows from the catalog
        /// (<see cref="IBackPackProduct.Rewards"/>, "+$100"). A repeated product —
        /// "x2" on each of its rows. If the effects have nothing to show — a row with the product
        /// title, so the reward doesn't silently vanish from the card. Keys the game
        /// doesn't know are skipped: there's nothing to show for them, and such an item can't
        /// be selected anyway (see <see cref="StateOf"/>).
        ///
        /// The list is shared by all cards: the card consumes it immediately without storing it.
        /// </summary>
        private IReadOnlyList<BackPackRewardEntry> RewardEntries(BackPackRewardItem item)
        {
            _rewardsScratch.Clear();

            if (_catalog == null) return _rewardsScratch;

            _productsScratch.Clear();

            foreach (var key in RewardKeysOf(item))
            {
                if (_catalog.TryGetProduct(key, out var product))
                    _productsScratch.Add(product);
            }

            for (var i = 0; i < _productsScratch.Count; i++)
            {
                var product = _productsScratch[i];

                // The repeat was already counted at the first occurrence.
                if (_productsScratch.IndexOf(product) < i) continue;

                var count = CountOf(_productsScratch, product);
                var suffix = count > 1 ? " x" + count : "";
                var added = false;

                if (product.Rewards != null)
                {
                    foreach (var line in product.Rewards)
                    {
                        if (line.Icon == null && string.IsNullOrEmpty(line.Text))
                            continue;

                        _rewardsScratch.Add(new BackPackRewardEntry(line.Icon, line.Text + suffix));
                        added = true;
                    }
                }

                if (!added)
                {
                    var title = string.IsNullOrEmpty(product.Title) ? product.Id : product.Title;
                    _rewardsScratch.Add(new BackPackRewardEntry(null, title + suffix));
                }
            }

            return _rewardsScratch;
        }

        private BackPackItemState StateOf(BackPackRewardItem item)
        {
            if (IndexOfSelected(item) >= 0)
                return BackPackItemState.Selected;

            // Without an instance id this exact item can't be spent: by sku the server
            // would delete all items of that type.
            if (string.IsNullOrEmpty(item.Id))
                return BackPackItemState.Unavailable;

            if (!TryResolveProducts(item, _productsScratch))
                return BackPackItemState.Unavailable;

            // If even one product doesn't fit this moment, the item would turn out
            // inapplicable when granted.
            foreach (var product in _productsScratch)
            {
                if (!product.IsUsableIn(_context))
                    return BackPackItemState.Unavailable;
            }

            // What's already selected plus what this item grants — no more than can be
            // held of the product at once (Max). And if a run is already in progress, no more than
            // it will accept right now. An extra item would turn out inapplicable when granted.
            var runActive = _isRunActive != null && _isRunActive();

            for (var i = 0; i < _productsScratch.Count; i++)
            {
                var product = _productsScratch[i];

                // The repeat was already counted at the first occurrence.
                if (_productsScratch.IndexOf(product) < i) continue;

                var wanted = CountSelected(product.Id) + CountOf(_productsScratch, product);
                var limit = Mathf.Max(1, product.Max);

                if (runActive)
                    limit = Mathf.Min(limit, product.RemainingUses());

                if (wanted > limit)
                    return BackPackItemState.Unavailable;
            }

            return BackPackItemState.Available;
        }

        /// <summary>
        /// The products an item grants — one per each of its reward keys.
        /// If even one key is unknown to the game — false: an item can't be granted halfway
        /// and spent in full.
        /// </summary>
        private bool TryResolveProducts(BackPackRewardItem item, List<IBackPackProduct> products)
        {
            products.Clear();

            var keys = RewardKeysOf(item);
            if (_catalog == null || keys.Count == 0)
                return false;

            foreach (var key in keys)
            {
                if (!_catalog.TryGetProduct(key, out var product))
                {
                    products.Clear();
                    return false;
                }

                products.Add(product);
            }

            return true;
        }

        private bool CanApplyAll(List<IBackPackProduct> products, out IBackPackProduct blocked)
        {
            foreach (var product in products)
            {
                if (!product.CanApply())
                {
                    blocked = product;
                    return false;
                }
            }

            blocked = null;
            return true;
        }

        /// <summary>How many times the product occurs in the list — a repeated key in one item's rewards.</summary>
        private static int CountOf(List<IBackPackProduct> products, IBackPackProduct product)
        {
            var count = 0;

            foreach (var other in products)
            {
                if (other == product)
                    count++;
            }

            return count;
        }

        /// <summary>How many times the product occurs in the rewards of already selected items.</summary>
        private int CountSelected(string productId)
        {
            var count = 0;

            foreach (var selected in _selected)
            {
                foreach (var key in RewardKeysOf(selected))
                {
                    if (string.Equals(key?.Trim(), productId, StringComparison.Ordinal))
                        count++;
                }
            }

            return count;
        }

        /// <summary>
        /// The selection survives a backpack reload: item objects arrive as new ones,
        /// so we match by instance id. Whatever is no longer in the backpack leaves the selection.
        /// </summary>
        private void KeepSelectionOf(IReadOnlyList<BackPackRewardItem> items)
        {
            for (var i = _selected.Count - 1; i >= 0; i--)
            {
                var fresh = FindSame(items, _selected[i]);

                if (fresh == null)
                    _selected.RemoveAt(i);
                else
                    _selected[i] = fresh;
            }
        }

        private int IndexOfSelected(BackPackRewardItem item)
        {
            for (var i = 0; i < _selected.Count; i++)
            {
                if (IsSame(_selected[i], item))
                    return i;
            }

            return -1;
        }

        private static BackPackRewardItem FindSame(IReadOnlyList<BackPackRewardItem> items, BackPackRewardItem wanted)
        {
            foreach (var item in items)
            {
                if (IsSame(item, wanted))
                    return item;
            }

            return null;
        }

        /// <summary>The same instance in the backpack: by id, and without an id — the same object.</summary>
        private static bool IsSame(BackPackRewardItem a, BackPackRewardItem b) =>
            ReferenceEquals(a, b) ||
            (!string.IsNullOrEmpty(a.Id) && string.Equals(a.Id, b.Id, StringComparison.Ordinal));

        private static string Describe(IReadOnlyList<string> keys) =>
            keys == null || keys.Count == 0 ? "" : string.Join(", ", keys);

        // =========================================================
        // XSOLLA ID SIGN-IN
        // =========================================================

        /// <summary>What to show the guest instead of the list.</summary>
        private string GuestStatus()
        {
            if (_auth == null || !_auth.IsAvailable)
                return _signInUnavailableText;

            // The previous session is still being restored: too early to declare the player a guest.
            if (!_auth.IsReady && !_readyTimedOut)
                return _checkingText;

            return string.IsNullOrEmpty(_signInError) ? _signedOutText : _signInError;
        }

        /// <summary>
        /// The sign-in button is visible only to a guest, when sign-in is available and the previous session
        /// is no longer being restored — otherwise the player might press "sign in" while already
        /// almost signed in.
        /// </summary>
        private void RefreshSignInButton()
        {
            var pending = _signInTask != null && !_signInTask.IsCompleted;

            if (_signInButton != null)
            {
                var visible = _auth != null && _auth.IsAvailable && !_auth.IsSignedIn &&
                              (_auth.IsReady || _readyTimedOut);

                _signInButton.gameObject.SetActive(visible);
                _signInButton.interactable = !pending;
            }

            if (_signInLabel != null)
                _signInLabel.text = pending ? _signingInText : _signInText;
        }

        /// <summary>
        /// WARNING: SignInAsync is called SYNCHRONOUSLY, straight from the click, without a single
        /// await before it — otherwise the browser treats the sign-in window as unsolicited and
        /// blocks it (as in XsollaIdAccountWidget).
        /// </summary>
        private void OnSignIn()
        {
            if (_auth == null || !_auth.IsAvailable) return;

            // The sign-in window is already open — no need for a second one.
            if (_signInTask != null && !_signInTask.IsCompleted) return;

            var task = _auth.SignInAsync();

            _signInTask = task;
            _signInError = null;
            RefreshSignInButton();

            Forget(WatchSignInAsync(task));
        }

        private async Task WatchSignInAsync(Task<XsollaIdResult> task)
        {
            XsollaIdResult result;

            // IXsollaIdAuth doesn't throw, but a task broken by something
            // external must not leave the button stuck in "signing in..." forever.
            try
            {
                result = await task;
            }
            catch (Exception exception)
            {
                result = new XsollaIdResult.Failure(XsollaIdErrorCode.Network, exception.Message);
            }

            if (this == null) return;

            if (_signInTask == task)
                _signInTask = null;

            RefreshSignInButton();

            // A successful sign-in reloads the backpack on its own — via the SignedIn event.
            if (!(result is XsollaIdResult.Failure failure)) return;

            Debug.LogWarning($"[BackPack] Xsolla ID sign-in failed — {failure.Code}: {failure.Message}", this);

            _signInError = SignInErrorText(failure.Code);

            if (IsOpen && !_backPack.HasXsollaId)
                ShowSignInMessage(GuestStatus());
        }

        /// <summary>
        /// The player closed the sign-in window themselves — not an error, nothing to complain about.
        /// The rest is indistinguishable to the player: sign-in failed, worth trying again.
        /// </summary>
        private string SignInErrorText(XsollaIdErrorCode code)
        {
            switch (code)
            {
                case XsollaIdErrorCode.Cancelled:
                case XsollaIdErrorCode.SessionExpired:
                    return null;

                case XsollaIdErrorCode.PopupBlocked:
                    return _popupBlockedText;

                default:
                    return _signInFailedText;
            }
        }

        /// <summary>
        /// The player signed in — here or in the main menu account block. The xsollaID for
        /// BackPack is already stored by this point: XsollaIdTask subscribed to this
        /// event before the window and writes it to BackPackIdentity first.
        /// </summary>
        private void OnSignedIn(XsollaIdSession session)
        {
            _signInError = null;
            RefreshSignInButton();

            if (IsOpen)
                Forget(ReloadAsync());
        }

        /// <summary>The player signed out: someone else's backpack must not stay on screen.</summary>
        private void OnSignedOut()
        {
            RefreshSignInButton();

            if (IsOpen)
                Forget(ReloadAsync());
        }

        private void UnsubscribeAuth()
        {
            if (_auth == null) return;

            _auth.SignedIn -= OnSignedIn;
            _auth.SignedOut -= OnSignedOut;
        }

        /// <summary>
        /// IsReady has no event, and a restore that had nothing to
        /// restore won't send SignedOut either — so, like the account
        /// block, we poll until the answer is final.
        /// </summary>
        private void StartReadyWatch()
        {
            if (_readyWatch != null || _readyTimedOut || !isActiveAndEnabled) return;
            if (_auth == null || !_auth.IsAvailable || _auth.IsReady) return;

            _readyWatch = StartCoroutine(WatchReady());
        }

        private IEnumerator WatchReady()
        {
            // Real time: the menu may be running while game time is frozen.
            var deadline = Time.unscaledTime + _readyTimeoutSeconds;

            while (!_auth.IsReady)
            {
                if (Time.unscaledTime >= deadline)
                {
                    _readyTimedOut = true;
                    Debug.LogWarning(
                        $"[BackPack] Xsolla ID session wasn't restored within {_readyTimeoutSeconds:0} s — " +
                        "showing the guest state.", this);
                    break;
                }

                yield return new WaitForSecondsRealtime(0.2f);
            }

            _readyWatch = null;

            RefreshSignInButton();

            if (IsOpen)
                Forget(ReloadAsync());
        }

        // =========================================================
        // BACKPACK SITE
        // =========================================================

        /// <summary>
        /// Open straight from the click: in WebGL the browser allows a new tab
        /// only as a direct response to a player gesture.
        /// </summary>
        private void OnOpenBackPack()
        {
            if (!string.IsNullOrEmpty(_backPackUrl))
                Application.OpenURL(_backPackUrl);
        }

        // =========================================================
        // CONTINUE
        // =========================================================

        /// <summary>Without a "Continue" action there's nothing to press — the window was opened without calling <see cref="Prepare"/>.</summary>
        private void UpdateContinueButton()
        {
            if (_continueButton != null)
                _continueButton.interactable = _onContinue != null;
        }

        private void OnContinue() => Forget(ContinueAsync());

        private async Task ContinueAsync()
        {
            if (_onContinue == null)
            {
                Debug.LogError(
                    "[BackPack] the backpack wasn't told what to do on 'Continue' — " +
                    "the opening window must call Prepare before OpenAsync.", this);
                return;
            }

            await _onContinue();
            await CloseSelfAsync();
        }
    }
}
