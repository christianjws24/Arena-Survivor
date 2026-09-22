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

        private void Awake()
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
        }

        public void OnSpawnFromPool()
        {
            _isReturning = false;
            if (_spriteRenderer == null) _spriteRenderer = GetComponent<SpriteRenderer>();

            transform.localScale = Vector3.one * startScale;
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

                // Expansión no lineal (Ease Out Quad para sensación de impacto explosivo)
                float scaleT = 1f - Mathf.Pow(1f - t, 2f);
                float currentScale = Mathf.Lerp(startScale, targetScale, scaleT);
                transform.localScale = Vector3.one * currentScale;

                // Desvanecimiento progresivo del Alpha (1 a 0)
                float alpha = Mathf.Lerp(1f, 0f, t);
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
