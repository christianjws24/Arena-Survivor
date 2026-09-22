using System;
using System.Collections;
using System.Collections.Generic;
using ArenaSurvivor.Combat;
using ArenaSurvivor.Pool;
using ArenaSurvivor.Upgrades;
using UnityEngine;

namespace ArenaSurvivor.Entities
{
    public enum ParryState
    {
        Ready,     // Disponible para activarse
        Active,    // Ventana activa de desvío (0.2s)
        Cooldown   // Penalización por fallo (1.0s)
    }

    /// <summary>
    /// Controlador principal del jugador (Círculo).
    /// Gestiona: movimiento físico 2D, sistema de auto-aim con cero GC allocations,
    /// disparo automático, 3 vidas, I-Frames con parpadeo visual, feedback de cámara
    /// y mecánica activa de Parry (con Hit Stop y Contraataque de Billar).
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CircleCollider2D))]
    public class PlayerController : MonoBehaviour
    {
        public static PlayerController Instance { get; private set; }

        [Header("Movimiento")]
        [SerializeField] private float moveSpeed = 6f;

        [Header("Auto-Aim y Disparo")]
        [Tooltip("Tag del pool de balas a usar")]
        [SerializeField] private string bulletPoolTag = "PlayerBullet";
        [Tooltip("Radio de detección de enemigos")]
        [SerializeField] private float detectionRadius = 9f;
        [Tooltip("Cadencia de disparo (disparos por segundo)")]
        [SerializeField] private float fireRate = 2.5f;
        [Tooltip("Punto de salida del proyectil (si es null usa la posición del jugador)")]
        [SerializeField] private Transform firePoint;
        [Tooltip("Capa (LayerMask) asignada a los enemigos")]
        [SerializeField] private LayerMask enemyLayer;

        [Header("Mecánica de Parry")]
        [Tooltip("Hitbox circular de detección de Parry")]
        [SerializeField] private ParryHitbox parryHitbox;
        [Tooltip("Duración exacta de la ventana activa en segundos")]
        [SerializeField] private float parryActiveDuration = 0.2f;
        [Tooltip("Tiempo de recuperación tras un Parry exitoso antes de poder realizar otro")]
        [SerializeField] private float parrySuccessCooldown = 0.45f;
        [Tooltip("Tiempo de castigo por fallar el Parry sin golpear a nadie")]
        [SerializeField] private float parryMissCooldown = 1.35f;
        [Tooltip("Fuerza masiva de impulso transmitida al enemigo desvíado (efecto billar)")]
        [SerializeField] private float parryImpulseForce = 25f;
        [Tooltip("Daño infligido por el enemigo desvíado a otros enemigos")]
        [SerializeField] private float parryDamage = 60f;

        [Header("Salud y Daño")]
        [SerializeField] private int maxLives = 3;
        [SerializeField] private float iFrameDuration = 1.2f;
        [SerializeField] private float blinkInterval = 0.1f;
        [SerializeField] private SpriteRenderer spriteRenderer;

        [Header("Juice (Game Feel)")]
        [SerializeField] private float damageShakeIntensity = 0.4f;
        [SerializeField] private float damageShakeDuration = 0.35f;

        // Eventos para desacoplar UI o sistemas de audio futuros
        public event Action<int, int> OnLivesChanged; // currentLives, maxLives
        public event Action<ParryState> OnParryStateChanged;
        public event Action OnParrySuccess;
        public event Action OnPlayerDeath;

        #region Métodos de Mejoras (Upgrades)
        /// <summary>
        /// Velocidad de movimiento final considerando la mejora acumulable 'servomotors' (+10% por nivel).
        /// Fórmula: velocidadFinal = velocidadBase * (1 + (nivel * 0.10f))
        /// </summary>
        public float CurrentMoveSpeed
        {
            get
            {
                int servoLevel = UpgradeManager.Instance != null ? UpgradeManager.Instance.GetLevel("servomotors") : 0;
                return moveSpeed * (1f + (servoLevel * 0.10f));
            }
        }

        /// <summary>
        /// Cooldown de disparo considerando la mejora acumulable 'overclock' (-10% de tiempo de enfriamiento por nivel).
        /// Fórmula: cooldownFinal = cooldownBase * (1 - (nivel * 0.10f))
        /// </summary>
        public float CurrentFireCooldown
        {
            get
            {
                int overclockLevel = UpgradeManager.Instance != null ? UpgradeManager.Instance.GetLevel("overclock") : 0;
                float reductionMultiplier = Mathf.Max(0.2f, 1f - (overclockLevel * 0.10f));
                return (1f / Mathf.Max(0.1f, fireRate)) * reductionMultiplier;
            }
        }

        /// <summary>
        /// Daño base de colisión del Parry considerando la mejora 'heavy_caliber' (+20% por nivel).
        /// Fórmula: dañoFinal = dañoBase * (1 + (nivel * 0.20f))
        /// </summary>
        public float CurrentParryDamage
        {
            get
            {
                int caliberLevel = UpgradeManager.Instance != null ? UpgradeManager.Instance.GetLevel("heavy_caliber") : 0;
                return parryDamage * (1f + (caliberLevel * 0.20f));
            }
        }

        /// <summary>
        /// Multiplicador de cooldown de Parry reducido por el Power-Up 'rapid_parry' (-20% por nivel acumulable).
        /// </summary>
        public float CurrentParryCooldownMultiplier
        {
            get
            {
                int rapidLevel = UpgradeManager.Instance != null ? UpgradeManager.Instance.GetLevel("rapid_parry") : 0;
                return Mathf.Max(0.20f, 1f - (rapidLevel * 0.20f));
            }
        }

        /// <summary>
        /// Reduce porcentualmente el cooldown de disparo (Mejora Overclock, ej: 0.15f para -15%).
        /// </summary>
        public void ModifyFireCooldownReduction(float reductionPercent)
        {
            float factor = Mathf.Clamp(1f - reductionPercent, 0.2f, 1f);
            fireRate /= factor;
        }

        /// <summary>
        /// Incrementa porcentualmente la velocidad de movimiento (Mejora Agilidad Táctica, ej: 0.10f para +10%).
        /// </summary>
        public void ModifyMoveSpeed(float speedPercent)
        {
            moveSpeed *= (1f + speedPercent);
        }

        /// <summary>
        /// Incrementa porcentualmente el radio del hitbox de Parry (Mejora Parry Expansivo, ej: 0.20f para +20%).
        /// </summary>
        public void ModifyParryRadius(float radiusPercent)
        {
            if (parryHitbox != null)
            {
                parryHitbox.IncreaseRadiusPercent(radiusPercent);
            }
        }
        #endregion

        private Rigidbody2D _rb;
        private Vector2 _moveInput;
        private float _fireCooldown;
        private int _currentLives;
        private bool _isInvulnerable;
        private bool _isDead;

        private ParryState _parryState = ParryState.Ready;
        private bool _hasParriedSuccessfully;
        private Coroutine _parryCoroutine;
        private int _consecutiveParryStreak;
        private float _chainReflexesTimer; // Buff de Reflejos en Cadena (+50% active window)
        private float _parryCooldownRemaining;
        private float _parryCooldownTotal;

        [Header("Radar de Alto Rendimiento")]
        [Tooltip("Frecuencia de escaneo del radar en segundos (Tick Rate, ej: 0.1s = 10Hz)")]
        [SerializeField] private float radarScanInterval = 0.1f;

        // Buffer global pre-asignado para Physics2D.OverlapCircleNonAlloc (Cero GC Allocations)
        private static readonly Collider2D[] RadarHitsBuffer = new Collider2D[128];
        private float _radarScanTimer;
        private Transform _cachedTargetEnemy;

        public int CurrentLives => _currentLives;
        public int MaxLives => maxLives;
        public bool IsDead => _isDead;
        public ParryState CurrentParryState => _parryState;
        public float ParryCooldownRemaining => _parryCooldownRemaining;
        public float ParryCooldownTotal => _parryCooldownTotal;
        public float ParryCooldownNormalized => _parryCooldownTotal > 0.001f ? Mathf.Clamp01(1f - (_parryCooldownRemaining / _parryCooldownTotal)) : 1f;
        public float EffectiveSuccessCooldown => parrySuccessCooldown * CurrentParryCooldownMultiplier;
        public float EffectiveMissCooldown => parryMissCooldown * CurrentParryCooldownMultiplier;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _rb = GetComponent<Rigidbody2D>();
            if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();

            _currentLives = maxLives;

            EnsureParryHitboxSetup();

            if (!TryGetComponent<Combat.SatelliteOrbitController>(out _))
            {
                gameObject.AddComponent<Combat.SatelliteOrbitController>();
            }
        }

        private void EnsureParryHitboxSetup()
        {
            if (parryHitbox == null)
            {
                parryHitbox = GetComponentInChildren<ParryHitbox>(true);
            }

            if (parryHitbox == null)
            {
                // Creación procedural del hijo ParryHitbox si no fue asignado en el Inspector
                GameObject hitboxGo = new GameObject("ParryHitbox");
                hitboxGo.transform.SetParent(transform);
                hitboxGo.transform.localPosition = Vector3.zero;

                CircleCollider2D col = hitboxGo.AddComponent<CircleCollider2D>();
                col.radius = 1.35f;
                col.isTrigger = true;

                parryHitbox = hitboxGo.AddComponent<ParryHitbox>();
            }
        }

        private void Start()
        {
            OnLivesChanged?.Invoke(_currentLives, maxLives);
            OnParryStateChanged?.Invoke(_parryState);

            // Asegurar indicador de Cooldown de Parry en el HUD
            UI.ParryCooldownUI.EnsureExists();

            // Conectar cámara automáticamente si existe
            if (CameraShake.Instance != null)
            {
                CameraShake.Instance.SetFollowTarget(transform);
            }
        }

        private void Update()
        {
            if (_isDead) return;

            if (_chainReflexesTimer > 0f)
            {
                _chainReflexesTimer -= Time.deltaTime;
            }

            ReadInput();
            HandleAutoAimAndShooting();
        }

        private void FixedUpdate()
        {
            if (_isDead)
            {
#if UNITY_6000_0_OR_NEWER
                _rb.linearVelocity = Vector2.zero;
#else
                _rb.velocity = Vector2.zero;
#endif
                return;
            }

#if UNITY_6000_0_OR_NEWER
            _rb.linearVelocity = _moveInput * CurrentMoveSpeed;
#else
            _rb.velocity = _moveInput * CurrentMoveSpeed;
#endif

            // Mantener al jugador dentro de los límites de la arena sin trabar la velocidad
            if (ArenaBoundary.Instance != null)
            {
                Vector2 clamped = ArenaBoundary.Instance.ClampPosition(_rb.position, 0.6f);
                if ((clamped - _rb.position).sqrMagnitude > 0.001f)
                {
                    _rb.position = clamped;
                }
            }
        }

        /// <summary>
        /// Soporte universal para New Input System y Legacy Input Manager sin errores de configuración.
        /// </summary>
        private void ReadInput()
        {
            Vector2 input = Vector2.zero;
            bool parryPressed = false;

#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) input.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) input.y -= 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) input.x -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) input.x += 1f;

                if (kb.spaceKey.wasPressedThisFrame) parryPressed = true;
            }

            if (UnityEngine.InputSystem.Mouse.current != null)
            {
                if (UnityEngine.InputSystem.Mouse.current.rightButton.wasPressedThisFrame) parryPressed = true;
            }

            if (UnityEngine.InputSystem.Gamepad.current != null)
            {
                Vector2 stick = UnityEngine.InputSystem.Gamepad.current.leftStick.ReadValue();
                if (stick.sqrMagnitude > 0.05f) input = stick;

                if (UnityEngine.InputSystem.Gamepad.current.buttonEast.wasPressedThisFrame ||
                    UnityEngine.InputSystem.Gamepad.current.rightShoulder.wasPressedThisFrame)
                {
                    parryPressed = true;
                }
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (input == Vector2.zero)
            {
                input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            }

            if (Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(1))
            {
                parryPressed = true;
            }
#endif

            _moveInput = input.normalized;

            if (parryPressed && _parryState == ParryState.Ready)
            {
                TriggerParry();
            }
        }

        private void TriggerParry()
        {
            if (_parryCoroutine != null) StopCoroutine(_parryCoroutine);
            _parryCoroutine = StartCoroutine(ParryRoutine());
        }

        private IEnumerator ParryRoutine()
        {
            _parryState = ParryState.Active;
            _hasParriedSuccessfully = false;
            OnParryStateChanged?.Invoke(_parryState);

            if (parryHitbox != null)
            {
                parryHitbox.SetHitboxActive(true);
            }

            // Ventana activa exacta de 0.2s (ampliada un +50% si el buff de Reflejos en Cadena está activo)
            float activeDuration = parryActiveDuration;
            if (_chainReflexesTimer > 0f)
            {
                activeDuration *= 1.5f;
                _chainReflexesTimer = 0f; // Consumido en este parry
            }
            yield return new WaitForSeconds(activeDuration);

            if (parryHitbox != null)
            {
                parryHitbox.SetHitboxActive(false);
            }

            if (_hasParriedSuccessfully)
            {
                // Éxito: Pequeña ventana de cooldown para prevenir spam indiscriminado
                _parryState = ParryState.Cooldown;
                OnParryStateChanged?.Invoke(_parryState);

                float successCooldown = parrySuccessCooldown * CurrentParryCooldownMultiplier;
                _parryCooldownTotal = successCooldown;
                _parryCooldownRemaining = successCooldown;

                while (_parryCooldownRemaining > 0f)
                {
                    yield return null;
                    _parryCooldownRemaining -= Time.deltaTime;
                }

                _parryCooldownRemaining = 0f;
                _parryState = ParryState.Ready;
                OnParryStateChanged?.Invoke(_parryState);
            }
            else
            {
                // Fallo: Penalización de recuperación
                _parryState = ParryState.Cooldown;
                _consecutiveParryStreak = 0;
                OnParryStateChanged?.Invoke(_parryState);

                // Modificador 'defensive_anchor': reduce el cooldown de fallo a 0.15s si está en la Zona de Extracción
                float cooldownPenalty = parryMissCooldown * CurrentParryCooldownMultiplier;
                if (ExtractionZone.IsZoneActive && UpgradeManager.Instance != null && UpgradeManager.Instance.GetLevel("defensive_anchor") > 0)
                {
                    cooldownPenalty = 0.15f * CurrentParryCooldownMultiplier;
                }

                _parryCooldownTotal = cooldownPenalty;
                _parryCooldownRemaining = cooldownPenalty;

                while (_parryCooldownRemaining > 0f)
                {
                    yield return null;
                    _parryCooldownRemaining -= Time.deltaTime;
                }

                _parryCooldownRemaining = 0f;
                _parryState = ParryState.Ready;
                OnParryStateChanged?.Invoke(_parryState);
            }

            _parryCoroutine = null;
        }

        /// <summary>
        /// Invocado cuando un enemigo en estado Chasing entra al trigger del Parry en la ventana activa.
        /// </summary>
        public void OnParryTriggered(Enemy enemy)
        {
            if (_parryState != ParryState.Active || enemy == null || enemy.CurrentState != EnemyState.Chasing)
            {
                return;
            }

            // REGLA: No se le puede hacer parry a enemigos que estén siendo absorbidos por el vórtice
            if (enemy.IsBeingPulledByVortex)
            {
                return;
            }

            _hasParriedSuccessfully = true;
            OnParrySuccess?.Invoke();

            // Modificador 'vampirism': Racha de parries consecutivos para curar vida
            _consecutiveParryStreak++;
            int vampLevel = UpgradeManager.Instance != null ? UpgradeManager.Instance.GetLevel("vampirism") : 0;
            if (vampLevel > 0)
            {
                int requiredStreak = Mathf.Max(1, 4 - vampLevel);
                if (_consecutiveParryStreak >= requiredStreak)
                {
                    _consecutiveParryStreak = 0;
                    Heal(1);
                }
            }

            // 1. Game Feel: Hit Stop dramático (ralentiza el tiempo a 0.1f durante 0.15s reales)
            if (HitStopManager.Instance != null)
            {
                HitStopManager.Instance.TriggerHitStop(0.15f, 0.1f);
            }

            // 2. Game Feel: Sacudida de cámara vigorosa
            if (CameraShake.Instance != null)
            {
                CameraShake.Instance.Shake(0.2f, 0.35f);
            }

            // Modificador 'chain_reflexes' (Reflejos en Cadena): Otorga buff de 2s para el próximo parry
            if (UpgradeManager.Instance != null && UpgradeManager.Instance.GetLevel("chain_reflexes") > 0)
            {
                _chainReflexesTimer = 2.0f;
            }

            // Modificador 'gravitational_anomaly' (Anomalía Gravitacional): Spawnea agujero negro en el punto de impacto
            // TEMPORAL: Desactivado momentáneamente por problemas de optimización
            /*
            if (UpgradeManager.Instance != null && UpgradeManager.Instance.GetLevel("gravitational_anomaly") > 0)
            {
                if (ObjectPooler.Instance != null && ObjectPooler.Instance.HasPool("BlackHole"))
                {
                    ObjectPooler.Instance.SpawnFromPool("BlackHole", enemy.transform.position, Quaternion.identity);
                }
            }
            */

            // Daño efectivo de parry escalado con 'heavy_caliber' (+20% por nivel)
            float effectiveParryDamage = CurrentParryDamage;

            // Comportamiento Exclusivo 'mass_transposition' (Transposición de Masa):
            // En lugar de aplicar fuerza (Efecto Billar), intercambia posiciones y detona al enemigo en el origen.
            // Ignora y anula completamente ConvertIntoParriedProjectile / AddForce.
            bool hasMassTransposition = UpgradeManager.Instance != null && UpgradeManager.Instance.GetLevel("mass_transposition") > 0;
            if (hasMassTransposition)
            {
                Vector3 playerOriginalPos = transform.position;
                Vector3 enemyOriginalPos = enemy.transform.position;

                // 1. Intercambio de posiciones
                transform.position = enemyOriginalPos;
                if (_rb != null)
                {
                    _rb.position = enemyOriginalPos;
#if UNITY_6000_0_OR_NEWER
                    _rb.linearVelocity = Vector2.zero;
#else
                    _rb.velocity = Vector2.zero;
#endif
                }

                enemy.transform.position = playerOriginalPos;

                // 2. Detonación en área en la posición original del jugador
                float explosionRadius = 3.5f;
                Collider2D[] nearbyHits = Physics2D.OverlapCircleAll(playerOriginalPos, explosionRadius);
                for (int i = 0; i < nearbyHits.Length; i++)
                {
                    if (nearbyHits[i].TryGetComponent(out Enemy nearbyEnemy) && nearbyEnemy != enemy)
                    {
                        nearbyEnemy.TakeDamage(effectiveParryDamage * 1.5f);
                    }
                }

                if (ObjectPooler.Instance != null && ObjectPooler.Instance.HasPool("DeathVFX"))
                {
                    GameObject vfxObj = ObjectPooler.Instance.SpawnFromPool("DeathVFX", playerOriginalPos, Quaternion.identity);
                    if (vfxObj != null && vfxObj.TryGetComponent(out DeathVFX vfx))
                    {
                        vfx.PlayEffect(new Color(0.9f, 0.2f, 1f), 35, 1.8f);
                    }
                }

                // 3. Detonar y destruir al enemigo sin aplicar fuerza alguna
                enemy.Die();
            }
            else
            {
                // 3. Efecto Billar clásico: Dirección en base al movimiento actual o vector de empuje relativo
                Vector2 impulseDirection = _moveInput.sqrMagnitude > 0.01f
                    ? _moveInput.normalized
                    : ((Vector2)enemy.transform.position - (Vector2)transform.position).normalized;

                enemy.ConvertIntoParriedProjectile(impulseDirection, parryImpulseForce, effectiveParryDamage);

                // Modificador 'mirror_refractor': Divide el proyectil desviado en abanico
                int mirrorLevel = UpgradeManager.Instance != null ? UpgradeManager.Instance.GetLevel("mirror_refractor") : 0;
                if (mirrorLevel > 0 && ObjectPooler.Instance != null)
                {
                    float[] angles = mirrorLevel >= 2 ? new float[] { -30f, -15f, 15f, 30f } : new float[] { -25f, 25f };
                    for (int i = 0; i < angles.Length; i++)
                    {
                        Vector2 angledDir = Quaternion.Euler(0f, 0f, angles[i]) * impulseDirection;
                        GameObject extra = ObjectPooler.Instance.SpawnFromPool("Enemy_Red", enemy.transform.position, Quaternion.identity);
                        if (extra != null && extra.TryGetComponent(out Enemy extraEnemy))
                        {
                            extra.transform.localScale = Vector3.one * 0.6f;
                            extraEnemy.ConvertIntoParriedProjectile(angledDir, parryImpulseForce * 0.9f, effectiveParryDamage * 0.65f);
                        }
                    }
                }

                // Modificador 'parry_dash': Inyecta dash espectral invulnerable
                if (UpgradeManager.Instance != null && UpgradeManager.Instance.GetLevel("parry_dash") > 0)
                {
                    Vector2 dashDir = _moveInput.sqrMagnitude > 0.01f ? _moveInput : impulseDirection;
                    StartCoroutine(ParryDashRoutine(dashDir));
                }
            }
        }

        private IEnumerator ParryDashRoutine(Vector2 dir)
        {
            _isInvulnerable = true;
#if UNITY_6000_0_OR_NEWER
            _rb.linearVelocity = dir.normalized * 18f;
#else
            _rb.velocity = dir.normalized * 18f;
#endif
            yield return new WaitForSeconds(0.18f);
            _isInvulnerable = false;
        }

        /// <summary>
        /// Escanea el área mediante OverlapCircleNonAlloc con Tick Rate (10Hz) para cero GC y alto rendimiento.
        /// </summary>
        private void HandleAutoAimAndShooting()
        {
            _fireCooldown -= Time.deltaTime;
            _radarScanTimer -= Time.deltaTime;

            // 1. Escaneo por Tick Rate (ej. cada 0.1s en lugar de cada fotograma a 140 FPS)
            if (_radarScanTimer <= 0f)
            {
                _radarScanTimer = radarScanInterval;
                _cachedTargetEnemy = ScanClosestEnemy();
            }

            // 2. Disparar al objetivo más cercano cacheado
            if (_fireCooldown <= 0f)
            {
                // Si el objetivo murió o fue reciclado antes del siguiente tick, re-evaluar
                if (_cachedTargetEnemy != null && (!_cachedTargetEnemy.gameObject.activeInHierarchy ||
                    (_cachedTargetEnemy.TryGetComponent(out Enemy enemy) && enemy.CurrentState != EnemyState.Chasing)))
                {
                    _cachedTargetEnemy = ScanClosestEnemy();
                }

                if (_cachedTargetEnemy != null)
                {
                    ShootAt(_cachedTargetEnemy.position);
                    _fireCooldown = CurrentFireCooldown;
                }
            }
        }

        private Transform ScanClosestEnemy()
        {
            Vector2 currentPos = transform.position;

            // Uso de ContactFilter2D con useTriggers = true (CRÍTICO: Los enemigos optimizados usan BoxCollider2D.isTrigger = true)
            ContactFilter2D filter = ContactFilter2D.noFilter;
            filter.useTriggers = true;

            if (enemyLayer.value != 0)
            {
                filter.SetLayerMask(enemyLayer);
                filter.useLayerMask = true;
            }

            int hitCount = Physics2D.OverlapCircle(currentPos, detectionRadius, filter, RadarHitsBuffer);

            // Fallback automático sin filtro de LayerMask por si en el Inspector la layer asignada difiere
            if (hitCount == 0)
            {
                filter.useLayerMask = false;
                hitCount = Physics2D.OverlapCircle(currentPos, detectionRadius, filter, RadarHitsBuffer);
            }

            Transform bestTarget = null;
            float closestSqrDistance = float.MaxValue;

            for (int i = 0; i < hitCount; i++)
            {
                Collider2D col = RadarHitsBuffer[i];
                if (col == null || !col.gameObject.activeInHierarchy) continue;
                if (col.transform == transform || col.transform.IsChildOf(transform)) continue;

                // Buscar Enemy en el collider o en sus padres (máxima compatibilidad)
                Enemy enemy = col.GetComponent<Enemy>();
                if (enemy == null) enemy = col.GetComponentInParent<Enemy>();

                if (enemy != null && enemy.CurrentState == EnemyState.Chasing)
                {
                    float sqrDist = ((Vector2)enemy.transform.position - currentPos).sqrMagnitude;
                    if (sqrDist < closestSqrDistance)
                    {
                        closestSqrDistance = sqrDist;
                        bestTarget = enemy.transform;
                    }
                }
            }

            return bestTarget;
        }

        private void ShootAt(Vector3 targetPosition)
        {
            Vector3 origin = firePoint != null ? firePoint.position : transform.position;
            Vector2 direction = (targetPosition - origin).normalized;

            if (ObjectPooler.Instance != null)
            {
                GameObject bulletObj = ObjectPooler.Instance.SpawnFromPool(bulletPoolTag, origin, Quaternion.identity);
                if (bulletObj != null && bulletObj.TryGetComponent(out Projectile bullet))
                {
                    bullet.Launch(direction);
                }
            }
        }

        /// <summary>
        /// Recibe daño de un enemigo, resta 1 vida, activa I-Frames con parpadeo y sacudida de cámara.
        /// </summary>
        public void TakeDamage(int damageAmount)
        {
            if (_isInvulnerable || _isDead) return;

            _currentLives -= damageAmount;
            _consecutiveParryStreak = 0;
            OnLivesChanged?.Invoke(_currentLives, maxLives);

            // Game Feel: Sacudida intensa de cámara al recibir daño
            if (CameraShake.Instance != null)
            {
                CameraShake.Instance.Shake(damageShakeDuration, damageShakeIntensity);
            }

            if (_currentLives <= 0)
            {
                Die();
            }
            else
            {
                StartCoroutine(TriggerIFrames());
            }
        }

        private IEnumerator TriggerIFrames()
        {
            _isInvulnerable = true;
            float elapsed = 0f;

            Color originalColor = spriteRenderer != null ? spriteRenderer.color : Color.white;
            Color flashColor = new Color(originalColor.r, originalColor.g, originalColor.b, 0.3f);

            while (elapsed < iFrameDuration)
            {
                if (spriteRenderer != null)
                {
                    spriteRenderer.color = (spriteRenderer.color == originalColor) ? flashColor : originalColor;
                }

                yield return new WaitForSeconds(blinkInterval);
                elapsed += blinkInterval;
            }

            if (spriteRenderer != null)
            {
                spriteRenderer.color = originalColor;
            }

            _isInvulnerable = false;
        }

        private void Die()
        {
            _isDead = true;
            Debug.Log("<color=red><b>[GAME OVER]</b> El jugador ha perdido todas sus vidas.</color>");
            OnPlayerDeath?.Invoke();

            // 1. Eliminar inmediatamente los orbes satélites para que dejen de dañar enemigos
            if (TryGetComponent<Combat.SatelliteOrbitController>(out var satellite))
            {
                satellite.ClearOrbs();
            }

            // 2. Detener físicas y colisiones del jugador
            if (_rb != null)
            {
#if UNITY_6000_0_OR_NEWER
                _rb.linearVelocity = Vector2.zero;
#else
                _rb.velocity = Vector2.zero;
#endif
                _rb.simulated = false;
            }

            var colliders = GetComponents<Collider2D>();
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }

            // 3. Game Feel: Sacudida intensa final
            if (CameraShake.Instance != null)
            {
                CameraShake.Instance.Shake(0.5f, 0.6f);
            }

            // 4. Efecto de partículas de destrucción del jugador
            if (ObjectPooler.Instance != null)
            {
                GameObject vfxObj = ObjectPooler.Instance.SpawnFromPool("DeathVFX", transform.position, Quaternion.identity);
                if (vfxObj != null && vfxObj.TryGetComponent(out DeathVFX vfx))
                {
                    Color playerColor = spriteRenderer != null ? spriteRenderer.color : new Color(0.2f, 0.85f, 1f);
                    vfx.PlayEffect(playerColor, 45, 1.8f);
                }
            }

            if (spriteRenderer != null)
            {
                spriteRenderer.enabled = false;
            }

            // 5. Esperar a que se produzca la animación de explosión del jugador y luego pausar el juego
            StartCoroutine(PauseAfterDeathExplosion(0.85f));
        }

        private IEnumerator PauseAfterDeathExplosion(float delay)
        {
            yield return new WaitForSeconds(delay);
            Time.timeScale = 0f;
            Debug.Log("<color=red><b>[GAME OVER]</b> Animación de explosión finalizada. Juego pausado (Time.timeScale = 0).</color>");
        }

        /// <summary>
        /// Recupera vidas del jugador (utilizado por Vampirismo u otras mejoras).
        /// </summary>
        public void Heal(int amount)
        {
            if (_isDead) return;
            _currentLives = Mathf.Clamp(_currentLives + amount, 0, maxLives);
            OnLivesChanged?.Invoke(_currentLives, maxLives);
            Debug.Log($"<color=lime><b>[VAMPIRISMO]</b> ¡Vida recuperada (+{amount})! Vidas actuales: {_currentLives}/{maxLives}</color>");
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, detectionRadius);

            if (parryHitbox != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(transform.position, 1.35f);
            }
        }
    }
}
