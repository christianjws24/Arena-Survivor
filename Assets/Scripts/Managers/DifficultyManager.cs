using System;
using ArenaSurvivor.Entities;
using UnityEngine;

namespace ArenaSurvivor.Managers
{
    /// <summary>
    /// Reloj global y gestor de escalado dinámico de dificultad (DifficultyManager).
    /// Lleva el registro del tiempo de supervivencia de la partida y calcula multiplicadores
    /// de salud y velocidad para los enemigos conforme avanzan los minutos.
    /// </summary>
    [DisallowMultipleComponent]
    public class DifficultyManager : MonoBehaviour
    {
        private static DifficultyManager _instance;
        public static DifficultyManager Instance
        {
            get
            {
                if (_instance == null)
                {
#if UNITY_2023_1_OR_NEWER
                    _instance = UnityEngine.Object.FindFirstObjectByType<DifficultyManager>();
#else
                    _instance = UnityEngine.Object.FindObjectOfType<DifficultyManager>();
#endif
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("DifficultyManager");
                        _instance = go.AddComponent<DifficultyManager>();
                    }
                }
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Configuración de Escalado por Minuto")]
        [Tooltip("Incremento porcentual de salud máxima de los enemigos por cada minuto (0.15 = +15%/min)")]
        [SerializeField] private float healthIncreasePerMinute = 0.15f;

        [Tooltip("Incremento porcentual de velocidad de los enemigos por cada minuto (0.05 = +5%/min)")]
        [SerializeField] private float speedIncreasePerMinute = 0.05f;

        [Header("Estado de Tiempo")]
        [SerializeField] private float elapsedTime;
        [SerializeField] private bool isTimerRunning = true;

        // Eventos para UI o sistemas que reaccionen a cada minuto cumplido
        public event Action<int> OnMinutePassed; // minuteNumber

        private int _lastReportedMinute = 0;

        public float ElapsedTime => elapsedTime;
        public int ElapsedMinutes => (int)(elapsedTime / 60f);
        public int ElapsedSeconds => (int)(elapsedTime % 60f);
        public string FormattedTime => string.Format("{0:00}:{1:00}", ElapsedMinutes, ElapsedSeconds);

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
        }

        private void Start()
        {
            if (PlayerController.Instance != null)
            {
                PlayerController.Instance.OnPlayerDeath += StopTimer;
            }
        }

        private void OnDestroy()
        {
            if (PlayerController.Instance != null)
            {
                PlayerController.Instance.OnPlayerDeath -= StopTimer;
            }
        }

        private void Update()
        {
            if (!isTimerRunning) return;

            elapsedTime += Time.deltaTime;

            int currentMinute = (int)(elapsedTime / 60f);
            if (currentMinute > _lastReportedMinute)
            {
                _lastReportedMinute = currentMinute;
                OnMinutePassed?.Invoke(_lastReportedMinute);
                Debug.Log($"<color=cyan><b>[DifficultyManager]</b> ⏱️ Minuto {currentMinute} alcanzado! Multiplicador HP: x{GetHealthMultiplier():F2}, Velocidad: x{GetSpeedMultiplier():F2}</color>");
            }
        }

        /// <summary>
        /// Multiplicador de salud de enemigos: +15% por cada 60s transcurridos de juego.
        /// Minuto 0: x1.00 | Minuto 1: x1.15 | Minuto 5: x1.75
        /// </summary>
        public float GetHealthMultiplier()
        {
            float minutes = elapsedTime / 60f;
            return 1f + (minutes * healthIncreasePerMinute);
        }

        /// <summary>
        /// Multiplicador de velocidad de movimiento: +5% por cada 60s transcurridos de juego.
        /// Minuto 0: x1.00 | Minuto 1: x1.05 | Minuto 5: x1.25
        /// </summary>
        public float GetSpeedMultiplier()
        {
            float minutes = elapsedTime / 60f;
            return 1f + (minutes * speedIncreasePerMinute);
        }

        public void StopTimer()
        {
            isTimerRunning = false;
        }

        public void ResumeTimer()
        {
            isTimerRunning = true;
        }

        public void ResetTimer()
        {
            elapsedTime = 0f;
            _lastReportedMinute = 0;
            isTimerRunning = true;
        }
    }
}
