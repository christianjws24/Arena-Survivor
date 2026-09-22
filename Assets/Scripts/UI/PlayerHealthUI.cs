using ArenaSurvivor.Entities;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaSurvivor.UI
{
    /// <summary>
    /// UI básica y reactiva para visualizar la salud y vidas restantes del jugador.
    /// Se suscribe al evento OnLivesChanged de PlayerController.
    /// </summary>
    public class PlayerHealthUI : MonoBehaviour
    {
        [Header("Referencias")]
        [SerializeField] private PlayerController player;

        [Header("Componentes de UI")]
        [SerializeField] private Text livesText;
        [SerializeField] private Slider healthSlider;
        [SerializeField] private Image fillImage;

        [Header("Colores de Estado")]
        [SerializeField] private Color fullHealthColor = new Color(0.2f, 0.95f, 0.4f);   // Verde esmeralda
        [SerializeField] private Color midHealthColor = new Color(0.98f, 0.85f, 0.15f); // Amarillo
        [SerializeField] private Color lowHealthColor = new Color(0.95f, 0.25f, 0.25f); // Rojo crítico

        private void Awake()
        {
            FindPlayerIfNeeded();
            ConfigureSlider();
        }

        private void ConfigureSlider()
        {
            if (healthSlider != null)
            {
                healthSlider.interactable = false;
                healthSlider.transition = Selectable.Transition.None;
                var nav = healthSlider.navigation;
                nav.mode = Navigation.Mode.None;
                healthSlider.navigation = nav;
            }
        }

        private void OnEnable()
        {
            FindPlayerIfNeeded();
            if (player != null)
            {
                player.OnLivesChanged += UpdateHealthDisplay;
                UpdateHealthDisplay(player.CurrentLives, player.MaxLives);
            }
        }

        private void OnDisable()
        {
            if (player != null)
            {
                player.OnLivesChanged -= UpdateHealthDisplay;
            }
        }

        private void Start()
        {
            if (player != null)
            {
                UpdateHealthDisplay(player.CurrentLives, player.MaxLives);
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

        /// <summary>
        /// Actualiza los textos, slider y color según las vidas actuales.
        /// </summary>
        public void UpdateHealthDisplay(int currentLives, int maxLives)
        {
            float ratio = maxLives > 0 ? Mathf.Clamp01((float)currentLives / maxLives) : 0f;

            // 1. Barra de salud
            if (healthSlider != null)
            {
                healthSlider.value = ratio;
            }

            // 2. Color dinámico según salud restante
            Color targetColor = fullHealthColor;
            if (currentLives <= 1)
            {
                targetColor = lowHealthColor;
            }
            else if (currentLives < maxLives)
            {
                targetColor = midHealthColor;
            }

            if (fillImage != null)
            {
                fillImage.color = targetColor;
            }

            // 3. Texto descriptivo con iconos de corazón
            if (livesText != null)
            {
                string hearts = "";
                for (int i = 0; i < maxLives; i++)
                {
                    hearts += (i < currentLives) ? "♥ " : "♡ ";
                }

                livesText.text = $"VIDA: {hearts.Trim()} ({currentLives}/{maxLives})";
                livesText.color = targetColor;
            }
        }
    }
}
