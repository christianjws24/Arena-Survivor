using ArenaSurvivor.Entities;
using ArenaSurvivor.Pool;
using ArenaSurvivor.Upgrades;
using UnityEngine;

namespace ArenaSurvivor.Combat
{
    /// <summary>
    /// Gestiona el movimiento, daño, tiempo de vida y reciclaje de las balas del jugador.
    /// Diseñado para cero allocations por impacto.
    /// Por defecto se comporta como el disparo automático (100% del daño base + 'pierce' + 'bounce': rebota hacia enemigos cercanos).
    /// Las armas HJKL lo reconfiguran tras sacarlo del pool con Configure() (láseres, perdigones con caída de daño, etc.).
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Projectile : MonoBehaviour, IPoolable
    {
        [Header("Estadísticas del Proyectil")]
        [SerializeField] private float speed = 14f;
        [Tooltip("Solo se usa si no hay PlayerController en escena; normalmente el daño sale de PlayerController.GetDamage()")]
        [SerializeField] private float baseDamage = 35f;
        [SerializeField] private float damage = 35f;
        [SerializeField] private float maxLifetime = 3f;

        [Header("Filtro de Colisión")]
        [Tooltip("LayerMask de los enemigos a impactar")]
        [SerializeField] private LayerMask hitMask;

        // Geometría de Rebote: radio de búsqueda del siguiente enemigo y tiempo de vida mínimo tras rebotar
        private const float BounceSearchRadius = 7f;
        private const float BounceMinRemainingLifetime = 1f;
        private static readonly Collider2D[] BounceBuffer = new Collider2D[64];

        private Vector2 _direction;
        private float _currentLifetime;
        private bool _isReturningToPool;

        // Valores por defecto del prefab (restaurados al volver al pool)
        private float _defaultSpeed;
        private float _defaultLifetime;
        private Vector3 _defaultScale;
        private Color _defaultColor = Color.white;
        private Sprite _defaultSprite;
        private SpriteRenderer _spriteRenderer;
        private CircleCollider2D _circleCollider;
        private float _defaultRadius;
        private bool _defaultsCached;

        // Caída de daño por distancia recorrida (Laser Shotgun)
        private float _falloffStart = -1f;
        private float _falloffEnd;
        private float _falloffMinMultiplier = 1f;
        private float _distanceTravelled;

        public void SetDamage(float newDamage)
        {
            damage = newDamage;
        }

        /// <summary>
        /// Inicializa la dirección y orientación de la bala al dispararse.
        /// </summary>
        public void Launch(Vector2 direction)
        {
            _direction = direction.normalized;
            UpdateRotation();
        }

        /// <summary>
        /// Reconfigura la bala para un arma especial. Llamar justo después de sacarla del pool y antes de Launch().
        /// Con 'sprite' (pixel art) la bala no se estira: 'scaleMultiplier' solo agranda el radio de impacto igual que antes.
        /// Sin sprite, se tiñe con 'color' y se estira con 'scaleMultiplier' (formas provisionales).
        /// </summary>
        public void Configure(float newDamage, float newSpeed, float lifetime, int pierce, Sprite sprite, Color color, Vector2 scaleMultiplier, int bounces = 0)
        {
            CacheDefaults();
            damage = newDamage;
            speed = newSpeed;
            maxLifetime = lifetime;
            _pierceRemaining = pierce;
            _bouncesRemaining = bounces;

            if (sprite != null && _spriteRenderer != null)
            {
                _spriteRenderer.sprite = sprite;
                _spriteRenderer.color = Color.white;
                transform.localScale = _defaultScale;
                // Un CircleCollider2D escalado de forma no uniforme usa el mayor eje: se conserva ese mismo radio
                if (_circleCollider != null) _circleCollider.radius = _defaultRadius * Mathf.Max(scaleMultiplier.x, scaleMultiplier.y);
                return;
            }

            transform.localScale = new Vector3(_defaultScale.x * scaleMultiplier.x, _defaultScale.y * scaleMultiplier.y, _defaultScale.z);
            if (_spriteRenderer != null) _spriteRenderer.color = color;
        }

        /// <summary>
        /// Activa caída de daño por distancia: 100% hasta 'startDistance', interpolando hasta 'minMultiplier' en 'endDistance'.
        /// </summary>
        public void SetDamageFalloff(float startDistance, float endDistance, float minMultiplier)
        {
            _falloffStart = startDistance;
            _falloffEnd = Mathf.Max(startDistance + 0.01f, endDistance);
            _falloffMinMultiplier = minMultiplier;
        }

        private float CurrentDamage
        {
            get
            {
                if (_falloffStart < 0f) return damage;
                float t = Mathf.InverseLerp(_falloffStart, _falloffEnd, _distanceTravelled);
                return damage * Mathf.Lerp(1f, _falloffMinMultiplier, t);
            }
        }

        private void UpdateRotation()
        {
            float angle = Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        private int _pierceRemaining;
        private int _bouncesRemaining;

        private readonly System.Collections.Generic.HashSet<int> _hitTargets = new System.Collections.Generic.HashSet<int>();

        private void CacheDefaults()
        {
            if (_defaultsCached) return;
            _defaultsCached = true;
            _defaultSpeed = speed;
            _defaultLifetime = maxLifetime;
            _defaultScale = transform.localScale;
            _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            _circleCollider = GetComponent<CircleCollider2D>();
            if (_circleCollider != null) _defaultRadius = _circleCollider.radius;
            if (_spriteRenderer == null) return;

            // Disparo automático en pixel art (sin tinte); si no existe, la cápsula tintada del prefab
            Sprite art = BulletSprites.Player;
            if (art != null)
            {
                _spriteRenderer.sprite = art;
                _spriteRenderer.color = Color.white;
            }
            _defaultSprite = _spriteRenderer.sprite;
            _defaultColor = _spriteRenderer.color;
        }

        public void OnSpawnFromPool()
        {
            CacheDefaults();
            _currentLifetime = 0f;
            _distanceTravelled = 0f;
            _falloffStart = -1f;
            _isReturningToPool = false;
            _hitTargets.Clear();

            speed = _defaultSpeed;
            maxLifetime = _defaultLifetime;
            transform.localScale = _defaultScale;
            if (_circleCollider != null) _circleCollider.radius = _defaultRadius;
            if (_spriteRenderer != null)
            {
                _spriteRenderer.sprite = _defaultSprite;
                _spriteRenderer.color = _defaultColor;
            }

            // Consulta desacoplada O(1): Lee el nivel de las mejoras directamente
            _pierceRemaining = UpgradeManager.Instance != null ? UpgradeManager.Instance.GetLevel("pierce") : 0;
            _bouncesRemaining = UpgradeManager.Instance != null ? UpgradeManager.Instance.GetLevel("bounce") : 0;

            // Disparo automático = 100% del daño base (incluye Calibre Pesado y Anclaje Ofensivo)
            damage = PlayerController.Instance != null
                ? PlayerController.Instance.GetShotDamage(0f)
                : (baseDamage > 0f ? baseDamage : 35f);
        }

        public void OnReturnToPool()
        {
            _isReturningToPool = true;
            _hitTargets.Clear();
        }

        private void Update()
        {
            if (_isReturningToPool) return;

            // Movimiento constante
            float step = speed * Time.deltaTime;
            transform.Translate(Vector3.right * step, Space.Self);
            _distanceTravelled += step;

            // Control de tiempo de vida
            _currentLifetime += Time.deltaTime;
            if (_currentLifetime >= maxLifetime)
            {
                Despawn();
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_isReturningToPool) return;

            // 1. Detección directa del Escudo de Energía del Boss
            if (other.TryGetComponent(out BossEnergyShieldPart shieldPart))
            {
                // El escudo cuenta como su jefe: así una bala perforante no golpea escudo + núcleo + cañones
                // (que también desvían el daño al escudo) varias veces
                int targetId = shieldPart.MainBoss != null ? shieldPart.MainBoss.GetInstanceID() : shieldPart.GetInstanceID();
                if (_hitTargets.Contains(targetId)) return;
                _hitTargets.Add(targetId);

                shieldPart.TakeDamage(CurrentDamage);

                if (_pierceRemaining > 0)
                {
                    _pierceRemaining--;
                }
                else
                {
                    Despawn();
                }
                return;
            }

            // 2. Detección directa o compuesta de enemigos (en el propio collider o en el padre)
            Enemy enemy = other.GetComponent<Enemy>();
            if (enemy == null)
            {
                enemy = other.GetComponentInParent<Enemy>();
            }

            if (enemy != null)
            {
                int targetId = Enemy.GetHitTarget(enemy).GetInstanceID();
                if (_hitTargets.Contains(targetId)) return;
                _hitTargets.Add(targetId);

                enemy.TakeDamage(CurrentDamage);

                // Patrón modificador: si le quedan perforaciones, atraviesa; si no, rebota ('bounce') o se devuelve al pool
                if (_pierceRemaining > 0)
                {
                    _pierceRemaining--;
                }
                else if (_bouncesRemaining > 0 && TryBounceToNearbyEnemy())
                {
                    _bouncesRemaining--;
                }
                else
                {
                    Despawn();
                }
                return;
            }
            else if (!other.isTrigger && !other.CompareTag("Player") && !other.TryGetComponent(out PlayerController _))
            {
                // Colisionadores sólidos: la bala se destruye
                Despawn();
            }
        }

        /// <summary>
        /// Modificador 'bounce' (Geometría de Rebote): tras impactar, redirige la bala hacia el enemigo cercano
        /// más próximo que aún no ha golpeado. Devuelve false si no hay ninguno en rango.
        /// </summary>
        private bool TryBounceToNearbyEnemy()
        {
            Vector2 origin = transform.position;

            ContactFilter2D filter = ContactFilter2D.noFilter;
            filter.useTriggers = true;
            int count = Physics2D.OverlapCircle(origin, BounceSearchRadius, filter, BounceBuffer);

            Enemy best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Collider2D col = BounceBuffer[i];
                if (col == null || !col.gameObject.activeInHierarchy) continue;

                Enemy candidate = col.GetComponent<Enemy>();
                if (candidate == null) candidate = col.GetComponentInParent<Enemy>();
                if (candidate == null || candidate.CurrentState != EnemyState.Chasing || candidate.IsPhasingIn) continue;
                if (_hitTargets.Contains(candidate.GetInstanceID())) continue;

                float sqr = ((Vector2)candidate.transform.position - origin).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = candidate;
                }
            }

            if (best == null) return false;

            _direction = ((Vector2)best.transform.position - origin).normalized;
            UpdateRotation();

            // Garantiza tiempo suficiente para alcanzar al nuevo objetivo
            _currentLifetime = Mathf.Min(_currentLifetime, maxLifetime - BounceMinRemainingLifetime);
            return true;
        }

        private void Despawn()
        {
            if (_isReturningToPool) return;
            _isReturningToPool = true;

            if (ObjectPooler.Instance != null)
            {
                ObjectPooler.Instance.ReturnToPool(gameObject);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }
    }
}
