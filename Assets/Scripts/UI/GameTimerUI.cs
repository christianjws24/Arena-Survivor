using ArenaSurvivor.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaSurvivor.UI
{
    /// <summary>
    /// UI básica y limpia para mostrar el tiempo transcurrido de supervivencia (Reloj de Juego).
    /// Consulta el DifficultyManager y actualiza la visualización con cero asignaciones de memoria
    /// innecesarias (actualización optimizada únicamente por segundo cumplido).
    /// </summary>
    public class GameTimerUI : MonoBehaviour
    {
        [Header("Referencias de UI")]
        [Tooltip("Texto donde se mostrará el tiempo formateado (MM:SS)")]
        [SerializeField] private Text timeText;

        [Tooltip("Texto opcional para mostrar la dificultad o minuto actual")]
        [SerializeField] private Text minuteBadgeText;

        private int _lastDisplayedSeconds = -1;

        public Text TimeText
        {
            get => timeText;
            set => timeText = value;
        }

        public Text MinuteBadgeText
        {
            get => minuteBadgeText;
            set => minuteBadgeText = value;
        }

        private void Awake()
        {
            if (timeText == null)
            {
                timeText = GetComponentInChildren<Text>();
            }
        }

        private void Start()
        {
            UpdateDisplay(0);
        }

        private void Update()
        {
            if (DifficultyManager.Instance == null) return;

            int currentTotalSeconds = (int)DifficultyManager.Instance.ElapsedTime;
            if (currentTotalSeconds != _lastDisplayedSeconds)
            {
                _lastDisplayedSeconds = currentTotalSeconds;
                UpdateDisplay(currentTotalSeconds);
            }
        }

        private void UpdateDisplay(int totalSeconds)
        {
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;

            if (timeText != null)
            {
                timeText.text = $"{minutes:00}:{seconds:00}";
            }

            if (minuteBadgeText != null && DifficultyManager.Instance != null)
            {
                float hpMult = DifficultyManager.Instance.GetHealthMultiplier();
                minuteBadgeText.text = hpMult > 1.0f ? $"x{hpMult:F2} DIFICULTAD" : "TIEMPO";
            }
        }
    }
}
