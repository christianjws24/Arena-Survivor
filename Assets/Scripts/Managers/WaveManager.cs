using System;
using System.Collections;
using System.Collections.Generic;
using ArenaSurvivor.Combat;
using ArenaSurvivor.Data;
using ArenaSurvivor.Entities;
using ArenaSurvivor.Pool;
using ArenaSurvivor.UI;
using ArenaSurvivor.Upgrades;
using UnityEngine;
using Random = UnityEngine.Random;

namespace ArenaSurvivor.Managers
{
    /// <summary>
    /// Director de etapas del Demo.
    ///   Etapa 1: Grunt, Drone, Parasite y Carrier se van introduciendo poco a poco -> BOSS Custodius
    ///            (botín: Energy Shield / Laser Burst, el jugador elige).
    ///   Etapa 2: solo enemigos nuevos (Worm, Sentry, Caballier, Sidewinder), sin los de la Etapa 1 -> BOSS Mothership
    ///            (botín: Rail Gun / Fan Shot / Laser Shotgun). Durante el jefe los enemigos llegan por los lados o por abajo.
    ///   Fin del Demo -> pantalla de Demo Completada. "Seguir jugando" abre el Modo Libre, que mezcla ambas etapas
    ///   introduciendo los enemigos de forma escalonada (anticipo de la Etapa 3).
    /// Cada etapa se define por datos (StageDefinition) y se puede ajustar en el Inspector.
    /// </summary>
    public class WaveManager : MonoBehaviour
    {
        public enum SpawnMode
        {
            ScreenBorder,   // Aparece desde los bordes de la pantalla
            PhaseIn,        // Se materializa en un punto visible de la pantalla
            FourCorners     // Aparecen 4 a la vez, uno por esquina (Worm)
        }

        [Serializable]
        public class StageEnemyEntry
        {
            [Tooltip("Tag registrado en el ObjectPooler (ej: 'Enemy_Grunt')")]
            public string poolTag;
            [Tooltip("Segundos desde el inicio de la etapa a partir de los cuales empieza a aparecer")]
            public float unlockTime;
            [Tooltip("Probabilidad relativa de aparición")]
            [Min(0.1f)] public float spawnWeight = 10f;
            public SpawnMode spawnMode = SpawnMode.ScreenBorder;
            [Tooltip("Máximo de este enemigo vivo a la vez (0 = sin límite)")]
            public int maxAlive;
            [Tooltip("Si puede aparecer mientras el jefe está vivo (los jefes limpian la arena y solo dejan pasar a algunos enemigos)")]
            public bool allowedDuringBoss = true;
            [Tooltip("Máximo vivo a la vez durante el jefe (0 = usa 'maxAlive')")]
            public int bossFightMaxAlive;
        }

        [Serializable]
        public class RecurringBossEntry
        {
            [Tooltip("Tag del pool del jefe (ej: 'Boss_CentinelMajoris')")]
            public string poolTag;
            [Tooltip("IDs de armas que suelta para elegir 1 (las que ya están al máximo no se ofrecen)")]
            public List<string> drops = new List<string>();
            [Tooltip("Durante este jefe los enemigos no entran por el borde superior (Mothership)")]
            public bool blocksTopSpawns;
        }

        [Serializable]
        public class StageDefinition
        {
            public string stageName = "ETAPA";
            public string subtitle = "";
            [Tooltip("Segundos hasta que aparece el jefe (0 = etapa sin jefe / infinita)")]
            public float bossTime = 300f;
            public string bossPoolTag;
            [Tooltip("IDs de armas que el jefe suelta para elegir 1")]
            public List<string> bossDrops = new List<string>();
            [Tooltip("Durante el jefe los enemigos no entran por el borde superior (Mothership)")]
            public bool bossBlocksTopSpawns;

            [Header("Ritmo de aparición")]
            public float initialSpawnInterval = 1.2f;
            public float minSpawnInterval = 0.4f;
            [Tooltip("Reducción del intervalo por minuto de etapa")]
            public float spawnIntervalDecayPerMinute = 0.12f;
            [Tooltip("Multiplicador del intervalo mientras el jefe está vivo (más alto = menos enemigos)")]
            public float bossFightIntervalMultiplier = 2f;
            [Tooltip("Máximo de enemigos por golpe de spawn")]
            public int maxGroupSize = 3;

            [Header("Escalado de cantidad en el tiempo (Modo Libre)")]
            [Tooltip("Si es true, la CANTIDAD de enemigos sigue creciendo con el tiempo de etapa. Vida y velocidad no cambian.")]
            public bool scaleQuantityOverTime;
            [Tooltip("Enemigos vivos permitidos al empezar la etapa")]
            public int scaledAliveCapBase = 60;
            [Tooltip("Enemigos vivos extra permitidos por cada minuto de etapa")]
            public float scaledAliveCapPerMinute = 15f;
            [Tooltip("Tope absoluto de enemigos vivos (rendimiento y legibilidad)")]
            public int scaledAliveCapMax = 220;
            [Tooltip("Cada cuántos minutos se suma +1 enemigo al tamaño de grupo")]
            public float minutesPerExtraGroupEnemy = 2.5f;
            [Tooltip("Tamaño de grupo máximo con el escalado (sustituye a 'maxGroupSize')")]
            public int scaledMaxGroupSize = 6;
            [Tooltip("Crecimiento por minuto del límite de vivos de cada tipo (0.1 = +10% por minuto)")]
            public float typeCapGrowthPerMinute = 0.1f;

            [Header("Jefes recurrentes (etapas sin fin, ej. Modo Libre)")]
            [Tooltip("Segundos de etapa entre jefes (0 = sin jefes). Cuenta desde el inicio de la etapa y luego desde que muere el jefe anterior.")]
            public float recurringBossInterval;
            [Tooltip("Jefes que aparecen por turnos")]
            public List<RecurringBossEntry> recurringBosses = new List<RecurringBossEntry>();

            public List<StageEnemyEntry> enemies = new List<StageEnemyEntry>();
        }

        private enum Phase { Intro, Spawning, BossFight, StageClear, Finished }

        public static WaveManager Instance { get; private set; }

        [Header("Etapas (vacío = se usan las etapas del Demo por defecto)")]
        [SerializeField] private List<StageDefinition> stages = new List<StageDefinition>();
        [Tooltip("Índice de la etapa de Modo Libre que se activa al elegir 'Seguir jugando' tras el Demo")]
        [SerializeField] private int endlessStageIndex = 2;
        [Tooltip("Número de etapas que forman el Demo (al terminar la última se muestra 'Demo Completada')")]
        [SerializeField] private int demoStageCount = 2;

        [Header("Población")]
        [Tooltip("Límite global de enemigos vivos para proteger el rendimiento y la legibilidad")]
        [SerializeField] private int maxAliveEnemies = 140;
        [SerializeField] private float introDuration = 2.5f;

        [Header("Perímetro de Aparición (fuera de cámara)")]
        [SerializeField] private Camera targetCamera;
        [SerializeField] private float spawnMargin = 2.5f;
        [SerializeField] private float spawnBandThickness = 2.0f;
        [SerializeField] private float fallbackSpawnRadius = 14f;

        public event Action<int> OnStageStarted; // índice de etapa

        private int _stageIndex = -1;
        private Phase _phase = Phase.Intro;
        private float _stageTimer;
        private float _phaseTimer;
        private float _spawnTimer;
        private Transform _playerTransform;
        private bool _isSpawningActive = true;
        private readonly Dictionary<string, EnemyType> _tagTypes = new Dictionary<string, EnemyType>();
        private readonly List<StageEnemyEntry> _candidates = new List<StageEnemyEntry>(16);
        private readonly List<Enemy> _clearBuffer = new List<Enemy>(256);

        // Jefe del combate en curso (el de la etapa o uno recurrente del Modo Libre)
        private string _bossPoolTag;
        private List<string> _bossDrops;
        private bool _bossBlocksTopSpawns;
        private float _nextRecurringBossTime;
        private int _recurringBossIndex;

        private bool HasRecurringBosses(StageDefinition stage) =>
            stage != null && stage.bossTime <= 0f && stage.recurringBossInterval > 0f && stage.recurringBosses != null && stage.recurringBosses.Count > 0;

        public int CurrentStageIndex => _stageIndex;
        public StageDefinition CurrentStage => _stageIndex >= 0 && _stageIndex < stages.Count ? stages[_stageIndex] : null;
        public float StageTime => _stageTimer;
        public bool IsBossFight => _phase == Phase.BossFight;
        public bool IsEndless => CurrentStage != null && CurrentStage.bossTime <= 0f;

        /// <summary>Etiqueta corta para el HUD: "ETAPA 1", "¡JEFE!", "MODO LIBRE"...</summary>
        public string StageLabel
        {
            get
            {
                StageDefinition stage = CurrentStage;
                if (stage == null) return "";
                if (_phase == Phase.BossFight) return "¡JEFE!";
                if (stage.bossTime > 0f)
                {
                    int remaining = Mathf.Max(0, Mathf.CeilToInt(stage.bossTime - _stageTimer));
                    return $"{stage.stageName} · JEFE EN {remaining / 60}:{remaining % 60:00}";
                }
                if (HasRecurringBosses(stage) && _phase != Phase.StageClear)
                {
                    int remaining = Mathf.Max(0, Mathf.CeilToInt(_nextRecurringBossTime - _stageTimer));
                    return $"{stage.stageName} · JEFE EN {remaining / 60}:{remaining % 60:00}";
                }
                return stage.stageName;
            }
        }

        private void Awake()
        {
            Instance = this;
            if (stages == null || stages.Count == 0)
            {
                stages = BuildDefaultStages();
            }
        }

        private void OnEnable()
        {
            BossEvents.OnBossDefeated += HandleBossDefeated;
        }

        private void OnDisable()
        {
            BossEvents.OnBossDefeated -= HandleBossDefeated;
        }

        private void Start()
        {
            if (PlayerController.Instance != null)
            {
                _playerTransform = PlayerController.Instance.transform;
                PlayerController.Instance.OnPlayerDeath += StopSpawning;
            }

            BossHealthUI.EnsureExists();
            BeginStage(0);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (PlayerController.Instance != null)
            {
                PlayerController.Instance.OnPlayerDeath -= StopSpawning;
            }
        }

        #region Etapas por defecto (Demo)
        [ContextMenu("Restaurar etapas por defecto del Demo")]
        private void RestoreDefaultStages()
        {
            stages = BuildDefaultStages();
        }

        private static List<StageDefinition> BuildDefaultStages()
        {
            return new List<StageDefinition>
            {
                new StageDefinition
                {
                    stageName = "ETAPA 1",
                    subtitle = "Sobrevive hasta la llegada del CUSTODIUS",
                    bossTime = 300f,
                    bossPoolTag = "Boss_CentinelMajoris",
                    bossDrops = new List<string> { "energy_shield", "laser_burst" },
                    initialSpawnInterval = 1.1f,
                    minSpawnInterval = 0.4f,
                    spawnIntervalDecayPerMinute = 0.12f,
                    bossFightIntervalMultiplier = 2.2f,
                    maxGroupSize = 3,
                    enemies = new List<StageEnemyEntry>
                    {
                        // Durante el jefe solo salen Grunts y algunos Drones
                        new StageEnemyEntry { poolTag = "Enemy_Grunt", unlockTime = 0f, spawnWeight = 10f, bossFightMaxAlive = 6 },
                        new StageEnemyEntry { poolTag = "Enemy_Drone", unlockTime = 40f, spawnWeight = 6f, maxAlive = 14, bossFightMaxAlive = 3 },
                        new StageEnemyEntry { poolTag = "Enemy_Parasite", unlockTime = 100f, spawnWeight = 7f, allowedDuringBoss = false },
                        new StageEnemyEntry { poolTag = "Enemy_Carrier", unlockTime = 170f, spawnWeight = 1.5f, spawnMode = SpawnMode.PhaseIn, maxAlive = 2, allowedDuringBoss = false },
                    }
                },
                new StageDefinition
                {
                    stageName = "ETAPA 2",
                    subtitle = "Nuevas amenazas. Resiste hasta la llegada de la MOTHERSHIP",
                    bossTime = 300f,
                    bossPoolTag = "Boss_Mothership",
                    bossDrops = new List<string> { "rail_gun", "fan_shot", "laser_shotgun" },
                    bossBlocksTopSpawns = true,
                    initialSpawnInterval = 3.2f,
                    minSpawnInterval = 1.1f,
                    spawnIntervalDecayPerMinute = 0.35f,
                    bossFightIntervalMultiplier = 1.8f,
                    maxGroupSize = 2,
                    enemies = new List<StageEnemyEntry>
                    {
                        // Durante el jefe solo salen algunos Worms (un grupo de 4 a la vez) y Caballiers
                        new StageEnemyEntry { poolTag = "Enemy_Worm", unlockTime = 0f, spawnWeight = 6f, spawnMode = SpawnMode.FourCorners, maxAlive = 16, bossFightMaxAlive = 1 },
                        new StageEnemyEntry { poolTag = "Enemy_Sentry", unlockTime = 50f, spawnWeight = 2f, spawnMode = SpawnMode.PhaseIn, maxAlive = 3, allowedDuringBoss = false },
                        new StageEnemyEntry { poolTag = "Enemy_Caballier", unlockTime = 110f, spawnWeight = 4f, maxAlive = 6, bossFightMaxAlive = 2 },
                        new StageEnemyEntry { poolTag = "Enemy_Sidewinder", unlockTime = 170f, spawnWeight = 3f, maxAlive = 5, allowedDuringBoss = false },
                    }
                },
                new StageDefinition
                {
                    stageName = "MODO LIBRE",
                    subtitle = "Enemigos de ambas etapas, cada vez más juntos. Un jefe cada 5 minutos",
                    bossTime = 0f,
                    // Cada 5 minutos aparece un jefe, alternando Custodius y Mothership (con su botín de siempre)
                    recurringBossInterval = 300f,
                    recurringBosses = new List<RecurringBossEntry>
                    {
                        new RecurringBossEntry { poolTag = "Boss_CentinelMajoris", drops = new List<string> { "energy_shield", "laser_burst" } },
                        new RecurringBossEntry { poolTag = "Boss_Mothership", drops = new List<string> { "rail_gun", "fan_shot", "laser_shotgun" }, blocksTopSpawns = true },
                    },
                    initialSpawnInterval = 1.4f,
                    minSpawnInterval = 0.45f,
                    spawnIntervalDecayPerMinute = 0.1f,
                    maxGroupSize = 3,
                    // La dificultad crece solo en cantidad: más vivos a la vez, grupos más grandes y topes por tipo más altos
                    scaleQuantityOverTime = true,
                    scaledAliveCapBase = 60,
                    scaledAliveCapPerMinute = 15f,
                    scaledAliveCapMax = 220,
                    minutesPerExtraGroupEnemy = 2.5f,
                    scaledMaxGroupSize = 6,
                    typeCapGrowthPerMinute = 0.1f,
                    enemies = new List<StageEnemyEntry>
                    {
                        // Durante los jefes solo salen algunos Grunts, Worms, Drones y Caballiers
                        new StageEnemyEntry { poolTag = "Enemy_Grunt", unlockTime = 0f, spawnWeight = 9f, bossFightMaxAlive = 6 },
                        new StageEnemyEntry { poolTag = "Enemy_Worm", unlockTime = 0f, spawnWeight = 3f, spawnMode = SpawnMode.FourCorners, maxAlive = 12, bossFightMaxAlive = 1 },
                        new StageEnemyEntry { poolTag = "Enemy_Drone", unlockTime = 30f, spawnWeight = 5f, maxAlive = 12, bossFightMaxAlive = 3 },
                        new StageEnemyEntry { poolTag = "Enemy_Caballier", unlockTime = 60f, spawnWeight = 3f, maxAlive = 5, bossFightMaxAlive = 2 },
                        new StageEnemyEntry { poolTag = "Enemy_Parasite", unlockTime = 90f, spawnWeight = 6f, allowedDuringBoss = false },
                        new StageEnemyEntry { poolTag = "Enemy_Sentry", unlockTime = 120f, spawnWeight = 1.5f, spawnMode = SpawnMode.PhaseIn, maxAlive = 3, allowedDuringBoss = false },
                        new StageEnemyEntry { poolTag = "Enemy_Sidewinder", unlockTime = 150f, spawnWeight = 2f, maxAlive = 4, allowedDuringBoss = false },
                        new StageEnemyEntry { poolTag = "Enemy_Carrier", unlockTime = 180f, spawnWeight = 1f, spawnMode = SpawnMode.PhaseIn, maxAlive = 2, allowedDuringBoss = false },
                    }
                }
            };
        }
        #endregion

        #region Flujo de etapas
        private void BeginStage(int index)
        {
            if (index < 0 || index >= stages.Count)
            {
                _phase = Phase.Finished;
                return;
            }

            _stageIndex = index;
            _stageTimer = 0f;
            _spawnTimer = 0f;
            _phase = Phase.Intro;
            _phaseTimer = introDuration;

            DifficultyManager.Instance?.ResetStageClock();

            StageDefinition stage = stages[index];
            _nextRecurringBossTime = stage.recurringBossInterval;
            _recurringBossIndex = 0;
            StageBannerUI.Show(stage.stageName, stage.subtitle, new Color(0.4f, 0.95f, 1f));
            Debug.Log($"<color=yellow><b>[WaveManager]</b> ▶ {stage.stageName} iniciada.</color>");
            OnStageStarted?.Invoke(index);
        }

        private void Update()
        {
            if (!_isSpawningActive || _phase == Phase.Finished) return;

            if (_playerTransform == null && PlayerController.Instance != null)
            {
                _playerTransform = PlayerController.Instance.transform;
            }
            if (_playerTransform == null) return;

            HandleDebugKeys();

            StageDefinition stage = CurrentStage;
            if (stage == null) return;

            switch (_phase)
            {
                case Phase.Intro:
                    _phaseTimer -= Time.deltaTime;
                    if (_phaseTimer <= 0f) _phase = Phase.Spawning;
                    break;

                case Phase.Spawning:
                    _stageTimer += Time.deltaTime;
                    HandleSpawning(stage, 1f);
                    if (stage.bossTime > 0f && _stageTimer >= stage.bossTime)
                    {
                        StartBossFight(stage.bossPoolTag, stage.bossDrops, stage.bossBlocksTopSpawns);
                    }
                    else if (HasRecurringBosses(stage) && _stageTimer >= _nextRecurringBossTime)
                    {
                        // Modo Libre: jefes por turnos cada 'recurringBossInterval' segundos
                        RecurringBossEntry next = stage.recurringBosses[_recurringBossIndex % stage.recurringBosses.Count];
                        _recurringBossIndex++;
                        StartBossFight(next.poolTag, next.drops, next.blocksTopSpawns);
                    }
                    break;

                case Phase.BossFight:
                    _stageTimer += Time.deltaTime;
                    HandleSpawning(stage, stage.bossFightIntervalMultiplier);
                    break;
            }
        }

        private void StartBossFight(string poolTag, List<string> drops, bool blocksTopSpawns)
        {
            _phase = Phase.BossFight;
            _bossPoolTag = poolTag;
            _bossDrops = drops;
            _bossBlocksTopSpawns = blocksTopSpawns;

            // El jefe llega a una arena despejada: se retira a todos los enemigos y proyectiles hostiles,
            // y durante el combate solo aparecen los enemigos marcados como 'allowedDuringBoss' (con límites propios)
            ClearArena();
            _spawnTimer = 0f;

            AudioManager.Instance?.PlayBossAlert();
            StageBannerUI.Show("¡ALERTA!", "Un jefe se acerca", new Color(1f, 0.3f, 0.35f), 2.2f);
            SpawnBoss();
        }

        private void SpawnBoss()
        {
            if (ObjectPooler.Instance == null || string.IsNullOrEmpty(_bossPoolTag)) return;

            Vector2 spawnPos;
            if (_bossBlocksTopSpawns && CameraBounds.TryGet(out Vector2 camCenter, out Vector2 camHalf))
            {
                // Mothership: entra por el borde superior
                spawnPos = camCenter + new Vector2(0f, camHalf.y + 4f);
            }
            else
            {
                spawnPos = GetRandomCameraPerimeterPosition(false);
            }

            ObjectPooler.Instance.SpawnFromPool(_bossPoolTag, spawnPos, Quaternion.identity);
            Debug.Log($"<color=red><b>[WaveManager]</b> 🚨 Jefe '{_bossPoolTag}' desplegado.</color>");
        }

        private void HandleBossDefeated(IBossStatus boss)
        {
            if (_phase != Phase.BossFight) return;
            _phase = Phase.StageClear;

            // En el Modo Libre el jefe no termina la etapa: se elige botín y se sigue jugando
            if (HasRecurringBosses(CurrentStage)) StartCoroutine(RecurringBossClearRoutine());
            else StartCoroutine(StageClearRoutine());
        }

        private const string BossLootTitle = "★ BOTÍN DEL JEFE ★\n<size=15><color=#A0D8EF>ELIGE UNA ARMA (se dispara con H / J / K / L hacia donde miras)</color></size>\n<size=13><color=#FFFF88>(Presiona [1] / [2] / [3])</color></size>";

        /// <summary>
        /// Jefe recurrente derrotado (Modo Libre): banner, botín (si queda alguna arma por mejorar) y vuelta al spawn normal.
        /// El siguiente jefe llega 'recurringBossInterval' segundos después.
        /// </summary>
        private IEnumerator RecurringBossClearRoutine()
        {
            yield return new WaitForSeconds(1.2f);
            if (PlayerController.Instance != null && PlayerController.Instance.IsDead) yield break;

            List<UpgradeData> drops = UpgradeManager.Instance != null
                ? UpgradeManager.Instance.GetDropOptions(_bossDrops)
                : new List<UpgradeData>();

            StageBannerUI.Show("¡JEFE DERROTADO!", drops.Count > 0 ? "Elige tu botín" : "¡Sigue resistiendo!", new Color(0.45f, 1f, 0.5f), 2f);
            yield return new WaitForSeconds(1.4f);
            if (PlayerController.Instance != null && PlayerController.Instance.IsDead) yield break;

            if (drops.Count > 0 && LevelUpUIManager.Instance != null)
            {
                LevelUpUIManager.Instance.OpenChoiceModal(drops, ResumeAfterRecurringBoss, BossLootTitle);
            }
            else
            {
                ResumeAfterRecurringBoss();
            }
        }

        private void ResumeAfterRecurringBoss()
        {
            StageDefinition stage = CurrentStage;
            _phase = Phase.Spawning;
            _spawnTimer = 0f;
            _nextRecurringBossTime = _stageTimer + (stage != null ? stage.recurringBossInterval : 300f);
        }

        private IEnumerator StageClearRoutine()
        {
            StageDefinition stage = CurrentStage;
            yield return new WaitForSeconds(1.2f);

            ClearArena();
            StageBannerUI.Show($"{stage.stageName} COMPLETADA", "Elige tu botín", new Color(0.45f, 1f, 0.5f), 2f);
            yield return new WaitForSeconds(1.4f);

            if (PlayerController.Instance != null && PlayerController.Instance.IsDead) yield break;

            List<UpgradeData> drops = UpgradeManager.Instance != null
                ? UpgradeManager.Instance.GetDropOptions(_bossDrops)
                : new List<UpgradeData>();

            if (drops.Count > 0 && LevelUpUIManager.Instance != null)
            {
                LevelUpUIManager.Instance.OpenChoiceModal(drops, AdvanceAfterStageClear, BossLootTitle);
            }
            else
            {
                AdvanceAfterStageClear();
            }
        }

        private void AdvanceAfterStageClear()
        {
            int next = _stageIndex + 1;
            if (next >= demoStageCount || next >= stages.Count)
            {
                _phase = Phase.Finished;
                DifficultyManager.Instance?.StopTimer();
                GameManager.Instance?.ShowDemoComplete(ContinueEndless);
                return;
            }

            BeginStage(next);
        }

        /// <summary>
        /// "Seguir jugando" tras completar el Demo: Modo Libre con enemigos de ambas etapas.
        /// </summary>
        public void ContinueEndless()
        {
            DifficultyManager.Instance?.ResumeTimer();
            _isSpawningActive = true;
            BeginStage(Mathf.Clamp(endlessStageIndex, 0, stages.Count - 1));
        }

        /// <summary>
        /// Retira de la arena a todos los enemigos y proyectiles hostiles (transición entre etapas).
        /// </summary>
        private void ClearArena()
        {
            if (EnemyManager.Instance != null)
            {
                EnemyManager.Instance.GetActiveEnemies(_clearBuffer);
                foreach (Enemy enemy in _clearBuffer)
                {
                    if (enemy != null && enemy.CurrentState != EnemyState.Dead) enemy.Vanish();
                }
                _clearBuffer.Clear();
            }

            // Si se retiró un jefe sin derrotarlo (atajo de depuración), ocultar su barra
            BossHealthUI.Instance?.SetVisible(false);

            EnemyProjectile[] projectiles = FindObjectsByType<EnemyProjectile>(FindObjectsSortMode.None);
            foreach (EnemyProjectile p in projectiles)
            {
                if (p.gameObject.activeInHierarchy) p.Despawn();
            }
        }
        #endregion

        #region Spawning
        private void HandleSpawning(StageDefinition stage, float intervalMultiplier)
        {
            _spawnTimer += Time.deltaTime;

            float minutes = _stageTimer / 60f;
            float interval = Mathf.Max(stage.minSpawnInterval, stage.initialSpawnInterval - (minutes * stage.spawnIntervalDecayPerMinute));
            interval *= Mathf.Max(0.1f, intervalMultiplier);

            if (_spawnTimer < interval) return;
            _spawnTimer = 0f;

            int aliveCap = maxAliveEnemies;
            int maxGroup = stage.maxGroupSize;
            float minutesPerGroupStep = 2f;
            if (stage.scaleQuantityOverTime)
            {
                // Modo Libre: la cantidad crece con el tiempo (vida y velocidad intactas)
                aliveCap = Mathf.Min(stage.scaledAliveCapMax, stage.scaledAliveCapBase + Mathf.FloorToInt(stage.scaledAliveCapPerMinute * minutes));
                maxGroup = stage.scaledMaxGroupSize;
                minutesPerGroupStep = Mathf.Max(0.1f, stage.minutesPerExtraGroupEnemy);
            }

            int alive = EnemyManager.Instance != null ? EnemyManager.Instance.ActiveEnemyCount : 0;
            if (alive >= aliveCap) return;

            int groupSize = Mathf.Clamp(1 + Mathf.FloorToInt(minutes / minutesPerGroupStep), 1, Mathf.Max(1, maxGroup));
            for (int i = 0; i < groupSize; i++)
            {
                StageEnemyEntry entry = SelectEntry(stage);
                if (entry == null) break;
                SpawnEntry(entry, stage);
            }
        }

        private StageEnemyEntry SelectEntry(StageDefinition stage)
        {
            _candidates.Clear();
            float totalWeight = 0f;
            bool bossFight = _phase == Phase.BossFight;

            foreach (StageEnemyEntry entry in stage.enemies)
            {
                if (entry == null || string.IsNullOrEmpty(entry.poolTag) || _stageTimer < entry.unlockTime) continue;
                if (bossFight && !entry.allowedDuringBoss) continue;
                if (ObjectPooler.Instance == null || !ObjectPooler.Instance.HasPool(entry.poolTag)) continue;

                int cap = bossFight && entry.bossFightMaxAlive > 0 ? entry.bossFightMaxAlive : entry.maxAlive;
                if (cap > 0 && stage.scaleQuantityOverTime && !bossFight)
                {
                    // Modo Libre: los topes por tipo también crecen (ej. más Sentries y Carriers a la vez con el tiempo)
                    cap = Mathf.FloorToInt(cap * (1f + (stage.typeCapGrowthPerMinute * (_stageTimer / 60f))));
                }
                if (cap > 0 && CountAlive(entry.poolTag) >= cap) continue;

                _candidates.Add(entry);
                totalWeight += entry.spawnWeight;
            }

            if (_candidates.Count == 0) return null;

            float roll = Random.Range(0f, totalWeight);
            float cumulative = 0f;
            foreach (StageEnemyEntry entry in _candidates)
            {
                cumulative += entry.spawnWeight;
                if (roll <= cumulative) return entry;
            }
            return _candidates[_candidates.Count - 1];
        }

        private int CountAlive(string poolTag)
        {
            if (EnemyManager.Instance == null || !_tagTypes.TryGetValue(poolTag, out EnemyType type)) return 0;
            return EnemyManager.Instance.CountActiveOfType(type);
        }

        private void SpawnEntry(StageEnemyEntry entry, StageDefinition stage)
        {
            bool avoidTop = _bossBlocksTopSpawns && _phase == Phase.BossFight;

            switch (entry.spawnMode)
            {
                case SpawnMode.PhaseIn:
                    Vector2 playerPos = _playerTransform != null ? (Vector2)_playerTransform.position : Vector2.zero;
                    SpawnAt(entry.poolTag, CameraBounds.RandomVisiblePoint(avoidTop ? 0.6f : 0.7f, playerPos, 5f));
                    break;

                case SpawnMode.FourCorners:
                    foreach (Vector2 corner in GetCornerPositions(avoidTop))
                    {
                        SpawnAt(entry.poolTag, corner);
                    }
                    break;

                default:
                    SpawnAt(entry.poolTag, GetRandomCameraPerimeterPosition(avoidTop));
                    break;
            }
        }

        private void SpawnAt(string poolTag, Vector2 position)
        {
            if (ObjectPooler.Instance == null) return;
            GameObject go = ObjectPooler.Instance.SpawnFromPool(poolTag, position, Quaternion.identity);
            if (go != null && !_tagTypes.ContainsKey(poolTag) && go.TryGetComponent(out Enemy enemy) && enemy.Data != null)
            {
                _tagTypes[poolTag] = enemy.Data.enemyType;
            }
        }

        private Vector2[] GetCornerPositions(bool avoidTop)
        {
            if (!CameraBounds.TryGet(out Vector2 c, out Vector2 h, targetCamera))
            {
                Vector2 o = _playerTransform != null ? (Vector2)_playerTransform.position : Vector2.zero;
                float r = fallbackSpawnRadius;
                return new[] { o + new Vector2(-r, r), o + new Vector2(r, r), o + new Vector2(-r, -r), o + new Vector2(r, -r) };
            }

            float x = h.x + spawnMargin;
            float y = h.y + spawnMargin;
            if (avoidTop)
            {
                // Con el Mothership arriba: esquinas inferiores y laterales a media altura
                return new[] { c + new Vector2(-x, -y), c + new Vector2(x, -y), c + new Vector2(-x, 0f), c + new Vector2(x, 0f) };
            }
            return new[] { c + new Vector2(-x, y), c + new Vector2(x, y), c + new Vector2(-x, -y), c + new Vector2(x, -y) };
        }

        /// <summary>
        /// Posición de aparición fuera de los bordes de la cámara (adaptada a cámara ortográfica o en perspectiva).
        /// 'avoidTop' excluye el borde superior (los enemigos entran por los lados o por abajo).
        /// </summary>
        public Vector2 GetRandomCameraPerimeterPosition(bool avoidTop = false)
        {
            if (!CameraBounds.TryGet(out Vector2 camCenter, out Vector2 half, targetCamera))
            {
                Vector2 origin = _playerTransform != null ? (Vector2)_playerTransform.position : Vector2.zero;
                float angle = Random.Range(avoidTop ? 180f : 0f, 360f) * Mathf.Deg2Rad;
                return origin + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * fallbackSpawnRadius;
            }

            float extraDepth = Random.Range(0f, spawnBandThickness);
            float mx = half.x + spawnMargin + extraDepth;
            float my = half.y + spawnMargin + extraDepth;

            float horizontalLen = half.x * 2f * (avoidTop ? 0.5f : 1f); // solo borde inferior si se evita el superior
            float verticalLen = half.y * 2f;
            bool horizontalSide = Random.value < (horizontalLen / (horizontalLen + verticalLen));

            if (horizontalSide)
            {
                float signY = avoidTop ? -1f : (Random.value < 0.5f ? 1f : -1f);
                return camCenter + new Vector2(Random.Range(-mx, mx), my * signY);
            }

            float signX = Random.value < 0.5f ? 1f : -1f;
            float maxY = avoidTop ? half.y * 0.3f : my;
            return camCenter + new Vector2(mx * signX, Random.Range(-my, maxY));
        }
        #endregion

        private void HandleDebugKeys()
        {
            // Activas también en la build para que el equipo pueda testear (B / N / G / F6)
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;

            // B: invoca ya al jefe de la etapa actual
            if (kb.bKey.wasPressedThisFrame && (_phase == Phase.Spawning || _phase == Phase.Intro) && CurrentStage != null)
            {
                if (CurrentStage.bossTime > 0f)
                {
                    Debug.Log("<color=cyan><b>[WaveManager Debug]</b> Invocando jefe de la etapa (Tecla 'B').</color>");
                    _stageTimer = CurrentStage.bossTime;
                    _phase = Phase.Spawning;
                }
                else if (HasRecurringBosses(CurrentStage))
                {
                    Debug.Log("<color=cyan><b>[WaveManager Debug]</b> Invocando el siguiente jefe del Modo Libre (Tecla 'B').</color>");
                    _nextRecurringBossTime = _stageTimer;
                    _phase = Phase.Spawning;
                }
            }

            // N: avanza 1 minuto dentro de la etapa
            if (kb.nKey.wasPressedThisFrame)
            {
                Debug.Log("<color=cyan><b>[WaveManager Debug]</b> +1 minuto de etapa (Tecla 'N').</color>");
                _stageTimer += 60f;
            }

            // G: concede todas las armas de jefe para probarlas
            if (kb.gKey.wasPressedThisFrame && UpgradeManager.Instance != null)
            {
                Debug.Log("<color=cyan><b>[WaveManager Debug]</b> Armas de prueba concedidas (Tecla 'G').</color>");
                foreach (string id in new[] { "energy_shield", "laser_burst", "rail_gun", "fan_shot", "laser_shotgun" })
                {
                    UpgradeManager.Instance.AddUpgrade(id);
                }
            }

            // F6: salta a la siguiente etapa
            if (kb.f6Key.wasPressedThisFrame && _phase != Phase.StageClear)
            {
                Debug.Log("<color=cyan><b>[WaveManager Debug]</b> Saltando a la siguiente etapa (F6).</color>");
                ClearArena();
                AdvanceAfterStageClear();
            }
#endif
        }

        private void StopSpawning()
        {
            _isSpawningActive = false;
        }

        private void OnDrawGizmosSelected()
        {
            if (!CameraBounds.TryGet(out Vector2 c, out Vector2 h, targetCamera)) return;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireCube(c, new Vector3(h.x * 2f, h.y * 2f, 0f));
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.7f);
            Gizmos.DrawWireCube(c, new Vector3((h.x + spawnMargin) * 2f, (h.y + spawnMargin) * 2f, 0f));
        }
    }
}
