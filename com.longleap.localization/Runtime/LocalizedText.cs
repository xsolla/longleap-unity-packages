using UnityEngine;
using TMPro;

namespace LongLeap.Localization
{
    [DisallowMultipleComponent]
    public class LocalizedText : MonoBehaviour
    {
        [Header("Key")]
        [SerializeField] private LocKey key;

        private TMP_Text _tmpText;
        private UnityEngine.UI.Text _uiText;
        private object[] _args;

        void OnEnable()
        {
            _tmpText = GetComponent<TMP_Text>();
            _uiText = _tmpText == null ? GetComponent<UnityEngine.UI.Text>() : null;

            Loc.OnLanguageChanged += Apply;
            Apply();
        }

        void OnDisable()
        {
            Loc.OnLanguageChanged -= Apply;
        }

        public void SetKey(string newKey)
        {
            key = newKey;
            Apply();
        }

        public void SetFormatArgs(params object[] args)
        {
            _args = args;
            Apply();
        }

        void Apply()
        {
            if (key.IsEmpty)
                return;

            // Локализация ещё не поднялась (или прямо сейчас меняет язык): Resolve
            // отдал бы сам ключ, и в кадре повисло бы «menu_play». Оставляем то,
            // что стоит в префабе, и ждём события — оно придёт и на Loc.Init, и на
            // конец загрузки таблиц, а мы на него подписаны с OnEnable. Проспать
            // его нельзя: включённые позже компоненты применяют перевод сами, в
            // своём OnEnable, когда готовность уже true.
            if (!Loc.IsReady)
                return;

            string result = _args == null || _args.Length == 0
                ? Loc.Resolve(key)
                : Loc.ResolveFormat(key, _args);

            if (_tmpText != null)
                _tmpText.text = result;
            else if (_uiText != null)
                _uiText.text = result;
        }
    }
}
