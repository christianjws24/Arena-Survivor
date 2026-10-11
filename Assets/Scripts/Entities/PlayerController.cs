using System;
using System.Collections;
using System.Collections.Generic;
using ArenaSurvivor.Combat;
using ArenaSurvivor.Data;
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
    /// Teclado: WASD/flechas mueven y fijan la dirección de mira (para las armas H J K + especial L), Espacio hace Parry.
    /// Mando: stick izquierdo mueve, Círculo / R1 hace Parry, armas normales en R2 / L2 / L1 y arma especial en Triángulo.
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
        [Tooltip("Si no hay enemigos en el radio normal, alcance extendido para objetivos pesados (torretas, jefes)")]
        [SerializeField] private float heavyTargetRadius = 16f;
        [Tooltip("Cadencia de disparo base (disparos por segundo): 2 = un disparo cada medio segundo")]
        [SerializeField] private float fireRate = 2f;
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
        [Tooltip("Empuje que recibe el jugador al hacerle Parry a un jefe (o a una de sus partes)")]
        [SerializeField] private float bossParryKnockback = 16f;

        [Header("Desvío Espectral (parry_dash)")]
        [SerializeField] private float parryDashSpeed = 18f;
        [SerializeField] private float parryDashDuration = 0.18f;
        [Tooltip("Invulnerabilidad extra al terminar el dash, para no recibir daño al aterrizar junto a enemigos")]
        [SerializeField] private float parryDashGraceTime = 0.4f;
        [Tooltip("Radio en el que el dash aparta a los enemigos ligeros que tiene delante")]
        [SerializeField] private float parryDashPushRadius = 1.1f;
        [Tooltip("Velocidad con la que el dash aparta a esos enemigos de su trayectoria")]
        [SerializeField] private float parryDashPushForce = 14f;

        [Header("Modelo de Daño")]
        [Tooltip("Daño base del jugador: 100% = un disparo automático. Todas las armas escalan como porcentaje de este valor.")]
        [SerializeField] private float baseDamage = 35f;

        [Header("Salud y Daño")]
        [SerializeField] private int maxLives = 3;
        [Tooltip("Tiempo de invulnerabilidad tras recibir un golpe: los enemigos no pueden volver a dañarte durante este lapso")]
        [SerializeField] private float iFrameDuration = 3f;
        [SerializeField] private float blinkInterval = 0.1f;
        [SerializeField] private SpriteRenderer spriteRenderer;
        [Tooltip("Velocidad de giro del sprite de la nave hacia la dirección de movimiento (grados/segundo)")]
        [SerializeField] private float shipTurnSpeed = 1080f;

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
                return moveSpeed * Ship.MoveSpeedMultiplier * (1f + (servoLevel * 0.10f));
            }
        }

        /// <summary>
        /// Cooldown de disparo considerando la mejora acumulable 'overclock' (-10% de tiempo de enfriamiento por nivel)
        /// y la cadencia extra del Anclaje Ofensivo.
        /// Fórmula: cooldownFinal = (1 / cadencia) * (1 - (nivel * 0.10f)) / multiplicadorAnclaje
        /// </summary>
        public float CurrentFireCooldown
        {
            get
            {
                int overclockLevel = UpgradeManager.Instance != null ? UpgradeManager.Instance.GetLevel("overclock") : 0;
                float reductionMultiplier = Mathf.Max(0.2f, 1f - (overclockLevel * 0.10f));
                float anchorFireRate = AnchorZoneController.GetPlayerFireRateMultiplier(transform.position);
                float shipFireRate = fireRate * Ship.FireRateMultiplier;
                return (1f / Mathf.Max(0.1f, shipFireRate)) * reductionMultiplier / anchorFireRate;
            }
        }

        /// <summary>
        /// Daño base del jugador (100%). Referencia común para todas las armas.
        /// </summary>
        public float BaseDamage => baseDamage;

        /// <summary>
        /// Multiplicador global de daño: 'heavy_caliber' (+20% por nivel, aditivo) x Anclaje Ofensivo (x1.5 dentro de la zona).
        /// </summary>
        public float DamageMultiplier
        {
            get
            {
                int caliberLevel = UpgradeManager.Instance != null ? UpgradeManager.Instance.GetLevel("heavy_caliber") : 0;
                return (1f + (caliberLevel * 0.20f)) * AnchorZoneController.GetPlayerDamageMultiplier(transform.position);
            }
        }

        /// <summary>
        /// Daño final de un ataque expresado como bonus porcentual sobre el daño base.
        /// Ej: Laser Burst (+15%) => GetDamage(0.15f); Rail Gun (+300%) => GetDamage(3f); Laser Shotgun (-25%) => GetDamage(-0.25f).
        /// </summary>
        public float GetDamage(float bonusPercentOfBase = 0f)
        {
            return baseDamage * Mathf.Max(0f, 1f + bonusPercentOfBase) * DamageMultiplier;
        }

        /// <summary>
        /// Daño de un DISPARO (automático o arma HJKL): GetDamage con el bonus de daño de la nave (Slayer +15%).
        /// Los efectos de Parry (vórtice, relevos, contraataque) usan GetDamage sin este bonus.
        /// </summary>
        public float GetShotDamage(float bonusPercentOfBase = 0f)
        {
            return GetDamage(bonusPercentOfBase) * Ship.ShotDamageMultiplier;
        }

        /// <summary>
        /// Daño de colisión del Parry escalado con el multiplicador global ('heavy_caliber', Anclaje Ofensivo).
        /// </summary>
        public float CurrentParryDamage => parryDamage * DamageMultiplier;

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
        private Vector2 _facingDirection = Vector2.up;
        private Vector2 _knockbackVelocity;
        private Vector2 _dashVelocity;
        private float _dashTimer;
        private Transform _facingIndicator;
        private Transform _shipVisual;
        private PlayerWeapons _weapons;
        private float _fireCooldown;
        private int _currentLives;
        private float _invulnerableTimer; // Invulnerabilidad restante (I-Frames, escudo, dash): gana siempre la más larga
        private bool _isDead;

        private ParryState _parryState = ParryState.Ready;
        private bool _hasParriedSuccessfully;
        private Coroutine _parryCoroutine;
        private int _consecutiveParryStreak;
        private float _chainReflexesTimer; // Buff de Reflejos en Cadena (+50% active window)
        private float _parryCooldownRemaining;
        private float _parryCooldownTotal;
        // Enemigos desviados en la activación actual: solo el primero cuenta como Parry (vampirismo, energía).
        // Los siguientes (ej. cadena de la Transposición de Masa) son un efecto aparte.
        private int _parryHitsThisActivation;
        private bool _bossParriedThisActivation;
        private static readonly Collider2D[] DashHitsBuffer = new Collider2D[32];

        [Header("Radar de Alto Rendimiento")]
        [Tooltip("Frecuencia de escaneo del radar en segundos (Tick Rate, ej: 0.1s = 10Hz)")]
        [SerializeField] private float radarScanInterval = 0.1f;

        // Buffer global pre-asignado para Physics2D.OverlapCircleNonAlloc (Cero GC Allocations)
        private static readonly Collider2D[] RadarHitsBuffer = new Collider2D[128];
        private float _radarScanTimer;
        private Transform _cachedTargetEnemy;

        /// <summary>
        /// Dirección de mira: último sentido de movimiento (8 direcciones con teclado). Las armas HJKL disparan hacia aquí.
        /// </summary>
        public Vector2 FacingDirection => _facingDirection;
        public Vector2 MoveInput => _moveInput;
        public bool IsInvulnerable => _invulnerableTimer > 0f;

        public int CurrentLives => _currentLives;
        public int MaxLives => maxLives;
        public bool IsDead => _isDead;
        public ParryState CurrentParryState => _parryState;
        public float ParryCooldownRemaining => _parryCooldownRemaining;
        public float ParryCooldownTotal => _parryCooldownTotal;
        public float ParryCooldownNormalized => _parryCooldownTotal > 0.001f ? Mathf.Clamp01(1f - (_parryCooldownRemaining / _parryCooldownTotal)) : 1f;
        public float EffectiveSuccessCooldown => BaseParrySuccessCooldown * CurrentParryCooldownMultiplier;
        public float EffectiveMissCooldown => BaseParryMissCooldown * CurrentParryCooldownMultiplier;

        /// <summary>Nave elegida en el menú (Slayer, Spartan o Celerus).</summary>
        public ShipDefinition Ship => _ship ?? (_ship = ShipSelection.Current);
        private ShipDefinition _ship;

        /// <summary>Renderer visible del jugador: el sprite de la nave si existe, si no el círculo raíz.</summary>
        public SpriteRenderer ShipRenderer => spriteRenderer;

        // Slayer (5s) y Celerus (2.5s) tienen un cooldown fijo de Parry, acierte o falle; Spartan usa los cooldowns normales
        private float BaseParrySuccessCooldown => Ship.FixedParryCooldown > 0f ? Ship.FixedParryCooldown : parrySuccessCooldown;
        private float BaseParryMissCooldown => Ship.FixedParryCooldown > 0f ? Ship.FixedParryCooldown : parryMissCooldown;

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

            // Nave elegida en el menú: su color identifica al jugador (Slayer naranja, Spartan cian, Celerus rosa)
            _ship = ShipSelection.Current;
            if (!EnsureShipVisual() && spriteRenderer != null) spriteRenderer.color = _ship.Color;
            Debug.Log($"<color=cyan><b>[NAVE]</b> {_ship.DisplayName} ({_ship.Tagline})</color>");

            _currentLives = maxLives;

            EnsureParryHitboxSetup();

            if (!TryGetComponent<Combat.SatelliteOrbitController>(out _))
            {
                gameObject.AddComponent<Combat.SatelliteOrbitController>();
            }

            if (!TryGetComponent(out _weapons))
            {
                _weapons = gameObject.AddComponent<PlayerWeapons>();
            }

            if (!TryGetComponent<AnchorZoneController>(out _))
            {
                gameObject.AddComponent<AnchorZoneController>();
            }

            if (!TryGetComponent<HealthDropDirector>(out _))
            {
                gameObject.AddComponent<HealthDropDirector>();
            }

            // La nave ya apunta hacia donde se mueve; la flecha solo hace falta con el círculo de respaldo
            if (_shipVisual == null) EnsureFacingIndicator();
        }

        /// <summary>
        /// Crea el hijo "ShipVisual" con el sprite de la nave elegida y lo usa como renderer del jugador.
        /// Va en un hijo para poder rotarlo sin girar el collider, el parry ni los satélites.
        /// Devuelve false si el sprite no existe (se mantiene el círculo tintado).
        /// </summary>
        private bool EnsureShipVisual()
        {
            Sprite sprite = _ship.LoadSprite();
            if (sprite == null) return false;

            SpriteRenderer rootRenderer = spriteRenderer;
            GameObject go = new GameObject("ShipVisual");
            go.transform.SetParent(transform, false);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            if (rootRenderer != null)
            {
                sr.sortingLayerID = rootRenderer.sortingLayerID;
                sr.sortingOrder = rootRenderer.sortingOrder;
                sr.sharedMaterial = rootRenderer.sharedMaterial;
                rootRenderer.enabled = false;
            }

            spriteRenderer = sr;
            _shipVisual = go.transform;
            _shipVisual.localRotation = FacingRotation();
            return true;
        }

        // Los sprites de nave miran hacia arriba (+Y)
        private Quaternion FacingRotation()
        {
            float angle = Mathf.Atan2(_facingDirection.y, _facingDirection.x) * Mathf.Rad2Deg - 90f;
            return Quaternion.Euler(0f, 0f, angle);
        }

        private void UpdateShipVisual()
        {
            if (_shipVisual == null) return;
            _shipVisual.localRotation = Quaternion.RotateTowards(_shipVisual.localRotation, FacingRotation(), shipTurnSpeed * Time.deltaTime);
        }

        /// <summary>
        /// Pequeña flecha orbitando al jugador que muestra hacia dónde dispararán las armas HJKL.
        /// </summary>
        private void EnsureFacingIndicator()
        {
            Transform existing = transform.Find("FacingIndicator");
            if (existing != null)
            {
                _facingIndicator = existing;
                return;
            }

            GameObject go = new GameObject("FacingIndicator");
            go.transform.SetParent(transform, false);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = ProceduralSprites.Triangle;
            sr.color = new Color(0.4f, 1f, 1f, 0.85f);
            sr.sortingOrder = 6;
            _facingIndicator = go.transform;
            UpdateFacingIndicator();
        }

        private void UpdateFacingIndicator()
        {
            if (_facingIndicator == null) return;
            // Compensar la escala del jugador para que la flecha mida ~0.32u y quede a ~0.8u del centro
            float parentScale = Mathf.Max(0.01f, transform.lossyScale.x);
            _facingIndicator.localScale = Vector3.one * (0.32f / parentScale);
            _facingIndicator.localPosition = (Vector3)(_facingDirection * (0.8f / parentScale));
            float angle = Mathf.Atan2(_facingDirection.y, _facingDirection.x) * Mathf.Rad2Deg;
            _facingIndicator.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        /// <summary>
        /// Aplica un empuje externo que decae con el tiempo (ej: retroceso del Rail Gun).
        /// </summary>
        public void ApplyKnockback(Vector2 velocity)
        {
            _knockbackVelocity += velocity;
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

            // Menú de testeo de power-ups (F1)
            UI.UpgradeDebugMenu.EnsureExists();

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

            if (_invulnerableTimer > 0f)
            {
                _invulnerableTimer -= Time.deltaTime;
            }

            ReadInput();
            UpdateShipVisual();
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

            Vector2 velocity;
            if (_dashTimer > 0f)
            {
                _dashTimer -= Time.fixedDeltaTime;
                velocity = _dashVelocity;
                PushEnemiesOutOfDashPath();
            }
            else
            {
                velocity = (_moveInput * CurrentMoveSpeed) + _knockbackVelocity;
            }
            _knockbackVelocity = Vector2.MoveTowards(_knockbackVelocity, Vector2.zero, 45f * Time.fixedDeltaTime);

#if UNITY_6000_0_OR_NEWER
            _rb.linearVelocity = velocity;
#else
            _rb.velocity = velocity;
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

            if (Input.GetKeyDown(KeyCode.Space))
            {
                parryPressed = true;
            }
#endif

            _moveInput = input.normalized;
            if (_moveInput.sqrMagnitude > 0.01f)
            {
                _facingDirection = _moveInput;
                UpdateFacingIndicator();
            }

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
            _parryHitsThisActivation = 0;
            _bossParriedThisActivation = false;
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

                float successCooldown = BaseParrySuccessCooldown * CurrentParryCooldownMultiplier;
                _parryCooldownTotal = successCooldown;
                _parryCooldownRemaining = successCooldown;

                while (_parryCooldownRemaining > 0f)
                {
                    yield return null;
                    // Anclaje Defensivo: dentro de su zona el cooldown se consume casi al instante
                    _parryCooldownRemaining -= Time.deltaTime / AnchorZoneController.GetPlayerParryCooldownMultiplier(transform.position);
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

                // Penalización por fallo de Parry
                float cooldownPenalty = BaseParryMissCooldown * CurrentParryCooldownMultiplier;

                _parryCooldownTotal = cooldownPenalty;
                _parryCooldownRemaining = cooldownPenalty;

                while (_parryCooldownRemaining > 0f)
                {
                    yield return null;
                    // Anclaje Defensivo: dentro de su zona el cooldown se consume casi al instante
                    _parryCooldownRemaining -= Time.deltaTime / AnchorZoneController.GetPlayerParryCooldownMultiplier(transform.position);
                }

                _parryCooldownRemaining = 0f;
                _parryState = ParryState.Ready;
                OnParryStateChanged?.Invoke(_parryState);
            }

            _parryCoroutine = null;
        }

        /// <summary>
        /// Procesa el desvío exitoso de un proyectil hostil durante la ventana de Parry.
        /// </summary>
        public void OnParryProjectile(EnemyProjectile projectile)
        {
            if (_parryState != ParryState.Active || projectile == null || projectile.IsParried)
            {
                return;
            }

            _hasParriedSuccessfully = true;
            OnParrySuccess?.Invoke();

            // Impacto breve y vuelta suave: congela 40 ms y recupera la velocidad en 80 ms
            if (HitStopManager.Instance != null)
            {
                HitStopManager.Instance.TriggerHitStop(0.04f, 0.05f, 0.08f);
            }

            if (CameraShake.Instance != null)
            {
                CameraShake.Instance.Shake(0.18f, 0.3f);
            }

            Vector2 reflectDir = (Vector2)projectile.transform.position - (Vector2)transform.position;
            if (reflectDir.sqrMagnitude < 0.01f)
            {
                reflectDir = -projectile.transform.right;
            }

            projectile.Reflect(reflectDir.normalized, CurrentParryDamage * 1.5f);
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

            // Un jefe solo recibe un Parry por activación aunque el aro toque a la vez escudo, núcleo y cañones
            // (antes cada parte tocada aplicaba el daño de Parry por separado)
            if (enemy.IsBoss && _bossParriedThisActivation)
            {
                return;
            }

            _hasParriedSuccessfully = true;

            // Solo el primer enemigo de cada activación cuenta como Parry. Los demás (la cadena de intercambios de la
            // Transposición de Masa o varios enemigos en el mismo aro) aplican su efecto de combate, pero no suman
            // racha de Vampirismo ni energía de Parry.
            bool isFirstHitThisActivation = _parryHitsThisActivation == 0;
            _parryHitsThisActivation++;

            if (isFirstHitThisActivation)
            {
                OnParrySuccess?.Invoke();

                // Modificador 'vampirism' (nivel único): cada 5 parries consecutivos cura 1 vida
                _consecutiveParryStreak++;
                int vampLevel = UpgradeManager.Instance != null ? UpgradeManager.Instance.GetLevel("vampirism") : 0;
                if (vampLevel > 0)
                {
                    const int requiredStreak = 5;
                    if (_consecutiveParryStreak >= requiredStreak)
                    {
                        _consecutiveParryStreak = 0;
                        Heal(1);
                    }
                }
            }
            else
            {
                // Efecto encadenado: mantiene el sonido de impacto sin contar como Parry
                Managers.AudioManager.Instance?.PlayParryImpact();
            }

            // 1. Game Feel: Hit Stop con peso pero sin tirón (congela 60 ms y recupera la velocidad en 100 ms con ease-out)
            if (HitStopManager.Instance != null)
            {
                HitStopManager.Instance.TriggerHitStop(0.06f, 0.05f, 0.1f);
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
            // (optimizado: el vórtice escanea a 10Hz en lugar de usar OnTriggerStay2D por enemigo)
            if (UpgradeManager.Instance != null && UpgradeManager.Instance.GetLevel("gravitational_anomaly") > 0)
            {
                if (ObjectPooler.Instance != null && ObjectPooler.Instance.HasPool("BlackHole"))
                {
                    ObjectPooler.Instance.SpawnFromPool("BlackHole", enemy.transform.position, Quaternion.identity);
                }
            }

            // Daño efectivo de parry escalado con 'heavy_caliber' (+20% por nivel)
            float effectiveParryDamage = CurrentParryDamage;
            bool isHeavyOrBoss = enemy.IsHeavy;

            // Jefes: el choque te empuja hacia atrás con fuerza (una vez por activación)
            Vector2 awayFromBoss = Vector2.zero;
            if (enemy.IsBoss)
            {
                _bossParriedThisActivation = true;
                awayFromBoss = (Vector2)transform.position - (Vector2)enemy.transform.position;
                awayFromBoss = awayFromBoss.sqrMagnitude > 0.001f ? awayFromBoss.normalized : -_facingDirection;
                ApplyKnockback(awayFromBoss * bossParryKnockback);
            }
            Vector2 impulseDirection;
            Vector3 burstOrigin; // Punto desde el que salen los fragmentos de 'mirror_refractor'

            // Comportamiento Exclusivo 'mass_transposition' (Transposición de Masa):
            // En lugar del empuje normal (Efecto Billar), intercambia posiciones y detona al enemigo en el origen.
            // Naves pesadas, torretas y partes de jefes no pueden intercambiarse de posición: reciben el parry normal
            bool hasMassTransposition = UpgradeManager.Instance != null && UpgradeManager.Instance.GetLevel("mass_transposition") > 0;
            if (hasMassTransposition && !isHeavyOrBoss)
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

                // 3. Detonación: daño triple (explota si no lo resiste).
                // Se convierte en proyectil desviado ANTES del daño para no anular las mejoras de parry:
                // si muere cuenta como "enemigo desviado destruido" ('geometric_shrapnel') y si sobrevive
                // sale despedido desde tu origen como bola de billar ('chain_reaction').
                impulseDirection = ((Vector2)playerOriginalPos - (Vector2)enemyOriginalPos).normalized;
                if (impulseDirection.sqrMagnitude < 0.01f) impulseDirection = Vector2.up;
                burstOrigin = playerOriginalPos;

                enemy.ConvertIntoParriedProjectile(impulseDirection, parryImpulseForce, effectiveParryDamage);
                enemy.TakeDamage(effectiveParryDamage * 3f);
            }
            else
            {
                // 3. Efecto de Parry: Dirección en base al movimiento actual o vector de empuje relativo
                impulseDirection = _moveInput.sqrMagnitude > 0.01f
                    ? _moveInput.normalized
                    : ((Vector2)enemy.transform.position - (Vector2)transform.position).normalized;
                burstOrigin = enemy.transform.position;

                if (isHeavyOrBoss)
                {
                    // Bosses y naves capitales: reciben daño directo de parry sin salir volando
                    enemy.TakeDamage(effectiveParryDamage);
                }
                else
                {
                    // 1. Aplica daño directo de contraataque
                    enemy.TakeDamage(effectiveParryDamage);

                    // 2. Solo si sobrevive al daño (vida restante > 0), sale empujado sin explotar
                    if (enemy.CurrentHealth > 0 && enemy.CurrentState != EnemyState.Dead)
                    {
                        enemy.ConvertIntoParriedProjectile(impulseDirection, parryImpulseForce, effectiveParryDamage);
                    }
                }

                // Modificador 'parry_dash': Inyecta dash espectral invulnerable (contra un jefe, siempre alejándote de él)
                if (UpgradeManager.Instance != null && UpgradeManager.Instance.GetLevel("parry_dash") > 0)
                {
                    Vector2 dashDir = enemy.IsBoss ? awayFromBoss
                        : (_moveInput.sqrMagnitude > 0.01f ? _moveInput : impulseDirection);
                    StartParryDash(dashDir);
                }
            }

            // Modificador 'mirror_refractor': Divide el proyectil desviado en abanico (también con Transposición de Masa)
            int mirrorLevel = UpgradeManager.Instance != null ? UpgradeManager.Instance.GetLevel("mirror_refractor") : 0;
            if (mirrorLevel > 0 && ObjectPooler.Instance != null && !isHeavyOrBoss)
            {
                float[] angles = mirrorLevel >= 2 ? new float[] { -30f, -15f, 15f, 30f } : new float[] { -25f, 25f };
                for (int i = 0; i < angles.Length; i++)
                {
                    Vector2 angledDir = Quaternion.Euler(0f, 0f, angles[i]) * impulseDirection;
                    GameObject extra = ObjectPooler.Instance.SpawnFromPool("Enemy_Red", burstOrigin, Quaternion.identity);
                    if (extra != null && extra.TryGetComponent(out Enemy extraEnemy))
                    {
                        extra.transform.localScale = Vector3.one * 0.6f;
                        extraEnemy.IsPhantomProjectile = true;
                        extraEnemy.ConvertIntoParriedProjectile(angledDir, parryImpulseForce * 0.9f, effectiveParryDamage * 0.65f);
                    }
                }
            }
        }

        private void StartParryDash(Vector2 dir)
        {
            if (dir.sqrMagnitude < 0.0001f) dir = _facingDirection;

            // El dash se aplica en FixedUpdate (antes la velocidad se sobrescribía en el siguiente paso físico y no se notaba).
            // La invulnerabilidad dura el dash + un margen: antes terminaba justo al aterrizar y los enemigos de delante
            // te golpeaban en ese mismo instante.
            _dashVelocity = dir.normalized * parryDashSpeed;
            _dashTimer = parryDashDuration;
            GrantInvulnerability(parryDashDuration + parryDashGraceTime);
        }

        /// <summary>
        /// Durante el dash, aparta hacia los lados a los enemigos ligeros que hay en la trayectoria para no aterrizar dentro de ellos.
        /// </summary>
        private void PushEnemiesOutOfDashPath()
        {
            Vector2 dashDir = _dashVelocity.sqrMagnitude > 0.0001f ? _dashVelocity.normalized : _facingDirection;
            Vector2 lookAhead = _rb.position + (dashDir * (parryDashPushRadius * 0.5f));

            ContactFilter2D filter = ContactFilter2D.noFilter;
            filter.useTriggers = true;
            int count = Physics2D.OverlapCircle(lookAhead, parryDashPushRadius, filter, DashHitsBuffer);

            for (int i = 0; i < count; i++)
            {
                Collider2D col = DashHitsBuffer[i];
                if (col == null || col.transform.IsChildOf(transform)) continue;
                if (!col.TryGetComponent(out Enemy enemy) || enemy.IsHeavy || enemy.CurrentState != EnemyState.Chasing) continue;

                Vector2 toEnemy = (Vector2)enemy.transform.position - _rb.position;
                Vector2 side = new Vector2(-dashDir.y, dashDir.x);
                if (Vector2.Dot(toEnemy, side) < 0f) side = -side;
                enemy.ApplyKnockback(((side * 0.8f) + (dashDir * 0.6f)).normalized * parryDashPushForce);
            }
        }

        /// <summary>
        /// Concede invulnerabilidad durante 'duration' segundos sin acortar la que ya hubiera (I-Frames, dash, escudo).
        /// </summary>
        private void GrantInvulnerability(float duration)
        {
            _invulnerableTimer = Mathf.Max(_invulnerableTimer, duration);
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

                if (_cachedTargetEnemy == null)
                {
                    _cachedTargetEnemy = ScanClosestEnemy(heavyTargetRadius, true);
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
            return ScanClosestEnemy(detectionRadius, false);
        }

        private Transform ScanClosestEnemy(float radius, bool heavyOnly)
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

            int hitCount = Physics2D.OverlapCircle(currentPos, radius, filter, RadarHitsBuffer);

            // Fallback automático sin filtro de LayerMask por si en el Inspector la layer asignada difiere
            if (hitCount == 0)
            {
                filter.useLayerMask = false;
                hitCount = Physics2D.OverlapCircle(currentPos, radius, filter, RadarHitsBuffer);
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

                if (enemy != null && enemy.CurrentState == EnemyState.Chasing && !enemy.IsPhasingIn && (!heavyOnly || enemy.IsHeavy))
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
            if (IsInvulnerable || _isDead) return;

            // Energy Shield (botín de jefe): absorbe el golpe completo
            if (_weapons != null && _weapons.TryAbsorbHit())
            {
                GrantInvulnerability(0.6f);
                return;
            }

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
            GrantInvulnerability(iFrameDuration);
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
                    Color playerColor = Ship.Color;
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
        /// Recupera vidas del jugador (Vampirismo, botiquines que sueltan los enemigos).
        /// </summary>
        public void Heal(int amount)
        {
            if (_isDead) return;
            _currentLives = Mathf.Clamp(_currentLives + amount, 0, maxLives);
            OnLivesChanged?.Invoke(_currentLives, maxLives);
            Debug.Log($"<color=lime><b>[VIDA]</b> ¡Vida recuperada (+{amount})! Vidas actuales: {_currentLives}/{maxLives}</color>");
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
