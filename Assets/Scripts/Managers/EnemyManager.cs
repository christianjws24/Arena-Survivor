using System;
using ArenaSurvivor.Entities;
using UnityEngine;

namespace ArenaSurvivor.Managers
{
    /// <summary>
    /// Gestor centralizado del bucle de actualización de enemigos (Manager-led Loop).
    /// Optimizado para soportar 1,000+ enemigos simultáneos sin caídas de FPS:
    /// 1. Elimina Update() y FixedUpdate() individuales de cada enemigo: un solo Update() centralizado.
    /// 2. Estructura de datos contigua en memoria (Array denso con swap-back removal O(1) y cero GC allocs).
    /// 3. Partición espacial (Spatial Hash Grid) para cálculo de separación local O(1) con sqrMagnitude.
    /// </summary>
    [DisallowMultipleComponent]
    public class EnemyManager : MonoBehaviour
    {
        private static EnemyManager _instance;
        public static EnemyManager Instance
        {
            get
            {
                if (_instance == null)
                {
#if UNITY_2023_1_OR_NEWER
                    _instance = UnityEngine.Object.FindFirstObjectByType<EnemyManager>();
#else
                    _instance = UnityEngine.Object.FindObjectOfType<EnemyManager>();
#endif
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("EnemyManager");
                        _instance = go.AddComponent<EnemyManager>();
                    }
                }
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Capacidad Inicial")]
        [SerializeField] private int initialCapacity = 2048;

        [Header("Algoritmo de Separación (Flocking / Boids Ultraligero)")]
        [Tooltip("Radio de influencia de repulsión entre enemigos")]
        [SerializeField] private float separationRadius = 0.85f;

        [Tooltip("Intensidad de la fuerza de separación")]
        [SerializeField] private float separationForce = 3.5f;

        [Tooltip("Tamaño de celda para la partición espacial (se recomienda 1.5x a 2x el radio)")]
        [SerializeField] private float cellSize = 1.6f;

        [Header("Barrera Invisible de Jefes")]
        [Tooltip("Margen extra alrededor de cada pieza visible de un jefe que los enemigos que caminan no pueden pisar")]
        [SerializeField] private float bossBarrierPadding = 0.4f;

        // Zonas elípticas (una por sprite visible del jefe: casco, escudo, cañones, generadores) recalculadas cada frame.
        // Solo frenan a enemigos en persecución: los proyectiles desviados (Parry) sí pueden atravesarlas y golpear al jefe.
        private struct BossBarrier
        {
            public Vector2 Center;
            public Vector2 HalfSize;
        }
        private readonly System.Collections.Generic.List<BossBarrier> _bossBarriers = new System.Collections.Generic.List<BossBarrier>(32);
        private readonly System.Collections.Generic.List<SpriteRenderer> _bossRendererBuffer = new System.Collections.Generic.List<SpriteRenderer>(32);

        // Array denso de enemigos activos contiguo en memoria
        private Enemy[] _activeEnemies;
        private int _activeCount;

        // Estructuras de la cuadrícula espacial (Spatial Hash Grid sin allocations)
        private const int HASH_TABLE_SIZE = 2048;
        private readonly int[] _gridHead = new int[HASH_TABLE_SIZE];
        private int[] _gridNext;

        public int ActiveEnemyCount => _activeCount;

        /// <summary>
        /// Cuenta los enemigos activos de un tipo (límites de población por tipo en las etapas).
        /// </summary>
        public int CountActiveOfType(Data.EnemyType type)
        {
            EnsureInitialized();
            int count = 0;
            for (int i = 0; i < _activeCount; i++)
            {
                Enemy e = _activeEnemies[i];
                if (e != null && e.Data != null && e.Data.enemyType == type && e.CurrentState != EnemyState.Dead) count++;
            }
            return count;
        }

        /// <summary>
        /// Copia los enemigos activos a un buffer (para operaciones que modifican la lista, como limpiar la arena).
        /// </summary>
        public void GetActiveEnemies(System.Collections.Generic.List<Enemy> buffer)
        {
            EnsureInitialized();
            buffer.Clear();
            for (int i = 0; i < _activeCount; i++)
            {
                if (_activeEnemies[i] != null) buffer.Add(_activeEnemies[i]);
            }
        }

        private void EnsureInitialized()
        {
            if (_activeEnemies == null)
            {
                _activeEnemies = new Enemy[initialCapacity];
                _gridNext = new int[initialCapacity];
                _activeCount = 0;
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            EnsureInitialized();
        }

        /// <summary>
        /// Registra un enemigo activo en el array denso en O(1) sin allocations.
        /// </summary>
        public void RegisterEnemy(Enemy enemy)
        {
            if (enemy == null) return;
            EnsureInitialized();

            if (_activeCount >= _activeEnemies.Length)
            {
                int newCap = _activeEnemies.Length * 2;
                Array.Resize(ref _activeEnemies, newCap);
                Array.Resize(ref _gridNext, newCap);
            }

            _activeEnemies[_activeCount] = enemy;
            enemy.ManagerIndex = _activeCount;
            _activeCount++;
        }

        /// <summary>
        /// Desregistra un enemigo en O(1) intercambiándolo con el último elemento (Swap-back).
        /// </summary>
        public void UnregisterEnemy(Enemy enemy)
        {
            if (enemy == null) return;
            EnsureInitialized();
            if (enemy.ManagerIndex < 0 || enemy.ManagerIndex >= _activeCount) return;

            int removeIndex = enemy.ManagerIndex;
            int lastIndex = _activeCount - 1;

            if (removeIndex != lastIndex)
            {
                Enemy lastEnemy = _activeEnemies[lastIndex];
                _activeEnemies[removeIndex] = lastEnemy;
                if (lastEnemy != null)
                {
                    lastEnemy.ManagerIndex = removeIndex;
                }
            }

            _activeEnemies[lastIndex] = null;
            _activeCount--;
            enemy.ManagerIndex = -1;
        }

        private void Update()
        {
            EnsureInitialized();
            if (_activeCount == 0) return;

            float dt = Time.deltaTime;
            Vector2 playerPos = PlayerController.Instance != null 
                ? (Vector2)PlayerController.Instance.transform.position 
                : Vector2.zero;

            // 1. Limpiar cuadrícula espacial O(K) instantáneo con Array.Fill
            Array.Fill(_gridHead, -1);

            // Asegurar capacidad de _gridNext
            if (_gridNext.Length < _activeCount)
            {
                Array.Resize(ref _gridNext, _activeEnemies.Length);
            }

            // 2. Insertar enemigos en la cuadrícula espacial (O(N))
            for (int i = 0; i < _activeCount; i++)
            {
                Enemy enemy = _activeEnemies[i];
                if (enemy == null) continue;

                Vector2 pos = enemy.Position;
                int cx = Mathf.FloorToInt(pos.x / cellSize);
                int cy = Mathf.FloorToInt(pos.y / cellSize);
                int hash = ComputeSpatialHash(cx, cy);

                _gridNext[i] = _gridHead[hash];
                _gridHead[hash] = i;
            }

            // Zonas prohibidas de los jefes vivos (para que los enemigos no se superpongan visualmente con ellos)
            RebuildBossBarriers();

            // 3. Evaluar separación y ejecutar Tick en cada enemigo (O(N * 9 celdas locales))
            float radiusSqr = separationRadius * separationRadius;

            for (int i = 0; i < _activeCount; i++)
            {
                Enemy current = _activeEnemies[i];
                if (current == null) continue;

                Vector2 separation = Vector2.zero;

                // Solo calcular separación para enemigos en persecución activa
                if (current.CurrentState == EnemyState.Chasing)
                {
                    Vector2 pos = current.Position;
                    int cx = Mathf.FloorToInt(pos.x / cellSize);
                    int cy = Mathf.FloorToInt(pos.y / cellSize);

                    // Consultar únicamente las 9 celdas contiguas (3x3)
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        for (int oy = -1; oy <= 1; oy++)
                        {
                            int neighborHash = ComputeSpatialHash(cx + ox, cy + oy);
                            int neighborIndex = _gridHead[neighborHash];

                            while (neighborIndex != -1)
                            {
                                if (neighborIndex != i)
                                {
                                    Enemy other = _activeEnemies[neighborIndex];
                                    if (other != null && other.CurrentState == EnemyState.Chasing)
                                    {
                                        Vector2 diff = pos - other.Position;
                                        float sqrDist = diff.sqrMagnitude;

                                        // Repulsión rápida con distancia cuadrada
                                        if (sqrDist < radiusSqr && sqrDist > 0.0001f)
                                        {
                                            float factor = 1f - (sqrDist / radiusSqr);
                                            separation += (diff / Mathf.Sqrt(sqrDist)) * (factor * separationForce);
                                        }
                                    }
                                }

                                neighborIndex = _gridNext[neighborIndex];
                            }
                        }
                    }
                }

                // Empuje externo (Rail Gun) aplicado aparte del Tick para que lo respeten todos los enemigos
                current.UpdateKnockback(dt);

                // Invocación manual del Tick en lugar de 1,000 llamadas nativas a Update()
                current.Tick(dt, playerPos, separation);

                // Barrera invisible: los enemigos que caminan no pueden entrar en la zona de un jefe
                if (_bossBarriers.Count > 0 && !current.IsBoss && current.CurrentState == EnemyState.Chasing)
                {
                    ResolveBossBarriers(current);
                }
            }
        }

        /// <summary>
        /// Una zona elíptica por cada sprite visible de cada jefe vivo (incluye escudo y partes).
        /// </summary>
        private void RebuildBossBarriers()
        {
            _bossBarriers.Clear();
            for (int i = 0; i < _activeCount; i++)
            {
                Enemy enemy = _activeEnemies[i];
                if (enemy == null || !(enemy is IShieldedBoss) || enemy.CurrentState == EnemyState.Dead) continue;

                enemy.GetComponentsInChildren(false, _bossRendererBuffer);
                for (int r = 0; r < _bossRendererBuffer.Count; r++)
                {
                    SpriteRenderer sr = _bossRendererBuffer[r];
                    if (sr == null || !sr.enabled || sr.color.a < 0.05f) continue;

                    Bounds b = sr.bounds;
                    if (b.extents.x < 0.05f || b.extents.y < 0.05f) continue;
                    _bossBarriers.Add(new BossBarrier { Center = b.center, HalfSize = b.extents });
                }
            }
        }

        /// <summary>
        /// Saca al enemigo de cualquier zona de jefe en la que haya entrado, proyectándolo al borde de la elipse.
        /// </summary>
        private void ResolveBossBarriers(Enemy enemy)
        {
            Vector3 scale = enemy.transform.lossyScale;
            float selfRadius = (Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y)) * 0.5f) + bossBarrierPadding;
            Vector2 pos = enemy.transform.position;
            bool moved = false;

            // Dos pasadas por si al salir de una zona cae dentro de otra contigua
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < _bossBarriers.Count; i++)
                {
                    BossBarrier barrier = _bossBarriers[i];
                    Vector2 axes = barrier.HalfSize + new Vector2(selfRadius, selfRadius);
                    Vector2 q = new Vector2((pos.x - barrier.Center.x) / axes.x, (pos.y - barrier.Center.y) / axes.y);
                    float sqr = q.sqrMagnitude;
                    if (sqr >= 1f) continue;

                    q = sqr > 0.000001f ? q / Mathf.Sqrt(sqr) : Vector2.down;
                    pos = barrier.Center + new Vector2(q.x * axes.x, q.y * axes.y);
                    moved = true;
                }
            }

            if (moved)
            {
                enemy.transform.position = new Vector3(pos.x, pos.y, enemy.transform.position.z);
            }
        }

        private static int ComputeSpatialHash(int x, int y)
        {
            unchecked
            {
                int hash = (x * 73856093) ^ (y * 19349663);
                return (hash & 0x7FFFFFFF) % HASH_TABLE_SIZE;
            }
        }
    }
}
