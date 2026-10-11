using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArenaSurvivor.Pool
{
    /// <summary>
    /// Sistema centralizado de Object Pooling.
    /// Garantiza CERO llamadas a Instantiate() o Destroy() durante el gameplay para evitar picos de Garbage Collector.
    /// </summary>
    [DisallowMultipleComponent]
    public class ObjectPooler : MonoBehaviour
    {
        [System.Serializable]
        public class Pool
        {
            [Tooltip("Identificador único del pool (ej: 'PlayerBullet', 'Enemy_Red')")]
            public string tag;

            [Tooltip("Prefab base para instanciar")]
            public GameObject prefab;

            [Tooltip("Cantidad inicial de objetos a pre-instanciar en Awake")]
            [Min(1)]
            public int size = 20;

            [Tooltip("¿Puede crecer dinámicamente si se agota en momentos de alta exigencia?")]
            public bool shouldExpand = true;
        }

        public static ObjectPooler Instance { get; private set; }

        [Header("Configuración de Pools")]
        [SerializeField] private List<Pool> pools = new List<Pool>();

        // Diccionario rápido de colas para acceder en O(1)
        private readonly Dictionary<string, Queue<GameObject>> _poolDictionary = new Dictionary<string, Queue<GameObject>>();
        private readonly Dictionary<string, Pool> _poolConfigs = new Dictionary<string, Pool>();
        private readonly Dictionary<int, string> _instanceToTagMap = new Dictionary<int, string>();

        // Contenedores para mantener limpia la jerarquía de Unity
        private readonly Dictionary<string, Transform> _poolContainers = new Dictionary<string, Transform>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            InitializePools();

            // Contenido nuevo (Etapa 2, Mothership...) sin prefab asignado en la escena: se genera en runtime
            Managers.RuntimeContentFactory.EnsureRuntimePools(this);
        }

        /// <summary>
        /// Registra un pool en runtime (ej: plantillas procedurales). Ignora tags ya existentes.
        /// La plantilla puede estar inactiva: sus clones se activan al salir del pool.
        /// </summary>
        public bool RegisterPool(string tag, GameObject template, int size, bool shouldExpand = true)
        {
            if (string.IsNullOrEmpty(tag) || template == null || _poolDictionary.ContainsKey(tag)) return false;

            var pool = new Pool { tag = tag, prefab = template, size = Mathf.Max(1, size), shouldExpand = shouldExpand };
            pools.Add(pool);

            GameObject container = new GameObject($"Pool_{tag}");
            container.transform.SetParent(transform);
            _poolContainers[tag] = container.transform;
            _poolConfigs[tag] = pool;

            Queue<GameObject> objectQueue = new Queue<GameObject>(pool.size);
            for (int i = 0; i < pool.size; i++)
            {
                objectQueue.Enqueue(CreateNewObjectForPool(pool, container.transform));
            }

            _poolDictionary.Add(tag, objectQueue);
            return true;
        }

        /// <summary>
        /// Instancia todos los objetos desactivados al inicio y los organiza en la jerarquía.
        /// </summary>
        private void InitializePools()
        {
            foreach (Pool pool in pools)
            {
                if (pool.prefab == null)
                {
                    Debug.LogWarning($"[ObjectPooler] El pool con tag '{pool.tag}' no tiene prefab asignado.");
                    continue;
                }

                if (_poolDictionary.ContainsKey(pool.tag))
                {
                    Debug.LogWarning($"[ObjectPooler] Tag duplicado detectado: '{pool.tag}'. Ignorando duplicado.");
                    continue;
                }

                // Crear contenedor padre en la jerarquía
                GameObject container = new GameObject($"Pool_{pool.tag}");
                container.transform.SetParent(transform);
                _poolContainers[pool.tag] = container.transform;

                Queue<GameObject> objectQueue = new Queue<GameObject>(pool.size);
                _poolConfigs[pool.tag] = pool;

                for (int i = 0; i < pool.size; i++)
                {
                    GameObject obj = CreateNewObjectForPool(pool, container.transform);
                    objectQueue.Enqueue(obj);
                }

                _poolDictionary.Add(pool.tag, objectQueue);
            }
        }

        private GameObject CreateNewObjectForPool(Pool pool, Transform parent)
        {
            GameObject obj = Instantiate(pool.prefab, parent);
            obj.name = $"{pool.tag}_{obj.GetInstanceID()}";
            obj.SetActive(false);

            // Registramos el ID de la instancia para permitir devoluciones sin necesidad de pasar el tag manualmente
            _instanceToTagMap[obj.GetInstanceID()] = pool.tag;

            return obj;
        }

        /// <summary>
        /// Comprueba si existe un pool registrado con el tag especificado.
        /// </summary>
        public bool HasPool(string tag)
        {
            if (_poolDictionary == null) return false;
            if (_poolDictionary.ContainsKey(tag)) return true;
            if (tag == "Enemy_Grunt" && _poolDictionary.ContainsKey("Enemy_Red")) return true;
            if (tag == "Enemy_Drone" && _poolDictionary.ContainsKey("Enemy_Yellow")) return true;
            if (tag == "Enemy_Parasite" && _poolDictionary.ContainsKey("Enemy_Green")) return true;
            if (tag == "Enemy_Carrier" && _poolDictionary.ContainsKey("Enemy_Blue")) return true;
            if (tag == "EnemyLaser" && _poolDictionary.ContainsKey("EnemyBullet")) return true;
            return false;
        }

        /// <summary>
        /// Obtiene un objeto del pool, lo reposiciona, lo activa y ejecuta OnSpawnFromPool().
        /// </summary>
        public GameObject SpawnFromPool(string tag, Vector3 position, Quaternion rotation)
        {
            if (!_poolDictionary.TryGetValue(tag, out Queue<GameObject> objectQueue))
            {
                // Fallbacks automáticos de compatibilidad
                string fallbackTag = null;
                if (tag == "Enemy_Grunt" && _poolDictionary.ContainsKey("Enemy_Red")) fallbackTag = "Enemy_Red";
                else if (tag == "Enemy_Drone" && _poolDictionary.ContainsKey("Enemy_Yellow")) fallbackTag = "Enemy_Yellow";
                else if (tag == "Enemy_Parasite" && _poolDictionary.ContainsKey("Enemy_Green")) fallbackTag = "Enemy_Green";
                else if (tag == "Enemy_Carrier" && _poolDictionary.ContainsKey("Enemy_Blue")) fallbackTag = "Enemy_Blue";
                else if (tag == "EnemyLaser" && _poolDictionary.ContainsKey("EnemyBullet")) fallbackTag = "EnemyBullet";

                if (!string.IsNullOrEmpty(fallbackTag))
                {
                    tag = fallbackTag;
                    objectQueue = _poolDictionary[fallbackTag];
                }
                else
                {
                    Debug.LogWarning($"[ObjectPooler] No existe un pool registrado con el tag: '{tag}'. Asegúrate de agregarlo a la lista de Pools en el Inspector.");
                    return null;
                }
            }

            GameObject objectToSpawn;

            if (objectQueue.Count == 0)
            {
                Pool poolConfig = _poolConfigs[tag];
                if (poolConfig.shouldExpand)
                {
                    Transform container = _poolContainers[tag];
                    objectToSpawn = CreateNewObjectForPool(poolConfig, container);
                    Debug.LogWarning($"[ObjectPooler] Pool '{tag}' agotado. Auto-expandiendo +1 (Rendimiento: considera aumentar 'size' en el inspector).");
                }
                else
                {
                    Debug.LogWarning($"[ObjectPooler] Pool '{tag}' vacío y no está configurado para expandir.");
                    return null;
                }
            }
            else
            {
                objectToSpawn = objectQueue.Dequeue();
            }

            // Reposicionamiento y activación
            Transform objTransform = objectToSpawn.transform;
            objTransform.position = position;
            objTransform.rotation = rotation;
            objectToSpawn.SetActive(true);

            // Notificar a componentes poolables
            if (objectToSpawn.TryGetComponent(out IPoolable poolable))
            {
                poolable.OnSpawnFromPool();
            }

            return objectToSpawn;
        }

        /// <summary>
        /// Devuelve un objeto al pool correspondiente usando su tag.
        /// </summary>
        public void ReturnToPool(string tag, GameObject obj)
        {
            if (obj == null) return;

            if (!_poolDictionary.TryGetValue(tag, out Queue<GameObject> objectQueue))
            {
                Debug.LogError($"[ObjectPooler] Intento de devolver a un pool inexistente: '{tag}'");
                obj.SetActive(false);
                return;
            }

            if (obj.TryGetComponent(out IPoolable poolable))
            {
                poolable.OnReturnToPool();
            }

            obj.SetActive(false);

            // Reemparentar en su contenedor si fue movido
            if (_poolContainers.TryGetValue(tag, out Transform container))
            {
                obj.transform.SetParent(container);
            }

            objectQueue.Enqueue(obj);
        }

        /// <summary>
        /// Sobrecarga conveniente: Devuelve el objeto al pool infiriendo automáticamente su tag.
        /// </summary>
        public void ReturnToPool(GameObject obj)
        {
            if (obj == null) return;

            if (_instanceToTagMap.TryGetValue(obj.GetInstanceID(), out string tag))
            {
                ReturnToPool(tag, obj);
            }
            else
            {
                Debug.LogWarning($"[ObjectPooler] El objeto '{obj.name}' no pertenece a ningún pool registrado. Desactivando por seguridad.");
                obj.SetActive(false);
            }
        }
    }
}
