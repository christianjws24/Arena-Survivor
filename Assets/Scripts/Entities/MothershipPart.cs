using ArenaSurvivor.Combat;
using ArenaSurvivor.Pool;
using UnityEngine;

namespace ArenaSurvivor.Entities
{
    /// <summary>
    /// Cañón destructible del Boss Mothership (1000 HP).
    /// Mientras el Escudo de Energía de la nave esté activo, redirige el daño al escudo.
    /// Al destruirse deja de disparar y queda como chatarra apagada.
    /// Su lógica de ataque la dirige la nave (BossMothership); esta clase solo gestiona vida y daño.
    /// </summary>
    public class MothershipPart : Enemy
    {
        public enum PartKind { LowerGun, UpperGun }

        [SerializeField] private BossMothership mainBoss;
        [SerializeField] private PartKind kind = PartKind.LowerGun;
        [SerializeField] private bool isLeft = true;
        [SerializeField] private float partMaxHealth = 1000f;

        [Header("Juice Visual")]
        [SerializeField] private Color activeColor = new Color(1f, 0.55f, 0.2f, 1f);
        [SerializeField] private Color destroyedColor = new Color(0.25f, 0.25f, 0.3f, 0.7f);

        private bool _isDestroyed;
        private Collider2D _col;

        public PartKind Kind => kind;
        public bool IsLeft => isLeft;
        public bool IsDestroyed => _isDestroyed;
        public override bool IsHeavy => true;
        public override bool IsBoss => true;

        protected override void Awake()
        {
            base.Awake();
            _col = GetComponent<Collider2D>();
        }

        public void Initialize(BossMothership boss, PartKind partKind, bool left, float maxHealth, Color color)
        {
            mainBoss = boss;
            kind = partKind;
            isLeft = left;
            partMaxHealth = maxHealth;
            activeColor = color;
            Repair();
        }

        public void Repair()
        {
            _isDestroyed = false;
            _maxHealth = partMaxHealth;
            _currentHealth = partMaxHealth;
            _currentState = EnemyState.Chasing;
            _defaultColor = activeColor;

            if (_col == null) _col = GetComponent<Collider2D>();
            if (_col != null) _col.enabled = true;
            if (spriteRenderer != null) spriteRenderer.color = activeColor;
        }

        public override void OnSpawnFromPool()
        {
            // Las partes no se registran en el EnemyManager: la nave las controla
            Repair();
        }

        public override void OnReturnToPool()
        {
            _currentState = EnemyState.Dead;
        }

        public override void Tick(float deltaTime, Vector2 playerPos, Vector2 separation)
        {
            // Sin movimiento propio: sigue a la nave como hijo del transform
        }

        public override void TakeDamage(float amount)
        {
            if (_isDestroyed) return;

            if (mainBoss != null && mainBoss.IsShieldActive)
            {
                mainBoss.DamageEnergyShield(amount);
                return;
            }

            _currentHealth -= amount;
            if (_hitFlashCoroutine != null) StopCoroutine(_hitFlashCoroutine);
            _hitFlashCoroutine = StartCoroutine(HitFlashEffect());
            mainBoss?.NotifyStatusChanged();

            if (_currentHealth <= 0f)
            {
                DestroyPart();
            }
        }

        private void DestroyPart()
        {
            if (_isDestroyed) return;
            _isDestroyed = true;
            _currentHealth = 0f;
            _currentState = EnemyState.Dead;

            if (_col != null) _col.enabled = false;
            if (_hitFlashCoroutine != null)
            {
                StopCoroutine(_hitFlashCoroutine);
                _hitFlashCoroutine = null;
            }
            if (spriteRenderer != null) spriteRenderer.color = destroyedColor;

            CameraShake.Instance?.Shake(0.25f, 0.35f);
            if (ObjectPooler.Instance != null && ObjectPooler.Instance.HasPool("DeathVFX"))
            {
                GameObject vfxObj = ObjectPooler.Instance.SpawnFromPool("DeathVFX", transform.position, Quaternion.identity);
                if (vfxObj != null && vfxObj.TryGetComponent(out DeathVFX vfx))
                {
                    vfx.PlayEffect(new Color(1f, 0.5f, 0.15f), 35, 1.8f);
                }
            }

            mainBoss?.NotifyStatusChanged();
        }

        public override void Die()
        {
            DestroyPart();
        }

        public override void ConvertIntoParriedProjectile(Vector2 impulseDirection, float impulseForce, float damage = 50f, float maxDuration = 2f)
        {
            TakeDamage(damage);
        }
    }
}
