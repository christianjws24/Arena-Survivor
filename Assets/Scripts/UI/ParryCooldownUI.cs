using ArenaSurvivor.Entities;
using ArenaSurvivor.Upgrades;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaSurvivor.UI
{
    /// <summary>
    /// UI reactiva y estética para monitorear el estado, disponibilidad y enfriamiento (cooldown) del Parry.
    /// Muestra barra de progreso, tiempo restante en segundos y la tasa de recuperación actual
    /// afectada por el Power-Up 'Reflejos Cinéticos' (rapid_parry).
    /// </summary>
    public class ParryCooldownUI : MonoBehaviour
    {
        public static ParryCooldownUI Instance { get; private set; }

        [Header("Referencias del Jugador")]
        [SerializeField] private PlayerController player;

        [Header("Seguimiento del Jugador")]
        [Tooltip("Si es true, la barra se ubica dinámicamente debajo del jugador en tiempo real.")]
        [SerializeField] private bool followPlayer = true;
        [Tooltip("Desplazamiento relativo en el espacio del mundo respecto a la posición del jugador.")]
        [SerializeField] private Vector3 worldOffset = new Vector3(0f, -0.75f, 0f);

        [Header("Componentes de UI")]
        [SerializeField] private Slider parrySlider;
        [SerializeField] private Image fillImage;
        [SerializeField] private Text statusText;
        [SerializeField] private Text cadenceText;

        [Header("Paleta de Colores de Estado")]
        [SerializeField] private Color readyColor = new Color(0.18f, 0.95f, 1f, 1f);      // Cian eléctrico vibrante
        [SerializeField] private Color activeColor = new Color(1f, 0.92f, 0.2f, 1f);     // Oro / amarillo activo
        [SerializeField] private Color cooldownColor = new Color(1f, 0.35f, 0.35f, 1f);  // Naranja / rojo recuperación
        [SerializeField] private Color cooldownBgColor = new Color(0.2f, 0.2f, 0.28f, 0.7f);

        private RectTransform _panelRect;
        private Canvas _parentCanvas;
        private RectTransform _canvasRect;
        private Camera _mainCam;
        private CanvasGroup _canvasGroup;

        /// <summary>
        /// Garantiza la existencia de la barra de Cooldown del Parry en runtime (Auto-reparación).
        /// Se ubica de forma compacta y sigue dinámicamente al jugador.
        /// </summary>
        public static ParryCooldownUI EnsureExists()
        {
            if (Instance != null) return Instance;

#if UNITY_2023_1_OR_NEWER
            var existing = Object.FindFirstObjectByType<ParryCooldownUI>();
#else
            var existing = Object.FindObjectOfType<ParryCooldownUI>();
#endif
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }

#if UNITY_2023_1_OR_NEWER
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
#else
            Canvas canvas = Object.FindObjectOfType<Canvas>();
#endif
            if (canvas == null) return null;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            // Panel contenedor centrado, compacto para HUD de combate bajo el jugador
            GameObject panelGo = new GameObject("ParryCooldownPanel");
            panelGo.transform.SetParent(canvas.transform, false);
            RectTransform rect = panelGo.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(68f, 10f);

            var cg = panelGo.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;

            var bg = panelGo.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.05f, 0.10f, 0.85f);
            bg.raycastTarget = false;

            // Slider
            GameObject sliderGo = new GameObject("ParrySlider");
            sliderGo.transform.SetParent(panelGo.transform, false);
            RectTransform sliderRect = sliderGo.AddComponent<RectTransform>();
            sliderRect.anchorMin = Vector2.zero;
            sliderRect.anchorMax = Vector2.one;
            sliderRect.pivot = new Vector2(0.5f, 0.5f);
            sliderRect.anchoredPosition = Vector2.zero;
            sliderRect.sizeDelta = new Vector2(-4f, -4f);

            var slider = sliderGo.AddComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.interactable = false;
            slider.transition = Selectable.Transition.None;
            var nav = slider.navigation;
            nav.mode = Navigation.Mode.None;
            slider.navigation = nav;

            var sliderBg = sliderGo.AddComponent<Image>();
            sliderBg.color = new Color(0.18f, 0.18f, 0.25f, 0.6f);
            sliderBg.raycastTarget = false;

            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderGo.transform, false);
            RectTransform fillAreaRect = fillArea.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = Vector2.zero;
            fillAreaRect.anchorMax = Vector2.one;
            fillAreaRect.sizeDelta = Vector2.zero;

            GameObject fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            RectTransform fillRect = fill.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.sizeDelta = Vector2.zero;
            var fillImg = fill.AddComponent<Image>();
            fillImg.color = new Color(0.18f, 0.95f, 1f, 1f);
            fillImg.raycastTarget = false;
            slider.fillRect = fillRect;

            // Status Text (Encima de la barra)
            GameObject stGo = new GameObject("StatusText");
            stGo.transform.SetParent(panelGo.transform, false);
            RectTransform stRect = stGo.AddComponent<RectTransform>();
            stRect.anchorMin = new Vector2(0.5f, 1f);
            stRect.anchorMax = new Vector2(0.5f, 1f);
            stRect.pivot = new Vector2(0.5f, 0f);
            stRect.anchoredPosition = new Vector2(0f, 2f);
            stRect.sizeDelta = new Vector2(80f, 14f);
            var stTxt = stGo.AddComponent<Text>();
            stTxt.alignment = TextAnchor.MiddleCenter;
            stTxt.fontSize = 10;
            stTxt.fontStyle = FontStyle.Bold;
            stTxt.font = font;
            stTxt.color = Color.white;
            stTxt.text = "⚡ LISTO";
            stTxt.raycastTarget = false;

            var comp = panelGo.AddComponent<ParryCooldownUI>();
            comp.parrySlider = slider;
            comp.fillImage = fillImg;
            comp.statusText = stTxt;
            comp._canvasGroup = cg;
            comp.ConfigureSlider();

            Instance = comp;
            return comp;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _panelRect = GetComponent<RectTransform>();
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.interactable = false;
            }

            _parentCanvas = GetComponentInParent<Canvas>();
            if (_parentCanvas != null)
            {
                _canvasRect = _parentCanvas.GetComponent<RectTransform>();
            }

            if (followPlayer && _panelRect != null)
            {
                // Asegurar anclajes centrados para posicionamiento suave
                _panelRect.anchorMin = new Vector2(0.5f, 0.5f);
                _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
                _panelRect.pivot = new Vector2(0.5f, 0.5f);

                // Si tenía dimensiones viejas grandes del HUD anterior, ajustar a compacto
                if (_panelRect.sizeDelta.x > 150f)
                {
                    _panelRect.sizeDelta = new Vector2(68f, 10f);
                }
            }

            FindPlayerIfNeeded();
            ConfigureSlider();
        }

        private void ConfigureSlider()
        {
            if (parrySlider != null)
            {
                parrySlider.minValue = 0f;
                parrySlider.maxValue = 1f;
                parrySlider.interactable = false;
                parrySlider.transition = Selectable.Transition.None;
                var nav = parrySlider.navigation;
                nav.mode = Navigation.Mode.None;
                parrySlider.navigation = nav;
            }
        }

        private void OnEnable()
        {
            FindPlayerIfNeeded();
            if (player != null)
            {
                player.OnParryStateChanged += HandleParryStateChanged;
            }
        }

        private void OnDisable()
        {
            if (player != null)
            {
                player.OnParryStateChanged -= HandleParryStateChanged;
            }
        }

        private void FindPlayerIfNeeded()
        {
            if (player == null)
            {
                player = PlayerController.Instance;
                if (player == null)
                {
#if UNITY_2023_1_OR_NEWER
                    player = Object.FindFirstObjectByType<PlayerController>();
#else
                    player = Object.FindObjectOfType<PlayerController>();
#endif
                }
            }
        }

        private void HandleParryStateChanged(ParryState state)
        {
            UpdateDisplay();
        }

        private void Update()
        {
            FindPlayerIfNeeded();
            if (player == null) return;

            UpdateDisplay();
        }

        private void LateUpdate()
        {
            UpdatePosition();
        }

        private void UpdatePosition()
        {
            if (!followPlayer) return;

            FindPlayerIfNeeded();
            if (player == null || player.IsDead)
            {
                if (_canvasGroup != null) _canvasGroup.alpha = 0f;
                return;
            }

            if (_mainCam == null)
            {
                _mainCam = Camera.main;
                if (_mainCam == null) return;
            }

            Vector3 worldPos = player.transform.position + worldOffset;
            Vector3 screenPos = _mainCam.WorldToScreenPoint(worldPos);

            // Si el objeto está detrás de la cámara, ocultar
            if (screenPos.z < 0f)
            {
                if (_canvasGroup != null) _canvasGroup.alpha = 0f;
                return;
            }

            if (_canvasGroup != null) _canvasGroup.alpha = 1f;

            if (_panelRect == null) _panelRect = GetComponent<RectTransform>();
            if (_parentCanvas == null) _parentCanvas = GetComponentInParent<Canvas>();
            if (_canvasRect == null && _parentCanvas != null) _canvasRect = _parentCanvas.GetComponent<RectTransform>();

            if (_parentCanvas != null && _canvasRect != null)
            {
                Camera uiCam = _parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _parentCanvas.worldCamera;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenPos, uiCam, out Vector2 localPoint))
                {
                    _panelRect.anchoredPosition = localPoint;
                }
            }
            else if (_panelRect != null)
            {
                _panelRect.position = screenPos;
            }
        }

        private void UpdateDisplay()
        {
            if (player == null) return;

            ParryState state = player.CurrentParryState;
            float remaining = player.ParryCooldownRemaining;
            float normalized = player.ParryCooldownNormalized;

            int rapidLevel = UpgradeManager.Instance != null ? UpgradeManager.Instance.GetLevel("rapid_parry") : 0;
            float reductionPercent = rapidLevel * 20f;
            float effectiveSuccessCD = player.EffectiveSuccessCooldown;

            // Actualizar slider
            if (parrySlider != null)
            {
                parrySlider.value = normalized;
            }

            // Actualizar badge de cadencia si existe
            if (cadenceText != null)
            {
                if (rapidLevel > 0)
                {
                    cadenceText.text = $"<color=#FFE600>CD: {effectiveSuccessCD:F2}s</color> <color=#A0FFA0>(-{reductionPercent:0}%)</color>";
                }
                else
                {
                    cadenceText.text = $"<color=#B0D4F0>CD: {effectiveSuccessCD:F2}s</color>";
                }
            }

            // Estados visuales y textos dinámicos
            switch (state)
            {
                case ParryState.Active:
                    if (fillImage != null) fillImage.color = activeColor;
                    if (parrySlider != null) parrySlider.value = 1f;
                    if (statusText != null)
                    {
                        statusText.text = "★ PARRY ★";
                        statusText.color = activeColor;
                    }
                    break;

                case ParryState.Cooldown:
                    if (fillImage != null)
                    {
                        // Gradiente suave de recuperación
                        fillImage.color = Color.Lerp(cooldownColor, readyColor, normalized * 0.7f);
                    }

                    if (statusText != null)
                    {
                        statusText.text = $"⏳ {remaining:F1}s";
                        statusText.color = new Color(1f, 0.85f, 0.7f);
                    }
                    break;

                case ParryState.Ready:
                default:
                    // Efecto de pulso suave cuando está listo para usar
                    if (fillImage != null)
                    {
                        float pingPong = Mathf.PingPong(Time.unscaledTime * 3.5f, 0.2f);
                        fillImage.color = new Color(readyColor.r, readyColor.g, readyColor.b, 0.8f + pingPong);
                    }

                    if (statusText != null)
                    {
                        statusText.text = "⚡ LISTO";
                        statusText.color = readyColor;
                    }
                    break;
            }
        }
    }
}
