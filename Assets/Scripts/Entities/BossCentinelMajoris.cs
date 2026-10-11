using System;
using System.Collections;
using ArenaSurvivor.Combat;
using ArenaSurvivor.Pool;
using UnityEngine;
using Random = UnityEngine.Random;

namespace ArenaSurvivor.Entities
{
    /// <summary>
    /// BOSS 1_CUSTODIUS:
    /// - Spawn: Aparece desde los bordes de la pantalla.
    /// - Comportamiento: Circula alrededor del jugador y dispara 3 láseres a la vez cada 3 segundos.
    /// - Diseño y Mecánicas:
    ///   • Nave compuesta por 2 secciones largas (Generadores de Escudo) a los lados y una pequeña esfera en el centro.
    ///   • Escudo de Energía (Energy Shield): 500hp. Tarda 3 segundos en recargar con 2 generadores activos y 6 segundos con 1 activo.
    ///     Si ambos generadores caen, el escudo queda permanentemente desactivado.
    ///   • Generadores de Escudo: 1000hp cada uno.
    ///   • Esfera Central (Núcleo): 1500hp.
    /// </summary>
    public class BossCentinelMajoris : Enemy, IShieldedBoss, IBossStatus
    {
        [Header("Estadísticas del Boss")]
        [Tooltip("Puntos de salud de la Esfera Central (Núcleo)")]
        [SerializeField] private float centerSphereMaxHealth = 1500f;

        [Tooltip("Puntos de salud del Escudo de Energía")]
        [SerializeField] private float energyShieldMaxHealth = 500f;

        [Tooltip("Tiempo de recarga del escudo con ambos generadores activos")]
        [SerializeField] private float rechargeTimeBothGenerators = 3.0f;

        [Tooltip("Tiempo de recarga del escudo con 1 solo generador activo")]
        [SerializeField] private float rechargeTimeSingleGenerator = 6.0f;

        [Header("Partes del Boss")]
        [SerializeField] private BossShieldGeneratorPart leftGenerator;
        [SerializeField] private BossShieldGeneratorPart rightGenerator;
        [SerializeField] private GameObject energyShieldVisual;
        [SerializeField] private SpriteRenderer energyShieldRenderer;

        [Header("Aviso Visual de Recarga del Escudo")]
        [Tooltip("Tamaño inicial del anillo de carga respecto al escudo: se contrae hasta el tamaño del escudo al completar la recarga")]
        [SerializeField] private float chargeRingStartScale = 2.4f;

        [Tooltip("Tamaño final de la onda expansiva al reactivarse el escudo (respecto al escudo)")]
        [SerializeField] private float shieldWaveEndScale = 2.8f;

        [Tooltip("Duración de la onda expansiva al reactivarse el escudo (segundos)")]
        [SerializeField] private float shieldWaveDuration = 0.5f;

        [Header("Ataque Láser")]
        [Tooltip("Intervalo en segundos entre cada ráfaga de 3 láseres")]
        [SerializeField] private float laserAttackInterval = 3.0f;

        [Tooltip("Tag del pool para los proyectiles láser del jefe")]
        [SerializeField] private string laserPoolTag = "EnemyLaser";

        [Tooltip("Velocidad de vuelo de los rayos láser")]
        [SerializeField] private float laserSpeed = 11f;

        [Header("Movimiento en Órbita")]
        [Tooltip("Radio de órbita alrededor del jugador")]
        [SerializeField] private float orbitRadius = 8.0f;

        [Tooltip("Sentido de giro de órbita (+1 o -1)")]
        [SerializeField] private float orbitDirection = 1f;

        // Estado del Escudo de Energía
        private float _currentEnergyShield;
        private bool _isShieldActive;
        private float _shieldRechargeTimer;
        private float _currentRechargeDuration;

        // Estado de Ataque
        private float _laserAttackTimer;

        // Aviso visual de recarga del escudo. Vive fuera de la jerarquía del jefe (sigue su posición) para no
        // agrandar la barrera invisible de EnemyManager, que se calcula con los sprites hijos del jefe.
        private static readonly Color ShieldFxColor = new Color(0.3f, 0.95f, 1f, 1f);
        private Transform _shieldFxRoot;
        private SpriteRenderer _chargeRing;
        private SpriteRenderer _chargeFill;
        private SpriteRenderer _shieldWave;
        private float _shieldWaveTimer = -1f;

        // Pixel art de CUSTODIUS (Resources/Bosses, 45 PPU con el jefe a escala 1.5 = misma densidad que las naves).
        // Si falta algún sprite se mantienen las formas tintadas de siempre.
        private const string CoreSpritePath = "Bosses/custodius_core";
        private const string LeftGeneratorSpritePath = "Bosses/custodius_generator_left";
        private const string RightGeneratorSpritePath = "Bosses/custodius_generator_right";
        private const float PodOffsetY = -7f / 45f; // la cápsula del ojo cuelga 7 px bajo la línea de los brazos
        private static readonly Color ArtHitFlashColor = new Color(1f, 0.55f, 0.55f, 1f);

        // Eventos para UI y sistemas del juego
        public static event Action<BossCentinelMajoris> OnBossSpawned;
        public static event Action<BossCentinelMajoris> OnBossDefeatedEvent;
        public event Action OnBossStatusChanged;

        public float CenterSphereHealth => _currentHealth;
        public float CenterSphereMaxHealth => centerSphereMaxHealth;
        public float EnergyShieldHealth => _currentEnergyShield;
        public float EnergyShieldMaxHealth => energyShieldMaxHealth;
        public bool IsShieldActive => _isShieldActive;
        public float ShieldRechargeRemaining => Mathf.Max(0f, _currentRechargeDuration - _shieldRechargeTimer);
        public float ShieldRechargeDuration => _currentRechargeDuration;
        public bool IsRecharging => !_isShieldActive && ActiveGeneratorsCount > 0;

        // IBossStatus (HUD genérico de jefes)
        public string BossDisplayName => "CUSTODIUS";
        public string CoreLabel => "NÚCLEO";
        public float CoreHealth => _currentHealth;
        public float CoreMaxHealth => centerSphereMaxHealth;

        public string GetPartsStatus(bool leftSide)
        {
            BossShieldGeneratorPart gen = leftSide ? leftGenerator : rightGenerator;
            string label = leftSide ? "GEN-IZQ" : "GEN-DER";
            if (gen == null) return string.Empty;
            return gen.IsDestroyed
                ? $"<color=#FF4444>[{label}: DESTRUIDO]</color>"
                : $"<color=#00E5FF>[{label}: {Mathf.CeilToInt(gen.CurrentHealth)}/{Mathf.CeilToInt(gen.MaxHealth)}]</color>";
        }

        public BossShieldGeneratorPart LeftGenerator => leftGenerator;
        public BossShieldGeneratorPart RightGenerator => rightGenerator;

        public int ActiveGeneratorsCount
        {
            get
            {
                int count = 0;
                if (leftGenerator != null && !leftGenerator.IsDestroyed) count++;
                if (rightGenerator != null && !rightGenerator.IsDestroyed) count++;
                return count;
            }
        }

        protected override void Awake()
        {
            base.Awake();

            EnsurePartsSetup();
        }

        private void EnsurePartsSetup()
        {
            if (leftGenerator == null)
            {
                leftGenerator = transform.Find("LeftGenerator")?.GetComponent<BossShieldGeneratorPart>();
            }
            if (rightGenerator == null)
            {
                rightGenerator = transform.Find("RightGenerator")?.GetComponent<BossShieldGeneratorPart>();
            }

            if (leftGenerator != null) leftGenerator.Initialize(this, true);
            if (rightGenerator != null) rightGenerator.Initialize(this, false);
            ApplyArt();

            if (energyShieldVisual == null)
            {
                Transform shieldT = transform.Find("EnergyShieldVisual");
                if (shieldT != null) energyShieldVisual = shieldT.gameObject;
            }

            if (energyShieldVisual != null)
            {
                // Asegurar anclaje concéntrico exacto al centro del jefe
                energyShieldVisual.transform.localPosition = Vector3.zero;
                energyShieldVisual.transform.localRotation = Quaternion.identity;

                // CRÍTICO: El escudo visual es un Compound Collider hijo y NUNCA debe tener Rigidbody2D propio
                // para evitar que la gravedad física de Unity 2D lo tire hacia abajo al spawnear.
                var rbs = energyShieldVisual.GetComponents<Rigidbody2D>();
                for (int i = 0; i < rbs.Length; i++)
                {
                    if (Application.isPlaying) Destroy(rbs[i]);
                    else DestroyImmediate(rbs[i]);
                }

                var boxCols = energyShieldVisual.GetComponents<BoxCollider2D>();
                for (int i = 0; i < boxCols.Length; i++)
                {
                    if (Application.isPlaying) Destroy(boxCols[i]);
                    else DestroyImmediate(boxCols[i]);
                }

                if (energyShieldRenderer == null)
                {
                    energyShieldRenderer = energyShieldVisual.GetComponent<SpriteRenderer>();
                }

                var shieldCol = energyShieldVisual.GetComponent<CircleCollider2D>();
                if (shieldCol == null)
                {
                    shieldCol = energyShieldVisual.AddComponent<CircleCollider2D>();
                }
                shieldCol.isTrigger = true;
                shieldCol.radius = 0.5f;
                shieldCol.offset = Vector2.zero;

                var shieldPart = energyShieldVisual.GetComponent<BossEnergyShieldPart>();
                if (shieldPart == null)
                {
                    shieldPart = energyShieldVisual.AddComponent<BossEnergyShieldPart>();
                }
                shieldPart.Initialize(this);

                // Escudo en pixel art (aro + panal). Mide 1 unidad: con la escala del escudo (4.8) y la del jefe (1.5)
                // queda en 7.2 u, el tamaño real de su collider (el anillo provisional medía el doble que la zona que bloquea)
                Sprite shieldArt = FxSprites.ShieldBoss;
                if (shieldArt != null && energyShieldRenderer != null) energyShieldRenderer.sprite = shieldArt;
            }
        }

        private void ApplyArt()
        {
            if (spriteRenderer == null) return;
            Sprite core = Resources.Load<Sprite>(CoreSpritePath);
            if (core == null) return;

            spriteRenderer.sprite = core;
            if (TryGetComponent(out CircleCollider2D col))
            {
                col.radius = 0.68f;
                col.offset = new Vector2(0f, PodOffsetY);
            }

            if (leftGenerator != null) leftGenerator.ApplyArt(Resources.Load<Sprite>(LeftGeneratorSpritePath), ArtHitFlashColor);
            if (rightGenerator != null) rightGenerator.ApplyArt(Resources.Load<Sprite>(RightGeneratorSpritePath), ArtHitFlashColor);

            _hitFlashColor = ArtHitFlashColor;
            _defaultColor = Color.white;
            spriteRenderer.color = Color.white;
        }

        public override void OnSpawnFromPool()
        {
            // Ejecutar ciclo base de Enemy primero (registro en EnemyManager, etc.)
            base.OnSpawnFromPool();

            // Tras el ciclo base: ApplyArt deshace el tinte del EnemyData sobre el núcleo (el sprite ya trae sus colores)
            EnsurePartsSetup();

            // Configurar Esfera Central (1500 HP garantizados)
            _maxHealth = centerSphereMaxHealth;
            _currentHealth = centerSphereMaxHealth;

            // Configurar Escudo de Energía (500 HP)
            _currentEnergyShield = energyShieldMaxHealth;
            _isShieldActive = true;
            _shieldRechargeTimer = 0f;

            // Asegurar que el escudo visual esté concéntrico y activo
            if (energyShieldVisual != null)
            {
                energyShieldVisual.transform.localPosition = Vector3.zero;
                energyShieldVisual.transform.localRotation = Quaternion.identity;
                energyShieldVisual.SetActive(true);
            }

            // Reparar generadores laterales (1000 HP cada uno)
            if (leftGenerator != null) leftGenerator.RepairGenerator();
            if (rightGenerator != null) rightGenerator.RepairGenerator();

            EnsureShieldFx();
            HideShieldFx();

            orbitDirection = Random.value > 0.5f ? 1f : -1f;
            _laserAttackTimer = laserAttackInterval;

            OnBossSpawned?.Invoke(this);
            BossEvents.RaiseSpawned(this);
            OnBossStatusChanged?.Invoke();

            Debug.Log("<color=red><b>[BOSS FIGHT]</b> ⚠️ ¡CUSTODIUS HA ENTRADO A LA ARENA!</color>");
        }

        protected override void UpdateChasingBehavior(float deltaTime, Vector2 playerPos, Vector2 separation)
        {
            // 1. Movimiento orbital circular alrededor del jugador
            Vector2 currentPos = transform.position;
            Vector2 toPlayer = playerPos - currentPos;
            float currentDist = toPlayer.magnitude;

            if (currentDist < 0.001f) currentDist = 0.001f;

            Vector2 radialDir = toPlayer / currentDist;
            Vector2 tangentDir = new Vector2(-radialDir.y, radialDir.x) * orbitDirection;

            // Corrección de distancia para mantener ~8 unidades
            float radialError = currentDist - orbitRadius;
            float radialCorrection = Mathf.Clamp(radialError, -2.0f, 2.0f);

            Vector2 moveDir = (tangentDir * 0.9f + radialDir * (radialCorrection * 0.7f)).normalized;

            float speed = _currentMoveSpeed * _stasisSpeedMultiplier;
            Vector2 velocity = (moveDir * speed) + separation;

            transform.Translate(velocity * deltaTime, Space.World);

            // Orientación: La nave encara continuamente hacia el jugador con su frente (+Y)
            // garantizando que las secciones laterales (generadores) queden a ambos lados
            float angle = Mathf.Atan2(radialDir.y, radialDir.x) * Mathf.Rad2Deg - 90f;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);

            // 2. Control de recarga del Escudo de Energía (y su aviso visual)
            UpdateShieldRecharge(deltaTime);
            UpdateShieldFx(deltaTime);

            // 3. Ataque: Disparar 3 láseres a la vez cada 3 segundos
            _laserAttackTimer -= deltaTime * AttackRateMultiplier;
            if (_laserAttackTimer <= 0f)
            {
                _laserAttackTimer = laserAttackInterval;
                ShootTripleLaser(playerPos);
            }

            // Animación de pulso del escudo si está activo
            if (_isShieldActive && energyShieldRenderer != null)
            {
                float pulse = Mathf.PingPong(Time.time * 3f, 0.25f);
                energyShieldRenderer.color = new Color(0.2f, 0.9f, 1f, 0.55f + pulse);
            }
        }

        private void UpdateShieldRecharge(float deltaTime)
        {
            if (_isShieldActive) return;

            int activeGens = ActiveGeneratorsCount;

            // Si ambos generadores están destruidos, el escudo NO puede recargarse
            if (activeGens <= 0)
            {
                return;
            }

            // Duración según generadores: 3s con 2 activos, 6s con 1 activo
            _currentRechargeDuration = (activeGens >= 2) ? rechargeTimeBothGenerators : rechargeTimeSingleGenerator;

            _shieldRechargeTimer += deltaTime;
            OnBossStatusChanged?.Invoke();

            if (_shieldRechargeTimer >= _currentRechargeDuration)
            {
                RechargeShield();
            }
        }

        private void RechargeShield()
        {
            _currentEnergyShield = energyShieldMaxHealth;
            _isShieldActive = true;
            _shieldRechargeTimer = 0f;
            SetShieldVisualActive(true);

            Debug.Log("<color=cyan><b>[CUSTODIUS]</b> 🛡️ ¡Escudo de Energía RECUPERADO (500 HP)!</color>");
            OnBossStatusChanged?.Invoke();

            if (energyShieldRenderer != null)
            {
                StartCoroutine(ShieldFlashEffect(new Color(0.3f, 1f, 1f, 1f)));
            }

            // Onda expansiva: el escudo vuelve a estar arriba
            if (_shieldWave != null)
            {
                _shieldWaveTimer = 0f;
                _shieldWave.enabled = true;
            }
            CameraShake.Instance?.Shake(0.15f, 0.2f);
            Managers.AudioManager.Instance?.PlayShieldAbsorb();
        }

        #region Aviso visual de recarga del escudo
        private void EnsureShieldFx()
        {
            if (_shieldFxRoot != null) return;

            _shieldFxRoot = new GameObject("CentinelShieldFX").transform;
            _shieldFxRoot.position = transform.position;

            int order = energyShieldRenderer != null ? energyShieldRenderer.sortingOrder + 1 : 6;
            Sprite disc = FxSprites.Disc != null ? FxSprites.Disc : ProceduralSprites.Circle;
            Sprite ring = FxSprites.Ring != null ? FxSprites.Ring : ProceduralSprites.Ring;
            _chargeFill = CreateFxSprite("ChargeFill", disc, order - 2);
            _chargeRing = CreateFxSprite("ChargeRing", ring, order);
            _shieldWave = CreateFxSprite("ShieldWave", ring, order);
        }

        private SpriteRenderer CreateFxSprite(string name, Sprite sprite, int order)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(_shieldFxRoot, false);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            sr.enabled = false;
            return sr;
        }

        private void HideShieldFx()
        {
            _shieldWaveTimer = -1f;
            if (_chargeRing != null) _chargeRing.enabled = false;
            if (_chargeFill != null) _chargeFill.enabled = false;
            if (_shieldWave != null) _shieldWave.enabled = false;
        }

        /// <summary>Diámetro del escudo en unidades de mundo (los sprites procedurales miden 1 unidad).</summary>
        private float ShieldWorldDiameter
        {
            get
            {
                if (energyShieldRenderer != null && energyShieldRenderer.sprite != null)
                {
                    return energyShieldRenderer.sprite.bounds.size.x * Mathf.Abs(energyShieldRenderer.transform.lossyScale.x);
                }
                return 4f;
            }
        }

        /// <summary>
        /// Mientras el escudo recarga: un anillo que se contrae hacia el jefe y parpadea cada vez más rápido,
        /// y un relleno que se va haciendo visible. Al completarse, una onda expansiva hacia afuera.
        /// </summary>
        private void UpdateShieldFx(float deltaTime)
        {
            if (_shieldFxRoot == null) return;
            _shieldFxRoot.position = transform.position;
            float diameter = ShieldWorldDiameter;

            bool charging = IsRecharging && _currentRechargeDuration > 0f;
            if (charging)
            {
                float t = Mathf.Clamp01(_shieldRechargeTimer / _currentRechargeDuration);
                float blink = 0.7f + (0.3f * Mathf.Sin(Time.time * Mathf.Lerp(6f, 24f, t)));

                _chargeRing.enabled = true;
                _chargeRing.transform.localScale = Vector3.one * (diameter * Mathf.Lerp(chargeRingStartScale, 1f, t * t));
                _chargeRing.color = new Color(ShieldFxColor.r, ShieldFxColor.g, ShieldFxColor.b, Mathf.Lerp(0.35f, 1f, t) * blink);

                _chargeFill.enabled = true;
                _chargeFill.transform.localScale = Vector3.one * diameter;
                _chargeFill.color = new Color(ShieldFxColor.r, ShieldFxColor.g, ShieldFxColor.b, Mathf.Lerp(0f, 0.22f, t) * blink);
            }
            else
            {
                if (_chargeRing.enabled) _chargeRing.enabled = false;
                if (_chargeFill.enabled) _chargeFill.enabled = false;
            }

            if (_shieldWaveTimer >= 0f)
            {
                _shieldWaveTimer += deltaTime;
                float p = Mathf.Clamp01(_shieldWaveTimer / Mathf.Max(0.05f, shieldWaveDuration));
                if (p >= 1f)
                {
                    _shieldWaveTimer = -1f;
                    _shieldWave.enabled = false;
                }
                else
                {
                    float easeOut = 1f - ((1f - p) * (1f - p));
                    _shieldWave.transform.localScale = Vector3.one * (diameter * Mathf.Lerp(1f, shieldWaveEndScale, easeOut));
                    _shieldWave.color = new Color(ShieldFxColor.r, ShieldFxColor.g, ShieldFxColor.b, (1f - p) * 0.9f);
                }
            }
        }

        public override void OnReturnToPool()
        {
            base.OnReturnToPool();
            HideShieldFx();
        }

        private void OnDestroy()
        {
            if (_shieldFxRoot != null) Destroy(_shieldFxRoot.gameObject);
        }
        #endregion

        public void DamageEnergyShield(float amount)
        {
            if (!_isShieldActive) return;

            _currentEnergyShield -= amount;
            if (energyShieldRenderer != null)
            {
                StartCoroutine(ShieldFlashEffect(Color.white));
            }

            OnBossStatusChanged?.Invoke();

            if (_currentEnergyShield <= 0f)
            {
                BreakShield();
            }
        }

        private void BreakShield()
        {
            _currentEnergyShield = 0f;
            _isShieldActive = false;
            _shieldRechargeTimer = 0f;
            SetShieldVisualActive(false);

            int activeGens = ActiveGeneratorsCount;
            _currentRechargeDuration = (activeGens >= 2) ? rechargeTimeBothGenerators : rechargeTimeSingleGenerator;

            Debug.Log($"<color=yellow><b>[CUSTODIUS]</b> 💥 ¡Escudo ROTO! Tiempo de recarga: {(activeGens > 0 ? _currentRechargeDuration + "s" : "DESACTIVADO PERMANENTE")}.</color>");

            // Game Feel
            if (CameraShake.Instance != null)
            {
                CameraShake.Instance.Shake(0.3f, 0.45f);
            }

            if (ObjectPooler.Instance != null && ObjectPooler.Instance.HasPool("DeathVFX"))
            {
                GameObject vfxObj = ObjectPooler.Instance.SpawnFromPool("DeathVFX", transform.position, Quaternion.identity);
                if (vfxObj != null && vfxObj.TryGetComponent(out DeathVFX vfx))
                {
                    vfx.PlayEffect(new Color(0.2f, 0.9f, 1f), 40, 2.5f);
                }
            }

            OnBossStatusChanged?.Invoke();
        }

        private void SetShieldVisualActive(bool active)
        {
            if (energyShieldVisual != null)
            {
                if (active)
                {
                    energyShieldVisual.transform.localPosition = Vector3.zero;
                    energyShieldVisual.transform.localRotation = Quaternion.identity;
                }
                energyShieldVisual.SetActive(active);
            }
        }

        private IEnumerator ShieldFlashEffect(Color flashColor)
        {
            if (energyShieldRenderer == null) yield break;
            Color prev = energyShieldRenderer.color;
            energyShieldRenderer.color = flashColor;
            yield return new WaitForSeconds(0.06f);
            if (energyShieldRenderer != null && _isShieldActive)
            {
                energyShieldRenderer.color = prev;
            }
        }

        public void OnGeneratorDestroyed(BossShieldGeneratorPart generator)
        {
            OnBossStatusChanged?.Invoke();

            // Si el escudo estaba recargándose, ajustar la duración restante
            if (!_isShieldActive)
            {
                int remainingGens = ActiveGeneratorsCount;
                if (remainingGens == 1)
                {
                    _currentRechargeDuration = rechargeTimeSingleGenerator;
                }
                else if (remainingGens == 0)
                {
                    Debug.Log("<color=red><b>[CUSTODIUS]</b> ⚡ ¡Ambos generadores destruidos! El escudo NO volverá a recargarse.</color>");
                }
            }
        }

        public void NotifyGeneratorDamaged()
        {
            OnBossStatusChanged?.Invoke();
        }

        public override void TakeDamage(float amount)
        {
            // 1. Si el Escudo de Energía está activo, absorbe el daño
            if (_isShieldActive)
            {
                DamageEnergyShield(amount);
                return;
            }

            // 2. Si el escudo está caído, el impacto daña directamente la Esfera Central (1500 HP)
            base.TakeDamage(amount);
            OnBossStatusChanged?.Invoke();
        }

        /// <summary>
        /// Dispara 3 láseres a la vez cada 3 segundos: Centro (0°), Izquierda (-15°), Derecha (+15°).
        /// </summary>
        private void ShootTripleLaser(Vector2 playerPos)
        {
            if (ObjectPooler.Instance == null) return;

            Vector2 baseDir = (playerPos - (Vector2)transform.position).normalized;
            if (baseDir.sqrMagnitude < 0.001f) baseDir = transform.up;

            float[] spreadAngles = { -15f, 0f, 15f };

            string poolTag = ObjectPooler.Instance.HasPool(laserPoolTag) ? laserPoolTag : "EnemyBullet";

            for (int i = 0; i < spreadAngles.Length; i++)
            {
                Vector2 angledDir = Quaternion.Euler(0f, 0f, spreadAngles[i]) * baseDir;
                Vector2 spawnPos = (Vector2)transform.position + (angledDir * 1.5f);

                FireProjectile(poolTag, spawnPos, angledDir, laserSpeed);
            }
        }

        public override bool IsHeavy => true;
        public override bool IsBoss => true;

        public override void ConvertIntoParriedProjectile(Vector2 impulseDirection, float impulseForce, float damage = 50f, float maxDuration = 2f)
        {
            // La nave insignia es masiva e inmune al empuje billar de parry; recibe daño directo
            TakeDamage(damage);
        }

        public override void Die()
        {
            Debug.Log("<color=green><b>[VICTORIA]</b> 🏆 ¡CUSTODIUS HA SIDO COMPLETAMENTE DESTRUIDO!</color>");

            OnBossDefeatedEvent?.Invoke(this);
            BossEvents.RaiseDefeated(this);
            HideShieldFx();

            // Explosión titánica
            if (CameraShake.Instance != null)
            {
                CameraShake.Instance.Shake(0.7f, 0.85f);
            }

            base.Die();
        }
    }
}
