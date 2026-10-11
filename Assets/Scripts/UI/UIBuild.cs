using UnityEngine;
using UnityEngine.UI;

namespace ArenaSurvivor.UI
{
    /// <summary>
    /// Helpers mínimos para construir UI procedural (uGUI) con el mismo estilo que el resto del HUD.
    /// </summary>
    public static class UIBuild
    {
        private static Font _font;

        public static Font DefaultFont
        {
            get
            {
                if (_font == null)
                {
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
                return _font;
            }
        }

        public static Canvas FindHudCanvas()
        {
            Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            Canvas best = null;
            foreach (Canvas c in canvases)
            {
                if (!c.isRootCanvas) continue;
                if (best == null || c.renderMode == RenderMode.ScreenSpaceOverlay) best = c;
            }
            return best;
        }

        public static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = size;
            return rect;
        }

        public static RectTransform Stretch(string name, Transform parent)
        {
            RectTransform rect = CreateRect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            return rect;
        }

        public static Image AddImage(GameObject go, Color color, bool raycast = false)
        {
            Image img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        public static Text AddText(GameObject go, string text, int size, Color color, TextAnchor align, FontStyle style = FontStyle.Bold)
        {
            Text t = go.AddComponent<Text>();
            t.font = DefaultFont;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.raycastTarget = false;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }
    }
}
