using System;
using System.Collections;
using System.Collections.Generic;
using ArenaSurvivor.Upgrades;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaSurvivor.UI
{
    /// <summary>
    /// Gestor del Modal de Selección de Mejoras (Level Up Screen) e Inventario Activo.
    /// Congela el juego (Time.timeScale = 0), muestra 2 opciones aleatorias sin duplicados ni máximos,
    /// lista el inventario actual a la derecha, y al elegir, reanuda el tiempo (Time.timeScale = 1)
    /// y notifica a la ExtractionZone para detonar su explosión.
    /// </summary>
    [DisallowMultipleComponent]
    public class LevelUpUIManager : MonoBehaviour
    {
        private static LevelUpUIManager _instance;
        public static LevelUpUIManager Instance
        {
            get
            {
                if (_instance == null)
                {
#if UNITY_2023_1_OR_NEWER
                    _instance = UnityEngine.Object.FindFirstObjectByType<LevelUpUIManager>(FindObjectsInactive.Include);
#else
                    _instance = UnityEngine.Object.FindObjectOfType<LevelUpUIManager>(true);
#endif
                }
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Contenedor Principal del Modal")]
        [SerializeField] private GameObject modalPanel;
        [SerializeField] private CanvasGroup modalCanvasGroup;

        [Header("Opción 1")]
        [SerializeField] private Button option1Button;
        [SerializeField] private Text option1TitleText;
        [SerializeField] private Text option1LevelText;
        [SerializeField] private Text option1TypeText;
        [SerializeField] private Text option1DescText;
        [SerializeField] private Image option1CardImage;

        [Header("Opción 2")]
        [SerializeField] private Button option2Button;
        [SerializeField] private Text option2TitleText;
        [SerializeField] private Text option2LevelText;
        [SerializeField] private Text option2TypeText;
        [SerializeField] private Text option2DescText;
        [SerializeField] private Image option2CardImage;

        [Header("Opción 3")]
        [SerializeField] private Button option3Button;
        [SerializeField] private Text option3TitleText;
        [SerializeField] private Text option3LevelText;
        [SerializeField] private Text option3TypeText;
        [SerializeField] private Text option3DescText;
        [SerializeField] private Image option3CardImage;

        [Header("Panel de Inventario Activo (Lateral Derecho)")]
        [Tooltip("Transform contenedor con VerticalLayoutGroup donde se listan las mejoras actuales")]
        [SerializeField] private Transform inventoryContainer;
        [Tooltip("Plantilla o Prefab del item de inventario")]
        [SerializeField] private GameObject inventoryItemTemplate;

        [Header("Panel de Advertencia de Reemplazo (Exclusión)")]
        [SerializeField] private GameObject replacementWarningPanel;
        [SerializeField] private Text replacementWarningText;
        [SerializeField] private Button acceptReplacementButton;
        [SerializeField] private Button cancelReplacementButton;

        private Action _onFinishedCallback;
        private bool _isModalOpen;
        private UpgradeData _currentOption1;
        private UpgradeData _currentOption2;
        private UpgradeData _currentOption3;
        private UpgradeData _pendingUpgrade;
        private UpgradeData _conflictingUpgrade;
        private readonly List<GameObject> _spawnedInventoryItems = new List<GameObject>();

        public bool IsModalOpen => _isModalOpen;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

            if (modalCanvasGroup == null && modalPanel != null)
            {
                modalCanvasGroup = modalPanel.GetComponent<CanvasGroup>();
                if (modalCanvasGroup == null)
                {
                    modalCanvasGroup = modalPanel.AddComponent<CanvasGroup>();
                }
            }

            // Asegurar que la opción 3 exista y esté enlazada (incluso en escenas antiguas de 2 cartas)
            EnsureOption3CardSetup();

            // Asegurar EventSystem, GraphicRaycaster y configuraciones de raycast
            EnsureEventSystem();
            EnsureGraphicRaycaster();
            EnsureCardRaycasts();

            // Vincular efectos de hover en tarjetas
            if (option1Button != null && !option1Button.TryGetComponent<UICardHoverEffect>(out _))
            {
                option1Button.gameObject.AddComponent<UICardHoverEffect>();
            }
            if (option2Button != null && !option2Button.TryGetComponent<UICardHoverEffect>(out _))
            {
                option2Button.gameObject.AddComponent<UICardHoverEffect>();
            }
            if (option3Button != null && !option3Button.TryGetComponent<UICardHoverEffect>(out _))
            {
                option3Button.gameObject.AddComponent<UICardHoverEffect>();
            }

            // Ocultar modal al inicio mediante alpha sin romper la instancia
            HideModalInstant();

            // Vincular listeners a los botones
            if (option1Button != null)
            {
                option1Button.onClick.RemoveAllListeners();
                option1Button.onClick.AddListener(() => SelectUpgrade(_currentOption1));
            }

            if (option2Button != null)
            {
                option2Button.onClick.RemoveAllListeners();
                option2Button.onClick.AddListener(() => SelectUpgrade(_currentOption2));
            }

            if (option3Button != null)
            {
                option3Button.onClick.RemoveAllListeners();
                option3Button.onClick.AddListener(() => SelectUpgrade(_currentOption3));
            }

            if (inventoryItemTemplate != null)
            {
                inventoryItemTemplate.SetActive(false);
            }

            // Construir panel de advertencia de reemplazo si no existe en la escena
            EnsureReplacementWarningUI();
        }

        private void Update()
        {
            // Atajo de teclado directo si el modal está visible y no hay advertencia de reemplazo abierta
            if (modalCanvasGroup != null && modalCanvasGroup.alpha > 0.5f && (replacementWarningPanel == null || !replacementWarningPanel.activeSelf))
            {
#if ENABLE_INPUT_SYSTEM
                if (UnityEngine.InputSystem.Keyboard.current != null)
                {
                    var kb = UnityEngine.InputSystem.Keyboard.current;
                    if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame)
                    {
                        SelectUpgrade(_currentOption1);
                    }
                    else if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame)
                    {
                        if (_currentOption2 != null) SelectUpgrade(_currentOption2);
                    }
                    else if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame)
                    {
                        if (_currentOption3 != null) SelectUpgrade(_currentOption3);
                    }
                }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
                if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
                {
                    SelectUpgrade(_currentOption1);
                }
                else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
                {
                    if (_currentOption2 != null) SelectUpgrade(_currentOption2);
                }
                else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
                {
                    if (_currentOption3 != null) SelectUpgrade(_currentOption3);
                }
#endif
            }
        }

        /// <summary>
        /// Abre el modal de selección de mejoras, congela el tiempo del juego y renderiza las opciones.
        /// </summary>
        /// <param name="onFinishedCallback">Callback invocado al seleccionar una mejora para reanudar la ExtractionZone.</param>
        public void OpenLevelUpModal(Action onFinishedCallback)
        {
            if (modalPanel != null && !modalPanel.activeSelf) modalPanel.SetActive(true);
            if (!gameObject.activeSelf) gameObject.SetActive(true);

            // Asegurar que la vista de opciones principales esté activa y la advertencia oculta
            RestoreMainOptionsView();

            // Garantizar que la tarjeta 3 exista y esté enlazada (auto-reparación en runtime)
            EnsureOption3CardSetup();

            // Asegurar EventSystem activo para responder a clicks del ratón
            EnsureEventSystem();
            EnsureGraphicRaycaster();
            EnsureCardRaycasts();

            // Asegurar cursor visible y libre para hacer clic
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            // Re-vincular botones por si no estaban activos en Awake
            if (option1Button != null)
            {
                option1Button.interactable = true;
                option1Button.onClick.RemoveAllListeners();
                option1Button.onClick.AddListener(() => SelectUpgrade(_currentOption1));
            }

            if (option2Button != null)
            {
                option2Button.interactable = true;
                option2Button.onClick.RemoveAllListeners();
                option2Button.onClick.AddListener(() => SelectUpgrade(_currentOption2));
            }

            if (option3Button != null)
            {
                option3Button.interactable = true;
                option3Button.onClick.RemoveAllListeners();
                option3Button.onClick.AddListener(() => SelectUpgrade(_currentOption3));
            }

            _onFinishedCallback = onFinishedCallback;
            _isModalOpen = true;

            // Cancelar y purgar inmediatamente cualquier HitStop activo para que no sobreescriba Time.timeScale en tiempo real
            if (Combat.HitStopManager.Instance != null)
            {
                Combat.HitStopManager.Instance.CancelHitStop();
            }

            // Detener sacudida de cámara activa para evitar temblores residuales durante la lectura
            if (Combat.CameraShake.Instance != null)
            {
                Combat.CameraShake.Instance.StopShake();
            }

            // 1. Pausa estricta del juego
            Time.timeScale = 0f;

            // 2. Obtener 3 opciones aleatorias válidas (sin duplicados y sin mejoras maximizadas)
            List<UpgradeData> options = UpgradeManager.Instance != null 
                ? UpgradeManager.Instance.GetRandomUpgrades(3) 
                : new List<UpgradeData>();

            if (options.Count == 0)
            {
                Debug.Log("<color=yellow>[LevelUpUI] No hay mejoras disponibles para subir de nivel.</color>");
                CloseAndResume();
                return;
            }

            _currentOption1 = options[0];
            _currentOption2 = options.Count > 1 ? options[1] : null;
            _currentOption3 = options.Count > 2 ? options[2] : null;

            // 3. Poblar Tarjeta 1
            PopulateCard(_currentOption1, option1Button, option1TitleText, option1LevelText, option1TypeText, option1DescText, option1CardImage);

            // 4. Poblar Tarjeta 2
            if (_currentOption2 != null)
            {
                if (option2Button != null) option2Button.gameObject.SetActive(true);
                PopulateCard(_currentOption2, option2Button, option2TitleText, option2LevelText, option2TypeText, option2DescText, option2CardImage);
            }
            else
            {
                if (option2Button != null) option2Button.gameObject.SetActive(false);
            }

            // 5. Poblar Tarjeta 3
            if (_currentOption3 != null)
            {
                if (option3Button != null) option3Button.gameObject.SetActive(true);
                PopulateCard(_currentOption3, option3Button, option3TitleText, option3LevelText, option3TypeText, option3DescText, option3CardImage);
            }
            else
            {
                if (option3Button != null) option3Button.gameObject.SetActive(false);
            }

            // 6. Renderizar inventario de mejoras equipadas
            RenderInventory();

            // Configurar navegación horizontal explícita entre tarjetas
            if (option1Button != null)
            {
                Navigation nav1 = new Navigation();
                if (option2Button != null && _currentOption2 != null)
                {
                    nav1.mode = Navigation.Mode.Explicit;
                    nav1.selectOnRight = option2Button;
                    nav1.selectOnLeft = null;
                }
                else
                {
                    nav1.mode = Navigation.Mode.None;
                }
                option1Button.navigation = nav1;
            }

            if (option2Button != null)
            {
                Navigation nav2 = new Navigation();
                if (_currentOption2 != null)
                {
                    nav2.mode = Navigation.Mode.Explicit;
                    nav2.selectOnLeft = option1Button;
                    nav2.selectOnRight = (_currentOption3 != null && option3Button != null) ? option3Button : null;
                }
                else
                {
                    nav2.mode = Navigation.Mode.None;
                }
                option2Button.navigation = nav2;
            }

            if (option3Button != null)
            {
                Navigation nav3 = new Navigation();
                if (_currentOption3 != null && option2Button != null)
                {
                    nav3.mode = Navigation.Mode.Explicit;
                    nav3.selectOnLeft = option2Button;
                    nav3.selectOnRight = null;
                }
                else
                {
                    nav3.mode = Navigation.Mode.None;
                }
                option3Button.navigation = nav3;
            }

            // Enfocar inmediatamente la primera opción en el EventSystem para navegación con teclado/mando
            if (UnityEngine.EventSystems.EventSystem.current != null && option1Button != null)
            {
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(option1Button.gameObject);
            }

            // 6. Mostrar modal
            ShowModal();
        }

        private void PopulateCard(UpgradeData data, Button btn, Text title, Text levelTxt, Text typeTxt, Text desc, Image cardImg)
        {
            if (data == null) return;

            int currentLevel = UpgradeManager.Instance != null ? UpgradeManager.Instance.GetLevel(data.id) : 0;
            int nextLevel = currentLevel + 1;

            if (title != null)
            {
                title.text = data.upgradeName;
                title.color = data.themeColor;
            }

            if (levelTxt != null)
            {
                if (data.maxLevel == 1)
                {
                    levelTxt.text = "★ NUEVO COMPORTAMIENTO ★";
                }
                else
                {
                    levelTxt.text = $"NIVEL {nextLevel} / {data.maxLevel}";
                }
            }

            if (typeTxt != null)
            {
                typeTxt.text = $"[{data.upgradeType.ToString().ToUpper()}]";
                typeTxt.color = new Color(data.themeColor.r, data.themeColor.g, data.themeColor.b, 0.85f);
            }

            if (desc != null)
            {
                desc.text = data.GetDescriptionForLevel(nextLevel);
            }

            if (cardImg != null)
            {
                cardImg.color = new Color(data.themeColor.r * 0.18f, data.themeColor.g * 0.18f, data.themeColor.b * 0.18f, 0.95f);
            }
        }

        /// <summary>
        /// Renderiza las mejoras que el jugador ya tiene en su inventario activo.
        /// </summary>
        private void RenderInventory()
        {
            if (inventoryContainer == null) return;

            // Limpiar items previos generados
            foreach (var item in _spawnedInventoryItems)
            {
                if (item != null) Destroy(item);
            }
            _spawnedInventoryItems.Clear();

            if (UpgradeManager.Instance == null) return;

            var activeUpgrades = UpgradeManager.Instance.ActiveUpgrades;
            if (activeUpgrades.Count == 0)
            {
                CreateInventoryItemText("Sin mejoras previas aún", Color.gray);
                return;
            }

            foreach (var kvp in activeUpgrades)
            {
                string id = kvp.Key;
                int level = kvp.Value;
                if (level <= 0) continue;

                if (UpgradeManager.Instance.TryGetUpgradeData(id, out UpgradeData data))
                {
                    string text = $"{data.upgradeName}  •  Lvl {level}/{data.maxLevel}";
                    CreateInventoryItem(text, data.themeColor);
                }
                else
                {
                    CreateInventoryItem($"{id} - Lvl {level}", Color.white);
                }
            }
        }

        private void CreateInventoryItem(string text, Color color)
        {
            if (inventoryItemTemplate != null)
            {
                GameObject itemGo = Instantiate(inventoryItemTemplate, inventoryContainer);
                itemGo.SetActive(true);
                Text txt = itemGo.GetComponentInChildren<Text>();
                if (txt != null)
                {
                    txt.text = text;
                    txt.color = color;
                }
                _spawnedInventoryItems.Add(itemGo);
            }
            else
            {
                CreateInventoryItemText(text, color);
            }
        }

        private void CreateInventoryItemText(string text, Color color)
        {
            GameObject go = new GameObject("InventoryItem");
            go.transform.SetParent(inventoryContainer, false);
            Text txt = go.AddComponent<Text>();
            txt.text = text;
            txt.fontSize = 14;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            txt.color = color;
            txt.alignment = TextAnchor.MiddleLeft;
            _spawnedInventoryItems.Add(go);
        }

        /// <summary>
        /// Ejecutado cuando el jugador hace clic en una de las 2 opciones.
        /// </summary>
        private void SelectUpgrade(UpgradeData chosen)
        {
            Managers.AudioManager.Instance?.PlayUIClick();

            if (chosen != null && UpgradeManager.Instance != null)
            {
                // Comprobar conflicto de exclusión mutua
                if (UpgradeManager.Instance.HasConflict(chosen, out UpgradeData conflict))
                {
                    ShowReplacementWarning(chosen, conflict);
                    return;
                }

                UpgradeManager.Instance.AddUpgrade(chosen.id);
            }

            CloseAndResume();
        }

        private void CloseAndResume()
        {
            HideModal();

            // Limpiar selección del EventSystem
            if (UnityEngine.EventSystems.EventSystem.current != null)
            {
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            }

            _isModalOpen = false;

            // Restaurar tiempo
            Time.timeScale = 1f;

            // Disparar callback de vuelta a la ExtractionZone para que detone el EMP y se devuelva al pool
            Action cb = _onFinishedCallback;
            _onFinishedCallback = null;
            cb?.Invoke();
        }

        #region Sistema de Advertencia de Reemplazo (Exclusión de Mejoras)
        /// <summary>
        /// Garantiza la existencia del panel de advertencia de reemplazo en runtime con diseño UI cyberpunk/arcade.
        /// </summary>
        private void EnsureReplacementWarningUI()
        {
            if (replacementWarningPanel != null)
            {
                // Si ya estaba asignado, asegurar listeners
                if (acceptReplacementButton != null)
                {
                    acceptReplacementButton.onClick.RemoveAllListeners();
                    acceptReplacementButton.onClick.AddListener(OnAcceptReplacement);
                }
                if (cancelReplacementButton != null)
                {
                    cancelReplacementButton.onClick.RemoveAllListeners();
                    cancelReplacementButton.onClick.AddListener(OnCancelReplacement);
                }
                replacementWarningPanel.SetActive(false);
                return;
            }

            Transform parent = modalPanel != null ? modalPanel.transform : transform;

            // Panel contenedor centrado
            GameObject warningGo = new GameObject("ReplacementWarningPanel");
            warningGo.transform.SetParent(parent, false);
            RectTransform rect = warningGo.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -10f);
            rect.sizeDelta = new Vector2(580f, 350f);

            var bg = warningGo.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.05f, 0.10f, 0.98f);

            Font defaultFont = GetDefaultFont();

            // Texto descriptivo del conflicto
            GameObject textGo = new GameObject("WarningText");
            textGo.transform.SetParent(warningGo.transform, false);
            RectTransform tRect = textGo.AddComponent<RectTransform>();
            tRect.anchorMin = new Vector2(0f, 0.32f);
            tRect.anchorMax = new Vector2(1f, 1f);
            tRect.offsetMin = new Vector2(24f, 10f);
            tRect.offsetMax = new Vector2(-24f, -16f);

            replacementWarningText = textGo.AddComponent<Text>();
            replacementWarningText.font = defaultFont;
            replacementWarningText.fontSize = 17;
            replacementWarningText.alignment = TextAnchor.MiddleCenter;
            replacementWarningText.color = Color.white;
            replacementWarningText.raycastTarget = false;

            // Botón Aceptar Cambio (Izquierda/Centro)
            GameObject acceptGo = new GameObject("AcceptChangeButton");
            acceptGo.transform.SetParent(warningGo.transform, false);
            RectTransform aRect = acceptGo.AddComponent<RectTransform>();
            aRect.anchorMin = new Vector2(0.5f, 0f);
            aRect.anchorMax = new Vector2(0.5f, 0f);
            aRect.pivot = new Vector2(0.5f, 0f);
            aRect.anchoredPosition = new Vector2(-125f, 26f);
            aRect.sizeDelta = new Vector2(210f, 48f);

            var acceptImg = acceptGo.AddComponent<Image>();
            acceptImg.color = new Color(0.12f, 0.58f, 0.38f, 0.95f);
            acceptReplacementButton = acceptGo.AddComponent<Button>();
            acceptReplacementButton.targetGraphic = acceptImg;
            acceptGo.AddComponent<UICardHoverEffect>();

            GameObject aTextGo = new GameObject("Text");
            aTextGo.transform.SetParent(acceptGo.transform, false);
            RectTransform atRect = aTextGo.AddComponent<RectTransform>();
            atRect.anchorMin = Vector2.zero;
            atRect.anchorMax = Vector2.one;
            atRect.sizeDelta = Vector2.zero;
            var atTxt = aTextGo.AddComponent<Text>();
            atTxt.font = defaultFont;
            atTxt.fontSize = 15;
            atTxt.fontStyle = FontStyle.Bold;
            atTxt.alignment = TextAnchor.MiddleCenter;
            atTxt.color = Color.white;
            atTxt.text = "ACEPTAR CAMBIO";
            atTxt.raycastTarget = false;

            // Botón Cancelar (Derecha/Centro)
            GameObject cancelGo = new GameObject("CancelChangeButton");
            cancelGo.transform.SetParent(warningGo.transform, false);
            RectTransform cRect = cancelGo.AddComponent<RectTransform>();
            cRect.anchorMin = new Vector2(0.5f, 0f);
            cRect.anchorMax = new Vector2(0.5f, 0f);
            cRect.pivot = new Vector2(0.5f, 0f);
            cRect.anchoredPosition = new Vector2(125f, 26f);
            cRect.sizeDelta = new Vector2(210f, 48f);

            var cancelImg = cancelGo.AddComponent<Image>();
            cancelImg.color = new Color(0.58f, 0.15f, 0.22f, 0.95f);
            cancelReplacementButton = cancelGo.AddComponent<Button>();
            cancelReplacementButton.targetGraphic = cancelImg;
            cancelGo.AddComponent<UICardHoverEffect>();

            GameObject cTextGo = new GameObject("Text");
            cTextGo.transform.SetParent(cancelGo.transform, false);
            RectTransform ctRect = cTextGo.AddComponent<RectTransform>();
            ctRect.anchorMin = Vector2.zero;
            ctRect.anchorMax = Vector2.one;
            ctRect.sizeDelta = Vector2.zero;
            var ctTxt = cTextGo.AddComponent<Text>();
            ctTxt.font = defaultFont;
            ctTxt.fontSize = 15;
            ctTxt.fontStyle = FontStyle.Bold;
            ctTxt.alignment = TextAnchor.MiddleCenter;
            ctTxt.color = Color.white;
            ctTxt.text = "CANCELAR";
            ctTxt.raycastTarget = false;

            acceptReplacementButton.onClick.AddListener(OnAcceptReplacement);
            cancelReplacementButton.onClick.AddListener(OnCancelReplacement);

            replacementWarningPanel = warningGo;
            replacementWarningPanel.SetActive(false);
        }

        private Font GetDefaultFont()
        {
            if (option1TitleText != null && option1TitleText.font != null) return option1TitleText.font;
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        /// <summary>
        /// Muestra el panel de advertencia de reemplazo ocultando las opciones principales.
        /// </summary>
        private void ShowReplacementWarning(UpgradeData chosen, UpgradeData conflict)
        {
            _pendingUpgrade = chosen;
            _conflictingUpgrade = conflict;

            EnsureReplacementWarningUI();

            // 1. Ocultar opciones e inventario principal
            SetMainOptionsVisible(false);

            // 2. Poblar mensaje explicativo
            if (replacementWarningText != null)
            {
                replacementWarningText.text =
                    $"<color=#FF5555><b>⚠️ ADVERTENCIA DE EXCLUSIÓN ⚠️</b></color>\n\n" +
                    $"Esta mejora reemplazará a:\n" +
                    $"<size=21><color=#FFAA00><b>[{conflict.upgradeName}]</b></color></size>\n\n" +
                    $"¿Deseas sustituirla por <size=21><color=#00FFFF><b>[{chosen.upgradeName}]</b></color></size>?\n" +
                    $"<size=13><color=#AAAAAA>(Ambas mejoras alteran el comportamiento del Parry y no pueden coexistir)</color></size>";
            }

            if (replacementWarningPanel != null)
            {
                replacementWarningPanel.SetActive(true);
            }

            // Configurar navegación explícita entre Aceptar y Cancelar
            if (acceptReplacementButton != null && cancelReplacementButton != null)
            {
                Navigation aNav = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = cancelReplacementButton };
                acceptReplacementButton.navigation = aNav;

                Navigation cNav = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = acceptReplacementButton };
                cancelReplacementButton.navigation = cNav;
            }

            // Foco para teclado / mando
            if (UnityEngine.EventSystems.EventSystem.current != null && acceptReplacementButton != null)
            {
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(acceptReplacementButton.gameObject);
            }
        }

        private void OnAcceptReplacement()
        {
            Managers.AudioManager.Instance?.PlayUIClick();

            if (_conflictingUpgrade != null && _pendingUpgrade != null && UpgradeManager.Instance != null)
            {
                UpgradeManager.Instance.ReplaceUpgrade(_conflictingUpgrade.id, _pendingUpgrade.id);
            }

            _conflictingUpgrade = null;
            _pendingUpgrade = null;

            if (replacementWarningPanel != null)
            {
                replacementWarningPanel.SetActive(false);
            }

            CloseAndResume();
        }

        private void OnCancelReplacement()
        {
            Managers.AudioManager.Instance?.PlayUIClick();

            _conflictingUpgrade = null;
            _pendingUpgrade = null;

            if (replacementWarningPanel != null)
            {
                replacementWarningPanel.SetActive(false);
            }

            RestoreMainOptionsView();
        }

        private void RestoreMainOptionsView()
        {
            if (replacementWarningPanel != null)
            {
                replacementWarningPanel.SetActive(false);
            }

            SetMainOptionsVisible(true);

            // Re-enfocar opción 1 para teclado y mando
            if (UnityEngine.EventSystems.EventSystem.current != null && option1Button != null && option1Button.gameObject.activeInHierarchy)
            {
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(option1Button.gameObject);
            }
        }

        private void SetMainOptionsVisible(bool visible)
        {
            if (option1Button != null) option1Button.gameObject.SetActive(visible);
            if (option2Button != null && _currentOption2 != null) option2Button.gameObject.SetActive(visible);
            if (option3Button != null && _currentOption3 != null) option3Button.gameObject.SetActive(visible);

            if (inventoryContainer != null)
            {
                Transform invParent = inventoryContainer.parent;
                if (invParent != null && invParent != (modalPanel != null ? modalPanel.transform : transform))
                {
                    invParent.gameObject.SetActive(visible);
                }
                else
                {
                    inventoryContainer.gameObject.SetActive(visible);
                }
            }
        }
        #endregion

        private Coroutine _transitionCoroutine;

        private void ShowModal()
        {
            if (modalPanel != null && !modalPanel.activeSelf) modalPanel.SetActive(true);
            if (!gameObject.activeSelf) gameObject.SetActive(true);

            if (_transitionCoroutine != null) StopCoroutine(_transitionCoroutine);
            _transitionCoroutine = StartCoroutine(AnimateModalEntrance(0.3f));
        }

        private IEnumerator AnimateModalEntrance(float duration)
        {
            float elapsed = 0f;
            Vector3 startScale = Vector3.one * 0.9f;
            Vector3 targetScale = Vector3.one;

            Transform targetTransform = modalPanel != null ? modalPanel.transform : transform;
            targetTransform.localScale = startScale;

            if (modalCanvasGroup != null)
            {
                modalCanvasGroup.alpha = 0f;
                modalCanvasGroup.interactable = false;
                modalCanvasGroup.blocksRaycasts = true;
            }

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                // Ease-Out cúbico suave
                float ease = 1f - Mathf.Pow(1f - t, 3f);

                if (modalCanvasGroup != null)
                {
                    modalCanvasGroup.alpha = ease;
                }

                targetTransform.localScale = Vector3.Lerp(startScale, targetScale, ease);
                yield return null;
            }

            if (modalCanvasGroup != null)
            {
                modalCanvasGroup.alpha = 1f;
                modalCanvasGroup.interactable = true;
                modalCanvasGroup.blocksRaycasts = true;
            }

            targetTransform.localScale = targetScale;
            _transitionCoroutine = null;
        }

        private void HideModal()
        {
            if (_transitionCoroutine != null)
            {
                StopCoroutine(_transitionCoroutine);
                _transitionCoroutine = null;
            }

            if (modalCanvasGroup != null)
            {
                modalCanvasGroup.alpha = 0f;
                modalCanvasGroup.interactable = false;
                modalCanvasGroup.blocksRaycasts = false;
            }
        }

        private void HideModalInstant()
        {
            _isModalOpen = false;
            if (modalCanvasGroup != null)
            {
                modalCanvasGroup.alpha = 0f;
                modalCanvasGroup.interactable = false;
                modalCanvasGroup.blocksRaycasts = false;
            }
        }

        /// <summary>
        /// Garantiza que exista un EventSystem en la escena con el InputModule correcto para registrar clics.
        /// </summary>
        public static void EnsureEventSystem()
        {
#if UNITY_2023_1_OR_NEWER
            var es = UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
#else
            var es = UnityEngine.Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
#endif
            if (es == null)
            {
                GameObject esGo = new GameObject("EventSystem");
                es = esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
#if ENABLE_INPUT_SYSTEM
                esGo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
                esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
                Debug.Log("<color=cyan><b>[EventSystem]</b> EventSystem creado en runtime para registrar clics en UI.</color>");
            }
            else
            {
#if ENABLE_INPUT_SYSTEM
                // Si existe un StandaloneInputModule heredado, reemplazarlo para que funcione con el New Input System
                var standalone = es.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>();
                if (standalone != null && es.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() == null)
                {
                    Destroy(standalone);
                    es.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                    Debug.Log("<color=cyan><b>[EventSystem]</b> StandaloneInputModule migrado a InputSystemUIInputModule.</color>");
                }
#endif
            }
        }

        private void EnsureGraphicRaycaster()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null && canvas.GetComponent<GraphicRaycaster>() == null)
            {
                canvas.gameObject.AddComponent<GraphicRaycaster>();
            }
        }

        /// <summary>
        /// Asegura que los textos/imágenes decorativos hijos no bloqueen el evento de clic del botón principal.
        /// </summary>
        private void EnsureCardRaycasts()
        {
            if (option1Button != null)
            {
                var mainImg = option1Button.GetComponent<Image>();
                if (mainImg != null) mainImg.raycastTarget = true;

                foreach (var g in option1Button.GetComponentsInChildren<Graphic>(true))
                {
                    if (g.gameObject != option1Button.gameObject)
                    {
                        g.raycastTarget = false;
                    }
                }
            }

            if (option2Button != null)
            {
                var mainImg = option2Button.GetComponent<Image>();
                if (mainImg != null) mainImg.raycastTarget = true;

                foreach (var g in option2Button.GetComponentsInChildren<Graphic>(true))
                {
                    if (g.gameObject != option2Button.gameObject)
                    {
                        g.raycastTarget = false;
                    }
                }
            }

            if (option3Button != null)
            {
                var mainImg = option3Button.GetComponent<Image>();
                if (mainImg != null) mainImg.raycastTarget = true;

                foreach (var g in option3Button.GetComponentsInChildren<Graphic>(true))
                {
                    if (g.gameObject != option3Button.gameObject)
                    {
                        g.raycastTarget = false;
                    }
                }
            }
        }

        /// <summary>
        /// Garantiza que la tarjeta 3 exista, esté enlazada y distribuida simétricamente
        /// incluso en escenas pre-existentes que solo tenían las opciones 1 y 2.
        /// </summary>
        private void EnsureOption3CardSetup()
        {
            if (option3Button == null)
            {
                // 1. Buscar si ya existe un GameObject hijo llamado Option3_Card
                Transform card3Tr = null;
                if (modalPanel != null) card3Tr = modalPanel.transform.Find("Option3_Card");
                if (card3Tr == null && transform != null) card3Tr = transform.Find("Option3_Card");
                if (card3Tr == null && option2Button != null && option2Button.transform.parent != null)
                {
                    card3Tr = option2Button.transform.parent.Find("Option3_Card");
                }

                GameObject card3Go;
                if (card3Tr != null)
                {
                    card3Go = card3Tr.gameObject;
                }
                else if (option2Button != null)
                {
                    // Duplicar proceduralmente la tarjeta 2 para crear la tarjeta 3
                    card3Go = Instantiate(option2Button.gameObject, option2Button.transform.parent);
                    card3Go.name = "Option3_Card";
                }
                else
                {
                    return;
                }

                if (card3Go != null)
                {
                    card3Go.SetActive(true);
                }

                option3Button = card3Go.GetComponent<Button>();
                option3CardImage = card3Go.GetComponent<Image>();

                // Encontrar textos hijos y adaptar insignia/etiquetas
                foreach (var txt in card3Go.GetComponentsInChildren<Text>(true))
                {
                    string n = txt.gameObject.name.ToLower();
                    if (n.Contains("type")) option3TypeText = txt;
                    else if (n.Contains("title")) option3TitleText = txt;
                    else if (n.Contains("level")) option3LevelText = txt;
                    else if (n.Contains("desc")) option3DescText = txt;
                    
                    if (txt.text.Contains("[2]"))
                    {
                        txt.text = txt.text.Replace("[2]", "[3]");
                    }
                    else if (txt.text.Contains(" 2"))
                    {
                        txt.text = txt.text.Replace(" 2", " 3");
                    }
                }

                if (option3Button != null && !option3Button.TryGetComponent<UICardHoverEffect>(out _))
                {
                    option3Button.gameObject.AddComponent<UICardHoverEffect>();
                }

                Debug.Log("<color=cyan><b>[LevelUpUI]</b> ⭐ 3ra tarjeta de mejoras (Opción 3) auto-configurada y lista para elegir.</color>");
            }

            // Reajustar posiciones y anchos para 3 columnas equilibradas (Card 1: -390, Card 2: -130, Card 3: 130, Inv: 390)
            float cardWidth = 245f;
            float cardHeight = 380f;

            if (option1Button != null && option1Button.TryGetComponent<RectTransform>(out var r1))
            {
                r1.sizeDelta = new Vector2(cardWidth, cardHeight);
                r1.anchoredPosition = new Vector2(-390f, -20f);
            }

            if (option2Button != null && option2Button.TryGetComponent<RectTransform>(out var r2))
            {
                r2.sizeDelta = new Vector2(cardWidth, cardHeight);
                r2.anchoredPosition = new Vector2(-130f, -20f);
            }

            if (option3Button != null && option3Button.TryGetComponent<RectTransform>(out var r3))
            {
                r3.sizeDelta = new Vector2(cardWidth, cardHeight);
                r3.anchoredPosition = new Vector2(130f, -20f);
            }

            if (inventoryContainer != null)
            {
                Transform invPanel = inventoryContainer.parent;
                if (invPanel != null && invPanel.TryGetComponent<RectTransform>(out var rInv))
                {
                    rInv.sizeDelta = new Vector2(cardWidth, cardHeight);
                    rInv.anchoredPosition = new Vector2(390f, -20f);
                }
            }

            // Actualizar texto del encabezado para incluir la opción [3]
            if (modalPanel != null)
            {
                var titleTxt = modalPanel.transform.Find("ModalTitle")?.GetComponent<Text>();
                if (titleTxt != null && !titleTxt.text.Contains("[3]"))
                {
                    titleTxt.text = "⚡ PROTOCOLO DE EXTRACCIÓN COMPLETADO ⚡\n<size=15><color=#A0D8EF>SELECCIONA UNA MEJORA PARA REANUDAR EL COMBATE</color></size>\n<size=13><color=#FFFF88>(Haz clic en una opción o presiona [1] / [2] / [3] en el teclado)</color></size>";
                }
            }
        }
    }
}
