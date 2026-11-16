using System.Collections;
using LongLeap.DailyQuests;
using TMPro;
using UnityEngine;

namespace LongLeap.UI
{
    public sealed class DailyQuestRewardFlyout : MonoBehaviour
    {
        private TMP_Text label;

        public static void Play(Transform source, QuestDefinition quest, TMP_FontAsset font, IUiSounds sounds)
        {
            string title = null;
            Color color = Color.white;
            long cash = 0;
            foreach (var reward in quest.Rewards ?? System.Array.Empty<QuestReward>())
            {
                if (reward == null) continue;
                if (reward.Key.Value == "soft" && reward.Amount > 0)
                    cash += reward.Amount;
                if (reward.Key.Value == "unlimited_shield_seconds")
                {
                    title = "UNLIMITED SHIELD";
                    color = new Color32(133, 249, 252, 255);
                }
                else if (reward.Key.Value == "score_boost_seconds")
                {
                    title = "x2 BOOST";
                    color = new Color32(217, 249, 52, 255);
                }
            }
            if (title == null && cash > 0)
            {
                title = $"+${cash} CASH";
                color = new Color32(57, 255, 72, 255);
            }
            var canvas = source.GetComponentInParent<Canvas>();
            if (title == null || canvas == null) return;
            sounds?.Play(UiSound.RewardFlyout);
            var obj = new GameObject("QuestRewardFlyout", typeof(RectTransform));
            obj.layer = canvas.gameObject.layer;
            obj.transform.SetParent(canvas.rootCanvas.transform, false);
            obj.transform.SetAsLastSibling();
            var effect = obj.AddComponent<DailyQuestRewardFlyout>();
            effect.label = obj.AddComponent<TextMeshProUGUI>();
            effect.label.font = font != null ? font : TMP_Settings.defaultFontAsset;
            effect.label.fontStyle = FontStyles.Normal;
            effect.label.fontSize = 54f;
            effect.label.alignment = TextAlignmentOptions.Center;
            effect.label.color = color;
            effect.label.text = title;
            effect.label.raycastTarget = false;
            effect.label.textWrappingMode = TextWrappingModes.NoWrap;
            var rect = effect.label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(900f, 100f);
            rect.anchoredPosition = Vector2.zero;
            effect.StartCoroutine(effect.Animate());
        }

        private IEnumerator Animate()
        {
            var rect = label.rectTransform;
            var parent = (RectTransform)rect.parent;
            var start = rect.anchoredPosition;
            var target = new Vector2(0f, parent.rect.height * 0.5f - 60f);
            Color color = label.color;
            float elapsed = 0f;
            while (elapsed < 1.15f)
            {
                elapsed += Time.unscaledDeltaTime;
                float pop = Mathf.Clamp01(elapsed / 0.18f);
                float overshoot = 1f + 2.70158f * Mathf.Pow(pop - 1f, 3f) + 1.70158f * Mathf.Pow(pop - 1f, 2f);
                float flight = Mathf.Clamp01((elapsed - 0.45f) / 0.7f);
                float eased = flight * flight;
                rect.localScale = Vector3.one * Mathf.LerpUnclamped(0.55f, 1.2f, overshoot) * Mathf.Lerp(1f, 0.4f, eased);
                rect.anchoredPosition = Vector2.Lerp(start, target, eased) + new Vector2(Mathf.Sin(flight * Mathf.PI) * 45f, 0f);
                color.a = 1f - Mathf.Clamp01((flight - 0.65f) / 0.35f);
                label.color = color;
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
