using System;
using System.Collections.Generic;
using ArenaSurvivor.Combat;
using ArenaSurvivor.Managers;
using ArenaSurvivor.Pool;
using ArenaSurvivor.UI;
using ArenaSurvivor.Upgrades;
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

        [Tooltip("Energía (EXP) que da un enemigo de referencia con scoreValue = 10 (ej. Grunt). Cada enemigo da esta cantidad x (scoreValue / 10), "
               + "así que la experiencia por enemigo se ajusta con 'scoreValue' en su EnemyData.")]
        [SerializeField] private float killEnergyGain = 4f;

        [Tooltip("Tope de energía que puede dar un solo enemigo (evita que un jefe llene la barra varias veces)")]
        [SerializeField] private float maxEnergyPerKill = 40f;

        [Tooltip("Ganancia de energía al acertar un Parry")]
        [SerializeField] private float parryEnergyGain = 25f;

        [Header("Subida de Nivel Automática")]
        [Tooltip("Radio de la onda de choque de aniquilación al subir de nivel")]
        [SerializeField] private float shockwaveRadius = 10f;

        [Tooltip("Intensidad del temblor de cámara al detonar la onda de choque")]
        [SerializeField] private float shockwaveShakeIntensity = 0.55f;

        [Tooltip("Duración del temblor de cámara al detonar la onda de choque")]
        [SerializeField] private float shockwaveShakeDuration = 0.45f;

        // Notifica a la UI: currentEnergy, maxEnergy (Manteniendo desacoplada la vista de la lógica)
        public event Action<float, float> OnEnergyChanged;
        public event Action OnExtractionReady;
        public event Action OnExtractionStarted;
        public event Action OnExtractionEnded;

        private float _currentEnergy;
        private float _currentMaxEnergy;
        // Experiencia que sobra al llenar la barra (o que llega mientras se elige mejora, ej. la onda de choque):
        // se suma al siguiente nivel en lugar de perderse
        private float _overflowEnergy;
        private int _extractionsCompleted;
        private bool _isLevelingUp;
        private PlayerController _player;

        private readonly List<Collider2D> _shockwaveBuffer = new List<Collider2D>(64);
        private ContactFilter2D _enemyFilter;

        public float CurrentEnergy => _currentEnergy;
        public float MaxEnergy => _currentMaxEnergy;
        public bool IsEnergyFull => _currentEnergy >= _currentMaxEnergy;
        public bool IsZoneActive => false; // Zona eliminada según requerimiento
        public int ExtractionsCompleted => _extractionsCompleted;

        /// <summary>
        /// Calcula la energía máxima requerida para una subida de nivel usando escalado compuesto:
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

            _enemyFilter = new ContactFilter2D();
            _enemyFilter.useTriggers = true;
            _enemyFilter.useLayerMask = false;
        }

        private void OnEnable()
        {
            // Suscripción a eventos desacoplados
            Enemy.OnEnemyDefeated += HandleEnemyDefeated;

            if (_player != null)
            {
                _player.OnParrySuccess += HandleParrySuccess;
            }
        }

        private void OnDisable()
        {
            Enemy.OnEnemyDefeated -= HandleEnemyDefeated;

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

        private void HandleEnemyDefeated(Enemy enemy)
        {
            // Los proyectiles fantasma de 'mirror_refractor' no son enemigos reales: no dan experiencia
            if (enemy != null && enemy.IsPhantomProjectile) return;

            // La experiencia depende del enemigo: scoreValue de su EnemyData (Grunt = 10 = energía de referencia)
            float scoreScale = enemy != null && enemy.Data != null ? enemy.Data.scoreValue / 10f : 1f;
            AddEnergy(Mathf.Min(killEnergyGain * scoreScale, maxEnergyPerKill));
        }

        private void HandleParrySuccess()
        {
            AddEnergy(parryEnergyGain);
        }

        public void AddEnergy(float amount)
        {
            if (_player != null && _player.IsDead) return;

            if (_isLevelingUp)
            {
                if (amount > 0f) _overflowEnergy += amount;
                return;
            }

            float previousEnergy = _currentEnergy;
            float total = _currentEnergy + amount;
            if (total > _currentMaxEnergy)
            {
                _overflowEnergy += total - _currentMaxEnergy;
            }
            _currentEnergy = Mathf.Clamp(total, 0f, _currentMaxEnergy);

            if (_currentEnergy != previousEnergy)
            {
                NotifyEnergyChanged();

                if (IsEnergyFull)
                {
                    OnExtractionReady?.Invoke();
                    TriggerLevelUp();
                }
            }
        }

        private void TriggerLevelUp()
        {
            if (_isLevelingUp) return;
            _isLevelingUp = true;

            Debug.Log("<color=cyan><b>[SUBIDA DE NIVEL]</b> ⭐ ¡Experiencia máxima alcanzada! Abriendo selección de mejora automáticamente...</color>");
            OnExtractionStarted?.Invoke();

            if (LevelUpUIManager.Instance != null)
            {
                LevelUpUIManager.Instance.OpenLevelUpModal(OnUpgradeChosenAndResumed);
            }
            else
            {
                // Fallback por si la UI no está presente
                UpgradeManager.Instance?.ApplyRandomUpgrade();
                OnUpgradeChosenAndResumed();
            }
        }

        /// <summary>
        /// Invocado cuando el jugador elige su habilidad en el modal:
        /// Detona la explosión destructiva alrededor del jugador, avanza nivel y reanuda el juego.
        /// </summary>
        private void OnUpgradeChosenAndResumed()
        {
            // 1. Detonar onda de choque destructiva (Shockwave EMP) centrada en el jugador
            //    (la experiencia de los enemigos que destruye se acumula como sobrante)
            TriggerShockwaveEMP();

            // 2. Incrementar nivel y calcular nuevo requerimiento de energía
            _extractionsCompleted++;
            _currentEnergy = 0f;
            _currentMaxEnergy = CalculateMaxEnergyForExtraction(_extractionsCompleted);
            _isLevelingUp = false;

            OnExtractionEnded?.Invoke();
            NotifyEnergyChanged();

            Debug.Log($"<color=lime><b>[NIVEL ALCANZADO]</b> ¡Onda expansiva detonada! Siguiente nivel ({_extractionsCompleted + 1}): {_currentMaxEnergy} EXP requerida.</color>");

            // 3. La experiencia sobrante pasa al siguiente nivel (como mucho un nivel completo de golpe)
            float carried = Mathf.Min(_overflowEnergy, _currentMaxEnergy);
            _overflowEnergy = 0f;
            if (carried > 0f)
            {
                Debug.Log($"<color=lime><b>[EXP]</b> +{carried:0} de experiencia sobrante trasladada al siguiente nivel.</color>");
                AddEnergy(carried);
            }
        }

        private void TriggerShockwaveEMP()
        {
            Vector2 center = transform.position;

            // 1. Game Feel: Temblor de cámara dramático
            if (CameraShake.Instance != null)
            {
                CameraShake.Instance.Shake(shockwaveShakeDuration, shockwaveShakeIntensity);
            }

            // 2. Audio SFX
            AudioManager.Instance?.PlayShockwaveEMP();

            // 3. Onda de Choque (EMP): Destruye a todos los enemigos en el radio
            int hits = Physics2D.OverlapCircle(center, shockwaveRadius, _enemyFilter, _shockwaveBuffer);
            for (int i = 0; i < hits; i++)
            {
                Collider2D col = _shockwaveBuffer[i];
                if (col == null || !col.gameObject.activeInHierarchy) continue;

                if (col.TryGetComponent(out BossEnergyShieldPart shieldPart))
                {
                    shieldPart.TakeDamage(250f);
                    continue;
                }

                if (col.TryGetComponent(out Enemy enemy) && enemy.CurrentState != EnemyState.Dead)
                {
                    if (enemy.IsHeavy)
                    {
                        enemy.TakeDamage(250f);
                    }
                    else
                    {
                        enemy.Die();
                    }
                }
            }

            // 4. VFX de Onda Expansiva desde ObjectPooler
            if (ObjectPooler.Instance != null)
            {
                if (ObjectPooler.Instance.HasPool("ShockwaveVFX"))
                {
                    ObjectPooler.Instance.SpawnFromPool("ShockwaveVFX", center, Quaternion.identity);
                }

                if (ObjectPooler.Instance.HasPool("DeathVFX"))
                {
                    GameObject vfxObj = ObjectPooler.Instance.SpawnFromPool("DeathVFX", center, Quaternion.identity);
                    if (vfxObj != null && vfxObj.TryGetComponent(out DeathVFX vfx))
                    {
                        vfx.PlayEffect(new Color(0.2f, 0.9f, 1f), 60, 4.0f);
                    }
                }
            }
        }

        private void NotifyEnergyChanged()
        {
            OnEnergyChanged?.Invoke(_currentEnergy, _currentMaxEnergy);
        }
    }
}
