using System;
using System.Collections;
using ArenaSurvivor.Combat;
using ArenaSurvivor.Managers;
using ArenaSurvivor.Pool;
using UnityEngine;
using Random = UnityEngine.Random;

namespace ArenaSurvivor.Entities
{
    /// <summary>
    /// BOSS 2_MOTHERSHIP:
    /// - Spawn: entra desde el borde superior de la pantalla.
    /// - Comportamiento: se desplaza de lado a lado por la parte superior de la pantalla disparando desde 5 puntos.
    /// - Diseño y mecánicas:
    ///   a) 2 cañones inferiores (1000hp c/u): ráfagas de 6 láseres cortos alternando | y / ( |/|/ ):
    ///      "|" va directo al jugador y "/" ligeramente desviado a un lado, esté donde esté (también por encima de la nave).
    ///   b) 2 cañones superiores (1000hp c/u): barrido de balas en abanico de 140° centrado en el jugador,
    ///      de izquierda a derecha y luego de derecha a izquierda.
    ///   c) Rail Gun central (1500hp, núcleo): carga apuntando al jugador (línea de aviso) y dispara un rayo que cruza toda la pantalla.
    ///      El rayo se puede bloquear con un Parry en el momento justo.
    ///   Escudo de Energía (1500hp): protege toda la nave; al romperse se recarga a los 10s.
    ///   La nave cae al destruir el Rail Gun central. Los cañones destruidos dejan de disparar.
    /// - Botín: Rail Gun / Fan Shot / Laser Shotgun (el jugador elige 1).
    /// La jerarquía (casco, cañones, escudo) se genera sola en Awake si el prefab no la trae.
    /// </summary>
    public class BossMothership : Enemy, IShieldedBoss, IBossStatus
    {
        [Header("Vida")]
        [SerializeField] private float railGunCoreMaxHealth = 1500f;
        [SerializeField] private float gunMaxHealth = 1000f;
        [SerializeField] private float energyShieldMaxHealth = 1500f;
        [SerializeField] private float shieldRechargeTime = 10f;

        [Header("Movimiento (parte superior de la pantalla)")]
        [Tooltip("Altura sobre el jugador a la que patrulla (dentro de la pantalla)")]
        [SerializeField] private float hoverHeightAbovePlayer = 8.5f;
        [SerializeField] private float sweepAmplitude = 10f;
        [SerializeField] private float sweepPeriod = 9f;
        [SerializeField] private float followSpeed = 2.2f;

        [Header("Cañones Inferiores |/|/")]
        [SerializeField] private float lowerGunInterval = 3.8f;
        [SerializeField] private float lowerLaserSpeed = 10f;
        [SerializeField] private float lowerLaserSpacing = 0.12f;
        [Tooltip("Desvío en grados de los láseres '/' respecto a la línea directa al jugador")]
        [SerializeField] private float lowerLaserSlantAngle = 14f;

        [Header("Cañones Superiores (abanico)")]
        [SerializeField] private float upperGunInterval = 3.2f;
        [SerializeField] private float fanSweepDuration = 1.3f;
        [SerializeField] private float fanBulletInterval = 0.075f;
        [SerializeField] private float fanBulletSpeed = 6.5f;
        [Tooltip("Apertura total del abanico en grados, centrada en la dirección al jugador")]
        [SerializeField] private float fanArcDegrees = 140f;

        [Header("Rail Gun Central")]
        [SerializeField] private float railGunInterval = 8.5f;
        [SerializeField] private float railGunChargeTime = 1.6f;
        [Tooltip("Últimos segundos de la carga en los que la línea deja de seguir al jugador")]
        [SerializeField] private float railGunLockTime = 0.4f;
        [SerializeField] private float railGunLength = 70f;
        [SerializeField] private float railGunWidth = 1.1f;

        [Header("Pools")]
        [SerializeField] private string laserPoolTag = "EnemyLaser";
        [SerializeField] private string bulletPoolTag = "EnemyBullet";

        private MothershipPart _lowerLeft, _lowerRight, _upperLeft, _upperRight;
        private GameObject _shieldVisual;
        private SpriteRenderer _shieldRenderer;

        private float _currentEnergyShield;
        private bool _isShieldActive;
        private float _shieldRechargeTimer;

        private bool _entering;
        private float _sweepPhase;
        private float _anchorX;
        private float _lowerGunTimer;
        private float _upperGunTimer;
        private bool _nextFanFromLeft = true;
        private float _railGunTimer;
        private Coroutine _railGunRoutine;
        private BeamEffect _telegraph;

        public event Action OnBossStatusChanged;

        public override bool IsHeavy => true;
        public override bool IsBoss => true;

        #region IBossStatus / IShieldedBoss
        public string BossDisplayName => "MOTHERSHIP";
        public string CoreLabel => "RAIL GUN CENTRAL";
        public float CoreHealth => _currentHealth;
        public float CoreMaxHealth => railGunCoreMaxHealth;
        public float EnergyShieldHealth => _currentEnergyShield;
        public float EnergyShieldMaxHealth => energyShieldMaxHealth;
        public bool IsShieldActive => _isShieldActive;
        public bool IsRecharging => !_isShieldActive;
        public float ShieldRechargeRemaining => Mathf.Max(0f, shieldRechargeTime - _shieldRechargeTimer);

        public string GetPartsStatus(bool leftSide)
        {
            MothershipPart upper = leftSide ? _upperLeft : _upperRight;
            MothershipPart lower = leftSide ? _lowerLeft : _lowerRight;
            string side = leftSide ? "IZQ" : "DER";
            return $"{FormatPart($"SUP-{side}", upper)} {FormatPart($"INF-{side}", lower)}";
        }

        private static string FormatPart(string label, MothershipPart part)
        {
            if (part == null) return string.Empty;
            return part.IsDestroyed
                ? $"<color=#FF4444>[{label}: X]</color>"
                : $"<color=#FFB060>[{label}: {Mathf.CeilToInt(part.CurrentHealth)}]</color>";
        }

        public void NotifyStatusChanged() => OnBossStatusChanged?.Invoke();
        #endregion

        protected override void Awake()
        {
            base.Awake();
            EnsureHierarchy();
        }

        /// <summary>
        /// Construye casco, cañones y escudo si no existen (prefab mínimo o plantilla en runtime).
        /// </summary>
        private void EnsureHierarchy()
        {
            if (transform.Find("Hull") == null)
            {
                CreateVisual("Hull", Vector2.zero, new Vector2(13f, 2.2f), new Color(0.22f, 0.2f, 0.32f, 1f), ProceduralSprites.Square, 1);
                CreateVisual("Bridge", new Vector2(0f, 1.1f), new Vector2(4.5f, 1.2f), new Color(0.3f, 0.27f, 0.42f, 1f), ProceduralSprites.Square, 1);
                CreateVisual("RailBarrel", new Vector2(0f, -1.3f), new Vector2(0.6f, 1.6f), new Color(1f, 0.3f, 0.9f, 1f), ProceduralSprites.Square, 2);
            }

            _lowerLeft = EnsurePart("LowerGunLeft", new Vector2(-3.4f, -1.25f), new Vector2(1.2f, 0.9f), MothershipPart.PartKind.LowerGun, true, new Color(1f, 0.45f, 0.2f));
            _lowerRight = EnsurePart("LowerGunRight", new Vector2(3.4f, -1.25f), new Vector2(1.2f, 0.9f), MothershipPart.PartKind.LowerGun, false, new Color(1f, 0.45f, 0.2f));
            _upperLeft = EnsurePart("UpperGunLeft", new Vector2(-5.8f, 0.75f), new Vector2(1.1f, 1.1f), MothershipPart.PartKind.UpperGun, true, new Color(1f, 0.8f, 0.25f));
            _upperRight = EnsurePart("UpperGunRight", new Vector2(5.8f, 0.75f), new Vector2(1.1f, 1.1f), MothershipPart.PartKind.UpperGun, false, new Color(1f, 0.8f, 0.25f));

            Transform shieldT = transform.Find("EnergyShieldVisual");
            if (shieldT == null)
            {
                GameObject shield = new GameObject("EnergyShieldVisual");
                shield.transform.SetParent(transform, false);
                shield.transform.localScale = new Vector3(15.5f, 5.2f, 1f);
                var sr = shield.AddComponent<SpriteRenderer>();
                sr.sprite = ProceduralSprites.Circle;
                sr.sortingOrder = 6;
                // Collider elíptico aproximado (cápsula horizontal) en unidades locales del sprite de 1x1
                var capsule = shield.AddComponent<CapsuleCollider2D>();
                capsule.direction = CapsuleDirection2D.Horizontal;
                capsule.size = new Vector2(1f, 1f);
                capsule.isTrigger = true;
                shieldT = shield.transform;
            }

            _shieldVisual = shieldT.gameObject;
            _shieldRenderer = _shieldVisual.GetComponent<SpriteRenderer>();
            var shieldPart = _shieldVisual.GetComponent<BossEnergyShieldPart>();
            if (shieldPart == null) shieldPart = _shieldVisual.AddComponent<BossEnergyShieldPart>();
            shieldPart.Initialize(this, false);
        }

        private void CreateVisual(string name, Vector2 localPos, Vector2 size, Color color, Sprite sprite, int sortingOffset)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = 2 + sortingOffset;
        }

        private MothershipPart EnsurePart(string name, Vector2 localPos, Vector2 size, MothershipPart.PartKind kind, bool left, Color color)
        {
            Transform t = transform.Find(name);
            MothershipPart part;
            if (t == null)
            {
                GameObject go = new GameObject(name);
                go.layer = gameObject.layer;
                go.tag = gameObject.tag;
                go.transform.SetParent(transform, false);
                go.transform.localPosition = localPos;
                go.transform.localScale = new Vector3(size.x, size.y, 1f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = ProceduralSprites.Square;
                sr.sortingOrder = 5;
                part = go.AddComponent<MothershipPart>();
                var rb = go.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    rb.bodyType = RigidbodyType2D.Kinematic;
                    rb.gravityScale = 0f;
                }
            }
            else
            {
                part = t.GetComponent<MothershipPart>();
            }

            if (part != null) part.Initialize(this, kind, left, gunMaxHealth, color);
            return part;
        }

        public override void OnSpawnFromPool()
        {
            base.OnSpawnFromPool();
            EnsureHierarchy();

            _maxHealth = railGunCoreMaxHealth;
            _currentHealth = railGunCoreMaxHealth;
            transform.rotation = Quaternion.identity;

            _currentEnergyShield = energyShieldMaxHealth;
            _isShieldActive = true;
            _shieldRechargeTimer = 0f;
            if (_shieldVisual != null) _shieldVisual.SetActive(true);

            _entering = true;
            _sweepPhase = Random.value > 0.5f ? 0f : Mathf.PI;
            _anchorX = transform.position.x;
            _lowerGunTimer = 2.5f;
            _upperGunTimer = 4f;
            _railGunTimer = 6f;
            _railGunRoutine = null;

            BossEvents.RaiseSpawned(this);
            OnBossStatusChanged?.Invoke();
            Debug.Log("<color=red><b>[BOSS FIGHT]</b> ⚠️ ¡MOTHERSHIP HA ENTRADO A LA ARENA!</color>");
        }

        public override void OnReturnToPool()
        {
            base.OnReturnToPool();
            if (_railGunRoutine != null)
            {
                StopCoroutine(_railGunRoutine);
                _railGunRoutine = null;
            }
            HideTelegraph();
        }

        protected override void UpdateChasingBehavior(float deltaTime, Vector2 playerPos, Vector2 separation)
        {
            UpdateMovement(deltaTime, playerPos);
            UpdateShield(deltaTime);
            if (_entering) return;

            float attackDt = deltaTime * AttackRateMultiplier;

            _lowerGunTimer -= attackDt;
            if (_lowerGunTimer <= 0f)
            {
                _lowerGunTimer = lowerGunInterval;
                if (IsGunAlive(_lowerLeft)) StartCoroutine(LowerGunVolley(_lowerLeft, 0f));
                if (IsGunAlive(_lowerRight)) StartCoroutine(LowerGunVolley(_lowerRight, lowerLaserSpacing * 0.5f));
            }

            _upperGunTimer -= attackDt;
            if (_upperGunTimer <= 0f)
            {
                _upperGunTimer = upperGunInterval;
                MothershipPart gun = _nextFanFromLeft ? _upperLeft : _upperRight;
                if (!IsGunAlive(gun)) gun = _nextFanFromLeft ? _upperRight : _upperLeft;
                if (IsGunAlive(gun)) StartCoroutine(FanSweep(gun, _nextFanFromLeft));
                _nextFanFromLeft = !_nextFanFromLeft;
            }

            if (_railGunRoutine == null)
            {
                _railGunTimer -= attackDt;
                if (_railGunTimer <= 0f)
                {
                    _railGunTimer = railGunInterval;
                    _railGunRoutine = StartCoroutine(RailGunRoutine());
                }
            }

            if (_isShieldActive && _shieldRenderer != null)
            {
                float pulse = Mathf.PingPong(Time.time * 2.5f, 0.12f);
                _shieldRenderer.color = new Color(0.2f, 0.9f, 1f, 0.22f + pulse);
            }
        }

        private void UpdateMovement(float deltaTime, Vector2 playerPos)
        {
            float speedMult = _stasisSpeedMultiplier;
            float targetY = playerPos.y + hoverHeightAbovePlayer;

            // Mantener la patrulla dentro de la parte superior de la pantalla visible
            if (CameraBounds.TryGet(out Vector2 camCenter, out Vector2 camHalf))
            {
                targetY = Mathf.Min(targetY, camCenter.y + camHalf.y - 2.8f);
            }

            Vector2 pos = transform.position;
            if (_entering)
            {
                pos.y = Mathf.MoveTowards(pos.y, targetY, 6f * deltaTime * speedMult);
                pos.x = Mathf.MoveTowards(pos.x, playerPos.x, 3f * deltaTime * speedMult);
                transform.position = pos;
                if (Mathf.Abs(pos.y - targetY) < 0.2f)
                {
                    _entering = false;
                    _anchorX = pos.x;
                }
                return;
            }

            // El centro del barrido sigue lentamente al jugador; la nave oscila de lado a lado alrededor de él
            _anchorX = Mathf.MoveTowards(_anchorX, playerPos.x, followSpeed * 0.6f * deltaTime * speedMult);
            _sweepPhase += deltaTime * speedMult * (Mathf.PI * 2f / Mathf.Max(1f, sweepPeriod));
            float targetX = _anchorX + (Mathf.Sin(_sweepPhase) * sweepAmplitude);

            pos.x = Mathf.MoveTowards(pos.x, targetX, (sweepAmplitude * 1.2f) * deltaTime * speedMult);
            pos.y = Mathf.MoveTowards(pos.y, targetY, followSpeed * deltaTime * speedMult);
            transform.position = pos;
        }

        private static bool IsGunAlive(MothershipPart gun) => gun != null && !gun.IsDestroyed;

        #region Ataques
        private IEnumerator LowerGunVolley(MothershipPart gun, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);

            for (int i = 0; i < 6; i++)
            {
                if (!IsGunAlive(gun) || _currentState == EnemyState.Dead) yield break;

                // Siempre hacia el jugador (antes "|" iba recto hacia abajo y fallaba si el jugador estaba arriba o al lado)
                Vector2 aim = DirectionToPlayer(gun.transform.position);
                Vector2 origin = (Vector2)gun.transform.position + (aim * 0.6f);
                Vector2 dir = aim; // |
                if (i % 2 == 1)
                {
                    // "/" : ligeramente desviado, alternando lado para cerrar la esquiva
                    float slant = ((i / 2) % 2 == 0 ? 1f : -1f) * (gun.IsLeft ? -1f : 1f) * lowerLaserSlantAngle;
                    dir = Quaternion.Euler(0f, 0f, slant) * aim;
                }

                FireProjectile(laserPoolTag, origin, dir, lowerLaserSpeed);
                yield return new WaitForSeconds(lowerLaserSpacing);
            }
        }

        private IEnumerator FanSweep(MothershipPart gun, bool leftToRight)
        {
            // Abanico centrado en el jugador (sigue su posición durante el barrido): con el jugador debajo equivale
            // al barrido original de -160° a -20°, pero también le alcanza si está a los lados o por encima de la nave
            float halfArc = fanArcDegrees * 0.5f;
            float startOffset = leftToRight ? -halfArc : halfArc;
            float endOffset = -startOffset;
            float elapsed = 0f;
            float emitTimer = 0f;

            while (elapsed < fanSweepDuration)
            {
                if (!IsGunAlive(gun) || _currentState == EnemyState.Dead) yield break;

                float dt = Time.deltaTime;
                elapsed += dt;
                emitTimer -= dt;
                if (emitTimer <= 0f)
                {
                    emitTimer = fanBulletInterval;
                    Vector2 toPlayer = DirectionToPlayer(gun.transform.position);
                    float centerAngle = Mathf.Atan2(toPlayer.y, toPlayer.x) * Mathf.Rad2Deg;
                    float angle = (centerAngle + Mathf.Lerp(startOffset, endOffset, elapsed / fanSweepDuration)) * Mathf.Deg2Rad;
                    Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    FireProjectile(bulletPoolTag, (Vector2)gun.transform.position + (dir * 0.7f), dir, fanBulletSpeed);
                }
                yield return null;
            }
        }

        private IEnumerator RailGunRoutine()
        {
            Vector2 aimDir = Vector2.down;
            float elapsed = 0f;
            AudioManager.Instance?.PlayBossAlert();

            while (elapsed < railGunChargeTime)
            {
                if (_currentState == EnemyState.Dead) yield break;

                Vector2 origin = RailGunOrigin;
                bool locked = elapsed >= railGunChargeTime - railGunLockTime;
                if (!locked && PlayerController.Instance != null)
                {
                    Vector2 toPlayer = (Vector2)PlayerController.Instance.transform.position - origin;
                    if (toPlayer.sqrMagnitude > 0.01f) aimDir = toPlayer.normalized;
                }

                if (_telegraph == null)
                {
                    // Duración holgada: la línea la oculta esta rutina al disparar (o la nave al morir)
                    _telegraph = BeamEffect.Show(origin, aimDir, railGunLength, 0.18f, new Color(1f, 0.25f, 0.9f), railGunChargeTime + 1f, true);
                }
                else
                {
                    _telegraph.Retarget(origin, aimDir, railGunLength);
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            HideTelegraph();
            FireRailGun(RailGunOrigin, aimDir);
            _railGunRoutine = null;
        }

        private Vector2 RailGunOrigin => (Vector2)transform.position + new Vector2(0f, -2.1f);

        /// <summary>
        /// Dirección normalizada desde 'from' hasta el jugador (hacia abajo si no hay jugador).
        /// </summary>
        private static Vector2 DirectionToPlayer(Vector2 from)
        {
            if (PlayerController.Instance == null) return Vector2.down;
            Vector2 toPlayer = (Vector2)PlayerController.Instance.transform.position - from;
            return toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : Vector2.down;
        }

        private void FireRailGun(Vector2 origin, Vector2 dir)
        {
            BeamEffect.Show(origin, dir, railGunLength, railGunWidth * 1.4f, new Color(1f, 0.25f, 0.9f), 0.45f);
            AudioManager.Instance?.PlayRailgun();
            CameraShake.Instance?.Shake(0.3f, 0.4f);

            PlayerController player = PlayerController.Instance;
            if (player == null || player.IsDead) return;

            // Distancia del jugador a la línea del rayo
            Vector2 toPlayer = (Vector2)player.transform.position - origin;
            float along = Vector2.Dot(toPlayer, dir);
            if (along < 0f || along > railGunLength) return;
            float perpendicular = Mathf.Abs((toPlayer.x * dir.y) - (toPlayer.y * dir.x));
            if (perpendicular > (railGunWidth * 0.5f) + 0.45f) return;

            if (player.CurrentParryState == ParryState.Active)
            {
                // Parry perfecto: el rayo se bloquea y la nave recibe el contraataque
                player.OnParryTriggered(this);
            }
            else
            {
                player.TakeDamage(OutgoingDamage);
            }
        }

        private void HideTelegraph()
        {
            if (_telegraph != null)
            {
                _telegraph.Hide();
                _telegraph = null;
            }
        }
        #endregion

        #region Escudo
        private void UpdateShield(float deltaTime)
        {
            if (_isShieldActive) return;

            _shieldRechargeTimer += deltaTime;
            OnBossStatusChanged?.Invoke();
            if (_shieldRechargeTimer >= shieldRechargeTime)
            {
                _currentEnergyShield = energyShieldMaxHealth;
                _isShieldActive = true;
                _shieldRechargeTimer = 0f;
                if (_shieldVisual != null) _shieldVisual.SetActive(true);
                OnBossStatusChanged?.Invoke();
            }
        }

        public void DamageEnergyShield(float amount)
        {
            if (!_isShieldActive) return;

            _currentEnergyShield -= amount;
            if (_shieldRenderer != null) _shieldRenderer.color = new Color(1f, 1f, 1f, 0.5f);
            OnBossStatusChanged?.Invoke();

            if (_currentEnergyShield <= 0f)
            {
                _currentEnergyShield = 0f;
                _isShieldActive = false;
                _shieldRechargeTimer = 0f;
                if (_shieldVisual != null) _shieldVisual.SetActive(false);

                CameraShake.Instance?.Shake(0.3f, 0.45f);
                if (ObjectPooler.Instance != null && ObjectPooler.Instance.HasPool("DeathVFX"))
                {
                    GameObject vfxObj = ObjectPooler.Instance.SpawnFromPool("DeathVFX", transform.position, Quaternion.identity);
                    if (vfxObj != null && vfxObj.TryGetComponent(out DeathVFX vfx))
                    {
                        vfx.PlayEffect(new Color(0.2f, 0.9f, 1f), 50, 4f);
                    }
                }
                OnBossStatusChanged?.Invoke();
            }
        }
        #endregion

        public override void TakeDamage(float amount)
        {
            if (_isShieldActive)
            {
                DamageEnergyShield(amount);
                return;
            }

            base.TakeDamage(amount);
            OnBossStatusChanged?.Invoke();
        }

        public override void ConvertIntoParriedProjectile(Vector2 impulseDirection, float impulseForce, float damage = 50f, float maxDuration = 2f)
        {
            TakeDamage(damage);
        }

        public override void Die()
        {
            if (_currentState == EnemyState.Dead) return;
            Debug.Log("<color=green><b>[VICTORIA]</b> 🏆 ¡MOTHERSHIP DERRIBADA!</color>");

            HideTelegraph();
            BossEvents.RaiseDefeated(this);
            CameraShake.Instance?.Shake(0.8f, 0.9f);

            // Explosiones en cadena en los cañones
            if (ObjectPooler.Instance != null && ObjectPooler.Instance.HasPool("DeathVFX"))
            {
                MothershipPart[] parts = { _lowerLeft, _lowerRight, _upperLeft, _upperRight };
                foreach (MothershipPart part in parts)
                {
                    if (part == null) continue;
                    GameObject vfxObj = ObjectPooler.Instance.SpawnFromPool("DeathVFX", part.transform.position, Quaternion.identity);
                    if (vfxObj != null && vfxObj.TryGetComponent(out DeathVFX vfx))
                    {
                        vfx.PlayEffect(new Color(1f, 0.5f, 0.2f), 40, 2.5f);
                    }
                }
            }

            base.Die();
        }
    }
}
