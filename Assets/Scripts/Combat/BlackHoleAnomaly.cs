using System.Collections;
using ArenaSurvivor.Entities;
using ArenaSurvivor.Pool;
using UnityEngine;

namespace ArenaSurvivor.Combat
{
    /// <summary>
    /// Prefab / Entidad del Power-Up "Anomalía Gravitacional".
    /// Creado desde el ObjectPooler al impactar un Parry.
    /// Atrae lentamente a los enemigos hacia su epicentro durante 3 segundos (+1s por nivel) antes de auto-reciclarse.
    /// Daño por nivel (con el daño base de 35, sin Calibre Pesado):
    ///   Nv1: 3s x 22.75/tick  (~136)  -> destruye enemigos normales (Grunt, Drone, Parasite)
    ///   Nv2: 4s x 35/tick     (~280)  -> destruye también a los Worm morados de la Etapa 2 (200hp)
    ///   Nv3: 5s x 61.25/tick  (~612)  -> además daña a las naves pesadas que no puede arrastrar: destruye torretas Sentry (400hp)
    /// </summary>
    [RequireComponent(typeof(CircleCollider2D))]
    public class BlackHoleAnomaly : MonoBehaviour, IPoolable
    {
        /// <summary>
        /// Color propio del vórtice (verde lima): ningún enemigo lo usa, para no confundirlo con el Worm (morado).
        /// También tiñe a los enemigos que está atrayendo.
        /// </summary>
        public static readonly Color VortexColor = new Color(0.72f, 1f, 0.12f, 0.9f);
        private static readonly Color VoidCoreColor = new Color(0.02f, 0.01f, 0.04f, 0.8f);

        [Header("Parámetros Gravitacionales")]
        [Tooltip("Duración activa en segundos antes de colapsar")]
        [SerializeField] private float duration = 3f;

        [Tooltip("Velocidad o fuerza con la que atrae a los enemigos")]
        [SerializeField] private float pullSpeed = 5.5f;

        [Tooltip("Radio de atracción efectivo")]
        [SerializeField] private float pullRadius = 4.5f;

        [Header("Daño por Ticks")]
        [Tooltip("Segundos entre cada tick de daño a los enemigos atrapados")]
        [SerializeField] private float damageTickInterval = 0.5f;

        [Tooltip("Daño por tick según el nivel de la mejora (índice 0 = Nv1), como bonus sobre el daño base del jugador (-0.35 = 65% del daño base)")]
        [SerializeField] private float[] damageTickBonusByLevel = { -0.35f, 0f, 0.75f };

        [Tooltip("Nivel a partir del cual el vórtice también daña (sin arrastrar) a torretas y naves pesadas. Nunca a jefes.")]
        [SerializeField] private int damageHeavyFromLevel = 3;

        [Header("Referencias Visuales")]
        [SerializeField] private SpriteRenderer spriteRenderer;

        [Tooltip("Frecuencia de re-escaneo de enemigos atrapados (segundos)")]
        [SerializeField] private float scanInterval = 0.1f;

        private CircleCollider2D _col;
        private float _timer;
        private float _scanTimer;
        private float _damageTickTimer;
        private float _effectiveRadius;
        private readonly System.Collections.Generic.List<Collider2D> _scanBuffer = new System.Collections.Generic.List<Collider2D>(128);
        private readonly System.Collections.Generic.List<Enemy> _captured = new System.Collections.Generic.List<Enemy>(128);
        private readonly System.Collections.Generic.List<Enemy> _heavyInRange = new System.Collections.Generic.List<Enemy>(16);
        private int _level = 1;
        private bool _isRecycling;
        private Vector3 _baseScale = Vector3.one * 1.5f;

        private void Awake()
        {
            // OPTIMIZACIÓN: la atracción ya no usa OnTriggerStay2D (un callback por par enemigo-vórtice en cada paso físico),
            // sino un OverlapCircle cada 'scanInterval' y una traslación barata por frame.
            _col = GetComponent<CircleCollider2D>();
            if (_col != null)
            {
                _col.isTrigger = true;
                _col.radius = pullRadius;
                _col.enabled = false;
            }

            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            }

            ApplyVortexVisual();
        }

        /// <summary>
        /// Aro verde lima + núcleo oscuro: más oscuro que el fondo, así se lee como un "agujero" y no como un enemigo.
        /// </summary>
        private void ApplyVortexVisual()
        {
            if (spriteRenderer == null) return;

            // Pixel art: espiral lima con su núcleo de vacío ya dibujado (mismas 2 u que Ring.png, gira con el vórtice)
            Sprite vortex = FxSprites.Vortex;
            if (vortex != null)
            {
                spriteRenderer.sprite = vortex;
                spriteRenderer.color = Color.white;
                return;
            }

            spriteRenderer.color = VortexColor;

            if (transform.Find("VoidCore") != null || spriteRenderer.sprite == null) return;

            GameObject core = new GameObject("VoidCore");
            core.transform.SetParent(spriteRenderer.transform, false);
            // ProceduralSprites.Circle mide 1 unidad: escalarlo al 85% del aro
            float ringSize = spriteRenderer.sprite.bounds.size.x;
            core.transform.localScale = Vector3.one * (ringSize * 0.85f);

            SpriteRenderer coreRenderer = core.AddComponent<SpriteRenderer>();
            coreRenderer.sprite = ProceduralSprites.Circle;
            coreRenderer.color = VoidCoreColor;
            coreRenderer.sortingOrder = spriteRenderer.sortingOrder - 1;
        }

        public void OnSpawnFromPool()
        {
            // Nivel de 'gravitational_anomaly': +1s de duración y +0.5 de radio por nivel extra
            int level = Upgrades.UpgradeManager.Instance != null ? Upgrades.UpgradeManager.Instance.GetLevel("gravitational_anomaly") : 1;
            _level = Mathf.Max(1, level);
            int extra = _level - 1;
            _timer = duration + extra;
            _effectiveRadius = pullRadius + (extra * 0.5f);
            _scanTimer = 0f;
            _damageTickTimer = damageTickInterval;
            _captured.Clear();
            _heavyInRange.Clear();
            _isRecycling = false;
            transform.localScale = Vector3.zero;

            if (_col != null)
            {
                _col.enabled = false;
            }

            StartCoroutine(SpawnPopInRoutine());
        }

        public void OnReturnToPool()
        {
            _isRecycling = true;
            _captured.Clear();
            _heavyInRange.Clear();
            if (_col != null)
            {
                _col.enabled = false;
            }
        }

        private IEnumerator SpawnPopInRoutine()
        {
            float elapsed = 0f;
            float popDuration = 0.25f;
            while (elapsed < popDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / popDuration);
                transform.localScale = Vector3.Lerp(Vector3.zero, _baseScale, t);
                yield return null;
            }
            transform.localScale = _baseScale;
        }

        private void Update()
        {
            if (_isRecycling) return;

            // Rotación hipnótica constante del vórtice
            transform.Rotate(0f, 0f, -220f * Time.deltaTime);

            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = scanInterval;
                ScanCapturedEnemies();
            }

            for (int i = 0; i < _captured.Count; i++)
            {
                PullEnemy(_captured[i]);
            }

            _damageTickTimer -= Time.deltaTime;
            if (_damageTickTimer <= 0f)
            {
                _damageTickTimer = damageTickInterval;
                DamageCapturedEnemies();
            }

            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                CollapseAndReturn();
            }
        }

        private void ScanCapturedEnemies()
        {
            _captured.Clear();
            _heavyInRange.Clear();
            bool damagesHeavy = _level >= damageHeavyFromLevel;
            ContactFilter2D filter = ContactFilter2D.noFilter;
            filter.useTriggers = true;
            int hits = Physics2D.OverlapCircle(transform.position, _effectiveRadius, filter, _scanBuffer);
            for (int i = 0; i < hits; i++)
            {
                Collider2D col = _scanBuffer[i];
                if (col == null || !col.TryGetComponent(out Enemy enemy)) continue;

                if (!enemy.IsHeavy)
                {
                    _captured.Add(enemy);
                }
                else if (damagesHeavy && !enemy.IsBoss)
                {
                    // Los jefes, sus partes y las torretas son demasiado masivos para ser arrastrados,
                    // pero a nivel máximo el vórtice desgarra torretas y naves pesadas (no a los jefes)
                    _heavyInRange.Add(enemy);
                }
            }
        }

        /// <summary>
        /// Tick de daño a todos los enemigos que el vórtice está atrayendo (escala con el daño base del jugador).
        /// </summary>
        private void DamageCapturedEnemies()
        {
            float bonus = damageTickBonusByLevel != null && damageTickBonusByLevel.Length > 0
                ? damageTickBonusByLevel[Mathf.Clamp(_level - 1, 0, damageTickBonusByLevel.Length - 1)]
                : -0.35f;
            float tickDamage = PlayerController.Instance != null
                ? PlayerController.Instance.GetDamage(bonus)
                : 35f * (1f + bonus);

            DamageList(_captured, tickDamage);
            DamageList(_heavyInRange, tickDamage);
        }

        private static void DamageList(System.Collections.Generic.List<Enemy> enemies, float tickDamage)
        {
            for (int i = 0; i < enemies.Count; i++)
            {
                Enemy enemy = enemies[i];
                if (enemy == null || !enemy.gameObject.activeInHierarchy) continue;
                if (enemy.CurrentState != EnemyState.Chasing) continue;
                enemy.TakeDamage(tickDamage);
            }
        }

        private void PullEnemy(Enemy enemy)
        {
            if (enemy == null || !enemy.gameObject.activeInHierarchy) return;

            // Detección de enemigos en persecución para atracción gravitacional
            if (enemy.CurrentState == EnemyState.Chasing)
            {
                // Desarmar al enemigo mientras es arrastrado hacia el vórtice para no dañar injustamente al jugador
                enemy.MarkPulledByVortex(0.35f);

                Vector2 toCenter = (Vector2)transform.position - (Vector2)enemy.transform.position;
                float distance = toCenter.magnitude;

                if (distance > 0.15f)
                {
                    // La atracción es más intensa a medida que se acercan al vórtice
                    float intensity = Mathf.Clamp01(1f - (distance / _effectiveRadius));
                    float currentPull = pullSpeed * (0.65f + intensity * 0.85f);

                    Vector2 step = toCenter.normalized * (currentPull * Time.deltaTime);
                    enemy.transform.Translate(step, Space.World);

                }
            }
        }

        private void CollapseAndReturn()
        {
            if (_isRecycling) return;
            _isRecycling = true;

            StartCoroutine(CollapseRoutine());
        }

        private IEnumerator CollapseRoutine()
        {
            float elapsed = 0f;
            float collapseDuration = 0.2f;
            Vector3 currentScale = transform.localScale;

            while (elapsed < collapseDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / collapseDuration);
                transform.localScale = Vector3.Lerp(currentScale, Vector3.zero, t);
                yield return null;
            }

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
