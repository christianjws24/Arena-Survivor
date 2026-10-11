using System;
using ArenaSurvivor.Combat;
using ArenaSurvivor.Data;
using ArenaSurvivor.Entities;
using ArenaSurvivor.Pool;
using UnityEngine;

namespace ArenaSurvivor.Managers
{
    /// <summary>
    /// Catálogo de los enemigos de la Etapa 2 y del Boss Mothership, con sus estadísticas de diseño.
    /// - En runtime: si la escena no trae un pool para alguno de ellos, construye una plantilla procedural y registra el pool.
    ///   Así el juego funciona al dar Play sin depender de prefabs generados.
    /// - En el editor: ArenaPrototypeSetup usa las mismas definiciones para guardar prefabs y EnemyData editables en disco.
    /// </summary>
    public static class RuntimeContentFactory
    {
        public class EnemySpec
        {
            public string PoolTag;
            public string DataAssetName;
            public string DisplayName;
            public EnemyType Type;
            public EnemyColorType ColorType;
            public Color Color;
            public float Health;
            public float Speed;
            public int Damage = 1;
            public Vector2 Scale = Vector2.one;
            public float ShakeIntensity = 0.12f;
            public float ShakeDuration = 0.08f;
            public int Score = 10;
            public bool CircleSprite;
            public Type Component;
            public int PoolSize = 20;
        }

        public static readonly EnemySpec[] Specs =
        {
            // 5_Worm: 200hp, 4 desde las esquinas, velocidad regular
            new EnemySpec { PoolTag = "Enemy_Worm", DataAssetName = "EnemyData_Worm", DisplayName = "Worm", Type = EnemyType.Worm, ColorType = EnemyColorType.Green,
                Color = new Color(0.8f, 0.4f, 1f), Health = 200f, Speed = 3.1f, Scale = new Vector2(0.95f, 0.95f), CircleSprite = true,
                Component = typeof(WormEnemy), PoolSize = 32, Score = 12 },
            // 6_Sentry: 400hp, fasea en pantalla, inmóvil, 3 tiros cada 3s
            new EnemySpec { PoolTag = "Enemy_Sentry", DataAssetName = "EnemyData_Sentry", DisplayName = "Sentry", Type = EnemyType.Sentry, ColorType = EnemyColorType.Blue,
                Color = new Color(0.25f, 0.85f, 0.95f), Health = 400f, Speed = 0.1f, Scale = new Vector2(1.4f, 1.4f),
                ShakeIntensity = 0.2f, ShakeDuration = 0.12f, Component = typeof(SentryEnemy), PoolSize = 8, Score = 35 },
            // 7_Caballier: 300hp, zigzag rápido + tajo de energía
            new EnemySpec { PoolTag = "Enemy_Caballier", DataAssetName = "EnemyData_Caballier", DisplayName = "Caballier", Type = EnemyType.Caballier, ColorType = EnemyColorType.Yellow,
                Color = new Color(0.9f, 0.95f, 1f), Health = 300f, Speed = 6f, Scale = new Vector2(1.1f, 0.8f),
                ShakeIntensity = 0.15f, ShakeDuration = 0.1f, Component = typeof(CaballierEnemy), PoolSize = 16, Score = 30 },
            // 8_Sidewinder: 500hp, orbita y se detiene a disparar 2 tiros rápidos cada 4s
            new EnemySpec { PoolTag = "Enemy_Sidewinder", DataAssetName = "EnemyData_Sidewinder", DisplayName = "Sidewinder", Type = EnemyType.Sidewinder, ColorType = EnemyColorType.Red,
                Color = new Color(1f, 0.5f, 0.12f), Health = 500f, Speed = 4.2f, Scale = new Vector2(1.5f, 0.85f),
                ShakeIntensity = 0.2f, ShakeDuration = 0.12f, Component = typeof(SidewinderEnemy), PoolSize = 12, Score = 40 },
            // BOSS 2_MOTHERSHIP: núcleo (Rail Gun central) de 1500hp; el resto de partes se generan solas
            new EnemySpec { PoolTag = "Boss_Mothership", DataAssetName = "EnemyData_Mothership", DisplayName = "Mothership", Type = EnemyType.BossMothership, ColorType = EnemyColorType.Red,
                Color = new Color(1f, 0.3f, 0.9f), Health = 1500f, Speed = 1f, Scale = Vector2.one, CircleSprite = true,
                ShakeIntensity = 0.7f, ShakeDuration = 0.5f, Component = typeof(BossMothership), PoolSize = 1, Score = 500 },
        };

        private const int EnemyLayer = 3;

        /// <summary>
        /// Registra un pool procedural para cada enemigo nuevo que no tenga pool configurado en la escena.
        /// </summary>
        public static void EnsureRuntimePools(ObjectPooler pooler)
        {
            if (pooler == null) return;

            Transform templatesRoot = null;
            foreach (EnemySpec spec in Specs)
            {
                if (pooler.HasPool(spec.PoolTag)) continue;

                if (templatesRoot == null)
                {
                    templatesRoot = new GameObject("RuntimeTemplates").transform;
                    templatesRoot.SetParent(pooler.transform, false);
                    templatesRoot.gameObject.SetActive(false);
                }

                GameObject template = BuildEnemy(spec, CreateEnemyData(spec), false);
                template.transform.SetParent(templatesRoot, false);
                pooler.RegisterPool(spec.PoolTag, template, spec.PoolSize);
                Debug.Log($"<color=#88CCFF>[RuntimeContentFactory]</color> Pool '{spec.PoolTag}' generado en runtime (ejecuta Tools > Arena Survivor > 2 para guardarlo como prefab editable).");
            }
        }

        public static EnemyData CreateEnemyData(EnemySpec spec)
        {
            EnemyData data = ScriptableObject.CreateInstance<EnemyData>();
            data.name = spec.DataAssetName;
            data.enemyName = spec.DisplayName;
            data.enemyType = spec.Type;
            data.colorType = spec.ColorType;
            data.spriteColor = spec.Color;
            data.maxHealth = spec.Health;
            data.moveSpeed = spec.Speed;
            data.damage = spec.Damage;
            data.visualScale = spec.Scale;
            data.deathShakeIntensity = spec.ShakeIntensity;
            data.deathShakeDuration = spec.ShakeDuration;
            data.scoreValue = spec.Score;
            return data;
        }

        /// <summary>
        /// Construye el GameObject de un enemigo (cuerpo, físicas cinemáticas, trigger y comportamiento).
        /// 'active' = false para plantillas de pool en runtime (no ejecutan Awake hasta salir del pool).
        /// </summary>
        public static GameObject BuildEnemy(EnemySpec spec, EnemyData data, bool active)
        {
            GameObject go = new GameObject(spec.PoolTag);
            go.SetActive(active);
            go.layer = EnemyLayer;
            go.tag = "Enemy";

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = spec.CircleSprite ? ProceduralSprites.Circle : ProceduralSprites.Square;
            sr.color = spec.Color;
            sr.sortingOrder = spec.Type == EnemyType.BossMothership ? 4 : 3;

            Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.useFullKinematicContacts = true;

            BoxCollider2D col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = spec.Type == EnemyType.BossMothership ? new Vector2(1.8f, 1.8f) : Vector2.one;

            Enemy enemy = (Enemy)go.AddComponent(spec.Component);
            enemy.AssignData(data);
            go.transform.localScale = new Vector3(spec.Scale.x, spec.Scale.y, 1f);
            return go;
        }
    }
}
