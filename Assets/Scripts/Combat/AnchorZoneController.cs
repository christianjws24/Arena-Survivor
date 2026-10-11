using System.Collections.Generic;
using ArenaSurvivor.Entities;
using ArenaSurvivor.Pool;
using ArenaSurvivor.Upgrades;
using UnityEngine;

namespace ArenaSurvivor.Combat
{
    /// <summary>
    /// Modificadores de Anclaje (power-ups 'defensive_anchor', 'ofensive_anchor' y 'stasis_nova').
    /// Cada cierto tiempo ancla una zona circular pequeña en la posición del jugador:
    /// - Defensivo: dentro de la zona el cooldown del Parry es casi cero.
    /// - Ofensivo: dentro de la zona el jugador Y los enemigos ganan +50% de daño y cadencia (riesgo/recompensa).
    /// - Estallido Nova: al expirar la zona explota y ralentiza masivamente a los enemigos cercanos.
    /// Indicador de duración: un disco interior con su aro se va cerrando desde el borde hasta el centro a medida que se agota
    /// el tiempo, y en los últimos segundos la zona parpadea cada vez más rápido y se aclara hacia blanco.
    /// Se añade automáticamente al jugador.
    /// </summary>
    [DisallowMultipleComponent]
    public class AnchorZoneController : MonoBehaviour
    {
        public static AnchorZoneController Instance { get; private set; }

        [Header("Ciclo de la Zona")]
        [SerializeField] private float deployInterval = 14f;
        [SerializeField] private float zoneDuration = 6f;
        [SerializeField] private float zoneRadius = 3.2f;
        [Tooltip("Segundos finales en los que la zona parpadea avisando de que va a terminar")]
        [SerializeField] private float warningTime = 1.5f;

        [Header("Anclaje Defensivo")]
        [Tooltip("Multiplicador del cooldown del Parry dentro de la zona (casi cero)")]
        [SerializeField] private float defensiveParryCooldownMultiplier = 0.05f;

        [Header("Anclaje Ofensivo")]
        [SerializeField] private float offensivePlayerDamageMultiplier = 1.5f;
        [SerializeField] private float offensivePlayerFireRateMultiplier = 1.5f;
        [SerializeField] private float offensiveEnemyAttackRateMultiplier = 1.5f;
        [Tooltip("Daño extra (en vidas) de enemigos dentro de la zona ofensiva")]
        [SerializeField] private int offensiveEnemyDamageBonus = 1;

        [Header("Estallido Nova")]
        [SerializeField] private float novaRadius = 5f;
        [SerializeField] private float novaStasisDuration = 3.5f;
        [SerializeField] private float novaSpeedMultiplier = 0.15f;

        private enum AnchorMode { None, Defensive, Offensive }

        private float _cycleTimer = 2f;
        private float _zoneTimer;
        private bool _zoneActive;
        private Vector2 _zoneCenter;
        private AnchorMode _zoneMode;
        private GameObject _zoneVisual;
        private SpriteRenderer _zoneFill;
        private SpriteRenderer _zoneRing;
        private SpriteRenderer _timerFill;
        private SpriteRenderer _timerRing;
        private Color _zoneColor;
        private readonly List<Collider2D> _novaBuffer = new List<Collider2D>(64);

        private static readonly Color DefensiveColor = new Color(0.95f, 0.8f, 0.2f, 1f);
        private static readonly Color OffensiveColor = new Color(1f, 0.3f, 0.25f, 1f);

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_zoneVisual != null) Destroy(_zoneVisual);
        }

        private AnchorMode OwnedMode
        {
            get
            {
                var um = UpgradeManager.Instance;
                if (um == null) return AnchorMode.None;
                if (um.HasUpgrade("ofensive_anchor")) return AnchorMode.Offensive;
                if (um.HasUpgrade("defensive_anchor")) return AnchorMode.Defensive;
                return AnchorMode.None;
            }
        }

        private void Update()
        {
            if (PlayerController.Instance != null && PlayerController.Instance.IsDead)
            {
                if (_zoneActive) EndZone(false);
                return;
            }

            if (_zoneActive)
            {
                _zoneTimer -= Time.deltaTime;
                AnimateZone();
                if (_zoneTimer <= 0f) EndZone(true);
                return;
            }

            AnchorMode mode = OwnedMode;
            if (mode == AnchorMode.None) return;

            _cycleTimer -= Time.deltaTime;
            if (_cycleTimer <= 0f)
            {
                _cycleTimer = deployInterval;
                DeployZone(mode);
            }
        }

        private void DeployZone(AnchorMode mode)
        {
            _zoneActive = true;
            _zoneMode = mode;
            _zoneTimer = zoneDuration;
            _zoneCenter = transform.position;

            EnsureVisual();
            Color c = mode == AnchorMode.Offensive ? OffensiveColor : DefensiveColor;
            _zoneColor = c;
            _zoneVisual.transform.position = new Vector3(_zoneCenter.x, _zoneCenter.y, 0f);
            _zoneVisual.transform.localScale = Vector3.one * (zoneRadius * 2f);
            _zoneFill.color = new Color(c.r, c.g, c.b, 0.12f);
            _zoneRing.color = new Color(c.r, c.g, c.b, 0.85f);
            _timerFill.transform.localScale = Vector3.one;
            _timerRing.transform.localScale = Vector3.one;
            _zoneVisual.SetActive(true);
            AnimateZone();
        }

        private void AnimateZone()
        {
            if (_zoneRing == null || _timerFill == null || _timerRing == null) return;

            // 1. Indicador de tiempo: el disco interior se cierra hacia el centro (1 = recién anclada, 0 = se acaba)
            float remaining = Mathf.Clamp01(_zoneTimer / Mathf.Max(0.01f, zoneDuration));
            Vector3 timerScale = Vector3.one * Mathf.Max(0.001f, remaining);
            _timerFill.transform.localScale = timerScale;
            _timerRing.transform.localScale = timerScale;

            // 2. Aviso final: parpadeo cada vez más rápido y color que se aclara hacia blanco
            Color c = _zoneColor;
            float ringAlpha = 0.85f;
            float timerAlpha = 0.9f;
            if (_zoneTimer < warningTime)
            {
                float urgency = 1f - Mathf.Clamp01(_zoneTimer / Mathf.Max(0.01f, warningTime));
                float blink = Mathf.PingPong(Time.time * Mathf.Lerp(5f, 14f, urgency), 1f);
                ringAlpha = Mathf.Lerp(0.25f, 1f, blink);
                timerAlpha = ringAlpha;
                c = Color.Lerp(c, Color.white, urgency * 0.6f);
            }

            _zoneRing.color = new Color(c.r, c.g, c.b, ringAlpha);
            _timerRing.color = new Color(c.r, c.g, c.b, timerAlpha);
            _timerFill.color = new Color(c.r, c.g, c.b, 0.16f);
        }

        private void EndZone(bool allowNova)
        {
            _zoneActive = false;
            if (_zoneVisual != null) _zoneVisual.SetActive(false);

            if (allowNova && UpgradeManager.Instance != null && UpgradeManager.Instance.HasUpgrade("stasis_nova"))
            {
                TriggerStasisNova(_zoneCenter);
            }
        }

        private void TriggerStasisNova(Vector2 center)
        {
            ContactFilter2D filter = ContactFilter2D.noFilter;
            filter.useTriggers = true;
            int hits = Physics2D.OverlapCircle(center, novaRadius, filter, _novaBuffer);
            for (int i = 0; i < hits; i++)
            {
                Collider2D col = _novaBuffer[i];
                if (col == null) continue;
                if (col.TryGetComponent(out Enemy enemy) && enemy.CurrentState != EnemyState.Dead)
                {
                    enemy.ApplyStasis(novaStasisDuration, novaSpeedMultiplier);
                }
            }

            CameraShake.Instance?.Shake(0.25f, 0.3f);
            if (ObjectPooler.Instance != null)
            {
                if (ObjectPooler.Instance.HasPool("ShockwaveVFX"))
                {
                    ObjectPooler.Instance.SpawnFromPool("ShockwaveVFX", center, Quaternion.identity);
                }
                GameObject vfxObj = ObjectPooler.Instance.SpawnFromPool("DeathVFX", center, Quaternion.identity);
                if (vfxObj != null && vfxObj.TryGetComponent(out DeathVFX vfx))
                {
                    vfx.PlayEffect(new Color(0.35f, 0.75f, 1f), 40, 2.5f);
                }
            }
        }

        private void EnsureVisual()
        {
            if (_zoneVisual != null) return;

            // Pixel art (1 unidad, como las formas provisionales): disco con borde tramado, aro con marcas de dial
            // y aro fino para el temporizador. Todos blancos, se tiñen con el color de la zona.
            Sprite disc = FxSprites.Disc != null ? FxSprites.Disc : ProceduralSprites.Circle;
            Sprite zoneRing = FxSprites.RingAnchor != null ? FxSprites.RingAnchor : ProceduralSprites.Ring;
            Sprite timerRing = FxSprites.Ring != null ? FxSprites.Ring : ProceduralSprites.Ring;

            _zoneVisual = new GameObject("AnchorZone");
            _zoneFill = _zoneVisual.AddComponent<SpriteRenderer>();
            _zoneFill.sprite = disc;
            _zoneFill.sortingOrder = -6;

            GameObject timerFillGo = new GameObject("TimerFill");
            timerFillGo.transform.SetParent(_zoneVisual.transform, false);
            _timerFill = timerFillGo.AddComponent<SpriteRenderer>();
            _timerFill.sprite = disc;
            _timerFill.sortingOrder = -5;

            GameObject ringGo = new GameObject("Ring");
            ringGo.transform.SetParent(_zoneVisual.transform, false);
            _zoneRing = ringGo.AddComponent<SpriteRenderer>();
            _zoneRing.sprite = zoneRing;
            _zoneRing.sortingOrder = -4;

            GameObject timerRingGo = new GameObject("TimerRing");
            timerRingGo.transform.SetParent(_zoneVisual.transform, false);
            _timerRing = timerRingGo.AddComponent<SpriteRenderer>();
            _timerRing.sprite = timerRing;
            _timerRing.sortingOrder = -3;
            _zoneVisual.SetActive(false);
        }

        private bool IsInsideZone(Vector2 position, AnchorMode mode)
        {
            return _zoneActive && _zoneMode == mode && (position - _zoneCenter).sqrMagnitude <= zoneRadius * zoneRadius;
        }

        #region Consultas para Jugador y Enemigos
        /// <summary>Multiplicador del cooldown del Parry (Anclaje Defensivo).</summary>
        public static float GetPlayerParryCooldownMultiplier(Vector2 playerPos)
        {
            var inst = Instance;
            return inst != null && inst.IsInsideZone(playerPos, AnchorMode.Defensive) ? inst.defensiveParryCooldownMultiplier : 1f;
        }

        /// <summary>Multiplicador de daño del jugador (Anclaje Ofensivo).</summary>
        public static float GetPlayerDamageMultiplier(Vector2 playerPos)
        {
            var inst = Instance;
            return inst != null && inst.IsInsideZone(playerPos, AnchorMode.Offensive) ? inst.offensivePlayerDamageMultiplier : 1f;
        }

        /// <summary>Multiplicador de cadencia de disparo automático del jugador (Anclaje Ofensivo).</summary>
        public static float GetPlayerFireRateMultiplier(Vector2 playerPos)
        {
            var inst = Instance;
            return inst != null && inst.IsInsideZone(playerPos, AnchorMode.Offensive) ? inst.offensivePlayerFireRateMultiplier : 1f;
        }

        /// <summary>Multiplicador de cadencia de ataque de un enemigo dentro de la zona ofensiva.</summary>
        public static float GetEnemyAttackMultiplier(Vector2 enemyPos)
        {
            var inst = Instance;
            return inst != null && inst.IsInsideZone(enemyPos, AnchorMode.Offensive) ? inst.offensiveEnemyAttackRateMultiplier : 1f;
        }

        /// <summary>Daño extra de un enemigo dentro de la zona ofensiva.</summary>
        public static int GetEnemyDamageBonus(Vector2 enemyPos)
        {
            var inst = Instance;
            return inst != null && inst.IsInsideZone(enemyPos, AnchorMode.Offensive) ? inst.offensiveEnemyDamageBonus : 0;
        }
        #endregion
    }
}
