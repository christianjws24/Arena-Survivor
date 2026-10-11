using System;
using System.Collections;
using System.Collections.Generic;
using ArenaSurvivor.Entities;
using ArenaSurvivor.Managers;
using ArenaSurvivor.Pool;
using ArenaSurvivor.Upgrades;
using UnityEngine;

namespace ArenaSurvivor.Combat
{
    /// <summary>
    /// Armas adicionales del jugador (botín de jefes): 3 ranuras de armas normales y 1 ranura de arma especial.
    ///   Normales  (Laser Burst, Fan Shot, Laser Shotgun): teclado H / J / K  -  mando R2 / L2 / L1 (gatillos)
    ///   Especial  (Rail Gun):                             teclado L          -  mando Triángulo
    /// Disparan hacia la dirección de mira (último movimiento con WASD / stick izquierdo).
    /// Cada arma tiene una cantidad de munición: cada pulsación gasta 1 carga y, al agotarse, empieza el cooldown de recarga
    /// (que se reduce al subir de nivel). Entre disparos hay una pequeña pausa para no vaciar el cargador de golpe.
    ///
    /// Modelo de daño (porcentaje sobre PlayerController.BaseDamage, ya multiplicado por Calibre Pesado / Anclaje Ofensivo):
    ///   Laser Burst   +15%  x3 láseres perforantes          3 cargas, recarga 6s
    ///   Rail Gun      +300% rayo grueso a través de la pantalla, empuja enemigos y al jugador   2 cargas, recarga 20s
    ///   Fan Shot       0%   abanico de 180°               3 cargas, recarga 6s
    ///   Laser Shotgun -25%  x6 perdigones con caída de daño 4 cargas, recarga 5s
    ///   Energy Shield  pasivo: absorbe un golpe, se recarga en 60s (-3s por nivel). No ocupa ranura.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerWeapons : MonoBehaviour
    {
        public const int SlotCount = 4;
        /// <summary>Ranura reservada para el arma especial (las anteriores son para armas normales).</summary>
        public const int SpecialSlot = SlotCount - 1;
        public static readonly string[] SlotKeyLabels = { "H", "J", "K", "L" };
        public static readonly string[] SlotGamepadLabels = { "R2", "L2", "L1", "△" };

        public const string LaserBurstId = "laser_burst";
        public const string RailGunId = "rail_gun";
        public const string FanShotId = "fan_shot";
        public const string LaserShotgunId = "laser_shotgun";
        public const string EnergyShieldId = "energy_shield";

        public static PlayerWeapons Instance { get; private set; }

        [Header("Proyectiles")]
        [SerializeField] private string bulletPoolTag = "PlayerBullet";

        [Header("Munición (disparos antes de tener que recargar)")]
        [SerializeField] private int laserBurstAmmo = 3;
        [SerializeField] private int railGunAmmo = 2;
        [SerializeField] private int fanShotAmmo = 3;
        [SerializeField] private int laserShotgunAmmo = 4;
        [Tooltip("Pausa mínima entre dos disparos del mismo arma mientras le quede munición (segundos)")]
        [SerializeField] private float shotDelay = 0.35f;

        [Header("Cooldown de Recarga Base (segundos, empieza al agotar la munición)")]
        [SerializeField] private float laserBurstCooldown = 6f;
        [SerializeField] private float railGunCooldown = 20f;
        [SerializeField] private float fanShotCooldown = 6f;
        [SerializeField] private float laserShotgunCooldown = 5f;
        [Tooltip("Reducción de cooldown por cada nivel por encima del 1 (0.15 = -15%)")]
        [SerializeField] private float cooldownReductionPerLevel = 0.15f;

        [Header("Bonus de Daño sobre el Daño Base")]
        [SerializeField] private float laserBurstBonus = 0.15f;
        [SerializeField] private float railGunBonus = 3.0f;
        [SerializeField] private float fanShotBonus = 0f;
        [SerializeField] private float laserShotgunBonus = -0.25f;

        [Header("Rail Gun")]
        [SerializeField] private float railGunLength = 45f;
        [SerializeField] private float railGunWidth = 2.8f;
        [Tooltip("Retroceso que sufre el jugador al disparar")]
        [SerializeField] private float railGunKnockback = 17f;
        [Tooltip("Velocidad de empuje que recibe cada enemigo alcanzado (en la dirección del rayo)")]
        [SerializeField] private float railGunEnemyKnockback = 24f;

        [Header("Laser Shotgun (caída de daño)")]
        [Tooltip("Distancia hasta la que los perdigones hacen el 100%")]
        [SerializeField] private float shotgunFalloffStart = 2.5f;
        [Tooltip("Distancia a la que llegan al daño mínimo")]
        [SerializeField] private float shotgunFalloffEnd = 8f;
        [SerializeField] private float shotgunMinDamageMultiplier = 0.2f;

        [Header("Energy Shield")]
        [SerializeField] private float shieldBaseRecharge = 60f;
        [SerializeField] private float shieldRechargeReductionPerLevel = 3f;

        private readonly string[] _slots = new string[SlotCount];
        // _cooldownRemaining = recarga en curso (>0 solo mientras recarga). Al terminar, la munición se rellena.
        private readonly Dictionary<string, float> _cooldownRemaining = new Dictionary<string, float>();
        private readonly Dictionary<string, int> _ammo = new Dictionary<string, int>();
        private readonly Dictionary<string, float> _shotDelayRemaining = new Dictionary<string, float>();
        private readonly List<Collider2D> _beamHits = new List<Collider2D>(128);
        private readonly HashSet<int> _beamDamaged = new HashSet<int>();

        private PlayerController _player;
        private bool _shieldReady;
        private float _shieldRechargeRemaining;
        private SpriteRenderer _shieldVisual;

        public event Action OnSlotsChanged;

        public bool HasEnergyShield => GetLevel(EnergyShieldId) > 0;
        public bool IsShieldReady => _shieldReady;
        public float ShieldRechargeRemaining => _shieldRechargeRemaining;
        public float ShieldRechargeDuration => shieldBaseRecharge - (shieldRechargeReductionPerLevel * Mathf.Max(0, GetLevel(EnergyShieldId) - 1));

        private void Awake()
        {
            Instance = this;
            _player = GetComponent<PlayerController>();
        }

        private void OnEnable()
        {
            if (UpgradeManager.Instance != null)
            {
                UpgradeManager.Instance.OnUpgradeAdded += HandleUpgradeAdded;
                UpgradeManager.Instance.OnUpgradeRemoved += HandleUpgradeRemoved;
            }
        }

        private void Start()
        {
            // Re-suscripción por si UpgradeManager despertó después que el jugador
            if (UpgradeManager.Instance != null)
            {
                UpgradeManager.Instance.OnUpgradeAdded -= HandleUpgradeAdded;
                UpgradeManager.Instance.OnUpgradeRemoved -= HandleUpgradeRemoved;
                UpgradeManager.Instance.OnUpgradeAdded += HandleUpgradeAdded;
                UpgradeManager.Instance.OnUpgradeRemoved += HandleUpgradeRemoved;

                // Armas que ya se poseyeran (ej. trucos de depuración)
                foreach (var kvp in UpgradeManager.Instance.ActiveUpgrades)
                {
                    if (UpgradeManager.Instance.TryGetUpgradeData(kvp.Key, out UpgradeData data))
                    {
                        HandleUpgradeAdded(data, kvp.Value);
                    }
                }
            }

            UI.WeaponHUD.EnsureExists();
        }

        private void OnDisable()
        {
            if (UpgradeManager.Instance != null)
            {
                UpgradeManager.Instance.OnUpgradeAdded -= HandleUpgradeAdded;
                UpgradeManager.Instance.OnUpgradeRemoved -= HandleUpgradeRemoved;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        #region Ranuras
        public string GetSlotWeaponId(int slot) => slot >= 0 && slot < SlotCount ? _slots[slot] : null;

        /// <summary>Segundos de recarga restantes (0 = no está recargando).</summary>
        public float GetCooldownRemaining(string weaponId)
        {
            return weaponId != null && _cooldownRemaining.TryGetValue(weaponId, out float cd) ? cd : 0f;
        }

        /// <summary>Munición disponible del arma.</summary>
        public int GetAmmo(string weaponId)
        {
            if (weaponId == null) return 0;
            return _ammo.TryGetValue(weaponId, out int ammo) ? ammo : GetMaxAmmo(weaponId);
        }

        /// <summary>Capacidad del cargador del arma.</summary>
        public int GetMaxAmmo(string weaponId)
        {
            int baseAmmo;
            switch (weaponId)
            {
                case LaserBurstId: baseAmmo = laserBurstAmmo; break;
                case RailGunId: baseAmmo = railGunAmmo; break;
                case FanShotId: baseAmmo = fanShotAmmo; break;
                case LaserShotgunId: baseAmmo = laserShotgunAmmo; break;
                default: return 1;
            }

            // La nave Slayer lleva más munición en todas las armas HJKL (redondeo hacia arriba)
            return Mathf.Max(1, Mathf.CeilToInt(baseAmmo * Data.ShipSelection.Current.WeaponAmmoMultiplier));
        }

        public float GetCooldownDuration(string weaponId)
        {
            float baseCd;
            switch (weaponId)
            {
                case LaserBurstId: baseCd = laserBurstCooldown; break;
                case RailGunId: baseCd = railGunCooldown; break;
                case FanShotId: baseCd = fanShotCooldown; break;
                case LaserShotgunId: baseCd = laserShotgunCooldown; break;
                default: return 1f;
            }

            int level = Mathf.Max(1, GetLevel(weaponId));
            return baseCd * Mathf.Max(0.3f, 1f - (cooldownReductionPerLevel * (level - 1)));
        }

        private static int GetLevel(string id) => UpgradeManager.Instance != null ? UpgradeManager.Instance.GetLevel(id) : 0;

        /// <summary>
        /// Armas especiales: van en la ranura especial (L / Triángulo). Por ahora solo el Rail Gun.
        /// </summary>
        public static bool IsSpecialWeapon(string weaponId) => weaponId == RailGunId;

        public static bool IsSpecialSlot(int slot) => slot == SpecialSlot;

        /// <summary>
        /// Etiqueta del botón de la ranura: la del mando si hay uno conectado, si no la del teclado.
        /// </summary>
        public static string GetSlotLabel(int slot)
        {
            if (slot < 0 || slot >= SlotCount) return string.Empty;
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Gamepad.current != null) return SlotGamepadLabels[slot];
#endif
            return SlotKeyLabels[slot];
        }

        /// <summary>
        /// Ranura que ocupa (o ocuparía) el arma: la suya si ya la tiene, si no la primera libre de su tipo. -1 si no hay hueco.
        /// </summary>
        public int FindSlotFor(string weaponId)
        {
            int current = Array.IndexOf(_slots, weaponId);
            if (current >= 0) return current;

            if (IsSpecialWeapon(weaponId))
            {
                return _slots[SpecialSlot] == null ? SpecialSlot : -1;
            }

            for (int i = 0; i < SlotCount; i++)
            {
                if (!IsSpecialSlot(i) && _slots[i] == null) return i;
            }
            return -1;
        }

        private void HandleUpgradeAdded(UpgradeData data, int newLevel)
        {
            if (data == null || data.upgradeType != UpgradeType.Weapon) return;

            if (data.isPassiveWeapon)
            {
                if (data.id == EnergyShieldId && newLevel == 1)
                {
                    _shieldReady = true;
                    _shieldRechargeRemaining = 0f;
                    UpdateShieldVisual();
                }
                OnSlotsChanged?.Invoke();
                return;
            }

            if (Array.IndexOf(_slots, data.id) < 0)
            {
                int free = FindSlotFor(data.id);
                if (free < 0)
                {
                    Debug.LogWarning($"[PlayerWeapons] No hay ranuras libres para '{data.id}'.");
                    return;
                }
                _slots[free] = data.id;
                _cooldownRemaining[data.id] = 0f;
                _shotDelayRemaining[data.id] = 0f;
                _ammo[data.id] = GetMaxAmmo(data.id);
                Debug.Log($"<color=lime><b>[ARMAS]</b> {data.upgradeName} asignada a la tecla [{SlotKeyLabels[free]}] / mando [{SlotGamepadLabels[free]}]</color>");
            }

            OnSlotsChanged?.Invoke();
        }

        private void HandleUpgradeRemoved(UpgradeData data)
        {
            if (data == null) return;
            int idx = Array.IndexOf(_slots, data.id);
            if (idx >= 0) _slots[idx] = null;
            OnSlotsChanged?.Invoke();
        }
        #endregion

        private void Update()
        {
            // Recargas y pausa entre disparos
            for (int i = 0; i < SlotCount; i++)
            {
                string id = _slots[i];
                if (id == null) continue;

                if (_shotDelayRemaining.TryGetValue(id, out float delay) && delay > 0f)
                {
                    _shotDelayRemaining[id] = Mathf.Max(0f, delay - Time.deltaTime);
                }

                if (_cooldownRemaining.TryGetValue(id, out float cd) && cd > 0f)
                {
                    cd = Mathf.Max(0f, cd - Time.deltaTime);
                    _cooldownRemaining[id] = cd;
                    if (cd <= 0f)
                    {
                        // Recarga completada: cargador lleno
                        _ammo[id] = GetMaxAmmo(id);
                    }
                }
            }

            UpdateShieldRecharge();

            if (_player == null || _player.IsDead || Time.timeScale <= 0f) return;
            if (UI.LevelUpUIManager.Instance != null && UI.LevelUpUIManager.Instance.IsModalOpen) return;

            for (int i = 0; i < SlotCount; i++)
            {
                if (WasSlotPressed(i)) TryFireSlot(i);
            }
        }

        private static bool WasSlotPressed(int slot)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                switch (slot)
                {
                    case 0: if (kb.hKey.wasPressedThisFrame) return true; break;
                    case 1: if (kb.jKey.wasPressedThisFrame) return true; break;
                    case 2: if (kb.kKey.wasPressedThisFrame) return true; break;
                    case 3: if (kb.lKey.wasPressedThisFrame) return true; break;
                }
            }

            // Mando: armas normales en los gatillos (R2, L2, L1), arma especial en Triángulo (R1 / Círculo = Parry)
            var gp = UnityEngine.InputSystem.Gamepad.current;
            if (gp != null)
            {
                switch (slot)
                {
                    case 0: if (gp.rightTrigger.wasPressedThisFrame) return true; break;
                    case 1: if (gp.leftTrigger.wasPressedThisFrame) return true; break;
                    case 2: if (gp.leftShoulder.wasPressedThisFrame) return true; break;
                    case 3: if (gp.buttonNorth.wasPressedThisFrame) return true; break;
                }
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            switch (slot)
            {
                case 0: return Input.GetKeyDown(KeyCode.H);
                case 1: return Input.GetKeyDown(KeyCode.J);
                case 2: return Input.GetKeyDown(KeyCode.K);
                case 3: return Input.GetKeyDown(KeyCode.L);
            }
#endif
            return false;
        }

        private void TryFireSlot(int slot)
        {
            string id = _slots[slot];
            if (id == null || GetCooldownRemaining(id) > 0f || GetAmmo(id) <= 0) return;
            if (_shotDelayRemaining.TryGetValue(id, out float delay) && delay > 0f) return;

            Vector2 dir = _player.FacingDirection;
            switch (id)
            {
                case LaserBurstId: StartCoroutine(FireLaserBurst(dir)); break;
                case RailGunId: FireRailGun(dir); break;
                case FanShotId: FireFanShot(dir); break;
                case LaserShotgunId: FireLaserShotgun(dir); break;
                default: return;
            }

            // Cada disparo gasta 1 carga; al agotar el cargador empieza el cooldown de recarga
            int ammoLeft = GetAmmo(id) - 1;
            _ammo[id] = ammoLeft;
            if (ammoLeft <= 0)
            {
                _cooldownRemaining[id] = GetCooldownDuration(id);
            }
            else
            {
                _shotDelayRemaining[id] = shotDelay;
            }
        }

        #region Disparos
        private Projectile SpawnShot(Vector2 dir)
        {
            if (ObjectPooler.Instance == null) return null;
            GameObject obj = ObjectPooler.Instance.SpawnFromPool(bulletPoolTag, transform.position, Quaternion.identity);
            return obj != null && obj.TryGetComponent(out Projectile p) ? p : null;
        }

        private IEnumerator FireLaserBurst(Vector2 dir)
        {
            for (int i = 0; i < 3; i++)
            {
                Projectile p = SpawnShot(dir);
                if (p != null)
                {
                    p.Configure(_player.GetShotDamage(laserBurstBonus), 24f, 1.1f, 999, BulletSprites.LaserBurst, new Color(0.4f, 1f, 0.6f), new Vector2(2.4f, 0.6f));
                    p.Launch(dir);
                }
                AudioManager.Instance?.PlayWeaponFire();
                yield return new WaitForSeconds(0.08f);
            }
        }

        private void FireFanShot(Vector2 dir)
        {
            const int count = 15;
            for (int i = 0; i < count; i++)
            {
                float angle = Mathf.Lerp(-90f, 90f, i / (float)(count - 1));
                Vector2 shotDir = Quaternion.Euler(0f, 0f, angle) * dir;
                Projectile p = SpawnShot(shotDir);
                if (p != null)
                {
                    p.Configure(_player.GetShotDamage(fanShotBonus), 15f, 1.4f, 0, BulletSprites.Fan, new Color(1f, 0.85f, 0.25f), Vector2.one);
                    p.Launch(shotDir);
                }
            }
            AudioManager.Instance?.PlayWeaponFire();
            CameraShake.Instance?.Shake(0.08f, 0.08f);
        }

        private void FireLaserShotgun(Vector2 dir)
        {
            float[] spread = { -10f, -6f, -2f, 2f, 6f, 10f };
            for (int i = 0; i < spread.Length; i++)
            {
                Vector2 shotDir = Quaternion.Euler(0f, 0f, spread[i]) * dir;
                Projectile p = SpawnShot(shotDir);
                if (p != null)
                {
                    p.Configure(_player.GetShotDamage(laserShotgunBonus), 22f, 0.5f, 0, BulletSprites.Shotgun, new Color(1f, 0.55f, 0.15f), new Vector2(1.6f, 0.55f));
                    p.SetDamageFalloff(shotgunFalloffStart, shotgunFalloffEnd, shotgunMinDamageMultiplier);
                    p.Launch(shotDir);
                }
            }
            _player.ApplyKnockback(-dir * 4f);
            AudioManager.Instance?.PlayWeaponFire();
            CameraShake.Instance?.Shake(0.1f, 0.15f);
        }

        private void FireRailGun(Vector2 dir)
        {
            Vector2 origin = transform.position;
            float damage = _player.GetShotDamage(railGunBonus);

            Color railColor = new Color(1f, 0.3f, 0.95f);
            if (RailgunEffect.IsAvailable) RailgunEffect.Show(origin, dir, railGunLength, railColor, 0.4f);
            else BeamEffect.Show(origin, dir, railGunLength, railGunWidth, railColor, 0.4f);
            HitstopAndShake();
            AudioManager.Instance?.PlayRailgun();

            DamageAlongLine(origin, dir, railGunLength, railGunWidth, damage, _beamHits, _beamDamaged, railGunEnemyKnockback);

            // El retroceso empuja al jugador en sentido contrario al disparo
            _player.ApplyKnockback(-dir * railGunKnockback);
        }

        private static void HitstopAndShake()
        {
            HitStopManager.Instance?.TriggerHitStop(0.06f, 0.2f, 0.08f); // vuelta suave, como el Parry
            CameraShake.Instance?.Shake(0.3f, 0.45f);
        }

        /// <summary>
        /// Daña una vez a cada enemigo/escudo dentro de un rectángulo orientado (rayo).
        /// Si un jefe tiene el escudo activo, el rayo lo golpea una sola vez en vez de una vez por cada parte.
        /// 'enemyKnockback' (> 0) empuja a los enemigos ligeros supervivientes en la dirección del rayo.
        /// </summary>
        public static void DamageAlongLine(Vector2 origin, Vector2 dir, float length, float width, float damage,
            List<Collider2D> hitBuffer, HashSet<int> damagedBuffer, float enemyKnockback = 0f)
        {
            dir.Normalize();
            Vector2 center = origin + (dir * (length * 0.5f));
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            ContactFilter2D filter = ContactFilter2D.noFilter;
            filter.useTriggers = true;
            hitBuffer.Clear();
            damagedBuffer.Clear();
            int count = Physics2D.OverlapBox(center, new Vector2(length, width), angle, filter, hitBuffer);

            // 1. Escudos de energía primero (un impacto por jefe)
            for (int i = 0; i < count; i++)
            {
                Collider2D col = hitBuffer[i];
                if (col == null || !col.TryGetComponent(out BossEnergyShieldPart shield)) continue;
                Enemy owner = shield.MainBoss;
                if (owner == null || !damagedBuffer.Add(owner.GetInstanceID())) continue;
                shield.TakeDamage(damage);
            }

            // 2. Enemigos y partes de jefes
            for (int i = 0; i < count; i++)
            {
                Collider2D col = hitBuffer[i];
                if (col == null) continue;
                Enemy enemy = col.GetComponent<Enemy>();
                if (enemy == null) enemy = col.GetComponentInParent<Enemy>();
                if (enemy == null || enemy.CurrentState == EnemyState.Dead) continue;

                // Partes protegidas por un escudo que ya recibió el impacto
                IShieldedBoss shieldedOwner = enemy as IShieldedBoss ?? enemy.GetComponentInParent<IShieldedBoss>();
                if (shieldedOwner != null && shieldedOwner.IsShieldActive && shieldedOwner is Enemy ownerEnemy &&
                    damagedBuffer.Contains(ownerEnemy.GetInstanceID()))
                {
                    continue;
                }

                if (!damagedBuffer.Add(enemy.GetInstanceID())) continue;
                enemy.TakeDamage(damage);

                if (enemyKnockback > 0f && enemy.CurrentState == EnemyState.Chasing)
                {
                    enemy.ApplyKnockback(dir * enemyKnockback);
                }
            }
        }
        #endregion

        #region Energy Shield
        private void UpdateShieldRecharge()
        {
            if (!HasEnergyShield)
            {
                if (_shieldVisual != null && _shieldVisual.enabled) _shieldVisual.enabled = false;
                return;
            }

            if (!_shieldReady)
            {
                _shieldRechargeRemaining -= Time.deltaTime;
                if (_shieldRechargeRemaining <= 0f)
                {
                    _shieldReady = true;
                    _shieldRechargeRemaining = 0f;
                    AudioManager.Instance?.PlayShieldAbsorb();
                }
            }

            UpdateShieldVisual();
        }

        /// <summary>
        /// Consumido por PlayerController.TakeDamage: si el escudo está listo, absorbe el golpe completo.
        /// </summary>
        public bool TryAbsorbHit()
        {
            if (!HasEnergyShield || !_shieldReady) return false;

            _shieldReady = false;
            _shieldRechargeRemaining = ShieldRechargeDuration;
            UpdateShieldVisual();

            AudioManager.Instance?.PlayShieldAbsorb();
            CameraShake.Instance?.Shake(0.15f, 0.2f);
            if (ObjectPooler.Instance != null)
            {
                GameObject vfxObj = ObjectPooler.Instance.SpawnFromPool("DeathVFX", transform.position, Quaternion.identity);
                if (vfxObj != null && vfxObj.TryGetComponent(out DeathVFX vfx))
                {
                    vfx.PlayEffect(new Color(0.3f, 0.9f, 1f), 30, 1.6f);
                }
            }
            return true;
        }

        private void UpdateShieldVisual()
        {
            if (_shieldVisual == null)
            {
                if (!HasEnergyShield) return;
                GameObject go = new GameObject("EnergyShieldVisual");
                go.transform.SetParent(transform, false);
                _shieldVisual = go.AddComponent<SpriteRenderer>();
                _shieldVisual.sprite = FxSprites.ShieldPlayer != null ? FxSprites.ShieldPlayer : ProceduralSprites.Ring;
                _shieldVisual.sortingOrder = 7;
            }

            float parentScale = Mathf.Max(0.01f, transform.lossyScale.x);
            _shieldVisual.transform.localScale = Vector3.one * (1.9f / parentScale);
            _shieldVisual.enabled = HasEnergyShield && _shieldReady;
            if (_shieldVisual.enabled)
            {
                float pulse = 0.55f + Mathf.PingPong(Time.time * 1.5f, 0.35f);
                _shieldVisual.color = new Color(0.3f, 0.9f, 1f, pulse);
            }
        }
        #endregion
    }
}
