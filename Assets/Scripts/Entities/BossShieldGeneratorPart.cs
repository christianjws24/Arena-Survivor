using System;
using ArenaSurvivor.Combat;
using ArenaSurvivor.Pool;
using UnityEngine;

namespace ArenaSurvivor.Entities
{
    /// <summary>
    /// Generador de Escudo lateral del Boss Custodius (1000 HP).
    /// Si el Escudo de Energía está activo, redirige el daño al escudo.
    /// Si el escudo está caído, recibe daño directo hasta ser destruido,
    /// lo que duplica el tiempo de recarga del escudo (de 3s a 6s) o lo desactiva permanentemente si ambos caen.
    /// </summary>
    public class BossShieldGeneratorPart : Enemy
    {
        [Header("Referencia al Boss Principal")]
        [SerializeField] private BossCentinelMajoris mainBoss;
        [SerializeField] private bool isLeftGenerator = true;

        [Header("Juice Visual")]
        [SerializeField] private Color generatorActiveColor = new Color(0.2f, 0.8f, 1f, 1f); // Azul eléctrico
        [SerializeField] private Color generatorDestroyedColor = new Color(0.25f, 0.25f, 0.3f, 0.7f); // Apagado

        private bool _isDestroyed;
        private Collider2D _col;

        public bool IsDestroyed => _isDestroyed;
        public bool IsLeftGenerator => isLeftGenerator;

        protected override void Awake()
        {
            base.Awake();
            _col = GetComponent<Collider2D>();
            _currentState = EnemyState.Chasing;
            _maxHealth = 1000f;
            _currentHealth = 1000f;
            _defaultColor = generatorActiveColor;
        }

        /// <summary>
        /// Sustituye la cápsula tintada por el sprite de pixel art: escala 1, collider ajustado al sprite y sin tinte
        /// (el sprite ya trae sus colores). Destruido se sigue oscureciendo con generatorDestroyedColor.
        /// </summary>
        public void ApplyArt(Sprite sprite, Color hitFlashColor)
        {
            if (sprite == null) return;
            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer == null) return;

            spriteRenderer.sprite = sprite;
            transform.localScale = Vector3.one;
            if (TryGetComponent(out BoxCollider2D box))
            {
                box.size = sprite.bounds.size;
                box.offset = Vector2.zero;
            }

            generatorActiveColor = Color.white;
            _defaultColor = Color.white;
            _hitFlashColor = hitFlashColor;
            spriteRenderer.color = _isDestroyed ? generatorDestroyedColor : generatorActiveColor;
        }

        public void Initialize(BossCentinelMajoris boss, bool isLeft)
        {
            mainBoss = boss;
            isLeftGenerator = isLeft;
            _maxHealth = 1000f;
            _currentHealth = 1000f;
            _isDestroyed = false;
            _currentState = EnemyState.Chasing;

            if (_col == null) _col = GetComponent<Collider2D>();
            if (_col != null) _col.enabled = true;

            if (spriteRenderer != null)
            {
                spriteRenderer.color = generatorActiveColor;
            }
        }

        public override void OnSpawnFromPool()
        {
            _maxHealth = 1000f;
            _currentHealth = 1000f;
            _isDestroyed = false;
            _currentState = EnemyState.Chasing;

            if (_col == null) _col = GetComponent<Collider2D>();
            if (_col != null) _col.enabled = true;

            if (spriteRenderer != null)
            {
                spriteRenderer.color = generatorActiveColor;
            }
        }

        public override void OnReturnToPool()
        {
            _currentState = EnemyState.Dead;
        }

        public override void Tick(float deltaTime, Vector2 playerPos, Vector2 separation)
        {
            // La posición física del generador sigue a la nave principal como hijo en el transform
            // Cero traslación independiente
        }

        public override void TakeDamage(float amount)
        {
            if (_isDestroyed) return;

            // 1. Si el Escudo de Energía está activo, el impacto daña al escudo
            if (mainBoss != null && mainBoss.IsShieldActive)
            {
                mainBoss.DamageEnergyShield(amount);
                return;
            }

            // 2. Si el escudo está caído, el generador recibe el daño directamente (1000 HP)
            _currentHealth -= amount;

            if (_hitFlashCoroutine != null) StopCoroutine(_hitFlashCoroutine);
            _hitFlashCoroutine = StartCoroutine(HitFlashEffect());

            mainBoss?.NotifyGeneratorDamaged();

            if (_currentHealth <= 0f)
            {
                DestroyGenerator();
            }
        }

        private void DestroyGenerator()
        {
            if (_isDestroyed) return;
            _isDestroyed = true;
            _currentHealth = 0f;
            _currentState = EnemyState.Dead;

            // Desactivar colisionador para que las balas atraviesen hacia el núcleo y el auto-aim no lo seleccione
            if (_col == null) _col = GetComponent<Collider2D>();
            if (_col != null) _col.enabled = false;

            Debug.Log($"<color=orange><b>[CUSTODIUS]</b> 💥 ¡Generador de Escudo {(isLeftGenerator ? "IZQUIERDO" : "DERECHO")} DESTRUIDO!</color>");

            if (spriteRenderer != null)
            {
                spriteRenderer.color = generatorDestroyedColor;
            }

            // VFX y temblor
            if (Combat.CameraShake.Instance != null)
            {
                Combat.CameraShake.Instance.Shake(0.25f, 0.35f);
            }

            if (ObjectPooler.Instance != null && ObjectPooler.Instance.HasPool("DeathVFX"))
            {
                GameObject vfxObj = ObjectPooler.Instance.SpawnFromPool("DeathVFX", transform.position, Quaternion.identity);
                if (vfxObj != null && vfxObj.TryGetComponent(out DeathVFX vfx))
                {
                    vfx.PlayEffect(new Color(1f, 0.4f, 0.1f), 35, 1.8f);
                }
            }

            mainBoss?.OnGeneratorDestroyed(this);
        }

        public void RepairGenerator()
        {
            _isDestroyed = false;
            _currentHealth = 1000f;
            _maxHealth = 1000f;
            _currentState = EnemyState.Chasing;

            if (_col == null) _col = GetComponent<Collider2D>();
            if (_col != null) _col.enabled = true;

            if (spriteRenderer != null)
            {
                spriteRenderer.color = generatorActiveColor;
            }
        }

        public override bool IsHeavy => true;
        public override bool IsBoss => true;

        public override void ConvertIntoParriedProjectile(Vector2 impulseDirection, float impulseForce, float damage = 50f, float maxDuration = 2f)
        {
            // El generador es parte fija de la nave; recibe daño directo de contraataque sin lanzarse como billar
            TakeDamage(damage);
        }
    }
}
