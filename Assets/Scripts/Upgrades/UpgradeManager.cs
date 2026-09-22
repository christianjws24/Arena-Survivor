using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace ArenaSurvivor.Upgrades
{
    /// <summary>
    /// Gestor de Estado Global de Mejoras (Patrón Registro Global / Redux-like Store).
    /// Centraliza los niveles de cada power-up en un Dictionary<string, int> activeUpgrades.
    /// Cualquier entidad (Player, Proyectiles, Zona, Parry) consulta directamente este gestor O(1)
    /// eliminando condicionales "if-spaghetti" y habilitando escalabilidad infinita.
    /// </summary>
    [DisallowMultipleComponent]
    public class UpgradeManager : MonoBehaviour
    {
        public static UpgradeManager Instance { get; private set; }

        [Header("Catálogo General de Mejoras")]
        [Tooltip("Lista de todos los ScriptableObjects de mejoras disponibles en el juego")]
        [SerializeField] private List<UpgradeData> allUpgrades = new List<UpgradeData>();

        // Estado global: ID de mejora -> Nivel actual (0 = no poseída)
        private readonly Dictionary<string, int> _activeUpgrades = new Dictionary<string, int>();
        private readonly Dictionary<string, UpgradeData> _upgradeMap = new Dictionary<string, UpgradeData>();

        // Eventos para UI y sistemas reactivos
        public event Action<UpgradeData, int> OnUpgradeAdded; // upgradeData, newLevel
        public event Action<UpgradeData> OnUpgradeRemoved;    // upgradeData eliminada

        public IReadOnlyDictionary<string, int> ActiveUpgrades => _activeUpgrades;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            InitializeCatalog();
        }

        /// <summary>
        /// Mapea el catálogo por su ID único para consultas O(1).
        /// </summary>
        public void InitializeCatalog()
        {
#if UNITY_EDITOR
            if (allUpgrades != null)
            {
                string[] guids = UnityEditor.AssetDatabase.FindAssets("t:UpgradeData", new[] { "Assets/Data" });
                foreach (string guid in guids)
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<UpgradeData>(path);
                    if (asset != null && !allUpgrades.Contains(asset))
                    {
                        allUpgrades.Add(asset);
                    }
                }
            }
#endif

            _upgradeMap.Clear();
            foreach (var up in allUpgrades)
            {
                if (up != null && !string.IsNullOrEmpty(up.id))
                {
                    _upgradeMap[up.id] = up;
                }
            }
        }

        /// <summary>
        /// Comprueba si equipar una nueva mejora entra en conflicto con alguna ya equipada que comparta exclusiveTag.
        /// </summary>
        public bool HasConflict(UpgradeData newUpgrade, out UpgradeData currentConflict)
        {
            currentConflict = null;
            if (newUpgrade == null || !newUpgrade.HasExclusiveTag)
            {
                return false;
            }

            foreach (var kvp in _activeUpgrades)
            {
                if (kvp.Value <= 0) continue;
                if (kvp.Key == newUpgrade.id) continue;

                if (_upgradeMap.TryGetValue(kvp.Key, out UpgradeData activeData))
                {
                    if (activeData != null && activeData.HasExclusiveTag &&
                        string.Equals(activeData.exclusiveTag, newUpgrade.exclusiveTag, StringComparison.OrdinalIgnoreCase))
                    {
                        currentConflict = activeData;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Consulta el nivel de una mejora.
        /// Devuelve 0 si el jugador aún no la tiene.
        /// Cero asignaciones de GC en llamadas continuas.
        /// </summary>
        public int GetLevel(string upgradeId)
        {
            if (string.IsNullOrEmpty(upgradeId)) return 0;
            return _activeUpgrades.TryGetValue(upgradeId, out int level) ? level : 0;
        }

        /// <summary>
        /// Comprueba si el jugador posee al menos el nivel 1 de una mejora binaria o escalable.
        /// </summary>
        public bool HasUpgrade(string upgradeId)
        {
            return GetLevel(upgradeId) > 0;
        }

        /// <summary>
        /// Añade +1 nivel a una mejora respetando su NivelMaximo.
        /// Dispara el evento OnUpgradeAdded si el nivel sube con éxito.
        /// </summary>
        public bool AddUpgrade(string upgradeId)
        {
            if (!_upgradeMap.TryGetValue(upgradeId, out UpgradeData data))
            {
                Debug.LogWarning($"[UpgradeManager] Intento de añadir mejora inexistente: '{upgradeId}'");
                return false;
            }

            int currentLevel = GetLevel(upgradeId);
            if (currentLevel >= data.maxLevel)
            {
                Debug.LogWarning($"[UpgradeManager] La mejora '{data.upgradeName}' ya alcanzó su nivel máximo ({data.maxLevel}).");
                return false;
            }

            int newLevel = currentLevel + 1;
            _activeUpgrades[upgradeId] = newLevel;

            Debug.Log($"<color=lime><b>[UPGRADE LEVEL UP]</b> ⭐ {data.upgradeName} (ID: {data.id}) -> Nivel {newLevel}/{data.maxLevel} [{data.upgradeType}]</color>");
            OnUpgradeAdded?.Invoke(data, newLevel);
            return true;
        }

        /// <summary>
        /// Elimina por completo una mejora del inventario activo.
        /// </summary>
        public bool RemoveUpgrade(string upgradeId)
        {
            if (string.IsNullOrEmpty(upgradeId) || !_activeUpgrades.ContainsKey(upgradeId))
            {
                return false;
            }

            _activeUpgrades.Remove(upgradeId);

            if (_upgradeMap.TryGetValue(upgradeId, out UpgradeData data))
            {
                Debug.Log($"<color=orange><b>[UPGRADE REMOVED]</b> ❌ {data.upgradeName} (ID: {data.id}) desequipada.</color>");
                OnUpgradeRemoved?.Invoke(data);
            }

            return true;
        }

        /// <summary>
        /// Reemplaza una mejora activa por otra nueva (Resolución de conflicto de exclusión).
        /// </summary>
        public bool ReplaceUpgrade(string oldUpgradeId, string newUpgradeId)
        {
            RemoveUpgrade(oldUpgradeId);
            return AddUpgrade(newUpgradeId);
        }

        /// <summary>
        /// Retorna las mejoras del catálogo que aún no han alcanzado su nivel máximo.
        /// Usado directamente por la UI para mostrar las 3 opciones a elegir.
        /// </summary>
        public List<UpgradeData> GetAvailableUpgrades()
        {
            var available = new List<UpgradeData>();
            foreach (var pair in _upgradeMap)
            {
                // TEMPORAL: Mecánica de vórtice desactivada momentáneamente por problemas de optimización
                if (pair.Key == "gravitational_anomaly") continue;

                if (!pair.Value.IsMaxLevel(GetLevel(pair.Key)))
                {
                    available.Add(pair.Value);
                }
            }
            return available;
        }

        /// <summary>
        /// Selecciona aleatoriamente una mejora disponible y la sube de nivel.
        /// </summary>
        public void ApplyRandomUpgrade()
        {
            var available = GetAvailableUpgrades();
            if (available.Count == 0)
            {
                Debug.Log("<color=yellow><b>[UpgradeManager]</b> ¡Todas las mejoras del juego están maximizadas!</color>");
                return;
            }

            UpgradeData picked = available[Random.Range(0, available.Count)];
            AddUpgrade(picked.id);
        }

        /// <summary>
        /// Obtiene N mejoras aleatorias distintas del catálogo que aún no han alcanzado su nivel máximo.
        /// Garantiza 0 duplicados y filtra automáticamente las mejoras completadas.
        /// </summary>
        public List<UpgradeData> GetRandomUpgrades(int count = 3)
        {
            var available = GetAvailableUpgrades();
            var results = new List<UpgradeData>();

            if (available.Count == 0) return results;
            if (available.Count <= count)
            {
                results.AddRange(available);
                return results;
            }

            // Muestreo sin reemplazo (evita duplicados)
            var pool = new List<UpgradeData>(available);
            for (int i = 0; i < count && pool.Count > 0; i++)
            {
                int randomIndex = Random.Range(0, pool.Count);
                results.Add(pool[randomIndex]);
                pool.RemoveAt(randomIndex);
            }

            return results;
        }

        /// <summary>
        /// Busca la data de una mejora por su ID para el renderizado de la UI/inventario.
        /// </summary>
        public bool TryGetUpgradeData(string upgradeId, out UpgradeData data)
        {
            return _upgradeMap.TryGetValue(upgradeId, out data);
        }

        /// <summary>
        /// Registra una nueva mejora en el catálogo en runtime o desde el editor.
        /// </summary>
        public void RegisterUpgrade(UpgradeData data)
        {
            if (data == null || string.IsNullOrEmpty(data.id)) return;
            if (!allUpgrades.Contains(data)) allUpgrades.Add(data);
            _upgradeMap[data.id] = data;
        }
    }
}
