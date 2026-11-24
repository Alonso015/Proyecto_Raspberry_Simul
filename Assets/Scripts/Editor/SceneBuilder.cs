using System.Linq;
using ImatPiSim.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ImatPiSim.EditorTools
{
    /// <summary>
    /// Crea la escena principal (<c>Assets/Scenes/Main.unity</c>): una cámara ortográfica 2D y el
    /// objeto "Simulador" con <see cref="SimulationRunner"/> y <see cref="BoardView"/>.
    /// </summary>
    /// <remarks>
    /// Se usa desde el menú <b>IMAT Pi → Crear escena principal</b> o en modo batch:
    /// <c>Unity -batchmode -quit -projectPath . -executeMethod ImatPiSim.EditorTools.SceneBuilder.BuildMainScene</c>.
    /// Así la escena se puede regenerar sin editar su YAML a mano.
    /// </remarks>
    public static class SceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("IMAT Pi/Crear escena principal")]
        public static void BuildMainScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cam = new GameObject("Main Camera");
            cam.tag = "MainCamera";
            var camera = cam.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 4.5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.11f, 0.12f, 0.14f);
            cam.transform.position = new Vector3(0, 0, -10);

            var sim = new GameObject("Simulador");
            sim.AddComponent<SimulationRunner>();
            sim.AddComponent<BoardView>();

            if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log($"[IMAT Pi] Escena creada en {ScenePath}");
        }
    }
}
