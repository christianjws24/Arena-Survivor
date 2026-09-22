using ArenaSurvivor.Pool;
using ArenaSurvivor.Upgrades;
using UnityEngine;

namespace ArenaSurvivor.Combat
{
    /// <summary>
    /// Gestiona el movimiento, daño, tiempo de vida y reciclaje de las balas del jugador.
    /// Diseñado para cero allocations por impacto.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Projectile : MonoBehaviour, IPoolable
    {
        [Header("Estadísticas del Proyectil")]
        [SerializeField] private float speed = 14f;
        [SerializeField] private float baseDamage = 10f;
        [SerializeField] private float damage = 10f;
        [SerializeField] private float maxLifetime = 3f;

        [Header("Filtro de Colisión")]
        [Tooltip("LayerMask de los enemigos a impactar")]
        [SerializeField] private LayerMask hitMask;

        private Vector2 _direction;
        private float _currentLifetime;
        private bool _isReturningToPool;

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

        private void UpdateRotation()
        {
            float angle = Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        private int _pierceRemaining;
        private int _bouncesRemaining;

        public void OnSpawnFromPool()
        {
            _currentLifetime = 0f;
            _isReturningToPool = false;

            // Consulta desacoplada O(1): Lee el nivel de las mejoras directamente
            _pierceRemaining = UpgradeManager.Instance != null ? UpgradeManager.Instance.GetLevel("pierce") : 0;
            _bouncesRemaining = UpgradeManager.Instance != null ? UpgradeManager.Instance.GetLevel("bounce") : 0;

            // Calibre Pesado: Aumenta el daño base en un 20% por nivel
            int caliberLevel = UpgradeManager.Instance != null ? UpgradeManager.Instance.GetLevel("heavy_caliber") : 0;
            damage = (baseDamage > 0f ? baseDamage : 10f) * (1f + (caliberLevel * 0.20f));
        }

        public void OnReturnToPool()
        {
            _isReturningToPool = true;
        }

        private void Update()
        {
            if (_isReturningToPool) return;

            // Movimiento constante
            transform.Translate(Vector3.right * (speed * Time.deltaTime), Space.Self);

            // Modificador 'bounce' (Geometría de Rebote): Rebote en bordes de la cámara/arena
            if (_bouncesRemaining > 0 && Camera.main != null)
            {
                Vector3 vp = Camera.main.WorldToViewportPoint(transform.position);
                bool bounced = false;

                if ((vp.x <= 0.02f && _direction.x < 0f) || (vp.x >= 0.98f && _direction.x > 0f))
                {
                    _direction.x = -_direction.x;
                    bounced = true;
                }

                if ((vp.y <= 0.02f && _direction.y < 0f) || (vp.y >= 0.98f && _direction.y > 0f))
                {
                    _direction.y = -_direction.y;
                    bounced = true;
                }

                if (bounced)
                {
                    _bouncesRemaining--;
                    UpdateRotation();
                }
            }

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

            // Detección directa por componente para máxima robustez sin depender estrictamente de tags
            if (other.TryGetComponent(out Entities.Enemy enemy))
            {
                enemy.TakeDamage(damage);

                // Patrón modificador: si le quedan perforaciones, atraviesa; si no, se devuelve al pool
                if (_pierceRemaining > 0)
                {
                    _pierceRemaining--;
                }
                else
                {
                    Despawn();
                }
            }
            else if (!other.isTrigger && !other.CompareTag("Player") && !other.TryGetComponent(out Entities.PlayerController _))
            {
                // Rebote físico contra colisionadores sólidos
                if (_bouncesRemaining > 0)
                {
                    Vector2 hitNormal = ((Vector2)transform.position - (Vector2)other.bounds.center).normalized;
                    _direction = Vector2.Reflect(_direction, hitNormal);
                    UpdateRotation();
                    _bouncesRemaining--;
                }
                else
                {
                    Despawn();
                }
            }
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
