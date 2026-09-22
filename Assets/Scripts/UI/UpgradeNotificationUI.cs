using System.Collections;
using ArenaSurvivor.Upgrades;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaSurvivor.UI
{
    /// <summary>
    /// UI provisional y reactiva que muestra un banner/notificación con el nombre,
    /// nivel y efecto de la mejora seleccionada por el jugador.
    /// Se suscribe al evento OnUpgradeAdded de UpgradeManager.
    /// Utiliza CanvasGroup.alpha para mostrar/ocultar sin desactivar el GameObject,
    /// garantizando que nunca se pierda la suscripción a eventos posteriores.
    /// </summary>
    public class UpgradeNotificationUI : MonoBehaviour
    {
        [Header("Referencias de UI")]
        [Tooltip("CanvasGroup para animación suave de opacidad")]
        [SerializeField] private CanvasGroup bannerCanvasGroup;

        [Tooltip("Título o encabezado de la notificación")]
        [SerializeField] private Text headerText;

        [Tooltip("Texto con el nombre y nivel de la mejora")]
        [SerializeField] private Text upgradeTitleText;

        [Tooltip("Texto descriptivo del efecto")]
        [SerializeField] private Text descriptionText;

        [Tooltip("Texto persistente que muestra la última mejora activa")]
        [SerializeField] private Text persistentStatusText;

        [Header("Configuración")]
        [SerializeField] private float displayDuration = 4.0f;
        [SerializeField] private float fadeDuration = 0.35f;

        private Coroutine _displayCoroutine;
        private bool _isSubscribed = false;

        private void Awake()
        {
            if (bannerCanvasGroup == null)
            {
                bannerCanvasGroup = GetComponent<CanvasGroup>();
                if (bannerCanvasGroup == null)
                {
                    bannerCanvasGroup = gameObject.AddComponent<CanvasGroup>();
                }
            }

            // Ocultar banner inicialmente mediante alpha sin desactivar el GameObject
            if (bannerCanvasGroup != null)
            {
                bannerCanvasGroup.alpha = 0f;
                bannerCanvasGroup.interactable = false;
                bannerCanvasGroup.blocksRaycasts = false;
            }

            if (persistentStatusText != null)
            {
                persistentStatusText.text = "MEJORAS: Ninguna activa todavía";
            }
        }

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void Start()
        {
            TrySubscribe();
        }

        private void Update()
        {
            // Salvaguarda: reintentar suscripción si UpgradeManager inicializó después
            if (!_isSubscribed)
            {
                TrySubscribe();
            }
        }

        private void OnDisable()
        {
            if (_isSubscribed && UpgradeManager.Instance != null)
            {
                UpgradeManager.Instance.OnUpgradeAdded -= HandleUpgradeAdded;
                _isSubscribed = false;
            }
        }

        private void TrySubscribe()
        {
            if (_isSubscribed) return;

            if (UpgradeManager.Instance != null)
            {
                UpgradeManager.Instance.OnUpgradeAdded += HandleUpgradeAdded;
                _isSubscribed = true;
            }
        }

        private void HandleUpgradeAdded(UpgradeData upgradeData, int newLevel)
        {
            if (upgradeData == null) return;

            string levelInfo = upgradeData.maxLevel > 1 
                ? $" (NIVEL {newLevel}/{upgradeData.maxLevel})" 
                : " (DESBLOQUEADA)";

            // 1. Actualizar texto de estado persistente (siempre visible en la esquina)
            if (persistentStatusText != null)
            {
                persistentStatusText.text = $"ÚLTIMA MEJORA: <color=cyan>{upgradeData.upgradeName}</color>{levelInfo}";
            }

            // 2. Poblar datos en el banner emergente
            if (headerText != null)
            {
                headerText.text = "⚡ ¡NUEVA MEJORA SELECCIONADA! ⚡";
            }

            if (upgradeTitleText != null)
            {
                upgradeTitleText.text = $"{upgradeData.upgradeName}{levelInfo}";
                upgradeTitleText.color = upgradeData.themeColor;
            }

            if (descriptionText != null)
            {
                descriptionText.text = upgradeData.description;
            }

            // 3. Mostrar banner con animación suave (reinicia la rutina si ya había una en curso)
            if (_displayCoroutine != null)
            {
                StopCoroutine(_displayCoroutine);
            }
            _displayCoroutine = StartCoroutine(ShowBannerRoutine());
        }

        private IEnumerator ShowBannerRoutine()
        {
            if (bannerCanvasGroup == null) yield break;

            bannerCanvasGroup.interactable = false;
            bannerCanvasGroup.blocksRaycasts = false;

            // Fade In progresivo (usando unscaledDeltaTime por si hay HitStop activo)
            float t = 0f;
            float currentAlpha = bannerCanvasGroup.alpha;
            while (t < fadeDuration)
            {
                t += Time.unscaledDeltaTime;
                bannerCanvasGroup.alpha = Mathf.Lerp(currentAlpha, 1f, t / fadeDuration);
                yield return null;
            }
            bannerCanvasGroup.alpha = 1f;

            // Tiempo visible en pantalla
            float timer = 0f;
            while (timer < displayDuration)
            {
                timer += Time.unscaledDeltaTime;
                yield return null;
            }

            // Fade Out progresivo
            t = 0f;
            while (t < fadeDuration)
            {
                t += Time.unscaledDeltaTime;
                bannerCanvasGroup.alpha = Mathf.Lerp(1f, 0f, t / fadeDuration);
                yield return null;
            }
            bannerCanvasGroup.alpha = 0f;

            // IMPORTANTE: Nunca llamar SetActive(false) en el GameObject para no desactivar
            // el script ni perder la suscripción a las siguientes mejoras.
            _displayCoroutine = null;
        }
    }
}
