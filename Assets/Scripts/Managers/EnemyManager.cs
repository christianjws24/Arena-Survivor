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

        // Array denso de enemigos activos contiguo en memoria
        private Enemy[] _activeEnemies;
        private int _activeCount;

        // Estructuras de la cuadrícula espacial (Spatial Hash Grid sin allocations)
        private const int HASH_TABLE_SIZE = 2048;
        private readonly int[] _gridHead = new int[HASH_TABLE_SIZE];
        private int[] _gridNext;

        public int ActiveEnemyCount => _activeCount;

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

                // Invocación manual del Tick en lugar de 1,000 llamadas nativas a Update()
                current.Tick(dt, playerPos, separation);
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
