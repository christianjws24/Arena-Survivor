using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaSurvivor.UI
{
    /// <summary>
    /// Cartel grande centrado para anunciar etapas, alertas de jefe y "Etapa completada".
    /// Usa tiempo no escalado para verse bien aunque el juego esté en HitStop o pausa breve.
    /// </summary>
    public class StageBannerUI : MonoBehaviour
    {
        private static StageBannerUI _instance;

        private CanvasGroup _group;
        private Text _title;
        private Text _subtitle;
        private Image _band;
        private Coroutine _routine;

        public static void Show(string title, string subtitle, Color color, float duration = 2.6f)
        {
            StageBannerUI banner = EnsureExists();
            if (banner == null) return;
            if (banner._routine != null) banner.StopCoroutine(banner._routine);
            banner._routine = banner.StartCoroutine(banner.ShowRoutine(title, subtitle, color, duration));
        }

        private static StageBannerUI EnsureExists()
        {
            if (_instance != null) return _instance;

            Canvas canvas = UIBuild.FindHudCanvas();
            if (canvas == null) return null;

            RectTransform root = UIBuild.CreateRect("StageBanner", canvas.transform,
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(0f, 120f));
            _instance = root.gameObject.AddComponent<StageBannerUI>();
            _instance.Build(root);
            return _instance;
        }

        private void Build(RectTransform root)
        {
            _group = root.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            _band = UIBuild.AddImage(root.gameObject, new Color(0f, 0f, 0f, 0.55f));

            RectTransform titleRect = UIBuild.CreateRect("Title", root, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 18f), new Vector2(0f, 60f));
            _title = UIBuild.AddText(titleRect.gameObject, "", 46, Color.white, TextAnchor.MiddleCenter);

            RectTransform subRect = UIBuild.CreateRect("Subtitle", root, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -32f), new Vector2(0f, 30f));
            _subtitle = UIBuild.AddText(subRect.gameObject, "", 20, new Color(0.85f, 0.9f, 1f), TextAnchor.MiddleCenter, FontStyle.Normal);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private IEnumerator ShowRoutine(string title, string subtitle, Color color, float duration)
        {
            _title.text = title;
            _title.color = color;
            _subtitle.text = subtitle;
            _band.color = new Color(color.r * 0.15f, color.g * 0.15f, color.b * 0.15f, 0.7f);

            const float fade = 0.3f;
            float t = 0f;
            while (t < fade)
            {
                t += Time.unscaledDeltaTime;
                _group.alpha = t / fade;
                transform.localScale = new Vector3(1f, Mathf.Lerp(0.3f, 1f, t / fade), 1f);
                yield return null;
            }
            _group.alpha = 1f;
            transform.localScale = Vector3.one;

            float hold = Mathf.Max(0f, duration - (fade * 2f));
            while (hold > 0f)
            {
                hold -= Time.unscaledDeltaTime;
                yield return null;
            }

            t = 0f;
            while (t < fade)
            {
                t += Time.unscaledDeltaTime;
                _group.alpha = 1f - (t / fade);
                yield return null;
            }
            _group.alpha = 0f;
            _routine = null;
        }
    }
}
