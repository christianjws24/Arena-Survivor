#if UNITY_EDITOR
using System.IO;
using System.Collections.Generic;
using ArenaSurvivor.Managers;
using ArenaSurvivor.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArenaSurvivor.Editor
{
    /// <summary>
    /// Herramienta de Editor que automatiza la creación de la escena del Menú Principal (MainMenu.unity)
    /// y la configuración de las escenas en Build Settings (Índice 0: MainMenu, Índice 1: SampleScene).
    /// Ejecutable desde: Tools > Arena Survivor > 4. Crear Escena de Menú Principal y Configurar Build Settings.
    /// </summary>
    public static class MainMenuSceneBuilder
    {
        private const string ScenesFolder = "Assets/Scenes";
        private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
        private const string GameplayScenePath = "Assets/Scenes/SampleScene.unity";

        [MenuItem("Tools/Arena Survivor/4. Crear Escena de Menú Principal y Configurar Build Settings")]
        public static void CreateMainMenuSceneAndConfigureBuildSettings()
        {
            if (!Directory.Exists(ScenesFolder))
            {
                Directory.CreateDirectory(ScenesFolder);
            }

            Scene currentActiveScene = EditorSceneManager.GetActiveScene();
            string currentPath = currentActiveScene.path;

            // 1. Crear escena MainMenu si no existe
            bool sceneExists = File.Exists(MainMenuScenePath);
            if (!sceneExists)
            {
                // Guardar la escena actual antes de crear una nueva
                if (currentActiveScene.isDirty)
                {
                    EditorSceneManager.SaveScene(currentActiveScene);
                }

                Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                // Configurar Cámara 2D
                GameObject camGo = new GameObject("Main Camera");
                Camera cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = 5f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.03f, 0.04f, 0.07f);
                cam.depth = -1f;
                camGo.AddComponent<AudioListener>();
                camGo.transform.position = new Vector3(0f, 0f, -10f);

                // Configurar AudioManager (Audio y SFX procedurales)
                GameObject audioGo = new GameObject("AudioManager");
                audioGo.AddComponent<AudioManager>();

                // Configurar MainMenuManager (Controlador de UI con auto-generación procedural)
                GameObject menuGo = new GameObject("MainMenuManager");
                menuGo.AddComponent<MainMenuManager>();

                // Guardar la escena
                EditorSceneManager.SaveScene(newScene, MainMenuScenePath);
                Debug.Log($"<color=lime><b>[MainMenuSceneBuilder]</b> Escena creada exitosamente en: {MainMenuScenePath}</color>");

                // Si estábamos en SampleScene, podemos reabrirla
                if (!string.IsNullOrEmpty(currentPath) && currentPath != MainMenuScenePath && File.Exists(currentPath))
                {
                    EditorSceneManager.OpenScene(currentPath, OpenSceneMode.Single);
                }
            }
            else
            {
                Debug.Log($"<color=cyan><b>[MainMenuSceneBuilder]</b> La escena {MainMenuScenePath} ya existe.</color>");
            }

            // 2. Configurar EditorBuildSettings (Índice 0: MainMenu, Índice 1: SampleScene)
            ConfigureBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [InitializeOnLoadMethod]
        private static void OnEditorLoad()
        {
            // Verificación automática en segundo plano al cargar el proyecto
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(MainMenuScenePath))
                {
                    CreateMainMenuSceneAndConfigureBuildSettings();
                }
                else
                {
                    ConfigureBuildSettings();
                }
            };
        }

        public static void ConfigureBuildSettings()
        {
            var buildScenes = new List<EditorBuildSettingsScene>();

            // Escena 0: MainMenu
            if (File.Exists(MainMenuScenePath))
            {
                buildScenes.Add(new EditorBuildSettingsScene(MainMenuScenePath, true));
            }

            // Escena 1: SampleScene (o escena principal de juego)
            if (File.Exists(GameplayScenePath))
            {
                buildScenes.Add(new EditorBuildSettingsScene(GameplayScenePath, true));
            }

            // Agregar cualquier otra escena existente en Assets/Scenes
            string[] allSceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { ScenesFolder });
            foreach (string guid in allSceneGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path != MainMenuScenePath && path != GameplayScenePath)
                {
                    buildScenes.Add(new EditorBuildSettingsScene(path, true));
                }
            }

            EditorBuildSettings.scenes = buildScenes.ToArray();
            Debug.Log($"<color=cyan><b>[BuildSettings]</b> Escenas configuradas para compilación:</color>\n" +
                      $"  [0] {MainMenuScenePath}\n" +
                      $"  [1] {GameplayScenePath}");
        }
    }
}
#endif
