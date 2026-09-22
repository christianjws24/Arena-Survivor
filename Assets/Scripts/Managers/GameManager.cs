using System;
using System.Collections;
using ArenaSurvivor.Combat;
using ArenaSurvivor.Entities;
using ArenaSurvivor.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ArenaSurvivor.Managers
{
    /// <summary>
    /// Gestiona el ciclo principal de juego, pantalla de Game Over y reinicio de escena.
    /// Totalmente desacoplado: escucha el evento OnPlayerDeath de PlayerController.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Configuración de Game Over")]
        [Tooltip("Retraso en segundos para permitir que la animación de explosión del jugador se despliegue antes de pausar")]
        [SerializeField] private float deathExplosionDelay = 0.85f;

        [Header("Referencias de UI (Opcional - Se crea proceduralmente si está vacío)")]
        [SerializeField] private GameObject gameOverPanel;
        [SerializeField] private Button restartButton;
        [SerializeField] private CanvasGroup gameOverCanvasGroup;

        [Header("Referencias de Menú de Pausa (Opcional - Se crea proceduralmente)")]
        [SerializeField] private GameObject pausePanel;
        [SerializeField] private CanvasGroup pauseCanvasGroup;

        private bool _isGameOver;
        private bool _isPaused;

        public bool IsGameOver => _isGameOver;
        public bool IsPaused => _isPaused;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _isGameOver = false;
            _isPaused = false;

            // Asegurar que el tiempo comience corriendo
            Time.timeScale = 1f;

            EnsureGameOverUI();
            EnsurePauseUI();
        }

        private void Start()
        {
            // Suscripción al evento de muerte del jugador
            if (PlayerController.Instance != null)
            {
                PlayerController.Instance.OnPlayerDeath += HandlePlayerDeath;
            }
        }

        private void OnDestroy()
        {
            if (PlayerController.Instance != null)
            {
                PlayerController.Instance.OnPlayerDeath -= HandlePlayerDeath;
            }
        }

        private void Update()
        {
            // Atajo de teclado 'R' o 'Espacio' para reiniciar rápidamente en Game Over
            if (_isGameOver)
            {
#if ENABLE_INPUT_SYSTEM
                if (UnityEngine.InputSystem.Keyboard.current != null)
                {
                    var kb = UnityEngine.InputSystem.Keyboard.current;
                    if (kb.rKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)
                    {
                        RestartGame();
                    }
                }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
                if (Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
                {
                    RestartGame();
                }
#endif
                return;
            }

            // Entrada de Pausa (ESC / P / Gamepad Start)
            bool pauseRequested = false;
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb.escapeKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame)
                {
                    pauseRequested = true;
                }
            }
            if (UnityEngine.InputSystem.Gamepad.current != null)
            {
                var gp = UnityEngine.InputSystem.Gamepad.current;
                if (gp.startButton.wasPressedThisFrame)
                {
                    pauseRequested = true;
                }
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P))
            {
                pauseRequested = true;
            }
#endif

            if (pauseRequested)
            {
                // No pausar si el modal de subida de nivel está abierto
                bool isLevelUpOpen = LevelUpUIManager.Instance != null && LevelUpUIManager.Instance.IsModalOpen;
                if (!isLevelUpOpen)
                {
                    TogglePause();
                }
            }
        }

        private void HandlePlayerDeath()
        {
            if (_isGameOver) return;
            _isGameOver = true;

            // Si estaba en pausa, cerrar panel de pausa
            if (_isPaused && pausePanel != null)
            {
                pausePanel.SetActive(false);
                _isPaused = false;
            }

            // SFX de muerte
            AudioManager.Instance?.PlayPlayerDeath();

            StartCoroutine(GameOverSequenceRoutine());
        }

        private IEnumerator GameOverSequenceRoutine()
        {
            // 1. Permitir que la animación de explosión se reproduzca
            yield return new WaitForSeconds(deathExplosionDelay);

            // 2. Pausar el juego
            Time.timeScale = 0f;

            // 3. Desplegar pantalla de Game Over con fade in suave
            if (gameOverPanel != null)
            {
                gameOverPanel.SetActive(true);
            }

            if (gameOverCanvasGroup != null)
            {
                gameOverCanvasGroup.blocksRaycasts = true;
                gameOverCanvasGroup.interactable = true;

                float elapsed = 0f;
                float duration = 0.35f;
                while (elapsed < duration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    gameOverCanvasGroup.alpha = Mathf.Clamp01(elapsed / duration);
                    yield return null;
                }
                gameOverCanvasGroup.alpha = 1f;
            }

            // Liberar cursor para hacer clic en el botón
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            Debug.Log("<color=red><b>[GAME OVER]</b> Pantalla de derrota mostrada. Haz clic en 'Reiniciar' o presiona 'R'.</color>");
        }

        #region Control de Pausa y Escenas
        /// <summary>
        /// Alterna el estado de pausa del juego.
        /// </summary>
        public void TogglePause()
        {
            if (_isPaused)
            {
                ResumeGame();
            }
            else
            {
                PauseGame();
            }
        }

        /// <summary>
        /// Pausa el juego congelando Time.timeScale y abriendo el menú de pausa.
        /// </summary>
        public void PauseGame()
        {
            if (_isGameOver) return;
            _isPaused = true;

            // Detener cualquier HitStop en curso para evitar que despause Time.timeScale
            HitStopManager.Instance?.CancelHitStop();

            Time.timeScale = 0f;

            if (pausePanel != null)
            {
                pausePanel.SetActive(true);
            }
            if (pauseCanvasGroup != null)
            {
                pauseCanvasGroup.alpha = 1f;
                pauseCanvasGroup.interactable = true;
                pauseCanvasGroup.blocksRaycasts = true;
            }

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            AudioManager.Instance?.PlayUIClick();
            Debug.Log("<color=cyan><b>[PAUSA]</b> Partida pausada.</color>");
        }

        /// <summary>
        /// Reanuda el juego restaurando Time.timeScale a 1f y ocultando el menú de pausa.
        /// </summary>
        public void ResumeGame()
        {
            if (_isGameOver) return;
            _isPaused = false;

            Time.timeScale = 1f;

            if (pausePanel != null)
            {
                pausePanel.SetActive(false);
            }
            if (pauseCanvasGroup != null)
            {
                pauseCanvasGroup.interactable = false;
                pauseCanvasGroup.blocksRaycasts = false;
            }

            AudioManager.Instance?.PlayUIClick();
            Debug.Log("<color=cyan><b>[PAUSA]</b> Partida reanudada.</color>");
        }

        /// <summary>
        /// Recarga la escena actual inmediatamente restaurando la escala de tiempo.
        /// </summary>
        public void RestartGame()
        {
            Time.timeScale = 1f;
            _isPaused = false;
            int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
            SceneManager.LoadScene(currentSceneIndex);
        }

        /// <summary>
        /// Vuelve a la escena del Menú Principal.
        /// </summary>
        public void GoToMainMenu()
        {
            Time.timeScale = 1f;
            _isPaused = false;
            AudioManager.Instance?.PlayUIClick();

            if (Application.CanStreamedLevelBeLoaded("MainMenu"))
            {
                SceneManager.LoadScene("MainMenu");
            }
            else
            {
                SceneManager.LoadScene(0);
            }
        }

        /// <summary>
        /// Cierra la aplicación o detiene el modo Play en el editor de Unity.
        /// </summary>
        public void QuitGame()
        {
            AudioManager.Instance?.PlayUIClick();
            Debug.Log("[GameManager] Saliendo del juego...");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
        #endregion

        #region Generación Procedural de UI (Fallback Out-of-the-Box)
        private void EnsureGameOverUI()
        {
            if (gameOverPanel != null)
            {
                gameOverPanel.SetActive(false);
                return;
            }

            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null) return;

            // Panel Fondo
            gameOverPanel = new GameObject("GameOverModalPanel");
            gameOverPanel.transform.SetParent(canvas.transform, false);
            RectTransform panelRect = gameOverPanel.AddComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.sizeDelta = Vector2.zero;

            var bg = gameOverPanel.AddComponent<Image>();
            bg.color = new Color(0.04f, 0.02f, 0.04f, 0.92f);

            gameOverCanvasGroup = gameOverPanel.AddComponent<CanvasGroup>();
            gameOverCanvasGroup.alpha = 0f;
            gameOverCanvasGroup.interactable = false;
            gameOverCanvasGroup.blocksRaycasts = false;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

            // Título GAME OVER
            GameObject titleGo = new GameObject("TitleText");
            titleGo.transform.SetParent(gameOverPanel.transform, false);
            RectTransform titleRect = titleGo.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 0.5f);
            titleRect.anchorMax = new Vector2(0.5f, 0.5f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.anchoredPosition = new Vector2(0f, 60f);
            titleRect.sizeDelta = new Vector2(600f, 80f);

            Text titleTxt = titleGo.AddComponent<Text>();
            titleTxt.font = font;
            titleTxt.fontSize = 44;
            titleTxt.fontStyle = FontStyle.Bold;
            titleTxt.alignment = TextAnchor.MiddleCenter;
            titleTxt.color = new Color(0.95f, 0.2f, 0.2f);
            titleTxt.text = "¡FIN DE LA PARTIDA!";

            // Subtítulo
            GameObject subGo = new GameObject("SubtitleText");
            subGo.transform.SetParent(gameOverPanel.transform, false);
            RectTransform subRect = subGo.AddComponent<RectTransform>();
            subRect.anchorMin = new Vector2(0.5f, 0.5f);
            subRect.anchorMax = new Vector2(0.5f, 0.5f);
            subRect.pivot = new Vector2(0.5f, 0.5f);
            subRect.anchoredPosition = new Vector2(0f, 10f);
            subRect.sizeDelta = new Vector2(600f, 35f);

            Text subTxt = subGo.AddComponent<Text>();
            subTxt.font = font;
            subTxt.fontSize = 16;
            subTxt.alignment = TextAnchor.MiddleCenter;
            subTxt.color = new Color(0.8f, 0.8f, 0.85f);
            subTxt.text = "El núcleo del jugador ha sido destruido en la arena.";

            // Botón Reiniciar (Izquierda)
            GameObject btnGo = new GameObject("RestartButton");
            btnGo.transform.SetParent(gameOverPanel.transform, false);
            RectTransform btnRect = btnGo.AddComponent<RectTransform>();
            btnRect.anchorMin = new Vector2(0.5f, 0.5f);
            btnRect.anchorMax = new Vector2(0.5f, 0.5f);
            btnRect.pivot = new Vector2(0.5f, 0.5f);
            btnRect.anchoredPosition = new Vector2(-115f, -65f);
            btnRect.sizeDelta = new Vector2(210f, 50f);

            Image btnImg = btnGo.AddComponent<Image>();
            btnImg.color = new Color(0.2f, 0.75f, 1f, 0.9f);

            restartButton = btnGo.AddComponent<Button>();
            restartButton.targetGraphic = btnImg;
            restartButton.onClick.AddListener(RestartGame);
            btnGo.AddComponent<UI.UICardHoverEffect>();

            GameObject btnTextGo = new GameObject("Text");
            btnTextGo.transform.SetParent(btnGo.transform, false);
            RectTransform btRect = btnTextGo.AddComponent<RectTransform>();
            btRect.anchorMin = Vector2.zero;
            btRect.anchorMax = Vector2.one;
            btRect.sizeDelta = Vector2.zero;

            Text btnTxt = btnTextGo.AddComponent<Text>();
            btnTxt.font = font;
            btnTxt.fontSize = 17;
            btnTxt.fontStyle = FontStyle.Bold;
            btnTxt.alignment = TextAnchor.MiddleCenter;
            btnTxt.color = Color.white;
            btnTxt.text = "REINICIAR (R)";
            btnTxt.raycastTarget = false;

            // Botón Menú Principal (Derecha)
            GameObject menuBtnGo = new GameObject("MainMenuButton");
            menuBtnGo.transform.SetParent(gameOverPanel.transform, false);
            RectTransform menuBtnRect = menuBtnGo.AddComponent<RectTransform>();
            menuBtnRect.anchorMin = new Vector2(0.5f, 0.5f);
            menuBtnRect.anchorMax = new Vector2(0.5f, 0.5f);
            menuBtnRect.pivot = new Vector2(0.5f, 0.5f);
            menuBtnRect.anchoredPosition = new Vector2(115f, -65f);
            menuBtnRect.sizeDelta = new Vector2(210f, 50f);

            Image menuBtnImg = menuBtnGo.AddComponent<Image>();
            menuBtnImg.color = new Color(0.35f, 0.4f, 0.5f, 0.9f);

            Button menuBtn = menuBtnGo.AddComponent<Button>();
            menuBtn.targetGraphic = menuBtnImg;
            menuBtn.onClick.AddListener(GoToMainMenu);
            menuBtnGo.AddComponent<UI.UICardHoverEffect>();

            GameObject menuBtnTextGo = new GameObject("Text");
            menuBtnTextGo.transform.SetParent(menuBtnGo.transform, false);
            RectTransform menuBtRect = menuBtnTextGo.AddComponent<RectTransform>();
            menuBtRect.anchorMin = Vector2.zero;
            menuBtRect.anchorMax = Vector2.one;
            menuBtRect.sizeDelta = Vector2.zero;

            Text menuBtnTxt = menuBtnTextGo.AddComponent<Text>();
            menuBtnTxt.font = font;
            menuBtnTxt.fontSize = 17;
            menuBtnTxt.fontStyle = FontStyle.Bold;
            menuBtnTxt.alignment = TextAnchor.MiddleCenter;
            menuBtnTxt.color = Color.white;
            menuBtnTxt.text = "MENÚ PRINCIPAL";
            menuBtnTxt.raycastTarget = false;

            gameOverPanel.SetActive(false);
        }

        private void EnsurePauseUI()
        {
            if (pausePanel != null)
            {
                pausePanel.SetActive(false);
                return;
            }

            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null) return;

            // Panel Contenedor de Pantalla Completa
            pausePanel = new GameObject("PauseModalPanel");
            pausePanel.transform.SetParent(canvas.transform, false);
            RectTransform panelRect = pausePanel.AddComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.sizeDelta = Vector2.zero;

            var bg = pausePanel.AddComponent<Image>();
            bg.color = new Color(0.02f, 0.03f, 0.06f, 0.92f);

            pauseCanvasGroup = pausePanel.AddComponent<CanvasGroup>();
            pauseCanvasGroup.alpha = 1f;
            pauseCanvasGroup.interactable = true;
            pauseCanvasGroup.blocksRaycasts = true;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

            // Caja Central / Tarjeta
            GameObject cardGo = new GameObject("PauseCard");
            cardGo.transform.SetParent(pausePanel.transform, false);
            RectTransform cardRect = cardGo.AddComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.anchoredPosition = Vector2.zero;
            cardRect.sizeDelta = new Vector2(480f, 520f);

            var cardBg = cardGo.AddComponent<Image>();
            cardBg.color = new Color(0.08f, 0.1f, 0.16f, 0.96f);

            // Título PAUSA
            GameObject titleGo = new GameObject("PauseTitle");
            titleGo.transform.SetParent(cardGo.transform, false);
            RectTransform titleRect = titleGo.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -25f);
            titleRect.sizeDelta = new Vector2(440f, 50f);

            Text titleTxt = titleGo.AddComponent<Text>();
            titleTxt.font = font;
            titleTxt.fontSize = 38;
            titleTxt.fontStyle = FontStyle.Bold;
            titleTxt.alignment = TextAnchor.MiddleCenter;
            titleTxt.color = new Color(0.1f, 0.9f, 1f);
            titleTxt.text = "PAUSA";

            // Subtítulo
            GameObject subGo = new GameObject("PauseSubtitle");
            subGo.transform.SetParent(cardGo.transform, false);
            RectTransform subRect = subGo.AddComponent<RectTransform>();
            subRect.anchorMin = new Vector2(0.5f, 1f);
            subRect.anchorMax = new Vector2(0.5f, 1f);
            subRect.pivot = new Vector2(0.5f, 1f);
            subRect.anchoredPosition = new Vector2(0f, -75f);
            subRect.sizeDelta = new Vector2(440f, 25f);

            Text subTxt = subGo.AddComponent<Text>();
            subTxt.font = font;
            subTxt.fontSize = 14;
            subTxt.alignment = TextAnchor.MiddleCenter;
            subTxt.color = new Color(0.7f, 0.8f, 0.9f);
            subTxt.text = "Partida detenida momentáneamente";

            // Función local para crear botones de la lista de pausa
            GameObject CreatePauseButton(string name, string label, Vector2 pos, Color btnColor, UnityEngine.Events.UnityAction onClick)
            {
                GameObject bGo = new GameObject(name);
                bGo.transform.SetParent(cardGo.transform, false);
                RectTransform bRect = bGo.AddComponent<RectTransform>();
                bRect.anchorMin = new Vector2(0.5f, 1f);
                bRect.anchorMax = new Vector2(0.5f, 1f);
                bRect.pivot = new Vector2(0.5f, 1f);
                bRect.anchoredPosition = pos;
                bRect.sizeDelta = new Vector2(380f, 44f);

                Image bImg = bGo.AddComponent<Image>();
                bImg.color = btnColor;

                Button b = bGo.AddComponent<Button>();
                b.targetGraphic = bImg;
                b.onClick.AddListener(onClick);
                bGo.AddComponent<UI.UICardHoverEffect>();

                GameObject tGo = new GameObject("Text");
                tGo.transform.SetParent(bGo.transform, false);
                RectTransform tRect = tGo.AddComponent<RectTransform>();
                tRect.anchorMin = Vector2.zero;
                tRect.anchorMax = Vector2.one;
                tRect.sizeDelta = Vector2.zero;

                Text tTxt = tGo.AddComponent<Text>();
                tTxt.font = font;
                tTxt.fontSize = 16;
                tTxt.fontStyle = FontStyle.Bold;
                tTxt.alignment = TextAnchor.MiddleCenter;
                tTxt.color = Color.white;
                tTxt.text = label;
                tTxt.raycastTarget = false;

                return bGo;
            }

            // 1. Reanudar
            CreatePauseButton("ResumeButton", "REANUDAR (ESC)", new Vector2(0f, -120f), new Color(0.12f, 0.65f, 0.85f, 0.95f), ResumeGame);
            // 2. Reiniciar
            CreatePauseButton("RestartButton", "REINICIAR PARTIDA", new Vector2(0f, -174f), new Color(0.24f, 0.32f, 0.42f, 0.95f), RestartGame);
            // 3. Menú Principal
            CreatePauseButton("MainMenuButton", "MENÚ PRINCIPAL", new Vector2(0f, -228f), new Color(0.24f, 0.32f, 0.42f, 0.95f), GoToMainMenu);
            // 4. Salir
            CreatePauseButton("QuitButton", "SALIR AL ESCRITORIO", new Vector2(0f, -282f), new Color(0.72f, 0.2f, 0.22f, 0.95f), QuitGame);

            // Tarjeta inferior de Controles
            GameObject controlsGo = new GameObject("ControlsInfoBox");
            controlsGo.transform.SetParent(cardGo.transform, false);
            RectTransform controlsRect = controlsGo.AddComponent<RectTransform>();
            controlsRect.anchorMin = new Vector2(0.5f, 0f);
            controlsRect.anchorMax = new Vector2(0.5f, 0f);
            controlsRect.pivot = new Vector2(0.5f, 0f);
            controlsRect.anchoredPosition = new Vector2(0f, 20f);
            controlsRect.sizeDelta = new Vector2(420f, 150f);

            var controlsBg = controlsGo.AddComponent<Image>();
            controlsBg.color = new Color(0.04f, 0.05f, 0.09f, 0.85f);

            GameObject ctGo = new GameObject("ControlsText");
            ctGo.transform.SetParent(controlsGo.transform, false);
            RectTransform ctRect = ctGo.AddComponent<RectTransform>();
            ctRect.anchorMin = Vector2.zero;
            ctRect.anchorMax = Vector2.one;
            ctRect.offsetMin = new Vector2(12f, 10f);
            ctRect.offsetMax = new Vector2(-12f, -10f);

            Text ctTxt = ctGo.AddComponent<Text>();
            ctTxt.font = font;
            ctTxt.fontSize = 13;
            ctTxt.alignment = TextAnchor.UpperLeft;
            ctTxt.lineSpacing = 1.25f;
            ctTxt.color = new Color(0.85f, 0.88f, 0.95f);
            ctTxt.text = "<color=#00E5FF><b>RECORDATORIO DE CONTROLES:</b></color>\n" +
                         "• <b>[W, A, S, D]:</b> Mover la nave en la arena\n" +
                         "• <b>[Clic Izquierdo]:</b> Disparar proyectiles primarios\n" +
                         "• <b>[Clic Derecho]:</b> Parry frontal (refleja y atonta)\n" +
                         "• <b>[E / Barra Espacio]:</b> Sobrecarga de Energía\n" +
                         "• <b>[ESC / P]:</b> Pausar o reanudar el juego";

            pausePanel.SetActive(false);
        }
        #endregion
    }
}
