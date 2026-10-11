using ArenaSurvivor.Entities;
using ArenaSurvivor.Pool;
using UnityEngine;

namespace ArenaSurvivor.Combat
{
    /// <summary>
    /// Proyectil hostil disparado por enemigos (Drones, Custodius).
    /// Altamente optimizado para pooling (cero allocations en runtime).
    /// Daña al jugador al impactar y puede ser desviado / reflejado mediante la mecánica activa de Parry.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class EnemyProjectile : MonoBehaviour, IPoolable
    {
        [Header("Estadísticas de Vuelo")]
        [SerializeField] private float speed = 5f;
        [SerializeField] private int damage = 1;
        [SerializeField] private float maxLifetime = 5f;

        public enum BulletVisual { Orb, Laser }

        [Header("Juice Visual")]
        [Tooltip("Sprite de pixel art que usa la bala (Resources/Bullets). Si no existe, se usa el sprite del prefab tintado.")]
        [SerializeField] private BulletVisual visual = BulletVisual.Orb;
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Color hostileColor = new Color(1f, 0.5f, 0.1f, 1f); // Naranja hostil
        [SerializeField] private Color parriedColor = new Color(0.1f, 0.9f, 1f, 1f); // Cian reflejado

        [Header("Visibilidad (halo + núcleo)")]
        [Tooltip("Añade un halo del color de la bala y un núcleo blanco para que se distinga mejor sobre el fondo")]
        [SerializeField] private bool addGlow = false;
        [Tooltip("Tamaño del halo respecto a la bala")]
        [SerializeField] private float glowScale = 2f;
        [Tooltip("Opacidad máxima del halo (pulsa suavemente)")]
        [SerializeField, Range(0f, 1f)] private float glowAlpha = 0.4f;

        private SpriteRenderer _glowRenderer;
        private SpriteRenderer _coreRenderer;
        private Color _currentColor;

        // Pixel art: 2 fotogramas que se alternan (el parpadeo sustituye al halo) y versión cian al reflejarse
        private const float FrameTime = 0.07f;
        private Sprite[] _hostileFrames;
        private Sprite[] _parriedFrames;
        private Sprite[] _frames;
        private int _frameIndex;
        private float _frameTimer;
        private bool HasArt => _hostileFrames != null && _parriedFrames != null;

        private Vector2 _direction;
        private float _currentLifetime;
        private bool _isReturningToPool;
        private bool _isParried;
        private float _parriedDamage = 60f;

        public bool IsParried => _isParried;

        private void Awake()
        {
            if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            var col = GetComponent<Collider2D>();
            if (col != null) col.isTrigger = true;

            ApplyArt(col as CircleCollider2D);
            if (addGlow && spriteRenderer != null && !HasArt) CreateGlow();
        }

        /// <summary>
        /// Cambia la forma tintada del prefab por el pixel art, a escala 1 para no deformar los píxeles.
        /// El radio del collider se compensa para que el área de impacto en el mundo sea la misma que antes.
        /// </summary>
        private void ApplyArt(CircleCollider2D col)
        {
            bool laser = visual == BulletVisual.Laser;
            _hostileFrames = laser ? BulletSprites.LaserHostile : BulletSprites.OrbHostile;
            _parriedFrames = laser ? BulletSprites.LaserParried : BulletSprites.OrbParried;
            if (!HasArt || spriteRenderer == null) return;

            Vector3 s = transform.localScale;
            float hitScale = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y)); // un CircleCollider2D escala con el mayor eje
            transform.localScale = Vector3.one;
            if (col != null) col.radius *= hitScale;
        }

        private void CreateGlow()
        {
            // Reutilizar capas si la instancia se clonó de otra que ya las tenía
            Transform existingGlow = spriteRenderer.transform.Find("Glow");
            Transform existingCore = spriteRenderer.transform.Find("Core");
            _glowRenderer = existingGlow != null ? existingGlow.GetComponent<SpriteRenderer>() : CreateLayer("Glow", glowScale, spriteRenderer.sortingOrder - 1);
            _coreRenderer = existingCore != null ? existingCore.GetComponent<SpriteRenderer>() : CreateLayer("Core", 0.45f, spriteRenderer.sortingOrder + 1);
            if (_coreRenderer != null) _coreRenderer.color = new Color(1f, 1f, 1f, 0.9f);
        }

        private SpriteRenderer CreateLayer(string name, float scale, int order)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(spriteRenderer.transform, false);
            go.transform.localScale = Vector3.one * scale;
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = spriteRenderer.sprite;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>Color de la bala (hostil o reflejada), aplicado también al halo.</summary>
        private void SetColor(Color color)
        {
            _currentColor = color;
            if (HasArt)
            {
                // El sprite ya trae sus colores: solo se elige la versión hostil o la reflejada
                _frames = _isParried ? _parriedFrames : _hostileFrames;
                _frameIndex = 0;
                _frameTimer = 0f;
                if (spriteRenderer != null)
                {
                    spriteRenderer.sprite = _frames[0];
                    spriteRenderer.color = Color.white;
                }
                return;
            }
            if (spriteRenderer != null) spriteRenderer.color = color;
            if (_glowRenderer != null) _glowRenderer.color = new Color(color.r, color.g, color.b, glowAlpha);
        }

        public void Launch(Vector2 direction, float customSpeed = -1f, int customDamage = -1)
        {
            _direction = direction.normalized;
            if (customSpeed > 0f) speed = customSpeed;
            if (customDamage > 0) damage = customDamage;

            UpdateRotation();
        }

        private void UpdateRotation()
        {
            float angle = Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        public void OnSpawnFromPool()
        {
            _currentLifetime = 0f;
            _isReturningToPool = false;
            _isParried = false;
            SetColor(hostileColor);
        }

        public void OnReturnToPool()
        {
            _isReturningToPool = true;
        }

        private void Update()
        {
            if (_isReturningToPool) return;

            transform.Translate(Vector3.right * (speed * Time.deltaTime), Space.Self);

            // Parpadeo del pixel art (o halo pulsante con la forma tintada) para que la bala llame la atención
            if (_frames != null && spriteRenderer != null)
            {
                _frameTimer += Time.deltaTime;
                if (_frameTimer >= FrameTime)
                {
                    _frameTimer -= FrameTime;
                    _frameIndex = (_frameIndex + 1) % _frames.Length;
                    spriteRenderer.sprite = _frames[_frameIndex];
                }
            }
            else if (_glowRenderer != null)
            {
                float pulse = 0.75f + (0.25f * Mathf.Sin((Time.time + _currentLifetime) * 18f));
                _glowRenderer.color = new Color(_currentColor.r, _currentColor.g, _currentColor.b, glowAlpha * pulse);
            }

            _currentLifetime += Time.deltaTime;
            if (_currentLifetime >= maxLifetime)
            {
                Despawn();
            }
        }

        /// <summary>
        /// Refleja el proyectil enemigo al recibir un Parry del jugador (Contraataque activo).
        /// </summary>
        public void Reflect(Vector2 newDirection, float counterDamage = 70f, float speedBoost = 1.6f)
        {
            if (_isReturningToPool || _isParried) return;

            _isParried = true;
            _direction = newDirection.normalized;
            speed *= speedBoost;
            _parriedDamage = counterDamage;
            _currentLifetime = 0f; // Reiniciar tiempo de vida

            UpdateRotation();
            SetColor(parriedColor);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_isReturningToPool) return;

            // 1. Proyectil hostil normal -> Daña al jugador
            if (!_isParried)
            {
                if (other.TryGetComponent(out PlayerController player))
                {
                    player.TakeDamage(damage);
                    Despawn();
                }
            }
            // 2. Proyectil reflejado por Parry -> Daña a los enemigos
            else
            {
                if (other.TryGetComponent(out Entities.BossEnergyShieldPart shieldPart))
                {
                    shieldPart.TakeDamage(_parriedDamage);
                    Despawn();
                    return;
                }

                Enemy enemy = other.GetComponent<Enemy>();
                if (enemy == null) enemy = other.GetComponentInParent<Enemy>();

                if (enemy != null && enemy.CurrentState == EnemyState.Chasing)
                {
                    enemy.TakeDamage(_parriedDamage);
                    Despawn();
                }
            }
        }

        public void Despawn()
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
