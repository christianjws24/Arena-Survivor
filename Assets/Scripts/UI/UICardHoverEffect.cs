using System.Collections;
using ArenaSurvivor.Managers;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ArenaSurvivor.UI
{
    /// <summary>
    /// Efecto de Game Feel para tarjetas de interfaz (uGUI).
    /// Detecta eventos de cursor PointerEnter / PointerExit para reproducir sonido de Hover,
    /// realizar un micro-zoom (escala 1.0 a 1.04) con interpolación Ease-Out en tiempo real (unscaled).
    /// </summary>
    public class UICardHoverEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        [SerializeField] private float hoverScale = 1.04f;
        [SerializeField] private float animationDuration = 0.15f;

        private RectTransform _rectTransform;
        private Vector3 _originalScale = Vector3.one;
        private Coroutine _scaleCoroutine;

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            if (_rectTransform != null)
            {
                _originalScale = _rectTransform.localScale;
            }
        }

        private void OnDisable()
        {
            if (_rectTransform != null)
            {
                _rectTransform.localScale = _originalScale;
            }
            if (_scaleCoroutine != null)
            {
                StopCoroutine(_scaleCoroutine);
                _scaleCoroutine = null;
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            TriggerFocusFeedback();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            TriggerUnfocusFeedback();
        }

        public void OnSelect(BaseEventData eventData)
        {
            TriggerFocusFeedback();
        }

        public void OnDeselect(BaseEventData eventData)
        {
            TriggerUnfocusFeedback();
        }

        private void TriggerFocusFeedback()
        {
            // Reproducir sonido de hover
            AudioManager.Instance?.PlayUIHover();

            // Zoom suave Ease-Out
            AnimateScale(_originalScale * hoverScale);
        }

        private void TriggerUnfocusFeedback()
        {
            // Volver a escala base
            AnimateScale(_originalScale);
        }

        private void AnimateScale(Vector3 targetScale)
        {
            if (!gameObject.activeInHierarchy || _rectTransform == null) return;

            if (_scaleCoroutine != null) StopCoroutine(_scaleCoroutine);
            _scaleCoroutine = StartCoroutine(ScaleRoutine(targetScale));
        }

        private IEnumerator ScaleRoutine(Vector3 targetScale)
        {
            Vector3 startScale = _rectTransform.localScale;
            float elapsed = 0f;

            while (elapsed < animationDuration)
            {
                // Uso estricto de unscaledDeltaTime para funcionar con Time.timeScale = 0
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / animationDuration);
                // Curva Ease-Out cuadrática
                float easeOut = 1f - (1f - t) * (1f - t);

                _rectTransform.localScale = Vector3.Lerp(startScale, targetScale, easeOut);
                yield return null;
            }

            _rectTransform.localScale = targetScale;
            _scaleCoroutine = null;
        }
    }
}
