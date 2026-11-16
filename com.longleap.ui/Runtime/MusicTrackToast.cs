using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LongLeap.UI
{
    /// <summary>
    /// A passive copy of the menu's music panel, shown briefly when the track changes or a run
    /// starts. Where it may appear is the game's call: <c>isOnScreen</c> (e.g. intro or a run,
    /// but not the main menu, which shows the track itself) and <c>inRun</c>.
    /// </summary>
    public sealed class MusicTrackToast : MonoBehaviour
    {
        private RectTransform _rect;
        private CanvasGroup _group;
        private TMP_Text _text;
        private IMusicInfo _music;
        private Func<string, string> _format;
        private Func<bool> _isOnScreen;
        private Func<bool> _inRun;
        private Vector2 _shown;
        private float _started = float.NegativeInfinity;
        private bool _wasInSession;
        private const float SlideSeconds = .25f;
        private const float HoldSeconds = 2f;

        public static void Create(RectTransform source, TMP_Text sourceText,
            IMusicInfo music, Func<string, string> format, Func<bool> isOnScreen, Func<bool> inRun)
        {
            var canvas = source.GetComponentInParent<Canvas>(true);
            if (canvas == null) return;
            var parent = canvas.rootCanvas.transform;
            if (parent.GetComponentInChildren<MusicTrackToast>(true) != null) return;

            // Clone only the leaf panel; never move a window or clone the UI root.
            var copy = Instantiate(source.gameObject, parent, false);
            copy.name = "Music track toast";
            // Keep animated geometry out of the HUD canvas rebuilds.
            var toastCanvas = copy.GetComponent<Canvas>();
            if (toastCanvas == null) toastCanvas = copy.AddComponent<Canvas>();
            toastCanvas.overrideSorting = true;
            toastCanvas.sortingOrder = canvas.rootCanvas.sortingOrder + 10;
            var rect = (RectTransform)copy.transform;
            rect.anchorMin = rect.anchorMax = Vector2.one;
            rect.anchoredPosition = source.anchoredPosition;
            rect.localScale = source.localScale;
            var group = copy.GetComponent<CanvasGroup>();
            if (group == null) group = copy.AddComponent<CanvasGroup>();
            group.alpha = 0;
            group.blocksRaycasts = false;
            group.interactable = false;
            foreach (var graphic in copy.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
            foreach (var button in copy.GetComponentsInChildren<Button>(true))
                button.enabled = false;

            TMP_Text text = null;
            foreach (var candidate in copy.GetComponentsInChildren<TMP_Text>(true))
                if (candidate.name == sourceText.name) { text = candidate; break; }
            if (text == null) { Destroy(copy); return; }
            var toast = copy.AddComponent<MusicTrackToast>();
            toast._rect = rect;
            toast._shown = source.anchoredPosition;
            toast._group = group;
            toast._text = text;
            toast._music = music;
            toast._format = format;
            toast._isOnScreen = isOnScreen;
            toast._inRun = inRun;
            music.Changed += toast.OnMusicChanged;
            copy.SetActive(true);
        }

        private void OnMusicChanged()
        {
            if (!_music.HasMusic) { _started = float.NegativeInfinity; return; }
            if (IsOnScreen) Show();
        }

        private void Show()
        {
            if (!_music.HasMusic) return;
            _text.text = _format(_music.CurrentTrackName);
            _started = Time.unscaledTime;
        }

        private void Update()
        {
            if (_music == null || _rect == null || _group == null) return;
            bool session = InRun;
            // Show once when a run starts, not only on track changes.
            if (session && !_wasInSession) Show();
            _wasInSession = session;
            bool visibleScreen = IsOnScreen;
            if (!visibleScreen) _started = float.NegativeInfinity;
            float elapsed = Time.unscaledTime - _started;
            float progress = elapsed < SlideSeconds ? Mathf.Clamp01(elapsed / SlideSeconds) :
                elapsed < SlideSeconds + HoldSeconds ? 1f :
                1f - Mathf.Clamp01((elapsed - SlideSeconds - HoldSeconds) / SlideSeconds);
            _group.alpha = visibleScreen && progress > 0 ? 1f : 0f;
            float hiddenX = _rect.rect.width * _rect.pivot.x + 20f;
            _rect.anchoredPosition = new Vector2(
                Mathf.Lerp(hiddenX, _shown.x, Mathf.SmoothStep(0f, 1f, progress)), _shown.y);
        }

        private bool InRun => _inRun != null && _inRun();

        private bool IsOnScreen => _isOnScreen != null && _isOnScreen();

        private void OnDestroy()
        {
            if (_music != null) _music.Changed -= OnMusicChanged;
        }
    }
}
