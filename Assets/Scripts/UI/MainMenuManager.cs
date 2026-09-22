using System.Collections;
using ArenaSurvivor.Managers;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ArenaSurvivor.UI
{
    /// <summary>
    /// Controlador principal de la interfaz del Menú Principal.
    /// Título provisional: "Circule Extraccion".
    /// Ofrece opciones para Iniciar Partida (Jugar), Salir, y resumen de controles para el testeo.
    /// Incluye auto-generación procedural (Fallback) para funcionar inmediatamente out-of-the-box.
    /// </summary>
    [DisallowMultipleComponent]
    public class MainMenuManager : MonoBehaviour
    {
        [Header("Configuración de Escena")]
        [Tooltip("Nombre de la escena de juego que se cargará al pulsar Jugar")]
        [SerializeField] private string gameplaySceneName = "SampleScene";

        [Header("Referencias de UI (Opcional - Se genera automáticamente si está vacío)")]
        [SerializeField] private Canvas mainCanvas;
        [SerializeField] private Button playButton;
        [SerializeField] private Button quitButton;

        private void Awake()
        {
            // Asegurar que la escala de tiempo esté activa
            Time.timeScale = 1f;

            // Liberar y mostrar cursor para interactuar con el menú
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            EnsureEventSystem();
            EnsureMainMenuUI();
        }

        private void Start()
        {
            // Reproducir música suave si AudioManager está presente
            if (playButton != null)
            {
                playButton.onClick.RemoveAllListeners();
                playButton.onClick.AddListener(OnPlayClicked);
            }

            if (quitButton != null)
            {
                quitButton.onClick.RemoveAllListeners();
                quitButton.onClick.AddListener(OnQuitClicked);
            }
        }

        public void OnPlayClicked()
        {
            AudioManager.Instance?.PlayUIClick();
            Debug.Log($"<color=lime><b>[MainMenu]</b> Iniciando partida en '{gameplaySceneName}'...</color>");

            Time.timeScale = 1f;

            // Cargar la escena de gameplay por nombre o por índice siguiente
            if (Application.CanStreamedLevelBeLoaded(gameplaySceneName))
            {
                SceneManager.LoadScene(gameplaySceneName);
            }
            else if (SceneManager.sceneCountInBuildSettings > 1)
            {
                SceneManager.LoadScene(1);
            }
            else
            {
                // Si la escena activa es la única registrada
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
        }

        public void OnQuitClicked()
        {
            AudioManager.Instance?.PlayUIClick();
            Debug.Log("<color=red><b>[MainMenu]</b> Saliendo del juego...</color>");

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        #region Generación Procedural Out-of-the-Box
        private void EnsureEventSystem()
        {
#if UNITY_2023_1_OR_NEWER
            var es = FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
#else
            var es = FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
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
            }
        }

        private void EnsureMainMenuUI()
        {
            if (mainCanvas == null)
            {
#if UNITY_2023_1_OR_NEWER
                mainCanvas = FindFirstObjectByType<Canvas>();
#else
                mainCanvas = FindObjectOfType<Canvas>();
#endif
            }

            if (mainCanvas == null)
            {
                GameObject canvasGo = new GameObject("MainMenuCanvas");
                mainCanvas = canvasGo.AddComponent<Canvas>();
                mainCanvas.renderMode = RenderMode.ScreenSpaceOverlay;

                var scaler = canvasGo.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;

                canvasGo.AddComponent<GraphicRaycaster>();
            }

            // Si ya tiene los botones asignados, salir
            if (playButton != null && quitButton != null) return;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

            // Fondo con degradé oscuro
            GameObject bgGo = new GameObject("Background");
            bgGo.transform.SetParent(mainCanvas.transform, false);
            RectTransform bgRect = bgGo.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;

            var bgImg = bgGo.AddComponent<Image>();
            bgImg.color = new Color(0.03f, 0.04f, 0.07f, 1f);

            // Contenedor Central
            GameObject contentGo = new GameObject("MenuContainer");
            contentGo.transform.SetParent(mainCanvas.transform, false);
            RectTransform contentRect = contentGo.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0.5f, 0.5f);
            contentRect.anchorMax = new Vector2(0.5f, 0.5f);
            contentRect.pivot = new Vector2(0.5f, 0.5f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(900f, 650f);

            // Título: CIRCULE EXTRACCION
            GameObject titleGo = new GameObject("TitleText");
            titleGo.transform.SetParent(contentGo.transform, false);
            RectTransform titleRect = titleGo.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -20f);
            titleRect.sizeDelta = new Vector2(800f, 75f);

            Text titleTxt = titleGo.AddComponent<Text>();
            titleTxt.font = font;
            titleTxt.fontSize = 52;
            titleTxt.fontStyle = FontStyle.Bold;
            titleTxt.alignment = TextAnchor.MiddleCenter;
            titleTxt.color = new Color(0.0f, 0.95f, 0.85f); // Cyan Neón
            titleTxt.text = "CIRCULE EXTRACCION";

            // Subtítulo
            GameObject subGo = new GameObject("SubtitleText");
            subGo.transform.SetParent(contentGo.transform, false);
            RectTransform subRect = subGo.AddComponent<RectTransform>();
            subRect.anchorMin = new Vector2(0.5f, 1f);
            subRect.anchorMax = new Vector2(0.5f, 1f);
            subRect.pivot = new Vector2(0.5f, 1f);
            subRect.anchoredPosition = new Vector2(0f, -95f);
            subRect.sizeDelta = new Vector2(800f, 30f);

            Text subTxt = subGo.AddComponent<Text>();
            subTxt.font = font;
            subTxt.fontSize = 16;
            subTxt.fontStyle = FontStyle.Italic;
            subTxt.alignment = TextAnchor.MiddleCenter;
            subTxt.color = new Color(0.7f, 0.8f, 0.95f, 0.9f);
            subTxt.text = "PROTOTIPO DE TESTEO • SOBREVIVE Y ESCAPA DE LA ARENA";

            // Contenedor de Botones
            GameObject buttonsContainer = new GameObject("ButtonsArea");
            buttonsContainer.transform.SetParent(contentGo.transform, false);
            RectTransform btnAreaRect = buttonsContainer.AddComponent<RectTransform>();
            btnAreaRect.anchorMin = new Vector2(0.5f, 0.5f);
            btnAreaRect.anchorMax = new Vector2(0.5f, 0.5f);
            btnAreaRect.pivot = new Vector2(0.5f, 0.5f);
            btnAreaRect.anchoredPosition = new Vector2(0f, 60f);
            btnAreaRect.sizeDelta = new Vector2(380f, 160f);

            // Botón JUGAR
            GameObject playGo = new GameObject("PlayButton");
            playGo.transform.SetParent(buttonsContainer.transform, false);
            RectTransform pRect = playGo.AddComponent<RectTransform>();
            pRect.anchorMin = new Vector2(0.5f, 1f);
            pRect.anchorMax = new Vector2(0.5f, 1f);
            pRect.pivot = new Vector2(0.5f, 1f);
            pRect.anchoredPosition = new Vector2(0f, 0f);
            pRect.sizeDelta = new Vector2(340f, 56f);

            var playImg = playGo.AddComponent<Image>();
            playImg.color = new Color(0.0f, 0.72f, 0.65f, 0.95f);

            playButton = playGo.AddComponent<Button>();
            playButton.targetGraphic = playImg;
            playButton.onClick.AddListener(OnPlayClicked);
            playGo.AddComponent<UICardHoverEffect>();

            GameObject pTextGo = new GameObject("Text");
            pTextGo.transform.SetParent(playGo.transform, false);
            RectTransform ptRect = pTextGo.AddComponent<RectTransform>();
            ptRect.anchorMin = Vector2.zero;
            ptRect.anchorMax = Vector2.one;
            ptRect.sizeDelta = Vector2.zero;

            Text ptTxt = pTextGo.AddComponent<Text>();
            ptTxt.font = font;
            ptTxt.fontSize = 22;
            ptTxt.fontStyle = FontStyle.Bold;
            ptTxt.alignment = TextAnchor.MiddleCenter;
            ptTxt.color = Color.white;
            ptTxt.text = "▶  JUGAR";
            ptTxt.raycastTarget = false;

            // Botón SALIR
            GameObject quitGo = new GameObject("QuitButton");
            quitGo.transform.SetParent(buttonsContainer.transform, false);
            RectTransform qRect = quitGo.AddComponent<RectTransform>();
            qRect.anchorMin = new Vector2(0.5f, 1f);
            qRect.anchorMax = new Vector2(0.5f, 1f);
            qRect.pivot = new Vector2(0.5f, 1f);
            qRect.anchoredPosition = new Vector2(0f, -72f);
            qRect.sizeDelta = new Vector2(340f, 56f);

            var quitImg = quitGo.AddComponent<Image>();
            quitImg.color = new Color(0.35f, 0.16f, 0.2f, 0.95f);

            quitButton = quitGo.AddComponent<Button>();
            quitButton.targetGraphic = quitImg;
            quitButton.onClick.AddListener(OnQuitClicked);
            quitGo.AddComponent<UICardHoverEffect>();

            GameObject qTextGo = new GameObject("Text");
            qTextGo.transform.SetParent(quitGo.transform, false);
            RectTransform qtRect = qTextGo.AddComponent<RectTransform>();
            qtRect.anchorMin = Vector2.zero;
            qtRect.anchorMax = Vector2.one;
            qtRect.sizeDelta = Vector2.zero;

            Text qtTxt = qTextGo.AddComponent<Text>();
            qtTxt.font = font;
            qtTxt.fontSize = 20;
            qtTxt.fontStyle = FontStyle.Bold;
            qtTxt.alignment = TextAnchor.MiddleCenter;
            qtTxt.color = Color.white;
            qtTxt.text = "✖  SALIR";
            qtTxt.raycastTarget = false;

            // Tarjeta Inferior de Guía de Testeo / Instrucciones
            GameObject guideGo = new GameObject("GuideCard");
            guideGo.transform.SetParent(contentGo.transform, false);
            RectTransform guideRect = guideGo.AddComponent<RectTransform>();
            guideRect.anchorMin = new Vector2(0.5f, 0f);
            guideRect.anchorMax = new Vector2(0.5f, 0f);
            guideRect.pivot = new Vector2(0.5f, 0f);
            guideRect.anchoredPosition = new Vector2(0f, 25f);
            guideRect.sizeDelta = new Vector2(760f, 185f);

            var guideBg = guideGo.AddComponent<Image>();
            guideBg.color = new Color(0.07f, 0.09f, 0.14f, 0.95f);

            GameObject guideTextGo = new GameObject("GuideText");
            guideTextGo.transform.SetParent(guideGo.transform, false);
            RectTransform gtRect = guideTextGo.AddComponent<RectTransform>();
            gtRect.anchorMin = Vector2.zero;
            gtRect.anchorMax = Vector2.one;
            gtRect.offsetMin = new Vector2(20f, 14f);
            gtRect.offsetMax = new Vector2(-20f, -14f);

            Text gtTxt = guideTextGo.AddComponent<Text>();
            gtTxt.font = font;
            gtTxt.fontSize = 14;
            gtTxt.alignment = TextAnchor.UpperLeft;
            gtTxt.lineSpacing = 1.3f;
            gtTxt.color = new Color(0.85f, 0.9f, 0.96f);
            gtTxt.text = "<color=#00F5D4><b>GUÍA RÁPIDA DE TESTEO:</b></color>\n" +
                         "• <b>[W, A, S, D]:</b> Desplazamiento de la nave dentro de los límites de la arena.\n" +
                         "• <b>[Clic Izquierdo]:</b> Disparo primario hacia la posición del cursor.\n" +
                         "• <b>[Clic Derecho]:</b> <b>PARRY DEFENSIVO:</b> Refleja enemigos atacantes convirtiéndolos en proyectiles.\n" +
                         "• <b>[E / Barra Espacio]:</b> Sobrecarga de Energía: Onda expansiva que disuelve amenazas.\n" +
                         "• <b>[ESC / P]:</b> Pausar la partida en cualquier momento.";

            // Etiqueta de Versión
            GameObject verGo = new GameObject("VersionLabel");
            verGo.transform.SetParent(mainCanvas.transform, false);
            RectTransform vRect = verGo.AddComponent<RectTransform>();
            vRect.anchorMin = new Vector2(1f, 0f);
            vRect.anchorMax = new Vector2(1f, 0f);
            vRect.pivot = new Vector2(1f, 0f);
            vRect.anchoredPosition = new Vector2(-20f, 15f);
            vRect.sizeDelta = new Vector2(250f, 25f);

            Text vTxt = verGo.AddComponent<Text>();
            vTxt.font = font;
            vTxt.fontSize = 12;
            vTxt.alignment = TextAnchor.MiddleRight;
            vTxt.color = new Color(0.5f, 0.6f, 0.7f, 0.75f);
            vTxt.text = "Circule Extraccion • Build v0.1 Test";
        }
        #endregion
    }
}
