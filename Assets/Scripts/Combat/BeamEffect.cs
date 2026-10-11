using System.Collections.Generic;
using UnityEngine;

namespace ArenaSurvivor.Combat
{
    /// <summary>
    /// Rayo/línea visual reutilizable (Rail Gun del jugador, Rail Gun del Mothership y su línea de aviso).
    /// Solo es visual: el daño lo resuelve quien dispara. Se recicla en un pool interno sin Instantiate en gameplay.
    /// </summary>
    public class BeamEffect : MonoBehaviour
    {
        private static readonly Stack<BeamEffect> Pool = new Stack<BeamEffect>();

        private SpriteRenderer _core;
        private SpriteRenderer _glow;
        private Color _color;
        private float _duration;
        private float _elapsed;
        private bool _blink;
        private float _baseWidth;

        /// <summary>
        /// Muestra un rayo desde 'origin' en 'direction'.
        /// blink = true lo convierte en una línea de aviso parpadeante (telegrafiado).
        /// </summary>
        public static BeamEffect Show(Vector2 origin, Vector2 direction, float length, float width, Color color, float duration, bool blink = false)
        {
            BeamEffect beam = null;
            while (Pool.Count > 0 && beam == null)
            {
                beam = Pool.Pop();
            }

            if (beam == null)
            {
                GameObject go = new GameObject("BeamEffect");
                beam = go.AddComponent<BeamEffect>();
                beam.Build();
            }

            beam.gameObject.SetActive(true);
            beam.Setup(origin, direction, length, width, color, duration, blink);
            return beam;
        }

        // Pixel art: 3 franjas escalonadas (halo translúcido, color pleno, núcleo blanco) con anchos en píxeles enteros
        // (30 px por unidad, como las naves) y bordes nítidos
        private const float PixelsPerUnit = 30f;
        private const float CoreWidthRatio = 0.35f;
        private const float PixelMidRatio = 0.6f;
        private const float PixelCoreRatio = 0.28f;
        private bool _pixelArt;
        private SpriteRenderer _mid;

        private void Build()
        {
            Sprite pixel = FxSprites.Pixel;
            _pixelArt = pixel != null;
            Sprite sprite = _pixelArt ? pixel : ProceduralSprites.Square;

            _glow = gameObject.AddComponent<SpriteRenderer>();
            _glow.sprite = sprite;
            _glow.sortingOrder = 7;

            if (_pixelArt) _mid = CreateBand("Mid", sprite, 8);
            _core = CreateBand("Core", sprite, 9);
            _core.transform.localScale = new Vector3(1f, CoreWidthRatio, 1f);
        }

        private SpriteRenderer CreateBand(string name, Sprite sprite, int order)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>
        /// Aplica el ancho del rayo. En pixel art cada franja mide un número entero de píxeles (mínimo 1) y queda centrada,
        /// así al adelgazarse el rayo "salta" de píxel en píxel en vez de difuminarse.
        /// </summary>
        private void SetWidth(float width)
        {
            if (!_pixelArt)
            {
                transform.localScale = new Vector3(transform.localScale.x, width, 1f);
                return;
            }

            float glowPx = Mathf.Max(1f, Mathf.Round(width * PixelsPerUnit));
            transform.localScale = new Vector3(transform.localScale.x, glowPx / PixelsPerUnit, 1f);
            SetBandPixels(_mid, glowPx, PixelMidRatio);
            SetBandPixels(_core, glowPx, PixelCoreRatio);
        }

        private static void SetBandPixels(SpriteRenderer band, float glowPx, float ratio)
        {
            float px = Mathf.Max(1f, Mathf.Round(glowPx * ratio));
            if ((glowPx - px) % 2f != 0f) px = Mathf.Max(1f, px - 1f); // mismo margen a ambos lados
            band.transform.localScale = new Vector3(1f, px / glowPx, 1f);
        }

        private void Setup(Vector2 origin, Vector2 direction, float length, float width, Color color, float duration, bool blink)
        {
            if (direction.sqrMagnitude < 0.0001f) direction = Vector2.right;
            direction.Normalize();

            _color = color;
            _duration = Mathf.Max(0.01f, duration);
            _elapsed = 0f;
            _blink = blink;
            _baseWidth = width;

            transform.position = origin + (direction * (length * 0.5f));
            transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
            transform.localScale = new Vector3(length, width, 1f);
            SetWidth(width);
            ApplyAlpha(1f);
        }

        /// <summary>
        /// Reorienta un rayo activo (la línea de aviso sigue al jugador hasta fijarse).
        /// </summary>
        public void Retarget(Vector2 origin, Vector2 direction, float length)
        {
            if (direction.sqrMagnitude < 0.0001f) return;
            direction.Normalize();
            transform.position = origin + (direction * (length * 0.5f));
            transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
            transform.localScale = new Vector3(length, transform.localScale.y, 1f);
        }

        public void Hide()
        {
            if (!gameObject.activeSelf) return;
            gameObject.SetActive(false);
            Pool.Push(this);
        }

        private void ApplyAlpha(float alpha)
        {
            float glowAlpha = _pixelArt ? 0.35f : 0.45f;
            if (_glow != null) _glow.color = new Color(_color.r, _color.g, _color.b, _color.a * glowAlpha * alpha);
            if (_mid != null) _mid.color = new Color(_color.r, _color.g, _color.b, _color.a * alpha);
            if (_core != null) _core.color = new Color(1f, 1f, 1f, alpha);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_elapsed / _duration);

            if (_blink)
            {
                float blinkSpeed = Mathf.Lerp(6f, 22f, t);
                ApplyAlpha(0.35f + (Mathf.PingPong(_elapsed * blinkSpeed, 1f) * 0.65f));
            }
            else
            {
                // El rayo se adelgaza y desvanece
                SetWidth(Mathf.Lerp(_baseWidth, _baseWidth * 0.2f, t));
                ApplyAlpha(1f - t);
            }

            if (_elapsed >= _duration)
            {
                Hide();
            }
        }

        private void OnDestroy()
        {
            // Si la escena se descarga, evitar referencias muertas en el pool estático
            Pool.Clear();
        }
    }
}
