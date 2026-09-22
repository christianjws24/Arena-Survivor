#if UNITY_EDITOR
using System.IO;
using ArenaSurvivor.Combat;
using ArenaSurvivor.Data;
using ArenaSurvivor.Entities;
using ArenaSurvivor.Managers;
using ArenaSurvivor.Pool;
using ArenaSurvivor.UI;
using ArenaSurvivor.Upgrades;
using UnityEditor;
using UnityEngine;

namespace ArenaSurvivor.Editor
{
    /// <summary>
    /// Herramienta de Editor que automatiza la creación de Sprites procedurales,
    /// ScriptableObjects y Prefabs configurados en la carpeta Assets/Prefabs.
    /// Ejecutable desde el menú: Tools > Arena Survivor > Generar Prefabs y Assets del Prototipo.
    /// </summary>
    public static class ArenaPrototypeSetup
    {
        private const string PrefabsFolder = "Assets/Prefabs";
        private const string DataFolder = "Assets/Data";
        private const string SpritesFolder = "Assets/Sprites";

        [MenuItem("Tools/Arena Survivor/1. Generar Prefabs y Assets del Prototipo")]
        public static void GeneratePrototypeAssets()
        {
            EnsureDirectoriesExist();

            // 1. Generar sprites procedurales básicos (Círculo, Cuadrado, Cápsula, Anillo)
            Sprite circleSprite = GetOrCreateCircleSprite();
            Sprite squareSprite = GetOrCreateSquareSprite();
            Sprite capsuleSprite = GetOrCreateCapsuleSprite();
            Sprite ringSprite = GetOrCreateRingSprite();

            // 2. Generar ScriptableObjects para los 4 colores de enemigos
            EnemyData redData = GetOrCreateEnemyData("EnemyData_Red", EnemyColorType.Red, new Color(0.95f, 0.25f, 0.25f), 15f, 3.2f, 1, new Vector2(1f, 1f), 0.12f, 0.08f);
            EnemyData greenData = GetOrCreateEnemyData("EnemyData_Green", EnemyColorType.Green, new Color(0.25f, 0.9f, 0.35f), 8f, 5.0f, 1, new Vector2(0.75f, 0.75f), 0.08f, 0.06f);
            EnemyData blueData = GetOrCreateEnemyData("EnemyData_Blue", EnemyColorType.Blue, new Color(0.2f, 0.5f, 0.98f), 40f, 1.8f, 2, new Vector2(1.35f, 1.35f), 0.22f, 0.12f);
            EnemyData yellowData = GetOrCreateEnemyData("EnemyData_Yellow", EnemyColorType.Yellow, new Color(0.98f, 0.85f, 0.15f), 22f, 3.8f, 1, new Vector2(0.9f, 0.9f), 0.16f, 0.09f);

            // 3. Generar ScriptableObjects de Mejoras (10 Power-Ups de la Arquitectura Escalable)
            CreateAllPrototypeUpgrades();

            // 4. Generar Prefabs en Assets/Prefabs
            GameObject bulletPrefab = CreateBulletPrefab(capsuleSprite);
            GameObject redEnemyPrefab = CreateEnemyPrefab("Enemy_Red", squareSprite, redData);
            GameObject greenEnemyPrefab = CreateEnemyPrefab("Enemy_Green", squareSprite, greenData);
            GameObject blueEnemyPrefab = CreateEnemyPrefab("Enemy_Blue", squareSprite, blueData);
            GameObject yellowEnemyPrefab = CreateEnemyPrefab("Enemy_Yellow", squareSprite, yellowData);
            GameObject playerPrefab = CreatePlayerPrefab(circleSprite, ringSprite);
            GameObject deathVFXPrefab = CreateDeathVFXPrefab();
            GameObject extractionZonePrefab = CreateExtractionZonePrefab(circleSprite);
            GameObject shockwavePrefab = CreateShockwavePrefab(ringSprite);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("<color=green><b>[ArenaPrototypeSetup]</b> Prefabs y ScriptableObjects generados correctamente.</color>");
        }

        [MenuItem("Tools/Arena Survivor/2. Configurar Escena de Prueba Automáticamente")]
        public static void SetupCurrentScene()
        {
            GeneratePrototypeAssets();
            Sprite ringSprite = GetOrCreateRingSprite();

            // 1. Configurar Cámara Principal con CameraShake y color de fondo oscuro de arena
            Camera cam = Camera.main;
            if (cam != null)
            {
                cam.backgroundColor = new Color(0.11f, 0.11f, 0.15f);
                cam.clearFlags = CameraClearFlags.SolidColor;
                if (!cam.TryGetComponent<CameraShake>(out _))
                {
                    cam.gameObject.AddComponent<CameraShake>();
                }
            }

            // 2. Localizar o instanciar Player en la escena
#if UNITY_2023_1_OR_NEWER
            PlayerController player = Object.FindFirstObjectByType<PlayerController>();
#else
            PlayerController player = Object.FindObjectOfType<PlayerController>();
#endif
            if (player == null)
            {
                GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsFolder}/Player.prefab");
                if (playerPrefab != null)
                {
                    GameObject pGo = PrefabUtility.InstantiatePrefab(playerPrefab) as GameObject;
                    pGo.transform.position = Vector3.zero;
                    player = pGo.GetComponent<PlayerController>();
                    Undo.RegisterCreatedObjectUndo(pGo, "Crear Player");
                }
            }

            // 3. Conectar cámara al jugador
            if (cam != null && cam.TryGetComponent<CameraShake>(out var shaker) && player != null)
            {
                SerializedObject so = new SerializedObject(shaker);
                so.FindProperty("targetToFollow").objectReferenceValue = player.transform;
                so.ApplyModifiedProperties();
            }

            // 4. Localizar o crear ObjectPooler
#if UNITY_2023_1_OR_NEWER
            ObjectPooler pooler = Object.FindFirstObjectByType<ObjectPooler>();
#else
            ObjectPooler pooler = Object.FindObjectOfType<ObjectPooler>();
#endif
            if (pooler == null)
            {
                GameObject poolerGo = new GameObject("ObjectPooler");
                pooler = poolerGo.AddComponent<ObjectPooler>();
                Undo.RegisterCreatedObjectUndo(poolerGo, "Crear ObjectPooler");
            }

            // 5. Poblar pools serializados en el ObjectPooler
            SerializedObject poolerSO = new SerializedObject(pooler);
            SerializedProperty poolsProp = poolerSO.FindProperty("pools");
            poolsProp.ClearArray();

            void AddPoolEntry(string tag, string prefabName, int size)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsFolder}/{prefabName}.prefab");
                if (prefab == null) return;

                int index = poolsProp.arraySize;
                poolsProp.InsertArrayElementAtIndex(index);
                SerializedProperty entry = poolsProp.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("tag").stringValue = tag;
                entry.FindPropertyRelative("prefab").objectReferenceValue = prefab;
                entry.FindPropertyRelative("size").intValue = size;
                entry.FindPropertyRelative("shouldExpand").boolValue = true;
            }

            AddPoolEntry("PlayerBullet", "PlayerBullet", 40);
            AddPoolEntry("Enemy_Red", "Enemy_Red", 30);
            AddPoolEntry("Enemy_Green", "Enemy_Green", 30);
            AddPoolEntry("Enemy_Blue", "Enemy_Blue", 15);
            AddPoolEntry("Enemy_Yellow", "Enemy_Yellow", 20);
            AddPoolEntry("DeathVFX", "DeathVFX", 30);
            AddPoolEntry("ExtractionZone", "ExtractionZone", 3);
            AddPoolEntry("ShockwaveVFX", "ShockwaveVFX", 10);
            AddPoolEntry("BlackHole", "BlackHole", 10);
            poolerSO.ApplyModifiedProperties();

            // 6. Asegurar PlayerEnergy, SatelliteOrbitController y PlayerVFXController en el Player
            if (player != null)
            {
                if (!player.TryGetComponent<PlayerEnergy>(out _))
                {
                    player.gameObject.AddComponent<PlayerEnergy>();
                    Undo.RegisterCreatedObjectUndo(player.gameObject, "Agregar PlayerEnergy");
                }
                if (!player.TryGetComponent<Combat.SatelliteOrbitController>(out _))
                {
                    player.gameObject.AddComponent<Combat.SatelliteOrbitController>();
                    Undo.RegisterCreatedObjectUndo(player.gameObject, "Agregar SatelliteOrbitController");
                }
                EnsurePlayerVFXSetup(player, ringSprite);
            }

            // 7. Localizar o crear UpgradeManager y vincular las mejoras
#if UNITY_2023_1_OR_NEWER
            UpgradeManager upgradeMgr = Object.FindFirstObjectByType<UpgradeManager>();
#else
            UpgradeManager upgradeMgr = Object.FindObjectOfType<UpgradeManager>();
#endif
            if (upgradeMgr == null)
            {
                GameObject upGo = new GameObject("UpgradeManager");
                upgradeMgr = upGo.AddComponent<UpgradeManager>();
                Undo.RegisterCreatedObjectUndo(upGo, "Crear UpgradeManager");
            }

            SerializedObject upSO = new SerializedObject(upgradeMgr);
            SerializedProperty upList = upSO.FindProperty("allUpgrades");
            if (upList != null)
            {
                upList.ClearArray();
                string[] upgradeKeys = {
                    "pierce", "bounce", "satellite", "vampirism", "scorched_earth",
                    "chain_reaction", "parry_dash", "mirror_refractor", "defensive_anchor", "stasis_nova",
                    "chain_reflexes", "geometric_shrapnel", "mass_transposition", "gravitational_anomaly",
                    "overclock", "heavy_caliber", "servomotors", "rapid_parry"
                };

                foreach (string key in upgradeKeys)
                {
                    UpgradeData upAsset = AssetDatabase.LoadAssetAtPath<UpgradeData>($"{DataFolder}/Upgrade_{key}.asset");
                    if (upAsset == null) continue;
                    int idx = upList.arraySize;
                    upList.InsertArrayElementAtIndex(idx);
                    upList.GetArrayElementAtIndex(idx).objectReferenceValue = upAsset;
                }
                upSO.ApplyModifiedProperties();
                upgradeMgr.InitializeCatalog();
            }

            // 8. Localizar o crear EnemyManager
#if UNITY_2023_1_OR_NEWER
            EnemyManager enemyMgr = Object.FindFirstObjectByType<EnemyManager>();
#else
            EnemyManager enemyMgr = Object.FindObjectOfType<EnemyManager>();
#endif
            if (enemyMgr == null)
            {
                GameObject emGo = new GameObject("EnemyManager");
                enemyMgr = emGo.AddComponent<EnemyManager>();
                Undo.RegisterCreatedObjectUndo(emGo, "Crear EnemyManager");
            }

            // 9. Localizar o crear WaveManager
#if UNITY_2023_1_OR_NEWER
            WaveManager waveMgr = Object.FindFirstObjectByType<WaveManager>();
#else
            WaveManager waveMgr = Object.FindObjectOfType<WaveManager>();
#endif
            if (waveMgr == null)
            {
                GameObject waveGo = new GameObject("WaveManager");
                waveMgr = waveGo.AddComponent<WaveManager>();
                Undo.RegisterCreatedObjectUndo(waveGo, "Crear WaveManager");
            }

            // 9. Localizar o crear HitStopManager
#if UNITY_2023_1_OR_NEWER
            HitStopManager hitStop = Object.FindFirstObjectByType<HitStopManager>();
#else
            HitStopManager hitStop = Object.FindObjectOfType<HitStopManager>();
#endif
            if (hitStop == null)
            {
                GameObject hsGo = new GameObject("HitStopManager");
                hitStop = hsGo.AddComponent<HitStopManager>();
                Undo.RegisterCreatedObjectUndo(hsGo, "Crear HitStopManager");
            }

            // 10. Localizar o crear AudioManager
#if UNITY_2023_1_OR_NEWER
            AudioManager audioMgr = Object.FindFirstObjectByType<AudioManager>();
#else
            AudioManager audioMgr = Object.FindObjectOfType<AudioManager>();
#endif
            if (audioMgr == null)
            {
                GameObject aGo = new GameObject("AudioManager");
                audioMgr = aGo.AddComponent<AudioManager>();
                Undo.RegisterCreatedObjectUndo(aGo, "Crear AudioManager");
            }

            // 11. Localizar o crear ArenaBoundary
#if UNITY_2023_1_OR_NEWER
            ArenaBoundary arenaBoundary = Object.FindFirstObjectByType<ArenaBoundary>();
#else
            ArenaBoundary arenaBoundary = Object.FindObjectOfType<ArenaBoundary>();
#endif
            if (arenaBoundary == null)
            {
                GameObject bGo = new GameObject("ArenaBoundary");
                arenaBoundary = bGo.AddComponent<ArenaBoundary>();
                Undo.RegisterCreatedObjectUndo(bGo, "Crear ArenaBoundary");
            }

            // 12. Localizar o crear GameManager
#if UNITY_2023_1_OR_NEWER
            GameManager gameMgr = Object.FindFirstObjectByType<GameManager>();
#else
            GameManager gameMgr = Object.FindObjectOfType<GameManager>();
#endif
            if (gameMgr == null)
            {
                GameObject gmGo = new GameObject("GameManager");
                gameMgr = gmGo.AddComponent<GameManager>();
                Undo.RegisterCreatedObjectUndo(gmGo, "Crear GameManager");
            }

            // 13. Localizar o crear DifficultyManager
#if UNITY_2023_1_OR_NEWER
            DifficultyManager diffMgr = Object.FindFirstObjectByType<DifficultyManager>();
#else
            DifficultyManager diffMgr = Object.FindObjectOfType<DifficultyManager>();
#endif
            if (diffMgr == null)
            {
                GameObject dmGo = new GameObject("DifficultyManager");
                diffMgr = dmGo.AddComponent<DifficultyManager>();
                Undo.RegisterCreatedObjectUndo(dmGo, "Crear DifficultyManager");
            }

            // 14. Crear o asegurar Canvas de UI (Salud, Notificaciones de Mejoras, Barra de Energía y Modal)
            SetupGameHUD();

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            EditorUtility.DisplayDialog(
                "¡Arena Survivor Configurado!",
                "La escena actual ha sido configurada con éxito:\n\n" +
                "• Main Camera con CameraShake siguiendo al Jugador\n" +
                "• Jugador con Auto-Aim, 3 vidas, Parry con Hit-Stop y Energía\n" +
                "• UI de Salud del Jugador (top-left) con corazones y barra de vida\n" +
                "• UI de Notificación de Mejoras (top-center / top-right) al activarse\n" +
                "• UI de Barra de Energía y Protocolo de Extracción (bottom-center)\n" +
                "• ObjectPooler con balas, 4 enemigos, DeathVFX y ExtractionZone\n" +
                "• UpgradeManager con los 10 Power-Ups de la arquitectura escalable\n\n" +
                "¡Presiona PLAY en Unity para probar el prototipo!",
                "¡A Jugar!"
            );
        }

        private static void EnsureDirectoriesExist()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
            if (!AssetDatabase.IsValidFolder("Assets/Data")) AssetDatabase.CreateFolder("Assets", "Data");
            if (!AssetDatabase.IsValidFolder("Assets/Sprites")) AssetDatabase.CreateFolder("Assets", "Sprites");

            EnsureTagExists("Enemy");
        }

        private static void EnsureTagExists(string tag)
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return;

            SerializedObject tagManager = new SerializedObject(assets[0]);
            SerializedProperty tagsProp = tagManager.FindProperty("tags");

            for (int i = 0; i < tagsProp.arraySize; i++)
            {
                SerializedProperty t = tagsProp.GetArrayElementAtIndex(i);
                if (t.stringValue.Equals(tag)) return;
            }

            tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
            SerializedProperty newTag = tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1);
            newTag.stringValue = tag;
            tagManager.ApplyModifiedProperties();
        }

        private static Sprite GetOrCreateCircleSprite()
        {
            string path = $"{SpritesFolder}/Circle.png";
            if (File.Exists(path)) return AssetDatabase.LoadAssetAtPath<Sprite>(path);

            int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float radius = size * 0.48f;
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    if (dist <= radius)
                    {
                        float alpha = Mathf.Clamp01(radius - dist + 0.5f);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                    }
                    else
                    {
                        tex.SetPixel(x, y, Color.clear);
                    }
                }
            }

            tex.Apply();
            SaveTextureAsPNG(tex, path);
            SetTextureAsSprite(path);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static Sprite GetOrCreateSquareSprite()
        {
            string path = $"{SpritesFolder}/Square.png";
            if (File.Exists(path)) return AssetDatabase.LoadAssetAtPath<Sprite>(path);

            int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] colors = new Color[size * size];
            for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
            tex.SetPixels(colors);
            tex.Apply();

            SaveTextureAsPNG(tex, path);
            SetTextureAsSprite(path);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static Sprite GetOrCreateCapsuleSprite()
        {
            string path = $"{SpritesFolder}/Capsule.png";
            if (File.Exists(path)) return AssetDatabase.LoadAssetAtPath<Sprite>(path);

            int width = 32;
            int height = 16;
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            float r = height * 0.5f;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float leftDist = Vector2.Distance(new Vector2(x, y), new Vector2(r, r));
                    float rightDist = Vector2.Distance(new Vector2(x, y), new Vector2(width - r, r));

                    if (x < r && leftDist > r) tex.SetPixel(x, y, Color.clear);
                    else if (x > width - r && rightDist > r) tex.SetPixel(x, y, Color.clear);
                    else tex.SetPixel(x, y, Color.white);
                }
            }

            tex.Apply();
            SaveTextureAsPNG(tex, path);
            SetTextureAsSprite(path);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void SaveTextureAsPNG(Texture2D tex, string path)
        {
            byte[] bytes = tex.EncodeToPNG();
            File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        private static void SetTextureAsSprite(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 64;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }
        }

        private static EnemyData GetOrCreateEnemyData(
            string assetName,
            EnemyColorType colorType,
            Color color,
            float health,
            float speed,
            int damage,
            Vector2 scale,
            float shakeIntensity,
            float shakeDuration)
        {
            string path = $"{DataFolder}/{assetName}.asset";
            EnemyData data = AssetDatabase.LoadAssetAtPath<EnemyData>(path);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<EnemyData>();
                data.enemyName = assetName.Replace("EnemyData_", "") + " Enemy";
                data.colorType = colorType;
                data.spriteColor = color;
                data.maxHealth = health;
                data.moveSpeed = speed;
                data.damage = damage;
                data.visualScale = scale;
                data.deathShakeIntensity = shakeIntensity;
                data.deathShakeDuration = shakeDuration;

                AssetDatabase.CreateAsset(data, path);
            }
            return data;
        }

        private static GameObject CreatePlayerPrefab(Sprite circleSprite, Sprite ringSprite)
        {
            string prefabPath = $"{PrefabsFolder}/Player.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null) return existing;

            GameObject go = new GameObject("Player");
            go.tag = "Player";

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = circleSprite;
            sr.color = new Color(0.2f, 0.85f, 1f); // Azul cian minimalista
            sr.sortingOrder = 5;

            CircleCollider2D col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.48f;

            Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            PlayerController player = go.AddComponent<PlayerController>();
            go.AddComponent<PlayerEnergy>();

            // Crear hijo ParryHitbox con CircleCollider2D en modo Trigger
            GameObject hitboxGo = new GameObject("ParryHitbox");
            hitboxGo.transform.SetParent(go.transform);
            hitboxGo.transform.localPosition = Vector3.zero;

            CircleCollider2D parryCol = hitboxGo.AddComponent<CircleCollider2D>();
            parryCol.radius = 1.35f;
            parryCol.isTrigger = true;

            ParryHitbox hitbox = hitboxGo.AddComponent<ParryHitbox>();

            SerializedObject playerSO = new SerializedObject(player);
            playerSO.FindProperty("parryHitbox").objectReferenceValue = hitbox;
            playerSO.ApplyModifiedProperties();

            // Crear hijo TensionRing
            GameObject tensionRing = new GameObject("TensionRing");
            tensionRing.transform.SetParent(go.transform);
            tensionRing.transform.localPosition = Vector3.zero;
            SpriteRenderer ringSr = tensionRing.AddComponent<SpriteRenderer>();
            ringSr.sprite = ringSprite;
            ringSr.color = new Color(0.3f, 0.95f, 1f, 0.9f);
            ringSr.sortingOrder = 6;
            tensionRing.SetActive(false);

            PlayerVFXController vfx = go.AddComponent<PlayerVFXController>();
            SerializedObject vfxSO = new SerializedObject(vfx);
            vfxSO.FindProperty("player").objectReferenceValue = player;
            vfxSO.FindProperty("playerSpriteRenderer").objectReferenceValue = sr;
            vfxSO.FindProperty("tensionRingObject").objectReferenceValue = tensionRing;
            vfxSO.FindProperty("shockwavePoolTag").stringValue = "ShockwaveVFX";
            vfxSO.ApplyModifiedProperties();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject CreateBulletPrefab(Sprite capsuleSprite)
        {
            string prefabPath = $"{PrefabsFolder}/PlayerBullet.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null) return existing;

            GameObject go = new GameObject("PlayerBullet");
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = capsuleSprite;
            sr.color = new Color(1f, 0.95f, 0.3f); // Amarillo dorado
            sr.sortingOrder = 4;

            CircleCollider2D col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.25f;

            go.AddComponent<Projectile>();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject CreateEnemyPrefab(string name, Sprite squareSprite, EnemyData data)
        {
            string prefabPath = $"{PrefabsFolder}/{name}.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null) return existing;

            GameObject go = new GameObject(name);
            go.tag = "Enemy";

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = squareSprite;
            sr.color = data != null ? data.spriteColor : Color.white;
            sr.sortingOrder = 3;

            BoxCollider2D col = go.AddComponent<BoxCollider2D>();
            col.size = Vector2.one;
            col.isTrigger = true;

            Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.useFullKinematicContacts = true;

            Enemy enemy = go.AddComponent<Enemy>();
            enemy.SetData(data);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject CreateDeathVFXPrefab()
        {
            string prefabPath = $"{PrefabsFolder}/DeathVFX.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null) return existing;

            GameObject go = new GameObject("DeathVFX");
            ParticleSystem ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.loop = false;
            main.duration = 0.5f;
            main.startLifetime = 0.45f;
            main.startSpeed = 9f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.rateOverTime = 0;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.2f;

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
            );
            colorOverLifetime.color = grad;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            AnimationCurve curve = new AnimationCurve();
            curve.AddKey(0f, 1f);
            curve.AddKey(1f, 0f);
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, curve);

            go.AddComponent<DeathVFX>();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject CreateExtractionZonePrefab(Sprite circleSprite)
        {
            string prefabPath = $"{PrefabsFolder}/ExtractionZone.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null) return existing;

            GameObject go = new GameObject("ExtractionZone");

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = circleSprite;
            sr.color = new Color(0.2f, 0.8f, 1f, 0.28f);
            sr.sortingOrder = 1;
            go.transform.localScale = Vector3.one * 7f; // Diámetro de 7 unidades (radio 3.5)

            CircleCollider2D col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.5f;

            go.AddComponent<ExtractionZone>();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static void CreateAllPrototypeUpgrades()
        {
            // Power-Ups canónicos del sistema escalable:
            // Stackables (Escalables con el nivel)
            GetOrCreateUpgrade("pierce", "Balística Perforante", "+1 enemigo atravesado por nivel.", 5, UpgradeType.StatModifier, new Color(1f, 0.85f, 0.2f));
            GetOrCreateUpgrade("bounce", "Geometría de Rebote", "+1 rebote en las paredes de la arena por nivel.", 4, UpgradeType.StatModifier, new Color(0.2f, 0.8f, 1f));
            GetOrCreateUpgrade("satellite", "Módulo Satélite", "+1 orbe orbitando al jugador por nivel.", 4, UpgradeType.BehaviorUnlock, new Color(0.3f, 1f, 0.6f));
            GetOrCreateUpgrade("vampirism", "Vampirismo Rítmico", "Requiere 3 parrys seguidos al Nivel 1. Cada nivel extra reduce el requerimiento o aumenta la curación.", 3, UpgradeType.BehaviorUnlock, new Color(0.95f, 0.25f, 0.35f));
            GetOrCreateUpgrade("scorched_earth", "Tierra Calcinada", "Nivel 1 activa el daño en la Zona de Extracción. Niveles extra aumentan el DPS.", 5, UpgradeType.ZoneModifier, new Color(1f, 0.45f, 0.1f));

            // Binarias / Comportamiento (Nivel Máximo 1 o 2)
            GetOrCreateUpgrade("chain_reaction", "Relevo Técnico", "El enemigo desviado convierte a otros en proyectiles aliados al impactar.", 1, UpgradeType.BehaviorUnlock, new Color(0.85f, 0.3f, 1f));
            GetOrCreateUpgrade("parry_dash", "Desvío Espectral", "El Parry otorga un dash invulnerable automático.", 1, UpgradeType.BehaviorUnlock, new Color(0.3f, 0.9f, 1f), "Parry_Comportamiento");
            GetOrCreateUpgrade("mirror_refractor", "Espejo Refractor", "El enemigo desviado se divide en 3 proyectiles más pequeños (hasta 5 en Nivel 2).", 2, UpgradeType.BehaviorUnlock, new Color(0.6f, 0.7f, 1f));
            GetOrCreateUpgrade("defensive_anchor", "Anclaje Defensivo", "Reduce el cooldown de fallar el Parry casi a cero mientras estés en la Zona de Extracción.", 1, UpgradeType.ZoneModifier, new Color(0.9f, 0.75f, 0.2f));
            GetOrCreateUpgrade("stasis_nova", "Estallido Nova", "Cambia la explosión final de la extracción por una que aplica ralentización (Estasis) masiva.", 1, UpgradeType.ZoneModifier, new Color(0.2f, 0.6f, 1f));

            // 4 Nuevos Power-Ups de Parry y Exclusión
            GetOrCreateUpgrade("chain_reflexes", "Reflejos en Cadena", "Al acertar un Parry, otorga un buff de 2s que amplía la ventana activa del próximo Parry en un 50%.", 3, UpgradeType.StatModifier, new Color(0.25f, 0.95f, 0.6f));
            GetOrCreateUpgrade("geometric_shrapnel", "Metralla Geométrica", "Cuando un enemigo desviado choca y se destruye, libera 6 proyectiles en abanico circular de 360°.", 3, UpgradeType.BehaviorUnlock, new Color(1f, 0.85f, 0.1f));
            GetOrCreateUpgrade("mass_transposition", "Transposición de Masa", "Reemplaza el Efecto Billar: Intercambias posición con el enemigo y este explota instantáneamente en tu lugar de origen.", 1, UpgradeType.BehaviorUnlock, new Color(0.95f, 0.2f, 0.85f), "Parry_Comportamiento");
            GetOrCreateUpgrade("gravitational_anomaly", "Anomalía Gravitacional", "Al acertar un Parry, crea un vórtice en el punto de impacto que atrae a los enemigos cercanos durante 3 segundos.", 3, UpgradeType.ZoneModifier, new Color(0.6f, 0.2f, 1f));

            // 4 Mejoras de Estadísticas Base (Stat Modifiers)
            GetOrCreateUpgrade("overclock", "Overclock", "Reduce el cooldown del disparo automático en un 10% por nivel.", 5, UpgradeType.StatModifier, new Color(1f, 0.82f, 0.2f));
            GetOrCreateUpgrade("heavy_caliber", "Calibre Pesado", "Aumenta el daño base de los proyectiles y de colisión del Parry en un 20% por nivel.", 5, UpgradeType.StatModifier, new Color(1f, 0.35f, 0.2f));
            GetOrCreateUpgrade("servomotors", "Servomotores", "Aumenta la velocidad de movimiento del jugador en un 10% por nivel.", 5, UpgradeType.StatModifier, new Color(0.2f, 0.9f, 0.45f));
            GetOrCreateUpgrade("rapid_parry", "Reflejos Cinéticos", "Reduce el tiempo de enfriamiento (cooldown) del Parry en un 20% por nivel.", 4, UpgradeType.StatModifier, new Color(0.18f, 0.85f, 1f));
        }

        private static UpgradeData GetOrCreateUpgrade(string id, string name, string description, int maxLevel, UpgradeType type, Color color, string exclusiveTag = "")
        {
            string path = $"{DataFolder}/Upgrade_{id}.asset";
            UpgradeData up = AssetDatabase.LoadAssetAtPath<UpgradeData>(path);
            if (up == null)
            {
                up = ScriptableObject.CreateInstance<UpgradeData>();
                up.id = id;
                up.upgradeName = name;
                up.description = description;
                up.maxLevel = maxLevel;
                up.upgradeType = type;
                up.themeColor = color;
                up.exclusiveTag = exclusiveTag;
                AssetDatabase.CreateAsset(up, path);
            }
            else
            {
                up.id = id;
                up.upgradeName = name;
                up.description = description;
                up.maxLevel = maxLevel;
                up.upgradeType = type;
                up.themeColor = color;
                up.exclusiveTag = exclusiveTag;
                EditorUtility.SetDirty(up);
            }
            return up;
        }

        private static void SetupGameHUD()
        {
            // 1. Localizar o crear Canvas general
#if UNITY_2023_1_OR_NEWER
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
#else
            Canvas canvas = Object.FindObjectOfType<Canvas>();
#endif
            GameObject canvasGo;
            if (canvas == null)
            {
                canvasGo = new GameObject("UI_Canvas");
                canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                Undo.RegisterCreatedObjectUndo(canvasGo, "Crear Canvas");
            }
            else
            {
                canvasGo = canvas.gameObject;
            }

            // Asegurar EventSystem en la escena para capturar clics en UI
#if UNITY_2023_1_OR_NEWER
            var es = Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
#else
            var es = Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
#endif
            if (es == null)
            {
                GameObject esGo = new GameObject("EventSystem");
                es = esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
#if ENABLE_INPUT_SYSTEM
                esGo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
                esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
                Undo.RegisterCreatedObjectUndo(esGo, "Crear EventSystem");
            }

            Font defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (defaultFont == null) defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

            // 2. Montar cada módulo del HUD
            SetupPlayerHealthUI(canvasGo, defaultFont);
            SetupParryCooldownUI(canvasGo, defaultFont);
            SetupUpgradeNotificationUI(canvasGo, defaultFont);
            SetupGameTimerUI(canvasGo, defaultFont);
            SetupEnergyBarUI(canvasGo, defaultFont);
            SetupLevelUpModalUI(canvasGo, defaultFont);
        }

        private static void SetupPlayerHealthUI(GameObject canvasGo, Font font)
        {
#if UNITY_2023_1_OR_NEWER
            PlayerHealthUI existing = Object.FindFirstObjectByType<PlayerHealthUI>();
#else
            PlayerHealthUI existing = Object.FindObjectOfType<PlayerHealthUI>();
#endif
            if (existing != null) return;

            // Panel contenedor en esquina superior izquierda
            GameObject healthPanel = new GameObject("PlayerHealthPanel");
            healthPanel.transform.SetParent(canvasGo.transform, false);
            RectTransform rect = healthPanel.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(30f, -30f);
            rect.sizeDelta = new Vector2(280f, 52f);

            var bg = healthPanel.AddComponent<UnityEngine.UI.Image>();
            bg.color = new Color(0.08f, 0.08f, 0.14f, 0.90f);

            // Slider de salud
            GameObject sliderGo = new GameObject("HealthSlider");
            sliderGo.transform.SetParent(healthPanel.transform, false);
            RectTransform sliderRect = sliderGo.AddComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(0f, 0f);
            sliderRect.anchorMax = new Vector2(1f, 0f);
            sliderRect.pivot = new Vector2(0.5f, 0f);
            sliderRect.anchoredPosition = new Vector2(0f, 6f);
            sliderRect.sizeDelta = new Vector2(-16f, 12f);

            var slider = sliderGo.AddComponent<UnityEngine.UI.Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.interactable = false;
            slider.transition = UnityEngine.UI.Selectable.Transition.None;
            var nav = slider.navigation;
            nav.mode = UnityEngine.UI.Navigation.Mode.None;
            slider.navigation = nav;

            var sliderBg = sliderGo.AddComponent<UnityEngine.UI.Image>();
            sliderBg.color = new Color(0.2f, 0.2f, 0.25f, 0.7f);
            sliderBg.raycastTarget = false;

            // Fill Area
            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderGo.transform, false);
            RectTransform fillAreaRect = fillArea.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = Vector2.zero;
            fillAreaRect.anchorMax = Vector2.one;
            fillAreaRect.sizeDelta = Vector2.zero;

            // Fill
            GameObject fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            RectTransform fillRect = fill.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.sizeDelta = Vector2.zero;
            var fillImg = fill.AddComponent<UnityEngine.UI.Image>();
            fillImg.color = new Color(0.2f, 0.95f, 0.4f);
            slider.fillRect = fillRect;

            // Texto de vidas y corazones
            GameObject textGo = new GameObject("LivesText");
            textGo.transform.SetParent(healthPanel.transform, false);
            RectTransform textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 0.35f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.sizeDelta = new Vector2(-16f, 0f);
            var txt = textGo.AddComponent<UnityEngine.UI.Text>();
            txt.alignment = TextAnchor.MiddleLeft;
            txt.fontSize = 15;
            txt.fontStyle = FontStyle.Bold;
            txt.font = font;
            txt.color = new Color(0.2f, 0.95f, 0.4f);
            txt.text = "VIDA: ♥ ♥ ♥ (3/3)";

            // Componente script
            var healthUI = healthPanel.AddComponent<PlayerHealthUI>();
            SerializedObject so = new SerializedObject(healthUI);
            so.FindProperty("livesText").objectReferenceValue = txt;
            so.FindProperty("healthSlider").objectReferenceValue = slider;
            so.FindProperty("fillImage").objectReferenceValue = fillImg;
            so.ApplyModifiedProperties();
        }

        private static void SetupParryCooldownUI(GameObject canvasGo, Font font)
        {
#if UNITY_2023_1_OR_NEWER
            ParryCooldownUI existing = Object.FindFirstObjectByType<ParryCooldownUI>();
#else
            ParryCooldownUI existing = Object.FindObjectOfType<ParryCooldownUI>();
#endif
            if (existing != null) return;

            // Panel contenedor centrado, compacto para HUD de combate bajo el jugador
            GameObject parryPanel = new GameObject("ParryCooldownPanel");
            parryPanel.transform.SetParent(canvasGo.transform, false);
            RectTransform rect = parryPanel.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(68f, 10f);

            var cg = parryPanel.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;

            var bg = parryPanel.AddComponent<UnityEngine.UI.Image>();
            bg.color = new Color(0.05f, 0.05f, 0.10f, 0.85f);
            bg.raycastTarget = false;

            // Slider
            GameObject sliderGo = new GameObject("ParrySlider");
            sliderGo.transform.SetParent(parryPanel.transform, false);
            RectTransform sliderRect = sliderGo.AddComponent<RectTransform>();
            sliderRect.anchorMin = Vector2.zero;
            sliderRect.anchorMax = Vector2.one;
            sliderRect.pivot = new Vector2(0.5f, 0.5f);
            sliderRect.anchoredPosition = Vector2.zero;
            sliderRect.sizeDelta = new Vector2(-4f, -4f);

            var slider = sliderGo.AddComponent<UnityEngine.UI.Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.interactable = false;
            slider.transition = UnityEngine.UI.Selectable.Transition.None;
            var nav = slider.navigation;
            nav.mode = UnityEngine.UI.Navigation.Mode.None;
            slider.navigation = nav;

            var sliderBg = sliderGo.AddComponent<UnityEngine.UI.Image>();
            sliderBg.color = new Color(0.18f, 0.18f, 0.25f, 0.6f);
            sliderBg.raycastTarget = false;

            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderGo.transform, false);
            RectTransform fillAreaRect = fillArea.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = Vector2.zero;
            fillAreaRect.anchorMax = Vector2.one;
            fillAreaRect.sizeDelta = Vector2.zero;

            GameObject fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            RectTransform fillRect = fill.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.sizeDelta = Vector2.zero;
            var fillImg = fill.AddComponent<UnityEngine.UI.Image>();
            fillImg.color = new Color(0.18f, 0.95f, 1f, 1f);
            fillImg.raycastTarget = false;
            slider.fillRect = fillRect;

            // Status Text (Encima de la barra)
            GameObject stGo = new GameObject("StatusText");
            stGo.transform.SetParent(parryPanel.transform, false);
            RectTransform stRect = stGo.AddComponent<RectTransform>();
            stRect.anchorMin = new Vector2(0.5f, 1f);
            stRect.anchorMax = new Vector2(0.5f, 1f);
            stRect.pivot = new Vector2(0.5f, 0f);
            stRect.anchoredPosition = new Vector2(0f, 2f);
            stRect.sizeDelta = new Vector2(80f, 14f);
            var stTxt = stGo.AddComponent<UnityEngine.UI.Text>();
            stTxt.alignment = TextAnchor.MiddleCenter;
            stTxt.fontSize = 10;
            stTxt.fontStyle = FontStyle.Bold;
            stTxt.font = font;
            stTxt.color = Color.white;
            stTxt.text = "⚡ LISTO";
            stTxt.raycastTarget = false;

            var parryUI = parryPanel.AddComponent<ParryCooldownUI>();
            SerializedObject parrySO = new SerializedObject(parryUI);
            parrySO.FindProperty("parrySlider").objectReferenceValue = slider;
            parrySO.FindProperty("fillImage").objectReferenceValue = fillImg;
            parrySO.FindProperty("statusText").objectReferenceValue = stTxt;
            parrySO.ApplyModifiedProperties();
        }

        private static void SetupUpgradeNotificationUI(GameObject canvasGo, Font font)
        {
#if UNITY_2023_1_OR_NEWER
            UpgradeNotificationUI existing = Object.FindFirstObjectByType<UpgradeNotificationUI>();
#else
            UpgradeNotificationUI existing = Object.FindObjectOfType<UpgradeNotificationUI>();
#endif
            if (existing != null) return;

            // 1. Banner emergente central superior
            GameObject bannerGo = new GameObject("UpgradeNotificationBanner");
            bannerGo.transform.SetParent(canvasGo.transform, false);
            RectTransform bRect = bannerGo.AddComponent<RectTransform>();
            bRect.anchorMin = new Vector2(0.5f, 1f);
            bRect.anchorMax = new Vector2(0.5f, 1f);
            bRect.pivot = new Vector2(0.5f, 1f);
            bRect.anchoredPosition = new Vector2(0f, -30f);
            bRect.sizeDelta = new Vector2(620f, 95f);

            var bg = bannerGo.AddComponent<UnityEngine.UI.Image>();
            bg.color = new Color(0.06f, 0.06f, 0.12f, 0.95f);
            var cg = bannerGo.AddComponent<CanvasGroup>();

            // Header Text
            GameObject headerGo = new GameObject("HeaderText");
            headerGo.transform.SetParent(bannerGo.transform, false);
            RectTransform hRect = headerGo.AddComponent<RectTransform>();
            hRect.anchorMin = new Vector2(0f, 0.65f);
            hRect.anchorMax = new Vector2(1f, 1f);
            hRect.sizeDelta = new Vector2(-20f, 0f);
            var hTxt = headerGo.AddComponent<UnityEngine.UI.Text>();
            hTxt.alignment = TextAnchor.MiddleCenter;
            hTxt.fontSize = 13;
            hTxt.fontStyle = FontStyle.Bold;
            hTxt.font = font;
            hTxt.color = new Color(1f, 0.85f, 0.2f);
            hTxt.text = "⚡ ¡NUEVA MEJORA SELECCIONADA! ⚡";

            // Title Text
            GameObject titleGo = new GameObject("TitleText");
            titleGo.transform.SetParent(bannerGo.transform, false);
            RectTransform tRect = titleGo.AddComponent<RectTransform>();
            tRect.anchorMin = new Vector2(0f, 0.32f);
            tRect.anchorMax = new Vector2(1f, 0.68f);
            tRect.sizeDelta = new Vector2(-20f, 0f);
            var tTxt = titleGo.AddComponent<UnityEngine.UI.Text>();
            tTxt.alignment = TextAnchor.MiddleCenter;
            tTxt.fontSize = 17;
            tTxt.fontStyle = FontStyle.Bold;
            tTxt.font = font;
            tTxt.color = Color.cyan;
            tTxt.text = "Nombre de Mejora";

            // Description Text
            GameObject descGo = new GameObject("DescText");
            descGo.transform.SetParent(bannerGo.transform, false);
            RectTransform dRect = descGo.AddComponent<RectTransform>();
            dRect.anchorMin = new Vector2(0f, 0f);
            dRect.anchorMax = new Vector2(1f, 0.35f);
            dRect.sizeDelta = new Vector2(-20f, 0f);
            var dTxt = descGo.AddComponent<UnityEngine.UI.Text>();
            dTxt.alignment = TextAnchor.MiddleCenter;
            dTxt.fontSize = 12;
            dTxt.font = font;
            dTxt.color = new Color(0.85f, 0.85f, 0.9f);
            dTxt.text = "Descripción de la mejora";

            // 2. Badge persistente en esquina superior derecha
            GameObject badgeGo = new GameObject("ActiveUpgradeBadge");
            badgeGo.transform.SetParent(canvasGo.transform, false);
            RectTransform badgeRect = badgeGo.AddComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(1f, 1f);
            badgeRect.anchorMax = new Vector2(1f, 1f);
            badgeRect.pivot = new Vector2(1f, 1f);
            badgeRect.anchoredPosition = new Vector2(-30f, -30f);
            badgeRect.sizeDelta = new Vector2(340f, 40f);

            var badgeBg = badgeGo.AddComponent<UnityEngine.UI.Image>();
            badgeBg.color = new Color(0.08f, 0.08f, 0.14f, 0.85f);

            GameObject badgeTextGo = new GameObject("BadgeText");
            badgeTextGo.transform.SetParent(badgeGo.transform, false);
            RectTransform btRect = badgeTextGo.AddComponent<RectTransform>();
            btRect.anchorMin = Vector2.zero;
            btRect.anchorMax = Vector2.one;
            btRect.sizeDelta = new Vector2(-16f, 0f);
            var pTxt = badgeTextGo.AddComponent<UnityEngine.UI.Text>();
            pTxt.alignment = TextAnchor.MiddleRight;
            pTxt.fontSize = 13;
            pTxt.font = font;
            pTxt.color = Color.white;
            pTxt.text = "MEJORAS: Ninguna activa todavía";

            // Componente script
            var notifUI = bannerGo.AddComponent<UpgradeNotificationUI>();
            SerializedObject so = new SerializedObject(notifUI);
            so.FindProperty("bannerCanvasGroup").objectReferenceValue = cg;
            so.FindProperty("headerText").objectReferenceValue = hTxt;
            so.FindProperty("upgradeTitleText").objectReferenceValue = tTxt;
            so.FindProperty("descriptionText").objectReferenceValue = dTxt;
            so.FindProperty("persistentStatusText").objectReferenceValue = pTxt;
            so.ApplyModifiedProperties();
        }

        private static void SetupGameTimerUI(GameObject canvasGo, Font font)
        {
#if UNITY_2023_1_OR_NEWER
            GameTimerUI existing = Object.FindFirstObjectByType<GameTimerUI>();
#else
            GameTimerUI existing = Object.FindObjectOfType<GameTimerUI>();
#endif
            if (existing != null) return;

            // Panel contenedor en la parte superior central de la pantalla
            GameObject timerPanel = new GameObject("GameTimerPanel");
            timerPanel.transform.SetParent(canvasGo.transform, false);
            RectTransform rect = timerPanel.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -25f);
            rect.sizeDelta = new Vector2(170f, 52f);

            var bg = timerPanel.AddComponent<UnityEngine.UI.Image>();
            bg.color = new Color(0.06f, 0.08f, 0.14f, 0.90f);

            // Borde superior sutil brillante (decorativo cian)
            GameObject lineGo = new GameObject("TopGlowLine");
            lineGo.transform.SetParent(timerPanel.transform, false);
            RectTransform lineRect = lineGo.AddComponent<RectTransform>();
            lineRect.anchorMin = new Vector2(0f, 1f);
            lineRect.anchorMax = new Vector2(1f, 1f);
            lineRect.pivot = new Vector2(0.5f, 1f);
            lineRect.anchoredPosition = Vector2.zero;
            lineRect.sizeDelta = new Vector2(0f, 2f);
            var lineImg = lineGo.AddComponent<UnityEngine.UI.Image>();
            lineImg.color = new Color(0.2f, 0.85f, 1f, 0.8f);

            // Texto de subtítulo (TIEMPO / DIFICULTAD)
            GameObject badgeGo = new GameObject("MinuteBadgeText");
            badgeGo.transform.SetParent(timerPanel.transform, false);
            RectTransform badgeRect = badgeGo.AddComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(0f, 1f);
            badgeRect.anchorMax = new Vector2(1f, 1f);
            badgeRect.pivot = new Vector2(0.5f, 1f);
            badgeRect.anchoredPosition = new Vector2(0f, -6f);
            badgeRect.sizeDelta = new Vector2(-10f, 14f);

            var badgeTxt = badgeGo.AddComponent<UnityEngine.UI.Text>();
            badgeTxt.alignment = TextAnchor.MiddleCenter;
            badgeTxt.fontSize = 10;
            badgeTxt.fontStyle = FontStyle.Bold;
            badgeTxt.font = font;
            badgeTxt.color = new Color(0.55f, 0.75f, 0.95f, 0.9f);
            badgeTxt.text = "TIEMPO";

            // Texto principal del cronómetro (00:00)
            GameObject textGo = new GameObject("TimerText");
            textGo.transform.SetParent(timerPanel.transform, false);
            RectTransform tRect = textGo.AddComponent<RectTransform>();
            tRect.anchorMin = new Vector2(0f, 0f);
            tRect.anchorMax = new Vector2(1f, 1f);
            tRect.offsetMin = new Vector2(5f, 4f);
            tRect.offsetMax = new Vector2(-5f, -16f);

            var txt = textGo.AddComponent<UnityEngine.UI.Text>();
            txt.alignment = TextAnchor.MiddleCenter;
            txt.fontSize = 26;
            txt.fontStyle = FontStyle.Bold;
            txt.font = font;
            txt.color = new Color(1f, 0.95f, 0.4f);
            txt.text = "00:00";

            // Componente GameTimerUI
            var timerUI = timerPanel.AddComponent<GameTimerUI>();
            timerUI.TimeText = txt;
            timerUI.MinuteBadgeText = badgeTxt;

            Undo.RegisterCreatedObjectUndo(timerPanel, "Crear GameTimerUI");
        }

        private static void SetupEnergyBarUI(GameObject canvasGo, Font font)
        {
#if UNITY_2023_1_OR_NEWER
            EnergyBarUI existing = Object.FindFirstObjectByType<EnergyBarUI>();
#else
            EnergyBarUI existing = Object.FindObjectOfType<EnergyBarUI>();
#endif
            if (existing != null) return;

            // Panel contenedor en la parte inferior centrada
            GameObject barContainer = new GameObject("EnergyBarPanel");
            barContainer.transform.SetParent(canvasGo.transform, false);
            RectTransform rect = barContainer.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 35f);
            rect.sizeDelta = new Vector2(500f, 38f);

            var bgImage = barContainer.AddComponent<UnityEngine.UI.Image>();
            bgImage.color = new Color(0.08f, 0.08f, 0.14f, 0.88f);

            var slider = barContainer.AddComponent<UnityEngine.UI.Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.interactable = false;
            slider.transition = UnityEngine.UI.Selectable.Transition.None;
            var nav = slider.navigation;
            nav.mode = UnityEngine.UI.Navigation.Mode.None;
            slider.navigation = nav;

            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(barContainer.transform, false);
            RectTransform fillAreaRect = fillArea.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0f, 0f);
            fillAreaRect.anchorMax = new Vector2(1f, 1f);
            fillAreaRect.sizeDelta = new Vector2(-8f, -8f);

            GameObject fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            RectTransform fillRect = fill.AddComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.sizeDelta = Vector2.zero;
            var fillImg = fill.AddComponent<UnityEngine.UI.Image>();
            fillImg.color = new Color(0.15f, 0.75f, 1f);

            slider.fillRect = fillRect;

            GameObject textGo = new GameObject("StatusText");
            textGo.transform.SetParent(barContainer.transform, false);
            RectTransform textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            var txt = textGo.AddComponent<UnityEngine.UI.Text>();
            txt.alignment = TextAnchor.MiddleCenter;
            txt.fontSize = 15;
            txt.font = font;
            txt.color = Color.white;
            txt.text = "ENERGÍA: 0%";

            var uiScript = barContainer.AddComponent<EnergyBarUI>();
            SerializedObject so = new SerializedObject(uiScript);
            so.FindProperty("energySlider").objectReferenceValue = slider;
            so.FindProperty("fillImage").objectReferenceValue = fillImg;
            so.FindProperty("statusText").objectReferenceValue = txt;
            so.ApplyModifiedProperties();
        }

        [MenuItem("Tools/Arena Survivor/3. Actualizar Modal de Mejoras a 3 Opciones")]
        public static void ForceRebuildLevelUpModal()
        {
#if UNITY_2023_1_OR_NEWER
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
#else
            Canvas canvas = Object.FindObjectOfType<Canvas>();
#endif
            if (canvas == null)
            {
                Debug.LogError("[ArenaPrototypeSetup] No se encontró un Canvas en la escena activa.");
                return;
            }

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

#if UNITY_2023_1_OR_NEWER
            var oldPanels = Object.FindObjectsByType<LevelUpUIManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var oldPanels = Resources.FindObjectsOfTypeAll<LevelUpUIManager>();
#endif
            foreach (var p in oldPanels)
            {
                if (p != null && p.gameObject != null && !string.IsNullOrEmpty(p.gameObject.scene.name))
                {
                    Undo.DestroyObjectImmediate(p.gameObject);
                }
            }

            SetupLevelUpModalUI(canvas.gameObject, font);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            Debug.Log("<color=green><b>[ArenaPrototypeSetup]</b> ¡Modal de Mejoras actualizado con 3 opciones simétricas + Inventario!</color>");
        }

        private static void SetupLevelUpModalUI(GameObject canvasGo, Font font)
        {
#if UNITY_2023_1_OR_NEWER
            LevelUpUIManager existing = Object.FindFirstObjectByType<LevelUpUIManager>();
#else
            LevelUpUIManager existing = Object.FindObjectOfType<LevelUpUIManager>();
#endif
            if (existing != null)
            {
                SerializedObject existingSO = new SerializedObject(existing);
                var opt3Prop = existingSO.FindProperty("option3Button");
                if (opt3Prop != null && opt3Prop.objectReferenceValue != null)
                {
                    return; // Ya configurado con 3 opciones
                }
                // Si solo tenía 2 opciones, destruir panel obsoleto para regenerarlo con 3 opciones
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            // 1. Panel de Fondo a pantalla completa
            GameObject modalPanel = new GameObject("LevelUpModalPanel");
            modalPanel.transform.SetParent(canvasGo.transform, false);
            RectTransform modalRect = modalPanel.AddComponent<RectTransform>();
            modalRect.anchorMin = Vector2.zero;
            modalRect.anchorMax = Vector2.one;
            modalRect.sizeDelta = Vector2.zero;

            var bgImage = modalPanel.AddComponent<UnityEngine.UI.Image>();
            bgImage.color = new Color(0.04f, 0.04f, 0.08f, 0.94f);
            var cg = modalPanel.AddComponent<CanvasGroup>();

            // 2. Encabezado
            GameObject headerGo = new GameObject("ModalTitle");
            headerGo.transform.SetParent(modalPanel.transform, false);
            RectTransform hRect = headerGo.AddComponent<RectTransform>();
            hRect.anchorMin = new Vector2(0.5f, 1f);
            hRect.anchorMax = new Vector2(0.5f, 1f);
            hRect.pivot = new Vector2(0.5f, 1f);
            hRect.anchoredPosition = new Vector2(0f, -40f);
            hRect.sizeDelta = new Vector2(800f, 65f);

            var titleTxt = headerGo.AddComponent<UnityEngine.UI.Text>();
            titleTxt.alignment = TextAnchor.MiddleCenter;
            titleTxt.fontSize = 24;
            titleTxt.fontStyle = FontStyle.Bold;
            titleTxt.font = font;
            titleTxt.color = new Color(1f, 0.85f, 0.2f);
            titleTxt.text = "⚡ PROTOCOLO DE EXTRACCIÓN COMPLETADO ⚡\n<size=15><color=#A0D8EF>SELECCIONA UNA MEJORA PARA REANUDAR EL COMBATE</color></size>\n<size=13><color=#FFFF88>(Haz clic en una opción o presiona [1] / [2] / [3] en el teclado)</color></size>";

            // 3. Tarjetas de Opciones (3 columnas simétricas)
            var card1 = CreateOptionCard(modalPanel, "Option1_Card", new Vector2(-390f, -20f), font);
            var card2 = CreateOptionCard(modalPanel, "Option2_Card", new Vector2(-130f, -20f), font);
            var card3 = CreateOptionCard(modalPanel, "Option3_Card", new Vector2(130f, -20f), font);

            // 4. Panel de Inventario Activo (Lateral Derecho)
            GameObject invPanel = new GameObject("ActiveInventoryPanel");
            invPanel.transform.SetParent(modalPanel.transform, false);
            RectTransform invRect = invPanel.AddComponent<RectTransform>();
            invRect.anchorMin = new Vector2(0.5f, 0.5f);
            invRect.anchorMax = new Vector2(0.5f, 0.5f);
            invRect.pivot = new Vector2(0.5f, 0.5f);
            invRect.anchoredPosition = new Vector2(390f, -20f);
            invRect.sizeDelta = new Vector2(245f, 380f);

            var invBg = invPanel.AddComponent<UnityEngine.UI.Image>();
            invBg.color = new Color(0.08f, 0.08f, 0.14f, 0.90f);

            // Título Inventario
            GameObject invTitleGo = new GameObject("InventoryTitle");
            invTitleGo.transform.SetParent(invPanel.transform, false);
            RectTransform itRect = invTitleGo.AddComponent<RectTransform>();
            itRect.anchorMin = new Vector2(0f, 1f);
            itRect.anchorMax = new Vector2(1f, 1f);
            itRect.pivot = new Vector2(0.5f, 1f);
            itRect.anchoredPosition = new Vector2(0f, -10f);
            itRect.sizeDelta = new Vector2(-20f, 30f);
            var itTxt = invTitleGo.AddComponent<UnityEngine.UI.Text>();
            itTxt.alignment = TextAnchor.MiddleCenter;
            itTxt.fontSize = 15;
            itTxt.fontStyle = FontStyle.Bold;
            itTxt.font = font;
            itTxt.color = Color.cyan;
            itTxt.text = "INVENTARIO ACTIVO";

            // Contenedor con VerticalLayoutGroup
            GameObject invListGo = new GameObject("InventoryList");
            invListGo.transform.SetParent(invPanel.transform, false);
            RectTransform listRect = invListGo.AddComponent<RectTransform>();
            listRect.anchorMin = new Vector2(0f, 0f);
            listRect.anchorMax = new Vector2(1f, 1f);
            listRect.offsetMin = new Vector2(12f, 12f);
            listRect.offsetMax = new Vector2(-12f, -45f);

            var vlg = invListGo.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            vlg.spacing = 6f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // Template Item
            GameObject templateGo = new GameObject("InventoryItemTemplate");
            templateGo.transform.SetParent(invListGo.transform, false);
            RectTransform tmplRect = templateGo.AddComponent<RectTransform>();
            tmplRect.sizeDelta = new Vector2(0f, 26f);
            var tmplBg = templateGo.AddComponent<UnityEngine.UI.Image>();
            tmplBg.color = new Color(0.14f, 0.14f, 0.22f, 0.85f);

            GameObject tmplTextGo = new GameObject("Text");
            tmplTextGo.transform.SetParent(templateGo.transform, false);
            RectTransform ttRect = tmplTextGo.AddComponent<RectTransform>();
            ttRect.anchorMin = Vector2.zero;
            ttRect.anchorMax = Vector2.one;
            ttRect.offsetMin = new Vector2(8f, 0f);
            ttRect.offsetMax = new Vector2(-8f, 0f);
            var ttTxt = tmplTextGo.AddComponent<UnityEngine.UI.Text>();
            ttTxt.alignment = TextAnchor.MiddleLeft;
            ttTxt.fontSize = 12;
            ttTxt.font = font;
            ttTxt.color = Color.white;
            ttTxt.text = "Mejora - Lvl 1";
            templateGo.SetActive(false);

            // 6. Configurar script LevelUpUIManager
            var levelUpUI = modalPanel.AddComponent<LevelUpUIManager>();
            SerializedObject so = new SerializedObject(levelUpUI);
            so.FindProperty("modalPanel").objectReferenceValue = modalPanel;
            so.FindProperty("modalCanvasGroup").objectReferenceValue = cg;

            // Opción 1
            so.FindProperty("option1Button").objectReferenceValue = card1.button;
            so.FindProperty("option1TitleText").objectReferenceValue = card1.titleText;
            so.FindProperty("option1LevelText").objectReferenceValue = card1.levelText;
            so.FindProperty("option1TypeText").objectReferenceValue = card1.typeText;
            so.FindProperty("option1DescText").objectReferenceValue = card1.descText;
            so.FindProperty("option1CardImage").objectReferenceValue = card1.cardImage;

            // Opción 2
            so.FindProperty("option2Button").objectReferenceValue = card2.button;
            so.FindProperty("option2TitleText").objectReferenceValue = card2.titleText;
            so.FindProperty("option2LevelText").objectReferenceValue = card2.levelText;
            so.FindProperty("option2TypeText").objectReferenceValue = card2.typeText;
            so.FindProperty("option2DescText").objectReferenceValue = card2.descText;
            so.FindProperty("option2CardImage").objectReferenceValue = card2.cardImage;

            // Opción 3
            so.FindProperty("option3Button").objectReferenceValue = card3.button;
            so.FindProperty("option3TitleText").objectReferenceValue = card3.titleText;
            so.FindProperty("option3LevelText").objectReferenceValue = card3.levelText;
            so.FindProperty("option3TypeText").objectReferenceValue = card3.typeText;
            so.FindProperty("option3DescText").objectReferenceValue = card3.descText;
            so.FindProperty("option3CardImage").objectReferenceValue = card3.cardImage;

            // Inventario
            so.FindProperty("inventoryContainer").objectReferenceValue = invListGo.transform;
            so.FindProperty("inventoryItemTemplate").objectReferenceValue = templateGo;

            so.ApplyModifiedProperties();

            // Ocultar modal al inicio mediante alpha (mantiene el GameObject activo para que Awake e Instance inicialicen)
            cg.alpha = 0f;
            cg.interactable = false;
            cg.blocksRaycasts = false;
        }

        private struct OptionCardRefs
        {
            public UnityEngine.UI.Button button;
            public UnityEngine.UI.Image cardImage;
            public UnityEngine.UI.Text titleText;
            public UnityEngine.UI.Text levelText;
            public UnityEngine.UI.Text typeText;
            public UnityEngine.UI.Text descText;
        }

        private static OptionCardRefs CreateOptionCard(GameObject parent, string name, Vector2 pos, Font font)
        {
            OptionCardRefs refs = new OptionCardRefs();

            GameObject cardGo = new GameObject(name);
            cardGo.transform.SetParent(parent.transform, false);
            RectTransform cardRect = cardGo.AddComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.anchoredPosition = pos;
            cardRect.sizeDelta = new Vector2(245f, 380f);

            refs.cardImage = cardGo.AddComponent<UnityEngine.UI.Image>();
            refs.cardImage.color = new Color(0.1f, 0.12f, 0.18f, 0.95f);

            refs.button = cardGo.AddComponent<UnityEngine.UI.Button>();
            refs.button.targetGraphic = refs.cardImage;

            // Type Tag
            GameObject typeGo = new GameObject("TypeText");
            typeGo.transform.SetParent(cardGo.transform, false);
            RectTransform tyRect = typeGo.AddComponent<RectTransform>();
            tyRect.anchorMin = new Vector2(0f, 1f);
            tyRect.anchorMax = new Vector2(1f, 1f);
            tyRect.pivot = new Vector2(0.5f, 1f);
            tyRect.anchoredPosition = new Vector2(0f, -15f);
            tyRect.sizeDelta = new Vector2(-24f, 24f);
            refs.typeText = typeGo.AddComponent<UnityEngine.UI.Text>();
            refs.typeText.alignment = TextAnchor.MiddleCenter;
            refs.typeText.fontSize = 11;
            refs.typeText.fontStyle = FontStyle.Bold;
            refs.typeText.font = font;
            refs.typeText.color = Color.cyan;
            refs.typeText.text = "[STATMODIFIER]";

            // Title Text
            GameObject titleGo = new GameObject("TitleText");
            titleGo.transform.SetParent(cardGo.transform, false);
            RectTransform tRect = titleGo.AddComponent<RectTransform>();
            tRect.anchorMin = new Vector2(0f, 1f);
            tRect.anchorMax = new Vector2(1f, 1f);
            tRect.pivot = new Vector2(0.5f, 1f);
            tRect.anchoredPosition = new Vector2(0f, -45f);
            tRect.sizeDelta = new Vector2(-24f, 60f);
            refs.titleText = titleGo.AddComponent<UnityEngine.UI.Text>();
            refs.titleText.alignment = TextAnchor.MiddleCenter;
            refs.titleText.fontSize = 18;
            refs.titleText.fontStyle = FontStyle.Bold;
            refs.titleText.font = font;
            refs.titleText.color = Color.white;
            refs.titleText.text = "Nombre de Mejora";

            // Level Text
            GameObject lvlGo = new GameObject("LevelText");
            lvlGo.transform.SetParent(cardGo.transform, false);
            RectTransform lRect = lvlGo.AddComponent<RectTransform>();
            lRect.anchorMin = new Vector2(0f, 1f);
            lRect.anchorMax = new Vector2(1f, 1f);
            lRect.pivot = new Vector2(0.5f, 1f);
            lRect.anchoredPosition = new Vector2(0f, -110f);
            lRect.sizeDelta = new Vector2(-24f, 26f);
            refs.levelText = lvlGo.AddComponent<UnityEngine.UI.Text>();
            refs.levelText.alignment = TextAnchor.MiddleCenter;
            refs.levelText.fontSize = 13;
            refs.levelText.fontStyle = FontStyle.Bold;
            refs.levelText.font = font;
            refs.levelText.color = new Color(1f, 0.85f, 0.2f);
            refs.levelText.text = "NIVEL 1 / 5";

            // Desc Text
            GameObject descGo = new GameObject("DescText");
            descGo.transform.SetParent(cardGo.transform, false);
            RectTransform dRect = descGo.AddComponent<RectTransform>();
            dRect.anchorMin = new Vector2(0f, 0f);
            dRect.anchorMax = new Vector2(1f, 1f);
            dRect.offsetMin = new Vector2(16f, 65f);
            dRect.offsetMax = new Vector2(-16f, -145f);
            refs.descText = descGo.AddComponent<UnityEngine.UI.Text>();
            refs.descText.alignment = TextAnchor.MiddleCenter;
            refs.descText.fontSize = 13;
            refs.descText.font = font;
            refs.descText.color = new Color(0.9f, 0.9f, 0.95f);
            refs.descText.text = "Descripción del efecto.";

            // Action Button Badge
            GameObject selectBtnGo = new GameObject("SelectBadge");
            selectBtnGo.transform.SetParent(cardGo.transform, false);
            RectTransform sbRect = selectBtnGo.AddComponent<RectTransform>();
            sbRect.anchorMin = new Vector2(0f, 0f);
            sbRect.anchorMax = new Vector2(1f, 0f);
            sbRect.pivot = new Vector2(0.5f, 0f);
            sbRect.anchoredPosition = new Vector2(0f, 16f);
            sbRect.sizeDelta = new Vector2(-32f, 36f);
            var sbBg = selectBtnGo.AddComponent<UnityEngine.UI.Image>();
            sbBg.color = new Color(0.2f, 0.8f, 1f, 0.35f);

            GameObject sbTextGo = new GameObject("Text");
            sbTextGo.transform.SetParent(selectBtnGo.transform, false);
            RectTransform sbtRect = sbTextGo.AddComponent<RectTransform>();
            sbtRect.anchorMin = Vector2.zero;
            sbtRect.anchorMax = Vector2.one;
            sbtRect.sizeDelta = Vector2.zero;
            var sbtTxt = sbTextGo.AddComponent<UnityEngine.UI.Text>();
            sbtTxt.alignment = TextAnchor.MiddleCenter;
            sbtTxt.fontSize = 14;
            sbtTxt.fontStyle = FontStyle.Bold;
            sbtTxt.font = font;
            sbtTxt.color = Color.white;
            sbtTxt.text = "ELEGIR MEJORA";

            // Deshabilitar raycast en elementos decorativos hijos para no obstruir el clic de la tarjeta
            refs.typeText.raycastTarget = false;
            refs.titleText.raycastTarget = false;
            refs.levelText.raycastTarget = false;
            refs.descText.raycastTarget = false;
            sbBg.raycastTarget = false;
            sbtTxt.raycastTarget = false;

            return refs;
        }

        private static GameObject CreateShockwavePrefab(Sprite ringSprite)
        {
            string prefabPath = $"{PrefabsFolder}/ShockwaveVFX.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null) return existing;

            GameObject go = new GameObject("ShockwaveVFX");
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = ringSprite;
            sr.color = Color.white;
            sr.sortingOrder = 7;

            go.AddComponent<ShockwaveEffect>();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static Sprite GetOrCreateRingSprite()
        {
            string path = $"{SpritesFolder}/Ring.png";
            if (File.Exists(path)) return AssetDatabase.LoadAssetAtPath<Sprite>(path);

            int size = 128;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float outerRadius = size * 0.48f;
            float innerRadius = size * 0.38f;
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    if (dist <= outerRadius && dist >= innerRadius)
                    {
                        float edge1 = Mathf.Clamp01(dist - innerRadius);
                        float edge2 = Mathf.Clamp01(outerRadius - dist);
                        float alpha = Mathf.Min(edge1, edge2);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                    }
                    else
                    {
                        tex.SetPixel(x, y, Color.clear);
                    }
                }
            }

            tex.Apply();
            SaveTextureAsPNG(tex, path);
            SetTextureAsSprite(path);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void EnsurePlayerVFXSetup(PlayerController player, Sprite ringSprite)
        {
            if (player == null) return;

            Transform ringChild = player.transform.Find("TensionRing");
            GameObject tensionRing;
            if (ringChild == null)
            {
                tensionRing = new GameObject("TensionRing");
                tensionRing.transform.SetParent(player.transform, false);
                tensionRing.transform.localPosition = Vector3.zero;
                var sr = tensionRing.AddComponent<SpriteRenderer>();
                sr.sprite = ringSprite;
                sr.color = new Color(0.3f, 0.95f, 1f, 0.9f);
                sr.sortingOrder = 6;
                tensionRing.SetActive(false);
                Undo.RegisterCreatedObjectUndo(tensionRing, "Crear TensionRing");
            }
            else
            {
                tensionRing = ringChild.gameObject;
            }

            if (!player.TryGetComponent<PlayerVFXController>(out var vfx))
            {
                vfx = player.gameObject.AddComponent<PlayerVFXController>();
                Undo.RegisterCreatedObjectUndo(vfx, "Agregar PlayerVFXController");
            }

            SerializedObject vfxSO = new SerializedObject(vfx);
            vfxSO.FindProperty("player").objectReferenceValue = player;
            vfxSO.FindProperty("playerSpriteRenderer").objectReferenceValue = player.GetComponent<SpriteRenderer>();
            vfxSO.FindProperty("tensionRingObject").objectReferenceValue = tensionRing;
            vfxSO.FindProperty("shockwavePoolTag").stringValue = "ShockwaveVFX";
            vfxSO.ApplyModifiedProperties();
        }
    }
}
#endif
