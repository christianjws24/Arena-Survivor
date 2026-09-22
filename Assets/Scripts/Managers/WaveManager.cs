using System;
using System.Collections.Generic;
using ArenaSurvivor.Entities;
using ArenaSurvivor.Pool;
using UnityEngine;
using Random = UnityEngine.Random;

namespace ArenaSurvivor.Managers
{
    /// <summary>
    /// Gestiona las oleadas infinitas de enemigos con escalado progresivo de dificultad.
    /// Spawnea enemigos en un perímetro circular alrededor del jugador usando el ObjectPooler.
    /// </summary>
    public class WaveManager : MonoBehaviour
    {
        [System.Serializable]
        public class EnemyPoolEntry
        {
            [Tooltip("Tag registrado en el ObjectPooler (ej: 'Enemy_Red')")]
            public string poolTag;

            [Tooltip("Oleada mínima a partir de la cual empieza a aparecer este enemigo")]
            public int minWaveToSpawn = 1;

            [Tooltip("Probabilidad relativa de aparición de este tipo")]
            [Range(1f, 100f)]
            public float spawnWeight = 10f;
        }

        [Header("Configuración de Oleadas")]
        [Tooltip("Duración de cada oleada en segundos")]
        [SerializeField] private float waveDuration = 25f;

        [Header("Ritmo de Generación (Spawning)")]
        [Tooltip("Intervalo inicial en segundos entre spawns")]
        [SerializeField] private float initialSpawnInterval = 1.2f;

        [Tooltip("Intervalo mínimo de spawn (límite de velocidad máxima)")]
        [SerializeField] private float minSpawnInterval = 0.15f;

        [Tooltip("Reducción del intervalo de spawn por cada nueva oleada")]
        [SerializeField] private float spawnIntervalDecay = 0.08f;

        [Tooltip("Cantidad de enemigos spawneados por golpe de spawn")]
        [SerializeField] private int baseEnemiesPerSpawn = 1;

        [Header("Perímetro de Aparición (Dinámico fuera de Cámara)")]
        [Tooltip("Cámara de referencia para calcular los límites de pantalla (si es null usa Camera.main)")]
        [SerializeField] private Camera targetCamera;

        [Tooltip("Margen de seguridad en unidades del mundo más allá del borde de la pantalla")]
        [SerializeField] private float spawnMargin = 2.5f;

        [Tooltip("Grosor adicional de la banda de spawn exterior para distribuir los enemigos de forma natural")]
        [SerializeField] private float spawnBandThickness = 2.0f;

        [Tooltip("Radio de fallback por si no se localiza ninguna cámara activa en la escena")]
        [SerializeField] private float fallbackSpawnRadius = 14f;

        [Header("Tipos de Enemigos")]
        [SerializeField] private List<EnemyPoolEntry> enemyPoolEntries = new List<EnemyPoolEntry>();

        public event Action<int> OnWaveAdvanced; // currentWave

        private int _currentWave = 1;
        private float _waveTimer;
        private float _spawnTimer;
        private Transform _playerTransform;
        private bool _isSpawningActive = true;

        public int CurrentWave => _currentWave;
        public float WaveTimeRemaining => Mathf.Max(0f, waveDuration - _waveTimer);

        private void Awake()
        {
            if (enemyPoolEntries == null || enemyPoolEntries.Count == 0)
            {
                enemyPoolEntries = new List<EnemyPoolEntry>
                {
                    new EnemyPoolEntry { poolTag = "Enemy_Red", minWaveToSpawn = 1, spawnWeight = 10f },
                    new EnemyPoolEntry { poolTag = "Enemy_Green", minWaveToSpawn = 2, spawnWeight = 8f },
                    new EnemyPoolEntry { poolTag = "Enemy_Blue", minWaveToSpawn = 3, spawnWeight = 4f },
                    new EnemyPoolEntry { poolTag = "Enemy_Yellow", minWaveToSpawn = 4, spawnWeight = 6f }
                };
            }
        }

        private void Start()
        {
            if (PlayerController.Instance != null)
            {
                _playerTransform = PlayerController.Instance.transform;
                PlayerController.Instance.OnPlayerDeath += StopSpawning;
            }

            OnWaveAdvanced?.Invoke(_currentWave);
        }

        private void OnDestroy()
        {
            if (PlayerController.Instance != null)
            {
                PlayerController.Instance.OnPlayerDeath -= StopSpawning;
            }
        }

        private void Update()
        {
            if (!_isSpawningActive) return;

            if (_playerTransform == null && PlayerController.Instance != null)
            {
                _playerTransform = PlayerController.Instance.transform;
            }

            if (_playerTransform == null) return;

            HandleWaveProgression();
            HandleSpawning();
        }

        private void HandleWaveProgression()
        {
            _waveTimer += Time.deltaTime;
            if (_waveTimer >= waveDuration)
            {
                _waveTimer = 0f;
                _currentWave++;
                Debug.Log($"<color=yellow><b>[WaveManager]</b> ¡Iniciando Oleada {_currentWave}!</color>");
                OnWaveAdvanced?.Invoke(_currentWave);
            }
        }

        private void HandleSpawning()
        {
            _spawnTimer += Time.deltaTime;
            float currentSpawnInterval = Mathf.Max(minSpawnInterval, initialSpawnInterval - ((_currentWave - 1) * spawnIntervalDecay));

            if (_spawnTimer >= currentSpawnInterval)
            {
                _spawnTimer = 0f;

                // Conforme avanzan las oleadas, se pueden spawnear grupos de enemigos simultáneos
                int enemiesToSpawn = baseEnemiesPerSpawn + (_currentWave / 4);
                for (int i = 0; i < enemiesToSpawn; i++)
                {
                    SpawnSingleEnemy();
                }
            }
        }

        private void SpawnSingleEnemy()
        {
            string chosenTag = SelectEnemyTagForCurrentWave();
            if (string.IsNullOrEmpty(chosenTag)) return;

            Vector2 spawnPos = GetRandomCameraPerimeterPosition();

            if (ObjectPooler.Instance != null)
            {
                ObjectPooler.Instance.SpawnFromPool(chosenTag, spawnPos, Quaternion.identity);
            }
        }

        /// <summary>
        /// Calcula una posición de aparición dinámica y garantizada fuera de los bordes de la cámara activa.
        /// Se adapta automáticamente a cualquier cambio de zoom (orthographicSize), relación de aspecto (16:9, 21:9)
        /// o perspectiva, garantizando que el jugador nunca vea aparecer a un enemigo en pantalla.
        /// </summary>
        public Vector2 GetRandomCameraPerimeterPosition()
        {
            Camera cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam == null)
            {
                // Fallback circular si no se encuentra ninguna cámara activa
                Vector2 origin = _playerTransform != null ? (Vector2)_playerTransform.position : Vector2.zero;
                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                return origin + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * fallbackSpawnRadius;
            }

            Vector2 camCenter = cam.transform.position;
            float halfHeight;
            float halfWidth;

            if (cam.orthographic)
            {
                halfHeight = cam.orthographicSize;
                halfWidth = halfHeight * cam.aspect;
            }
            else
            {
                float distance = Mathf.Abs(cam.transform.position.z);
                halfHeight = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * distance;
                halfWidth = halfHeight * cam.aspect;
            }

            // Banda de profundidad aleatoria para no generar a los enemigos en una línea perfecta
            float extraDepth = Random.Range(0f, spawnBandThickness);
            float totalMarginX = spawnMargin + extraDepth;
            float totalMarginY = spawnMargin + extraDepth;

            // Ponderación según la longitud del borde para una distribución homogénea (ancho vs alto)
            float perimeterHorizontal = halfWidth * 2f;
            float perimeterVertical = halfHeight * 2f;
            float totalPerimeter = perimeterHorizontal + perimeterVertical;
            bool isHorizontalSide = Random.value < (perimeterHorizontal / totalPerimeter);

            Vector2 spawnOffset;

            if (isHorizontalSide)
            {
                // Borde Superior o Inferior
                float signY = Random.value < 0.5f ? 1f : -1f;
                float randomX = Random.Range(-(halfWidth + totalMarginX), (halfWidth + totalMarginX));
                float posY = (halfHeight + totalMarginY) * signY;
                spawnOffset = new Vector2(randomX, posY);
            }
            else
            {
                // Borde Izquierdo o Derecho
                float signX = Random.value < 0.5f ? 1f : -1f;
                float posX = (halfWidth + totalMarginX) * signX;
                float randomY = Random.Range(-(halfHeight + totalMarginY), (halfHeight + totalMarginY));
                spawnOffset = new Vector2(posX, randomY);
            }

            return camCenter + spawnOffset;
        }

        /// <summary>
        /// Selecciona un tipo de enemigo según su peso relativo y si ya está habilitado en la oleada actual.
        /// </summary>
        private string SelectEnemyTagForCurrentWave()
        {
            float totalWeight = 0f;
            List<EnemyPoolEntry> availableEntries = new List<EnemyPoolEntry>();

            foreach (var entry in enemyPoolEntries)
            {
                if (_currentWave >= entry.minWaveToSpawn)
                {
                    availableEntries.Add(entry);
                    totalWeight += entry.spawnWeight;
                }
            }

            if (availableEntries.Count == 0)
            {
                return enemyPoolEntries.Count > 0 ? enemyPoolEntries[0].poolTag : null;
            }

            float randomRoll = Random.Range(0f, totalWeight);
            float cumulative = 0f;

            foreach (var entry in availableEntries)
            {
                cumulative += entry.spawnWeight;
                if (randomRoll <= cumulative)
                {
                    return entry.poolTag;
                }
            }

            return availableEntries[0].poolTag;
        }

        private void StopSpawning()
        {
            _isSpawningActive = false;
        }

        private void OnDrawGizmosSelected()
        {
            Camera cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam != null)
            {
                Vector3 center = cam.transform.position;
                center.z = 0f;

                float halfHeight = cam.orthographic 
                    ? cam.orthographicSize 
                    : Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * Mathf.Abs(cam.transform.position.z);
                float halfWidth = halfHeight * cam.aspect;

                // 1. Límites exactos del campo visual de la cámara (Cian / Celeste)
                Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
                Gizmos.DrawWireCube(center, new Vector3(halfWidth * 2f, halfHeight * 2f, 0f));

                // 2. Línea de spawn interior (Margen mínimo fuera de cámara, Amarillo)
                Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.7f);
                float innerW = (halfWidth + spawnMargin) * 2f;
                float innerH = (halfHeight + spawnMargin) * 2f;
                Gizmos.DrawWireCube(center, new Vector3(innerW, innerH, 0f));

                // 3. Línea de spawn exterior (Banda máxima de aparición, Verde)
                Gizmos.color = new Color(0.25f, 0.95f, 0.4f, 0.5f);
                float outerW = (halfWidth + spawnMargin + spawnBandThickness) * 2f;
                float outerH = (halfHeight + spawnMargin + spawnBandThickness) * 2f;
                Gizmos.DrawWireCube(center, new Vector3(outerW, outerH, 0f));
            }
            else if (_playerTransform != null)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(_playerTransform.position, fallbackSpawnRadius);
            }
        }
    }
}
