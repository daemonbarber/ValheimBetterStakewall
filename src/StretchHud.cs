using TMPro;
using UnityEngine;

namespace ValheimBetterStakewall
{
    /// <summary>
    /// On-screen stretch info, drawn with the game's own TextMeshPro font and outline material
    /// (copied from the centre message text) and parented to the HUD root, so it hides with the HUD.
    /// </summary>
    internal static class StretchHud
    {
        private const string ErrorColor = "#FF6060";
        private const string DimColor = "#C8C8C8";

        private static TextMeshProUGUI _text;
        private static string _shown;

        public static void Show(string cost, string fillMode, string error)
        {
            if (!EnsureCreated()) return;

            string text = $"{cost}\n<size=80%><color={DimColor}>Fill: {fillMode}</color></size>";
            if (!string.IsNullOrEmpty(error)) text += $"\n<color={ErrorColor}>{error}</color>";

            if (text != _shown)
            {
                _text.text = text;
                _shown = text;
            }
            if (!_text.gameObject.activeSelf) _text.gameObject.SetActive(true);
        }

        public static void Hide()
        {
            if (_text && _text.gameObject.activeSelf) _text.gameObject.SetActive(false);
        }

        public static void Destroy()
        {
            if (_text) Object.Destroy(_text.gameObject);
            _text = null;
            _shown = null;
        }

        private static bool EnsureCreated()
        {
            if (_text) return true;   // also false once the HUD that owned it is destroyed (e.g. logout)

            var hud = Hud.instance;
            var style = MessageHud.instance ? MessageHud.instance.m_messageCenterText : null;
            if (!hud || !hud.m_rootObject || !style) return false;

            var go = new GameObject("BetterStakewall_Hud", typeof(RectTransform));
            go.layer = hud.m_rootObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(hud.m_rootObject.transform, false);
            // Lower-middle of the screen, clear of the crosshair; text grows downward from the anchor.
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.36f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(1400f, 200f);

            _text = go.AddComponent<TextMeshProUGUI>();
            _text.font = style.font;
            _text.fontSharedMaterial = style.fontSharedMaterial;
            _text.color = style.color;
            _text.fontSize = 24f;
            _text.alignment = TextAlignmentOptions.Top;
            _text.richText = true;
            _text.textWrappingMode = TextWrappingModes.NoWrap;
            _text.overflowMode = TextOverflowModes.Overflow;
            _text.raycastTarget = false;
            _shown = null;
            go.SetActive(false);
            return true;
        }
    }
}
