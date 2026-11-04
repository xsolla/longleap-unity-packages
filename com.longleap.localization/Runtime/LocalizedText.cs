using UnityEngine;
using TMPro;

namespace LongLeap.Localization
{
    /// <summary>
    /// Puts the translation of <see cref="Key"/> into the <see cref="TMP_Text"/>
    /// (preferred) or <see cref="UnityEngine.UI.Text"/> on the same GameObject and
    /// refreshes it when the locale changes.
    /// </summary>
    /// <remarks>
    /// While <see cref="Loc.IsReady"/> is false the text is left as authored in the
    /// prefab. An empty key set in the Inspector also leaves it untouched.
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("LongLeap/Localization/Localized Text")]
    public class LocalizedText : MonoBehaviour
    {
        [SerializeField] private LocKey key;

        private TMP_Text _tmpText;
        private UnityEngine.UI.Text _uiText;
        private object[] _args;
        private bool _reportedNoTarget;

        /// <summary>The localization key currently shown.</summary>
        public LocKey Key => key;

        void OnEnable()
        {
            _tmpText = GetComponent<TMP_Text>();
            _uiText = _tmpText == null ? GetComponent<UnityEngine.UI.Text>() : null;

            if (_tmpText == null && _uiText == null && !_reportedNoTarget)
            {
                _reportedNoTarget = true;
                Debug.LogWarning($"[Localization] LocalizedText on '{name}' has no " +
                    "TMP_Text or UI.Text component to write to.", this);
            }

            Loc.OnLanguageChanged += Apply;
            Apply();
        }

        void OnDisable()
        {
            Loc.OnLanguageChanged -= Apply;
        }

        /// <summary>
        /// Sets a new key and refreshes the text. A null or empty key clears the text.
        /// </summary>
        public void SetKey(string newKey)
        {
            key = newKey;

            if (key.IsEmpty)
            {
                SetText(string.Empty);
                return;
            }

            Apply();
        }

        /// <summary>
        /// Sets <see cref="string.Format(string, object[])"/> arguments for the
        /// translation and refreshes the text. No arguments disables formatting.
        /// </summary>
        public void SetFormatArgs(params object[] args)
        {
            _args = args;
            Apply();
        }

        void Apply()
        {
            if (key.IsEmpty)
                return;

            // Not ready (boot has not reached localization, or a locale switch is
            // loading): Resolve would return the raw key. Keep the prefab text and
            // wait for OnLanguageChanged, which fires on Loc.Init and when loading
            // finishes. Components enabled later apply in OnEnable, when ready.
            if (!Loc.IsReady)
                return;

            string result = _args == null || _args.Length == 0
                ? Loc.Resolve(key)
                : Loc.ResolveFormat(key, _args);

            SetText(result);
        }

        void SetText(string text)
        {
            if (_tmpText != null)
                _tmpText.text = text;
            else if (_uiText != null)
                _uiText.text = text;
        }
    }
}
