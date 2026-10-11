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
        [SerializeField] protected SpriteRenderer spriteRenderer;

        [Header("Visual Juice al ser Parried")]
        [SerializeField] protected Color parriedProjectileColor = new Color(0.2f, 1f, 1f, 1f); // Cian brillante

        protected Rigidbody2D _rb;
        protected Transform _playerTransform;
        protected float _currentHealth;
        protected EnemyState _currentState = EnemyState.Chasing;
        protected Color _defaultColor;
        // Tinte del destello de impacto. Blanco sirve para enemigos tintados; los sprites a color sin tinte usan otro tono para que se note.
        protected Color _hitFlashColor = Color.white;
        protected Coroutine _hitFlashCoroutine;
        protected Coroutine _projectileRoutine;
        protected float _projectileDamage = 40f;
        protected float _maxHealth;
        protected float _currentMoveSpeed;
        protected float _stasisSpeedMultiplier = 1f;

        public EnemyData Data => enemyData;
        public EnemyState CurrentState => _currentState;
        public int ManagerIndex { get; set; } = -1;
        public Vector2 Position => transform.position;
        public float CurrentHealth => _currentHealth;
        public float MaxHealth => _maxHealth;
        public bool IsPhantomProjectile { get; set; } = false;

        /// <summary>
        /// Naves capitales, torretas y jefes: reciben el daño del Parry pero no salen disparados (sin efecto billar)
        /// y la onda de choque de subida de nivel les hace daño en lugar de destruirlos.
        /// </summary>
        public virtual bool IsHeavy => false;

        /// <summary>
        /// Jefes y sus partes (cañones, generadores): al hacerles Parry empujan al jugador hacia atrás.
        /// </summary>
        public virtual bool IsBoss => false;

        /// <summary>
        /// Mientras se materializa en pantalla es inmune y no ataca.
        /// </summary>
        public bool IsPhasingIn => _isPhasingIn;
        protected bool _isPhasingIn;

        /// <summary>
        /// Multiplicador de cadencia de ataque (Anclaje Ofensivo acelera también a los enemigos dentro de su zona).
        /// </summary>
        protected float AttackRateMultiplier => Combat.AnchorZoneController.GetEnemyAttackMultiplier(transform.position);

        /// <summary>
        /// Daño de contacto/proyectil actual (base del EnemyData + bonus del Anclaje Ofensivo).
        /// </summary>
        public int OutgoingDamage
        {
            get
            {
                int baseDamage = enemyData != null ? enemyData.damage : 1;
                return baseDamage + Combat.AnchorZoneController.GetEnemyDamageBonus(transform.position);
            }
        }

        // Evento global para desacoplar sistemas como PlayerEnergy, UI o misiones
        public static event System.Action<Enemy> OnEnemyDefeated;

        protected Vector2 _parriedVelocity;
        protected float _projectileTimer;
        // Objetivos ya golpeados en el lanzamiento actual como proyectil desviado (uno por enemigo / uno por jefe)
        private readonly System.Collections.Generic.HashSet<int> _parryHitTargets = new System.Collections.Generic.HashSet<int>();

        // 'chain_reaction' (Relevo Técnico): nivel con el que este enemigo fue relevado (0 = no es un relevo)
        //   Nv1: sale empujado como proyectil aliado
        //   Nv2: además recibe daño por ticks mientras vuela
        //   Nv3: además explota al terminar el empuje (o al morir en pleno vuelo) con daño FIJO, para no arrasar con todo
        private const float RelayTickInterval = 0.3f;
        private const float RelayTickDamageBonus = -0.7f; // 30% del daño base del jugador por tick
        private const float RelayExplosionDamage = 300f;
        private const float RelayExplosionRadius = 2.5f;
        private int _chainRelayLevel;
        private float _relayTickTimer;

        /// <summary>
        /// Jefe al que pertenece el enemigo (él mismo si es el jefe, su padre si es una parte). Null si no es de un jefe.
        /// </summary>
        public static Enemy GetBossRoot(Enemy enemy)
        {
            if (enemy == null) return null;
            if (enemy is IShieldedBoss) return enemy;
            return enemy.GetComponentInParent<IShieldedBoss>() as Enemy;
        }

        /// <summary>
        /// Objetivo real de un impacto para las reglas de "un golpe por objetivo": si el enemigo es parte de un jefe con el
        /// escudo activo, todo el daño acaba en ese mismo escudo, así que cuenta como el jefe (un solo objetivo).
        /// </summary>
        public static Enemy GetHitTarget(Enemy enemy)
        {
            Enemy boss = GetBossRoot(enemy);
            return boss is IShieldedBoss shielded && shielded.IsShieldActive ? boss : enemy;
        }

        protected virtual void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            if (_rb != null)
            {
                _rb.bodyType = RigidbodyType2D.Kinematic;
                _rb.useFullKinematicContacts = true;
            }

            var col = GetComponent<Collider2D>();
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

        /// <summary>
        /// Asigna la configuración sin aplicarla (plantillas inactivas construidas en runtime o desde el editor).
        /// </summary>
        public void AssignData(EnemyData data)
        {
            enemyData = data;
        }

        public virtual void OnSpawnFromPool()
        {
            _currentState = EnemyState.Chasing;
            IsPhantomProjectile = false;
            _chainRelayLevel = 0;

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

        public virtual void OnReturnToPool()
        {
            // Desregistrar del EnemyManager
            if (Managers.EnemyManager.Instance != null)
            {
                Managers.EnemyManager.Instance.UnregisterEnemy(this);
            }

            _currentState = EnemyState.Dead;
            _stasisSpeedMultiplier = 1f;
            _vortexSuppressionTimer = 0f;
            _knockbackVelocity = Vector2.zero;
            _isPhasingIn = false;

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

        protected void ResetPhysics()
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

        public virtual void ApplyConfiguration()
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
        public virtual void Tick(float deltaTime, Vector2 playerPos, Vector2 separation)
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
                UpdateChasingBehavior(deltaTime, playerPos, separation);
            }
            else if (_currentState == EnemyState.ParriedProjectile)
            {
                transform.Translate(_parriedVelocity * deltaTime, Space.World);
                // Fricción progresiva
                _parriedVelocity = Vector2.MoveTowards(_parriedVelocity, Vector2.zero, 12f * deltaTime);

                // Relevo Técnico Nv2+: daño por ticks mientras sale empujado
                if (_chainRelayLevel >= 2)
                {
                    _relayTickTimer -= deltaTime;
                    if (_relayTickTimer <= 0f)
                    {
                        _relayTickTimer = RelayTickInterval;
                        float tickDamage = PlayerController.Instance != null ? PlayerController.Instance.GetDamage(RelayTickDamageBonus) : 10f;
                        TakeDamage(tickDamage);
                        if (_currentState == EnemyState.Dead) return;
                    }
                }

                _projectileTimer -= deltaTime;
                if (_projectileTimer <= 0f)
                {
                    RecoverFromParry();
                }
            }
        }

        protected virtual void UpdateChasingBehavior(float deltaTime, Vector2 playerPos, Vector2 separation)
        {
            Vector2 dir = (playerPos - (Vector2)transform.position).normalized;
            float speed = _currentMoveSpeed * _stasisSpeedMultiplier;
            Vector2 velocity = (dir * speed) + separation;

            // Traslación matemática pura (sin colisiones nativas de Box2D entre enemigos)
            transform.Translate(velocity * deltaTime, Space.World);
        }

        /// <summary>
        /// Restablece al enemigo a su comportamiento normal tras finalizar el impulso de parry.
        /// El enemigo NUNCA explota automáticamente salvo que su salud sea menor o igual a 0.
        /// </summary>
        public virtual void RecoverFromParry()
        {
            if (_currentState == EnemyState.Dead) return;

            // Si es un proyectil efímero generado por 'mirror_refractor', se recicla
            if (IsPhantomProjectile)
            {
                Die();
                return;
            }

            // Si su salud se agotó por daños recibidos, explota.
            // Relevo Técnico Nv3: al terminar el empuje el enemigo relevado detona (la explosión se resuelve en Die)
            if (_currentHealth <= 0f || _chainRelayLevel >= 3)
            {
                Die();
                return;
            }

            // Si aún conserva salud, recupera el estado de persecución activo
            _chainRelayLevel = 0;
            _currentState = EnemyState.Chasing;
            _parriedVelocity = Vector2.zero;

            if (spriteRenderer != null)
            {
                spriteRenderer.color = _defaultColor;
            }
        }

        /// <summary>
        /// Transforma al enemigo en un "Proyectil Aliado" impulsado (Efecto Billar).
        /// Pierde el comportamiento de persecución, no daña al jugador y destruye a otros enemigos al impactar.
        /// </summary>
        public virtual void ConvertIntoParriedProjectile(Vector2 impulseDirection, float impulseForce, float damage = 50f, float maxDuration = 2f)
        {
            if (_currentState == EnemyState.Dead || _currentHealth <= 0f) return;

            _currentState = EnemyState.ParriedProjectile;
            _chainRelayLevel = 0;
            _parryHitTargets.Clear();
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
        public virtual void TakeDamage(float amount)
        {
            if (_currentState == EnemyState.Dead || _isPhasingIn) return;

            _currentHealth -= amount;

            if (_hitFlashCoroutine != null) StopCoroutine(_hitFlashCoroutine);
            _hitFlashCoroutine = StartCoroutine(HitFlashEffect());

            if (_currentHealth <= 0f)
            {
                Die();
            }
        }

        protected virtual IEnumerator HitFlashEffect()
        {
            if (spriteRenderer != null)
            {
                spriteRenderer.color = _hitFlashColor;
                yield return new WaitForSeconds(0.06f);
                spriteRenderer.color = (_currentState == EnemyState.ParriedProjectile) ? parriedProjectileColor : _defaultColor;
            }
            _hitFlashCoroutine = null;
        }

        /// <summary>
        /// Retira al enemigo del campo sin contarlo como derrota (sin energía ni metralla). Usado al limpiar la arena entre etapas.
        /// </summary>
        public virtual void Vanish()
        {
            if (_currentState == EnemyState.Dead) return;
            _currentState = EnemyState.Dead;

            if (ObjectPooler.Instance != null)
            {
                GameObject vfxObj = ObjectPooler.Instance.SpawnFromPool("DeathVFX", transform.position, Quaternion.identity);
                if (vfxObj != null && vfxObj.TryGetComponent(out DeathVFX vfx))
                {
                    vfx.PlayEffect(enemyData != null ? enemyData.spriteColor : Color.white, 12, 0.8f);
                }
                ObjectPooler.Instance.ReturnToPool(gameObject);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Dispara un proyectil hostil desde el pool aplicando el daño saliente actual.
        /// </summary>
        protected EnemyProjectile FireProjectile(string poolTag, Vector2 origin, Vector2 direction, float speed)
        {
            if (ObjectPooler.Instance == null) return null;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector2.down;

            GameObject bulletObj = ObjectPooler.Instance.SpawnFromPool(poolTag, origin, Quaternion.identity);
            if (bulletObj != null && bulletObj.TryGetComponent(out EnemyProjectile proj))
            {
                proj.Launch(direction.normalized, speed, OutgoingDamage);
                return proj;
            }
            return null;
        }

        /// <summary>
        /// Efecto holográfico de materialización ("Phases into screen"): inmune e inactivo hasta terminar.
        /// </summary>
        protected void BeginPhaseIn(float duration)
        {
            StartCoroutine(PhaseInRoutine(duration));
        }

        private IEnumerator PhaseInRoutine(float duration)
        {
            _isPhasingIn = true;
            Color baseC = _defaultColor;
            float elapsed = 0f;

            while (elapsed < duration && _currentState != EnemyState.Dead)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float pulse = Mathf.PingPong(elapsed * 12f, 1f);
                float alpha = Mathf.Lerp(0.15f, 1f, t) * (0.6f + (pulse * 0.4f));
                if (spriteRenderer != null)
                {
                    spriteRenderer.color = new Color(baseC.r, baseC.g, baseC.b, alpha);
                }
                yield return null;
            }

            if (spriteRenderer != null) spriteRenderer.color = baseC;
            _isPhasingIn = false;
        }

        public virtual void Die()
        {
            if (_currentState == EnemyState.Dead) return;

            bool wasParriedProjectile = (_currentState == EnemyState.ParriedProjectile);
            bool explodesAsRelay = wasParriedProjectile && _chainRelayLevel >= 3;
            _chainRelayLevel = 0;
            _currentState = EnemyState.Dead;

            if (explodesAsRelay)
            {
                DetonateChainRelay();
            }

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
                        // Un golpe por objetivo en cada lanzamiento (todas las partes de un jefe cuentan como uno).
                        // Antes, al usar OnTriggerStay2D, los jefes y naves pesadas (que no salen despedidos) recibían
                        // el daño en cada paso físico mientras el proyectil los atravesaba y la vida bajaba de golpe.
                        Enemy boss = GetBossRoot(otherEnemy);
                        int targetId = (boss != null ? boss : otherEnemy).GetInstanceID();
                        if (!_parryHitTargets.Add(targetId)) return;

                        otherEnemy.TakeDamage(_projectileDamage);

                        if (otherEnemy.IsHeavy)
                        {
                            // Rebota contra la nave pesada en lugar de seguir atravesándola
                            Vector2 away = (Vector2)transform.position - (Vector2)otherEnemy.transform.position;
                            if (away.sqrMagnitude < 0.001f) away = -_parriedVelocity;
                            if (away.sqrMagnitude < 0.001f) away = Random.insideUnitCircle;
                            _parriedVelocity = away.normalized * (_parriedVelocity.magnitude * 0.5f);
                            return;
                        }

                        // Modificador 'chain_reaction' (Relevo Técnico):
                        // El enemigo golpeado se convierte a su vez en proyectil aliado rebotado (Nv2: ticks de daño, Nv3: explota)
                        int relayLevel = ArenaSurvivor.Upgrades.UpgradeManager.Instance != null
                            ? ArenaSurvivor.Upgrades.UpgradeManager.Instance.GetLevel("chain_reaction")
                            : 0;
                        if (relayLevel > 0)
                        {
                            Vector2 ricochetDir = ((Vector2)otherEnemy.transform.position - (Vector2)transform.position).normalized;
                            if (ricochetDir.sqrMagnitude < 0.001f) ricochetDir = Random.insideUnitCircle.normalized;
                            otherEnemy.ConvertIntoParriedProjectile(ricochetDir, 14f, _projectileDamage * 0.75f, 1.5f);
                            otherEnemy.BeginChainRelay(relayLevel);
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
                    player.TakeDamage(OutgoingDamage);
                }
            }
        }

        /// <summary>
        /// Marca a este enemigo (ya convertido en proyectil aliado) como relevo de 'chain_reaction' con el nivel actual.
        /// </summary>
        public void BeginChainRelay(int level)
        {
            if (_currentState != EnemyState.ParriedProjectile) return;
            _chainRelayLevel = level;
            _relayTickTimer = RelayTickInterval;
        }

        /// <summary>
        /// Relevo Técnico Nv3: explosión con daño FIJO (no escala con mejoras) a todo lo que haya alrededor.
        /// Un solo impacto por enemigo y uno por jefe (si el escudo está activo, lo recibe el escudo).
        /// </summary>
        private void DetonateChainRelay()
        {
            Vector2 center = transform.position;
            Collider2D[] hits = Physics2D.OverlapCircleAll(center, RelayExplosionRadius);
            var damaged = new System.Collections.Generic.HashSet<int> { GetInstanceID() };

            // 1. Escudos de energía primero (un impacto por jefe)
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i] == null || !hits[i].TryGetComponent(out BossEnergyShieldPart shield)) continue;
                Enemy owner = shield.MainBoss;
                if (owner == null || !damaged.Add(owner.GetInstanceID())) continue;
                shield.TakeDamage(RelayExplosionDamage);
            }

            // 2. Enemigos y partes de jefes
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i] == null) continue;
                Enemy target = hits[i].GetComponent<Enemy>();
                if (target == null) target = hits[i].GetComponentInParent<Enemy>();
                if (target == null || target.CurrentState == EnemyState.Dead) continue;
                if (!damaged.Add(GetHitTarget(target).GetInstanceID())) continue;
                target.TakeDamage(RelayExplosionDamage);
            }

            CameraShake.Instance?.Shake(0.12f, 0.18f);
            if (ObjectPooler.Instance != null)
            {
                GameObject vfxObj = ObjectPooler.Instance.SpawnFromPool("DeathVFX", center, Quaternion.identity);
                if (vfxObj != null && vfxObj.TryGetComponent(out DeathVFX vfx))
                {
                    vfx.PlayEffect(new Color(1f, 0.55f, 0.15f), 35, RelayExplosionRadius);
                }
            }
        }

        private Vector2 _knockbackVelocity;
        private const float KnockbackDeceleration = 55f;

        /// <summary>
        /// Empuja al enemigo con una velocidad que decae rápido (ej: Rail Gun). Naves pesadas y jefes son inmunes.
        /// Es independiente de Tick(), así funciona también en enemigos con movimiento propio.
        /// </summary>
        public void ApplyKnockback(Vector2 velocity)
        {
            if (_currentState == EnemyState.Dead || IsHeavy) return;
            _knockbackVelocity = velocity;
        }

        /// <summary>
        /// Avanza el empuje activo. Llamado por EnemyManager justo antes de Tick().
        /// </summary>
        public void UpdateKnockback(float deltaTime)
        {
            if (_knockbackVelocity.sqrMagnitude < 0.01f) return;
            transform.Translate(_knockbackVelocity * deltaTime, Space.World);
            _knockbackVelocity = Vector2.MoveTowards(_knockbackVelocity, Vector2.zero, KnockbackDeceleration * deltaTime);
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
                // Feedback visual: tinte del color del vórtice (verde lima) indicando pérdida de hostilidad
                spriteRenderer.color = Combat.BlackHoleAnomaly.VortexColor;
            }
        }

        private Coroutine _stasisCoroutine;

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
