using ArenaSurvivor.Entities;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaSurvivor.UI
{
    /// <summary>
    /// Barra de vida y estado superior para el Boss Custodius.
    /// Muestra en tiempo real la salud de la Esfera Central (1500), el Escudo de Energía (500)
    /// y el estado de los Generadores de Escudo laterales (1000 c/u).
    /// Se genera proceduralmente de forma automática al detectar el combate contra el jefe.
    /// </summary>
    public class BossHealthUI : MonoBehaviour
    {
        private static BossHealthUI _instance;
        public static BossHealthUI Instance => _instance;

        [Header("Contenedor Principal")]
        [SerializeField] private GameObject hudPanel;
        [SerializeField] private Text bossTitleText;

        [Header("Barra de Escudo de Energía (500 HP)")]
        [SerializeField] private Slider shieldSlider;
        [SerializeField] private Image shieldFillImage;
        [SerializeField] private Text shieldStatusText;

        [Header("Barra de Núcleo Central (1500 HP)")]
        [SerializeField] private Slider coreSlider;
        [SerializeField] private Image coreFillImage;
        [SerializeField] private Text coreStatusText;

        [Header("Estado de Generadores")]
        [SerializeField] private Text leftGenText;
        [SerializeField] private Text rightGenText;

        private IBossStatus _activeBoss;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            EnsureUIElements();
            SetVisible(false);
        }

        private void OnEnable()
        {
            BossEvents.OnBossSpawned += HandleBossSpawned;
            BossEvents.OnBossDefeated += HandleBossDefeated;
        }

        private void OnDisable()
        {
            BossEvents.OnBossSpawned -= HandleBossSpawned;
            BossEvents.OnBossDefeated -= HandleBossDefeated;
            if (_activeBoss != null)
            {
                _activeBoss.OnBossStatusChanged -= UpdateUI;
            }
        }

        private void HandleBossSpawned(IBossStatus boss)
        {
            if (_activeBoss != null)
            {
                _activeBoss.OnBossStatusChanged -= UpdateUI;
            }

            _activeBoss = boss;
            if (_activeBoss != null)
            {
                _activeBoss.OnBossStatusChanged += UpdateUI;
                if (bossTitleText != null) bossTitleText.text = $"⚠️ {_activeBoss.BossDisplayName} ⚠️";
            }

            SetVisible(true);
            UpdateUI();
        }

        private void HandleBossDefeated(IBossStatus boss)
        {
            if (_activeBoss == boss)
            {
                if (_activeBoss != null)
                {
                    _activeBoss.OnBossStatusChanged -= UpdateUI;
                }
                _activeBoss = null;
                SetVisible(false);
            }
        }

        private void UpdateUI()
        {
            if (_activeBoss == null) return;

            // 1. Núcleo Central
            float coreCurrent = Mathf.Max(0f, _activeBoss.CoreHealth);
            float coreMax = _activeBoss.CoreMaxHealth;
            if (coreSlider != null)
            {
                coreSlider.value = coreMax > 0f ? Mathf.Clamp01(coreCurrent / coreMax) : 0f;
            }
            if (coreStatusText != null)
            {
                coreStatusText.text = $"{_activeBoss.CoreLabel}: {Mathf.CeilToInt(coreCurrent)} / {Mathf.CeilToInt(coreMax)}";
            }

            // 2. Escudo de Energía
            float shieldCurrent = Mathf.Max(0f, _activeBoss.EnergyShieldHealth);
            float shieldMax = _activeBoss.EnergyShieldMaxHealth;
            if (shieldSlider != null)
            {
                shieldSlider.value = shieldMax > 0f ? Mathf.Clamp01(shieldCurrent / shieldMax) : 0f;
            }

            if (shieldStatusText != null)
            {
                if (_activeBoss.IsShieldActive)
                {
                    shieldStatusText.text = $"ESCUDO DE ENERGÍA: {Mathf.CeilToInt(shieldCurrent)} / {Mathf.CeilToInt(shieldMax)}";
                    if (shieldFillImage != null) shieldFillImage.color = new Color(0.15f, 0.85f, 1f, 1f);
                }
                else if (_activeBoss.IsRecharging)
                {
                    shieldStatusText.text = $"ESCUDO CAÍDO - RECARGANDO EN {_activeBoss.ShieldRechargeRemaining:F1}s";
                    if (shieldFillImage != null) shieldFillImage.color = new Color(1f, 0.75f, 0.2f, 0.8f);
                }
                else
                {
                    shieldStatusText.text = "ESCUDO DESACTIVADO PERMANENTE";
                    if (shieldFillImage != null) shieldFillImage.color = new Color(0.5f, 0.2f, 0.2f, 0.6f);
                }
            }

            // 3. Partes laterales (generadores, cañones...)
            if (leftGenText != null) leftGenText.text = _activeBoss.GetPartsStatus(true);
            if (rightGenText != null) rightGenText.text = _activeBoss.GetPartsStatus(false);
        }

        public void SetVisible(bool visible)
        {
            if (hudPanel != null) hudPanel.SetActive(visible);
        }

        private void EnsureUIElements()
        {
            if (hudPanel != null) return;

            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null) return;

            hudPanel = new GameObject("BossHUD_Panel");
            hudPanel.transform.SetParent(canvas.transform, false);

            RectTransform panelRect = hudPanel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 1f);
            panelRect.anchorMax = new Vector2(0.5f, 1f);
            panelRect.pivot = new Vector2(0.5f, 1f);
            panelRect.anchoredPosition = new Vector2(0f, -20f);
            panelRect.sizeDelta = new Vector2(620f, 95f);

            var bg = hudPanel.AddComponent<Image>();
            bg.color = new Color(0.04f, 0.05f, 0.08f, 0.88f);

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

            // Título
            GameObject titleGo = new GameObject("BossTitle");
            titleGo.transform.SetParent(hudPanel.transform, false);
            RectTransform tRect = titleGo.AddComponent<RectTransform>();
            tRect.anchorMin = new Vector2(0.5f, 1f);
            tRect.anchorMax = new Vector2(0.5f, 1f);
            tRect.pivot = new Vector2(0.5f, 1f);
            tRect.anchoredPosition = new Vector2(0f, -6f);
            tRect.sizeDelta = new Vector2(580f, 24f);

            bossTitleText = titleGo.AddComponent<Text>();
            bossTitleText.font = font;
            bossTitleText.fontSize = 17;
            bossTitleText.fontStyle = FontStyle.Bold;
            bossTitleText.alignment = TextAnchor.MiddleCenter;
            bossTitleText.color = new Color(1f, 0.3f, 0.35f);
            bossTitleText.text = "⚠️ CUSTODIUS ⚠️";

            // Barra Escudo
            GameObject sGo = new GameObject("ShieldSlider");
            sGo.transform.SetParent(hudPanel.transform, false);
            RectTransform sRect = sGo.AddComponent<RectTransform>();
            sRect.anchorMin = new Vector2(0.5f, 1f);
            sRect.anchorMax = new Vector2(0.5f, 1f);
            sRect.pivot = new Vector2(0.5f, 1f);
            sRect.anchoredPosition = new Vector2(0f, -32f);
            sRect.sizeDelta = new Vector2(580f, 14f);

            shieldSlider = sGo.AddComponent<Slider>();
            var sBg = sGo.AddComponent<Image>();
            sBg.color = new Color(0.1f, 0.15f, 0.22f, 0.8f);

            GameObject sFillArea = new GameObject("FillArea");
            sFillArea.transform.SetParent(sGo.transform, false);
            RectTransform sFaRect = sFillArea.AddComponent<RectTransform>();
            sFaRect.anchorMin = Vector2.zero;
            sFaRect.anchorMax = Vector2.one;
            sFaRect.sizeDelta = Vector2.zero;

            GameObject sFill = new GameObject("Fill");
            sFill.transform.SetParent(sFillArea.transform, false);
            RectTransform sfRect = sFill.AddComponent<RectTransform>();
            sfRect.anchorMin = Vector2.zero;
            sfRect.anchorMax = Vector2.one;
            sfRect.sizeDelta = Vector2.zero;
            shieldFillImage = sFill.AddComponent<Image>();
            shieldFillImage.color = new Color(0.15f, 0.85f, 1f);
            shieldSlider.fillRect = sfRect;

            GameObject sTxtGo = new GameObject("ShieldText");
            sTxtGo.transform.SetParent(sGo.transform, false);
            RectTransform stRect = sTxtGo.AddComponent<RectTransform>();
            stRect.anchorMin = Vector2.zero;
            stRect.anchorMax = Vector2.one;
            stRect.sizeDelta = Vector2.zero;
            shieldStatusText = sTxtGo.AddComponent<Text>();
            shieldStatusText.font = font;
            shieldStatusText.fontSize = 11;
            shieldStatusText.fontStyle = FontStyle.Bold;
            shieldStatusText.alignment = TextAnchor.MiddleCenter;
            shieldStatusText.color = Color.white;

            // Barra Núcleo Central
            GameObject cGo = new GameObject("CoreSlider");
            cGo.transform.SetParent(hudPanel.transform, false);
            RectTransform cRect = cGo.AddComponent<RectTransform>();
            cRect.anchorMin = new Vector2(0.5f, 1f);
            cRect.anchorMax = new Vector2(0.5f, 1f);
            cRect.pivot = new Vector2(0.5f, 1f);
            cRect.anchoredPosition = new Vector2(0f, -50f);
            cRect.sizeDelta = new Vector2(580f, 18f);

            coreSlider = cGo.AddComponent<Slider>();
            var cBg = cGo.AddComponent<Image>();
            cBg.color = new Color(0.18f, 0.08f, 0.1f, 0.8f);

            GameObject cFillArea = new GameObject("FillArea");
            cFillArea.transform.SetParent(cGo.transform, false);
            RectTransform cFaRect = cFillArea.AddComponent<RectTransform>();
            cFaRect.anchorMin = Vector2.zero;
            cFaRect.anchorMax = Vector2.one;
            cFaRect.sizeDelta = Vector2.zero;

            GameObject cFill = new GameObject("Fill");
            cFill.transform.SetParent(cFillArea.transform, false);
            RectTransform cfRect = cFill.AddComponent<RectTransform>();
            cfRect.anchorMin = Vector2.zero;
            cfRect.anchorMax = Vector2.one;
            cfRect.sizeDelta = Vector2.zero;
            coreFillImage = cFill.AddComponent<Image>();
            coreFillImage.color = new Color(0.95f, 0.2f, 0.35f);
            coreSlider.fillRect = cfRect;

            GameObject cTxtGo = new GameObject("CoreText");
            cTxtGo.transform.SetParent(cGo.transform, false);
            RectTransform ctRect = cTxtGo.AddComponent<RectTransform>();
            ctRect.anchorMin = Vector2.zero;
            ctRect.anchorMax = Vector2.one;
            ctRect.sizeDelta = Vector2.zero;
            coreStatusText = cTxtGo.AddComponent<Text>();
            coreStatusText.font = font;
            coreStatusText.fontSize = 12;
            coreStatusText.fontStyle = FontStyle.Bold;
            coreStatusText.alignment = TextAnchor.MiddleCenter;
            coreStatusText.color = Color.white;

            // Textos Generadores Izquierda / Derecha
            GameObject lgGo = new GameObject("LeftGenText");
            lgGo.transform.SetParent(hudPanel.transform, false);
            RectTransform lgRect = lgGo.AddComponent<RectTransform>();
            lgRect.anchorMin = new Vector2(0f, 0f);
            lgRect.anchorMax = new Vector2(0.5f, 0f);
            lgRect.pivot = new Vector2(0f, 0f);
            lgRect.anchoredPosition = new Vector2(20f, 6f);
            lgRect.sizeDelta = new Vector2(260f, 18f);
            leftGenText = lgGo.AddComponent<Text>();
            leftGenText.font = font;
            leftGenText.fontSize = 11;
            leftGenText.alignment = TextAnchor.MiddleLeft;
            leftGenText.supportRichText = true;
            leftGenText.horizontalOverflow = HorizontalWrapMode.Overflow;

            GameObject rgGo = new GameObject("RightGenText");
            rgGo.transform.SetParent(hudPanel.transform, false);
            RectTransform rgRect = rgGo.AddComponent<RectTransform>();
            rgRect.anchorMin = new Vector2(0.5f, 0f);
            rgRect.anchorMax = new Vector2(1f, 0f);
            rgRect.pivot = new Vector2(1f, 0f);
            rgRect.anchoredPosition = new Vector2(-20f, 6f);
            rgRect.sizeDelta = new Vector2(260f, 18f);
            rightGenText = rgGo.AddComponent<Text>();
            rightGenText.font = font;
            rightGenText.fontSize = 11;
            rightGenText.alignment = TextAnchor.MiddleRight;
            rightGenText.supportRichText = true;
            rightGenText.horizontalOverflow = HorizontalWrapMode.Overflow;

            hudPanel.SetActive(false);
        }

        public static void EnsureExists()
        {
            if (Instance == null)
            {
                Canvas canvas = FindFirstObjectByType<Canvas>();
                if (canvas != null)
                {
                    GameObject go = new GameObject("BossHealthUI");
                    go.transform.SetParent(canvas.transform, false);
                    go.AddComponent<BossHealthUI>();
                }
            }
        }
    }
}
