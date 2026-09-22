using System.Collections;
using ArenaSurvivor.Combat;
using ArenaSurvivor.Data;
using ArenaSurvivor.Pool;
using UnityEngine;

namespace ArenaSurvivor.Entities
{
    public enum EnemyState
    {
        Chasing,            // Persigue al jugador y le inflige daño
        ParriedProjectile,  // Contraataque activo ("efecto billar"): daña a otros enemigos a alta velocidad
        Dead                // Inactivo / regresando al pool
    }

    /// <summary>
    /// Comportamiento base para los enemigos cuadrados.
    /// Sigue al jugador, aplica daño al contacto, recibe impactos con flash visual,
    /// soporta la mecánica de Parry (convirtiéndose en proyectil contraataque) y se recicla en ObjectPooler.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(BoxCollider2D))]
    public class Enemy : MonoBehaviour, IPoolable
    {
        [Header("Datos del Enemigo")]
        [SerializeField] private EnemyData enemyData;

        [Header("Referencias Visuales")]
        [SerializeField] private SpriteRenderer spriteRenderer;

        [Header("Visual Juice al ser Parried")]
        [SerializeField] private Color parriedProjectileColor = new Color(0.2f, 1f, 1f, 1f); // Cian brillante

        private Rigidbody2D _rb;
        private Transform _playerTransform;
        private float _currentHealth;
        private EnemyState _currentState = EnemyState.Chasing;
        private Color _defaultColor;
        private Coroutine _hitFlashCoroutine;
        private Coroutine _projectileRoutine;
        private float _projectileDamage = 40f;

        public EnemyData Data => enemyData;
        public EnemyState CurrentState => _currentState;
        public int ManagerIndex { get; set; } = -1;
        public Vector2 Position => transform.position;

        // Evento global para desacoplar sistemas como PlayerEnergy, UI o misiones
        public static event System.Action<Enemy> OnEnemyDefeated;

        private Vector2 _parriedVelocity;
        private float _projectileTimer;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            if (_rb != null)
            {
                _rb.bodyType = RigidbodyType2D.Kinematic;
                _rb.useFullKinematicContacts = true;
            }

            var col = GetComponent<BoxCollider2D>();
            if (col != null)
            {
                col.isTrigger = true;
            }

            if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        /// <summary>
        /// Permite asignar o sobrescribir dinámicamente la configuración del enemigo.
        /// </summary>
        public void SetData(EnemyData data)
        {
            enemyData = data;
            ApplyConfiguration();
        }

        public void OnSpawnFromPool()
        {
            _currentState = EnemyState.Chasing;

            // Localizar al jugador si aún no está referenciado
            if (_playerTransform == null && PlayerController.Instance != null)
            {
                _playerTransform = PlayerController.Instance.transform;
            }

            ResetPhysics();
            ApplyConfiguration();

            // Registrar en el loop centralizado de EnemyManager
            if (Managers.EnemyManager.Instance != null)
            {
                Managers.EnemyManager.Instance.RegisterEnemy(this);
            }
        }

        public void OnReturnToPool()
        {
            // Desregistrar del EnemyManager
            if (Managers.EnemyManager.Instance != null)
            {
                Managers.EnemyManager.Instance.UnregisterEnemy(this);
            }

            _currentState = EnemyState.Dead;
            _stasisSpeedMultiplier = 1f;
            _vortexSuppressionTimer = 0f;

            if (_stasisCoroutine != null)
            {
                StopCoroutine(_stasisCoroutine);
                _stasisCoroutine = null;
            }

            if (_hitFlashCoroutine != null)
            {
                StopCoroutine(_hitFlashCoroutine);
                _hitFlashCoroutine = null;
            }

            if (_projectileRoutine != null)
            {
                StopCoroutine(_projectileRoutine);
                _projectileRoutine = null;
            }

            ResetPhysics();
        }

        private void ResetPhysics()
        {
            if (_rb != null)
            {
#if UNITY_6000_0_OR_NEWER
                _rb.linearVelocity = Vector2.zero;
                _rb.linearDamping = 0f;
#else
                _rb.velocity = Vector2.zero;
                _rb.drag = 0f;
#endif
                _rb.angularVelocity = 0f;
            }
        }

        private float _maxHealth;
        private float _currentMoveSpeed;

        private void ApplyConfiguration()
        {
            if (enemyData == null) return;

            // Consultar multiplicadores de escalado dinámico de dificultad (DifficultyManager)
            float hpMult = Managers.DifficultyManager.Instance != null 
                ? Managers.DifficultyManager.Instance.GetHealthMultiplier() 
                : 1f;
            float speedMult = Managers.DifficultyManager.Instance != null 
                ? Managers.DifficultyManager.Instance.GetSpeedMultiplier() 
                : 1f;

            _maxHealth = enemyData.maxHealth * hpMult;
            _currentHealth = _maxHealth;
            _currentMoveSpeed = (enemyData.moveSpeed > 0f ? enemyData.moveSpeed : 3f) * speedMult;
            _defaultColor = enemyData.spriteColor;

            if (spriteRenderer != null)
            {
                spriteRenderer.color = _defaultColor;
            }

            transform.localScale = new Vector3(enemyData.visualScale.x, enemyData.visualScale.y, 1f);
        }

        /// <summary>
        /// Actualización manual ejecutada exclusivamente por EnemyManager (Manager-led Loop).
        /// Cero llamadas nativas de Update() o FixedUpdate().
        /// Movimiento 100% vectorial con separación ligera tipo Boids integrada.
        /// </summary>
        public void Tick(float deltaTime, Vector2 playerPos, Vector2 separation)
        {
            if (_currentState == EnemyState.Dead) return;

            // Reducir temporizador de supresión de daño por vórtice gravitacional
            if (_vortexSuppressionTimer > 0f)
            {
                _vortexSuppressionTimer -= deltaTime;
                if (_vortexSuppressionTimer <= 0f && spriteRenderer != null && _currentState == EnemyState.Chasing)
                {
                    spriteRenderer.color = _defaultColor;
                }
            }

            if (_currentState == EnemyState.Chasing)
            {
                Vector2 dir = (playerPos - (Vector2)transform.position).normalized;
                float speed = _currentMoveSpeed * _stasisSpeedMultiplier;
                Vector2 velocity = (dir * speed) + separation;

                // Traslación matemática pura (sin colisiones nativas de Box2D entre enemigos)
                transform.Translate(velocity * deltaTime, Space.World);
            }
            else if (_currentState == EnemyState.ParriedProjectile)
            {
                transform.Translate(_parriedVelocity * deltaTime, Space.World);
                // Fricción progresiva
                _parriedVelocity = Vector2.MoveTowards(_parriedVelocity, Vector2.zero, 12f * deltaTime);

                _projectileTimer -= deltaTime;
                if (_projectileTimer <= 0f)
                {
                    Die();
                }
            }
        }

        /// <summary>
        /// Transforma al enemigo en un "Proyectil Aliado" impulsado (Efecto Billar).
        /// Pierde el comportamiento de persecución, no daña al jugador y destruye a otros enemigos al impactar.
        /// </summary>
        public void ConvertIntoParriedProjectile(Vector2 impulseDirection, float impulseForce, float damage = 50f, float maxDuration = 2f)
        {
            if (_currentState == EnemyState.Dead) return;

            _currentState = EnemyState.ParriedProjectile;
            _projectileDamage = damage;
            _projectileTimer = maxDuration;
            _parriedVelocity = impulseDirection.normalized * impulseForce;

            // Feedback visual: color fluorescente de contraataque
            if (spriteRenderer != null)
            {
                spriteRenderer.color = parriedProjectileColor;
            }
        }

        /// <summary>
        /// Aplica daño al enemigo con feedback de flash visual ("Juice").
        /// </summary>
        public void TakeDamage(float amount)
        {
            if (_currentState == EnemyState.Dead) return;

            _currentHealth -= amount;

            if (_hitFlashCoroutine != null) StopCoroutine(_hitFlashCoroutine);
            _hitFlashCoroutine = StartCoroutine(HitFlashEffect());

            if (_currentHealth <= 0f)
            {
                Die();
            }
        }

        private IEnumerator HitFlashEffect()
        {
            if (spriteRenderer != null)
            {
                spriteRenderer.color = Color.white;
                yield return new WaitForSeconds(0.06f);
                spriteRenderer.color = (_currentState == EnemyState.ParriedProjectile) ? parriedProjectileColor : _defaultColor;
            }
            _hitFlashCoroutine = null;
        }

        public void Die()
        {
            if (_currentState == EnemyState.Dead) return;

            bool wasParriedProjectile = (_currentState == EnemyState.ParriedProjectile);
            _currentState = EnemyState.Dead;

            // Game Feel: Sacudida al eliminar un enemigo solo si no es un proyectil de rebote
            if (CameraShake.Instance != null && enemyData != null && !wasParriedProjectile)
            {
                CameraShake.Instance.Shake(enemyData.deathShakeDuration, enemyData.deathShakeIntensity);
            }

            OnEnemyDefeated?.Invoke(this);

            // Modificador 'geometric_shrapnel' (Metralla Geométrica):
            // Al destruirse un enemigo desviado, dispara 6 proyectiles en 360 grados usando el pool
            if (wasParriedProjectile && ObjectPooler.Instance != null &&
                ArenaSurvivor.Upgrades.UpgradeManager.Instance != null &&
                ArenaSurvivor.Upgrades.UpgradeManager.Instance.GetLevel("geometric_shrapnel") > 0)
            {
                int count = 6;
                float angleStep = 360f / count;
                for (int i = 0; i < count; i++)
                {
                    float angleDeg = i * angleStep;
                    float angleRad = angleDeg * Mathf.Deg2Rad;
                    Vector2 dir = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));

                    GameObject bulletObj = ObjectPooler.Instance.SpawnFromPool("PlayerBullet", transform.position, Quaternion.identity);
                    if (bulletObj != null && bulletObj.TryGetComponent(out Combat.Projectile proj))
                    {
                        proj.SetDamage(12f);
                        proj.Launch(dir);
                    }
                }
            }

            // Efecto de partículas de destrucción con el color correspondiente
            if (ObjectPooler.Instance != null)
            {
                GameObject vfxObj = ObjectPooler.Instance.SpawnFromPool("DeathVFX", transform.position, Quaternion.identity);
                if (vfxObj != null && vfxObj.TryGetComponent(out DeathVFX vfx))
                {
                    Color effectColor = wasParriedProjectile
                        ? parriedProjectileColor
                        : (enemyData != null ? enemyData.spriteColor : Color.red);
                    float scale = enemyData != null ? enemyData.visualScale.x : 1f;
                    vfx.PlayEffect(effectColor, 25, scale);
                }
            }

            // Devolver al ObjectPooler sin instanciar/destruir
            if (ObjectPooler.Instance != null)
            {
                ObjectPooler.Instance.ReturnToPool(gameObject);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            HandleCollision(collision.collider);
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            HandleCollision(collision.collider);
        }

        private void OnTriggerStay2D(Collider2D collider)
        {
            HandleCollision(collider);
        }

        private void HandleCollision(Collider2D col)
        {
            if (_currentState == EnemyState.Dead) return;

            // 1. Si está en estado Proyectil Aliado (Billar), daña a otros enemigos normales
            if (_currentState == EnemyState.ParriedProjectile)
            {
                if (col.TryGetComponent(out Enemy otherEnemy) && otherEnemy != this)
                {
                    if (otherEnemy.CurrentState == EnemyState.Chasing)
                    {
                        otherEnemy.TakeDamage(_projectileDamage);

                        // Modificador 'chain_reaction' (Relevo Técnico):
                        // El enemigo golpeado se convierte a su vez en proyectil aliado rebotado
                        if (ArenaSurvivor.Upgrades.UpgradeManager.Instance != null &&
                            ArenaSurvivor.Upgrades.UpgradeManager.Instance.GetLevel("chain_reaction") > 0)
                        {
                            Vector2 ricochetDir = ((Vector2)otherEnemy.transform.position - (Vector2)transform.position).normalized;
                            if (ricochetDir.sqrMagnitude < 0.001f) ricochetDir = Random.insideUnitCircle.normalized;
                            otherEnemy.ConvertIntoParriedProjectile(ricochetDir, 14f, _projectileDamage * 0.75f, 1.5f);
                        }
                    }
                }
                return;
            }

            // 2. Si está en estado Chasing normal, solo daña al jugador
            if (_currentState == EnemyState.Chasing)
            {
                // Inofensivo mientras sea arrastrado hacia el vórtice gravitacional
                if (IsBeingPulledByVortex) return;

                if (col.TryGetComponent(out PlayerController player))
                {
                    int damageToDeal = enemyData != null ? enemyData.damage : 1;
                    player.TakeDamage(damageToDeal);
                }
            }
        }

        private float _vortexSuppressionTimer;
        public bool IsBeingPulledByVortex => _vortexSuppressionTimer > 0f;

        /// <summary>
        /// Marca al enemigo como atrapado/arrastrado por un vórtice gravitacional.
        /// Mientras este estado esté activo, no puede infligir daño al jugador.
        /// </summary>
        public void MarkPulledByVortex(float duration = 0.35f)
        {
            if (_currentState == EnemyState.Dead) return;
            _vortexSuppressionTimer = duration;

            if (spriteRenderer != null && _currentState == EnemyState.Chasing)
            {
                // Feedback visual: tinte púrpura traslúcido indicando pérdida de hostilidad
                spriteRenderer.color = new Color(0.7f, 0.4f, 1f, 0.85f);
            }
        }

        private Coroutine _stasisCoroutine;
        private float _stasisSpeedMultiplier = 1f;

        /// <summary>
        /// Aplica una ralentización masiva temporal (utilizado por el modificador 'stasis_nova').
        /// </summary>
        public void ApplyStasis(float duration, float speedMultiplier = 0.25f)
        {
            if (_currentState == EnemyState.Dead) return;
            if (_stasisCoroutine != null) StopCoroutine(_stasisCoroutine);
            _stasisCoroutine = StartCoroutine(StasisRoutine(duration, speedMultiplier));
        }

        private IEnumerator StasisRoutine(float duration, float speedMultiplier)
        {
            _stasisSpeedMultiplier = speedMultiplier;
            Color icyColor = new Color(0.35f, 0.75f, 1f, 0.9f);
            if (spriteRenderer != null)
            {
                spriteRenderer.color = icyColor;
            }

            yield return new WaitForSeconds(duration);

            _stasisSpeedMultiplier = 1f;
            if (spriteRenderer != null && _currentState != EnemyState.Dead)
            {
                spriteRenderer.color = (_currentState == EnemyState.ParriedProjectile) ? parriedProjectileColor : _defaultColor;
            }
            _stasisCoroutine = null;
        }
    }
}
