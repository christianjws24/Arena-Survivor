using System;
using ArenaSurvivor.Combat;
using ArenaSurvivor.Pool;
using UnityEngine;

namespace ArenaSurvivor.Entities
{
    /// <summary>
    /// Gestiona el flujo de energía del jugador.
    /// Se recarga pasivamente al derrotar enemigos (+5%) y activamente con Parries exitosos (+35%).
    /// Al llegar al 100%, permite activar el "Protocolo de Extracción" con la tecla 'E'.
    /// 
    /// Restricciones estrictas:
    /// 1. Cuando la energía llega al 100% (mejora lista), no se puede recargar más.
    /// 2. Cuando la Zona de Extracción está activa en el mapa (incluso estando dentro), NO se puede recargar energía.
    /// 3. Escalado progresivo: Cada extracción completada incrementa la energía requerida para la siguiente.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerEnergy : MonoBehaviour
    {
        [Header("Configuración de Energía y Escalado")]
        [Tooltip("Energía base requerida para la primera extracción")]
        [SerializeField] private float baseMaxEnergy = 120f;

        [Tooltip("Incremento plano base de energía requerida tras cada extracción")]
        [SerializeField] private float energyIncreasePerExtraction = 40f;

        [Tooltip("Multiplicador exponencial compuesto por cada extracción completada (ej. 1.25 para +25% acumulativo)")]
        [SerializeField] private float exponentialScalingFactor = 1.25f;

        [Tooltip("Ganancia de energía por cada enemigo eliminado")]
        [SerializeField] private float killEnergyGain = 4f;

        [Tooltip("Ganancia de energía al acertar un Parry")]
        [SerializeField] private float parryEnergyGain = 25f;

        [Header("Protocolo de Extracción")]
        [Tooltip("Si es true, la zona de extracción se despliega automáticamente al alcanzar el 100% de energía.")]
        [SerializeField] private bool autoDeployExtraction = true;

        [Tooltip("Tag del pool para la zona de extracción")]
        [SerializeField] private string extractionZonePoolTag = "ExtractionZone";

        [Tooltip("Tecla para activar la zona cuando la energía esté al 100% (si autoDeployExtraction está desactivado)")]
#pragma warning disable 0414
        [SerializeField] private KeyCode extractionKey = KeyCode.E;
#pragma warning restore 0414

        // Notifica a la UI: currentEnergy, maxEnergy (Manteniendo desacoplada la vista de la lógica)
        public event Action<float, float> OnEnergyChanged;
        public event Action OnExtractionReady;
        public event Action OnExtractionStarted;
        public event Action OnExtractionEnded;

        private float _currentEnergy;
        private float _currentMaxEnergy;
        private int _extractionsCompleted;
        private bool _isZoneActive;
        private PlayerController _player;

        public float CurrentEnergy => _currentEnergy;
        public float MaxEnergy => _currentMaxEnergy;
        public bool IsEnergyFull => _currentEnergy >= _currentMaxEnergy;
        public bool IsZoneActive => _isZoneActive || ExtractionZone.IsZoneActive;
        public int ExtractionsCompleted => _extractionsCompleted;

        /// <summary>
        /// Calcula la energía máxima requerida para una extracción dada usando escalado compuesto:
        /// Base + (incremento plano) escalado por un factor exponencial acumulativo.
        /// </summary>
        public float CalculateMaxEnergyForExtraction(int extractionIndex)
        {
            float flatScaled = baseMaxEnergy + (extractionIndex * energyIncreasePerExtraction);
            float exponentialMultiplier = Mathf.Pow(exponentialScalingFactor, extractionIndex);
            return Mathf.Round(flatScaled * exponentialMultiplier);
        }

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
            _currentMaxEnergy = CalculateMaxEnergyForExtraction(0);
        }

        private void OnEnable()
        {
            // Suscripción a eventos desacoplados
            Enemy.OnEnemyDefeated += HandleEnemyDefeated;
            ExtractionZone.OnZoneSpawned += HandleZoneSpawned;
            ExtractionZone.OnZoneClosed += HandleZoneClosed;

            if (_player != null)
            {
                _player.OnParrySuccess += HandleParrySuccess;
            }
        }

        private void OnDisable()
        {
            Enemy.OnEnemyDefeated -= HandleEnemyDefeated;
            ExtractionZone.OnZoneSpawned -= HandleZoneSpawned;
            ExtractionZone.OnZoneClosed -= HandleZoneClosed;

            if (_player != null)
            {
                _player.OnParrySuccess -= HandleParrySuccess;
            }
        }

        private void Start()
        {
            _currentEnergy = 0f;
            _currentMaxEnergy = CalculateMaxEnergyForExtraction(0);
            NotifyEnergyChanged();
        }

        private void Update()
        {
            if (_player != null && _player.IsDead) return;

            // Si la energía está llena y no hay otra zona activa, desplegar la zona
            if (IsEnergyFull && !IsZoneActive)
            {
                if (autoDeployExtraction)
                {
                    ActivateExtractionProtocol();
                    return;
                }

                bool triggerPressed = false;

#if ENABLE_INPUT_SYSTEM
                if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.eKey.wasPressedThisFrame)
                {
                    triggerPressed = true;
                }
                if (UnityEngine.InputSystem.Gamepad.current != null && UnityEngine.InputSystem.Gamepad.current.buttonNorth.wasPressedThisFrame)
                {
                    triggerPressed = true;
                }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
                if (Input.GetKeyDown(extractionKey))
                {
                    triggerPressed = true;
                }
#endif

                if (triggerPressed)
                {
                    ActivateExtractionProtocol();
                }
            }
        }

        private void HandleEnemyDefeated(Enemy enemy)
        {
            AddEnergy(killEnergyGain);
        }

        private void HandleParrySuccess()
        {
            AddEnergy(parryEnergyGain);
        }

        private void HandleZoneSpawned()
        {
            _isZoneActive = true;
            NotifyEnergyChanged();
        }

        private void HandleZoneClosed()
        {
            _isZoneActive = false;
            _extractionsCompleted++;

            // Escalado de dificultad: Cada ciclo requiere progresivamente más energía (curva compuesta)
            _currentMaxEnergy = CalculateMaxEnergyForExtraction(_extractionsCompleted);

            OnExtractionEnded?.Invoke();
            NotifyEnergyChanged();
            Debug.Log($"<color=cyan><b>[ENERGÍA DESBLOQUEADA]</b> Zona cerrada. Siguiente objetivo de extracción (Nivel {_extractionsCompleted + 1}): {_currentMaxEnergy} energía.</color>");
        }

        public void AddEnergy(float amount)
        {
            // REGLA: Si la energía ya está al máximo O la zona está activa en el mapa, NO se puede recargar energía
            if (IsEnergyFull || IsZoneActive) return;

            float previousEnergy = _currentEnergy;
            _currentEnergy = Mathf.Clamp(_currentEnergy + amount, 0f, _currentMaxEnergy);

            if (_currentEnergy != previousEnergy)
            {
                NotifyEnergyChanged();

                if (IsEnergyFull)
                {
                    if (autoDeployExtraction)
                    {
                        Debug.Log("<color=cyan><b>[PROTOCOLO DE EXTRACCIÓN ACTIVADO]</b> Energía al 100%. Desplegando Zona de Extracción automáticamente...</color>");
                        ActivateExtractionProtocol();
                    }
                    else
                    {
                        Debug.Log("<color=cyan><b>[PROTOCOLO DE EXTRACCIÓN LISTO]</b> Presiona 'E' para desplegar la Zona de Extracción.</color>");
                        OnExtractionReady?.Invoke();
                    }
                }
            }
        }

        private void ActivateExtractionProtocol()
        {
            // Reiniciar energía a 0 y marcar zona activa
            _currentEnergy = 0f;
            _isZoneActive = true;
            NotifyEnergyChanged();
            OnExtractionStarted?.Invoke();

            // Instanciar la Zona de Extracción desde el ObjectPooler en la posición del jugador
            if (ObjectPooler.Instance != null)
            {
                ObjectPooler.Instance.SpawnFromPool(extractionZonePoolTag, transform.position, Quaternion.identity);
            }
            else
            {
                Debug.LogWarning("[PlayerEnergy] ObjectPooler no encontrado para spawnear la Zona de Extracción.");
            }
        }

        private void NotifyEnergyChanged()
        {
            OnEnergyChanged?.Invoke(_currentEnergy, _currentMaxEnergy);
        }
    }
}
