using UnityEditor;
using UnityEditor.SceneManagement;

// Builds the mission once if it doesn't exist yet, and makes the Play button always start the mission scene.
[InitializeOnLoad]
public static class MissionAutoSetup
{
    const string ScenePath = "Assets/_Game/Scenes/Mission_PowerPlant.unity";

    static MissionAutoSetup()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) LevelBuilder.Build();
            UsePlayModeStartScene();
        };
    }

    public static void UsePlayModeStartScene()
    {
        var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        if (scene != null) EditorSceneManager.playModeStartScene = scene;
    }
}
