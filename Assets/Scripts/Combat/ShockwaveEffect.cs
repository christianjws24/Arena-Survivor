using System.Collections;
using ArenaSurvivor.Pool;
using UnityEngine;

namespace ArenaSurvivor.Combat
{
    /// <summary>
    /// Efecto visual de Onda Expansiva (Shockwave) generado en un Parry exitoso.
    /// Crece masivamente y se desvanece usando Time.unscaledDeltaTime para ejecutarse a velocidad
    /// normal a pesar del Hit-Stop (Time.timeScale = 0.1f). Compatible con Object Pooling.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class ShockwaveEffect : MonoBehaviour, IPoolable
    {
        [Header("Configuración de la Onda")]
        [Tooltip("Escala inicial al aparecer")]
        [SerializeField] private float startScale = 0.8f;

        [Tooltip("Escala máxima alcanzada al expandirse")]
        [SerializeField] private float targetScale = 4.5f;

        [Tooltip("Duración en segundos reales de la animación")]
        [SerializeField] private float duration = 0.3f;

        [Tooltip("Color base de la onda")]
        [SerializeField] private Color waveColor = Color.white;

        private SpriteRenderer _spriteRenderer;
        private Coroutine _expandCoroutine;
        private bool _isReturning;

        // Pixel art: fotogramas que miden exactamente el diámetro de su aro (de 1.6 a 9 unidades). Cada frame se elige el
        // fotograma de tamaño más cercano y se escala al diámetro exacto de ese instante: expansión continua sin estirar los píxeles.
        private Sprite[] _frames;
        private float _baseDiameter = 2f; // diámetro del sprite del prefab (Ring.png) con escala 1

        private void Awake()
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
            if (_spriteRenderer != null && _spriteRenderer.sprite != null) _baseDiameter = _spriteRenderer.sprite.bounds.size.x;
            _frames = FxSprites.Shockwave;
        }

        /// <summary>Ease-out cúbico: la onda sale disparada del impacto y frena al abrirse.</summary>
        private static float EaseOut(float t) => 1f - ((1f - t) * (1f - t) * (1f - t));

        private void ApplyExpansion(float t)
        {
            float diameter = Mathf.Lerp(startScale, targetScale, EaseOut(t)) * _baseDiameter;
            if (_frames == null)
            {
                transform.localScale = Vector3.one * (diameter / _baseDiameter);
                return;
            }

            // Los fotogramas están repartidos por diámetro: se elige el de tamaño más parecido al actual
            float first = _frames[0].bounds.size.x;
            float last = _frames[_frames.Length - 1].bounds.size.x;
            float k = Mathf.InverseLerp(first, last, diameter) * (_frames.Length - 1);
            Sprite frame = _frames[Mathf.Clamp(Mathf.RoundToInt(k), 0, _frames.Length - 1)];
            _spriteRenderer.sprite = frame;
            transform.localScale = Vector3.one * (diameter / Mathf.Max(0.01f, frame.bounds.size.x));
        }

        public void OnSpawnFromPool()
        {
            _isReturning = false;
            if (_spriteRenderer == null) _spriteRenderer = GetComponent<SpriteRenderer>();

            ApplyExpansion(0f);
            _spriteRenderer.color = waveColor;

            if (_expandCoroutine != null) StopCoroutine(_expandCoroutine);
            _expandCoroutine = StartCoroutine(ExpandAndFadeRoutine());
        }

        public void OnReturnToPool()
        {
            _isReturning = true;
            if (_expandCoroutine != null)
            {
                StopCoroutine(_expandCoroutine);
                _expandCoroutine = null;
            }
        }

        private IEnumerator ExpandAndFadeRoutine()
        {
            float elapsed = 0f;

            while (elapsed < duration)
            {
                // CRÍTICO: Usar Time.unscaledDeltaTime para ignorar la ralentización del Hit-Stop
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                ApplyExpansion(t);

                // Se mantiene nítida mientras se abre y se desvanece al final (1 - t²)
                float alpha = 1f - (t * t);
                _spriteRenderer.color = new Color(waveColor.r, waveColor.g, waveColor.b, alpha);

                yield return null;
            }

            ReturnToPool();
        }

        private void ReturnToPool()
        {
            if (_isReturning) return;
            _isReturning = true;

            if (ObjectPooler.Instance != null)
            {
                ObjectPooler.Instance.ReturnToPool(gameObject);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }
    }
}
