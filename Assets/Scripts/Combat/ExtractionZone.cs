using System;
using System.Collections.Generic;
using ArenaSurvivor.Entities;
using ArenaSurvivor.Pool;
using ArenaSurvivor.UI;
using ArenaSurvivor.Upgrades;
using UnityEngine;

namespace ArenaSurvivor.Combat
{
    /// <summary>
    /// Zona de Extracción táctica con mecánica de riesgo y recompensa.
    /// Requiere permanecer dentro durante 5 segundos continuos.
    /// Si el jugador sale antes de completarlo, el progreso se reinicia a 0.
    /// Al completarse: emite una onda de choque destructiva, sacude la cámara y otorga una mejora al jugador.
    /// Compatible 100% con Object Pooling.
    /// </summary>
    [RequireComponent(typeof(CircleCollider2D))]
    public class ExtractionZone : MonoBehaviour, IPoolable
    {
        [Header("Tiempos y Parámetros")]
        [Tooltip("Tiempo continuo en segundos que el jugador debe permanecer dentro")]
        [SerializeField] private float requiredTime = 5f;

        [Tooltip("Radio de la onda de choque de aniquilación al completarse la extracción")]
        [SerializeField] private float shockwaveRadius = 10f;

        [Header("Referencias Visuales")]
        [SerializeField] private SpriteRenderer zoneVisual;
        [SerializeField] private Color idleColor = new Color(0.2f, 0.8f, 1f, 0.35f);
        [SerializeField] private Color activeColor = new Color(0.3f, 1f, 0.4f, 0.5f);

        [Header("Juice (Game Feel)")]
        [SerializeField] private float completionShakeDuration = 0.45f;
        [SerializeField] private float completionShakeIntensity = 0.55f;

        // Eventos estáticos globales para notificar a PlayerEnergy y la UI
        public static event Action OnZoneSpawned;
        public static event Action OnZoneClosed;
        public static bool IsZoneActive { get; private set; }

        // Eventos de instancia para UI desacoplada
        public event Action<float, float> OnProgressChanged; // currentSeconds, requiredSeconds
        public event Action OnExtractionCompleted;

        private float _currentTimer;
        private bool _isPlayerInside;
        private bool _isCompleted;
        private CircleCollider2D _collider;

        private readonly List<Collider2D> _shockwaveBuffer = new List<Collider2D>(64);
        private ContactFilter2D _enemyFilter;

        public float Progress => Mathf.Clamp01(_currentTimer / requiredTime);
        public float RemainingTime => Mathf.Max(0f, requiredTime - _currentTimer);

        private void Awake()
        {
            _collider = GetComponent<CircleCollider2D>();
            _collider.isTrigger = true;
            if (zoneVisual == null) zoneVisual = GetComponentInChildren<SpriteRenderer>();

            _enemyFilter = new ContactFilter2D();
            _enemyFilter.useTriggers = true;
            _enemyFilter.useLayerMask = false;
        }

        public void OnSpawnFromPool()
        {
            _currentTimer = 0f;
            _isPlayerInside = false;
            _isCompleted = false;

            IsZoneActive = true;
            OnZoneSpawned?.Invoke();

            if (zoneVisual != null)
            {
                zoneVisual.color = idleColor;
            }

            OnProgressChanged?.Invoke(_currentTimer, requiredTime);
        }

        public void OnReturnToPool()
        {
            _currentTimer = 0f;
            _isPlayerInside = false;
            _isCompleted = true;

            if (IsZoneActive)
            {
                IsZoneActive = false;
                OnZoneClosed?.Invoke();
            }
        }

        private void OnDisable()
        {
            if (IsZoneActive)
            {
                IsZoneActive = false;
                OnZoneClosed?.Invoke();
            }
            Managers.AudioManager.Instance?.SetExtractionMuffle(false);
        }

        private void Update()
        {
            if (_isCompleted) return;

            // Modificador de Zona: 'scorched_earth' quema por DPS a los enemigos dentro del perímetro
            int scorchedLevel = UpgradeManager.Instance != null ? UpgradeManager.Instance.GetLevel("scorched_earth") : 0;
            if (scorchedLevel > 0)
            {
                float dps = 15f * scorchedLevel;
                DamageEnemiesInsideZone(dps * Time.deltaTime);
            }

            if (_isPlayerInside)
            {
                _currentTimer += Time.deltaTime;
                OnProgressChanged?.Invoke(_currentTimer, requiredTime);

                // Feedback visual dinámico que pulsa conforme se acerca al 100%
                if (zoneVisual != null)
                {
                    float pulse = Mathf.PingPong(Time.time * 4f, 0.2f);
                    Color baseCol = scorchedLevel > 0 ? new Color(1f, 0.5f, 0.2f, 0.6f) : activeColor;
                    zoneVisual.color = new Color(baseCol.r, baseCol.g, baseCol.b, baseCol.a + pulse);
                }

                if (_currentTimer >= requiredTime)
                {
                    CompleteExtraction();
                }
            }
        }

        private void DamageEnemiesInsideZone(float damage)
        {
            float radius = (_collider != null ? _collider.radius : 0.5f) * transform.localScale.x;
            int count = Physics2D.OverlapCircle(transform.position, radius, _enemyFilter, _shockwaveBuffer);
            for (int i = 0; i < count; i++)
            {
                Collider2D col = _shockwaveBuffer[i];
                if (col != null && col.TryGetComponent(out Enemy enemy) && enemy.CurrentState == EnemyState.Chasing)
                {
                    enemy.TakeDamage(damage);
                }
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            CheckPlayerPresence(other, true);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            CheckPlayerPresence(other, true);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            CheckPlayerPresence(other, false);
        }

        private void CheckPlayerPresence(Collider2D col, bool isInside)
        {
            if (_isCompleted) return;

            if (col.CompareTag("Player") || col.TryGetComponent(out PlayerController _))
            {
                if (isInside)
                {
                    _isPlayerInside = true;
                    Managers.AudioManager.Instance?.SetExtractionMuffle(true);
                }
                else
                {
                    // Castigo por huir: Reinicio inmediato del temporizador
                    _isPlayerInside = false;
                    _currentTimer = 0f;
                    Managers.AudioManager.Instance?.SetExtractionMuffle(false);

                    if (zoneVisual != null)
                    {
                        zoneVisual.color = idleColor;
                    }

                    OnProgressChanged?.Invoke(_currentTimer, requiredTime);
                    Debug.Log("<color=yellow><b>[EXTRACCIÓN CANCELADA]</b> Has salido del perímetro. Temporizador reiniciado.</color>");
                }
            }
        }

        private void CompleteExtraction()
        {
            if (_isCompleted) return;
            _isCompleted = true;

            Debug.Log("<color=lime><b>[EXTRACCIÓN EXITOSA]</b> Protocolo completado. Pausando juego para selección de mejora...</color>");

            // Si el LevelUpUIManager existe en escena, abre el modal y espera a que el jugador elija
            if (LevelUpUIManager.Instance != null)
            {
                LevelUpUIManager.Instance.OpenLevelUpModal(OnUpgradeChosenAndResumed);
            }
            else
            {
                // Fallback directo por si no hay UI en la escena
                if (UpgradeManager.Instance != null)
                {
                    UpgradeManager.Instance.ApplyRandomUpgrade();
                }
                OnUpgradeChosenAndResumed();
            }
        }

        /// <summary>
        /// Invocado cuando el jugador elige su mejora en el modal, reanudando la explosión y el ciclo de juego.
        /// </summary>
        private void OnUpgradeChosenAndResumed()
        {
            OnExtractionCompleted?.Invoke();

            // 1. Game Feel: Temblor de impacto dramático
            if (CameraShake.Instance != null)
            {
                CameraShake.Instance.Shake(completionShakeDuration, completionShakeIntensity);
            }

            // 2. Onda de Choque (EMP): Destruye a todos los enemigos en el radio
            TriggerShockwaveEMP();

            // 3. Devolver la zona al ObjectPooler
            if (ObjectPooler.Instance != null)
            {
                ObjectPooler.Instance.ReturnToPool(gameObject);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        private void TriggerShockwaveEMP()
        {
            Vector2 center = transform.position;
            int hits = Physics2D.OverlapCircle(center, shockwaveRadius, _enemyFilter, _shockwaveBuffer);

            for (int i = 0; i < hits; i++)
            {
                Collider2D col = _shockwaveBuffer[i];
                if (col == null || !col.gameObject.activeInHierarchy) continue;

                if (col.TryGetComponent(out Enemy enemy) && enemy.CurrentState != EnemyState.Dead)
                {
                    enemy.Die();
                }
            }

            // Modificador 'stasis_nova': Aplica congelación/estasis masiva (75% slow) al resto de la arena
            bool hasStasisNova = UpgradeManager.Instance != null && UpgradeManager.Instance.GetLevel("stasis_nova") > 0;
            if (hasStasisNova)
            {
                Collider2D[] allArenaEnemies = Physics2D.OverlapCircleAll(center, 35f, _enemyFilter.layerMask);
                for (int i = 0; i < allArenaEnemies.Length; i++)
                {
                    if (allArenaEnemies[i] != null && allArenaEnemies[i].TryGetComponent(out Enemy arenaEnemy))
                    {
                        if (arenaEnemy.CurrentState == EnemyState.Chasing)
                        {
                            arenaEnemy.ApplyStasis(6f, 0.25f);
                        }
                    }
                }
                Debug.Log("<color=cyan><b>[ESTALLIDO NOVA]</b> ¡Estasis masiva aplicada a todos los enemigos restantes por 6s!</color>");
            }

            // Efecto de partículas masivo en el centro
            if (ObjectPooler.Instance != null && ObjectPooler.Instance.HasPool("DeathVFX"))
            {
                GameObject vfxObj = ObjectPooler.Instance.SpawnFromPool("DeathVFX", transform.position, Quaternion.identity);
                if (vfxObj != null && vfxObj.TryGetComponent(out DeathVFX vfx))
                {
                    Color effectColor = hasStasisNova ? new Color(0.25f, 0.85f, 1f) : new Color(0.4f, 1f, 0.6f);
                    vfx.PlayEffect(effectColor, 55, 3.5f);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, shockwaveRadius);
        }
    }
}
