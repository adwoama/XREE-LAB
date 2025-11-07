using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor utility to save the currently selected GameObject as a prefab under Assets/Prefabs.
/// Use this to create a prefab for the waveform panel after you configure it in the scene.
/// </summary>
public static class SaveSelectedAsWaveformPrefab
{
    [MenuItem("XREE/Save Selected as Waveform Panel Prefab")]
    public static void SaveSelectedPrefab()
    {
        var go = Selection.activeGameObject;
        if (go == null)
        {
            Debug.LogWarning("No GameObject selected. Select the panel GameObject in the Hierarchy before running this.");
            return;
        }

        // Ensure folder exists
        string prefabFolder = "Assets/Prefabs";
        if (!AssetDatabase.IsValidFolder(prefabFolder))
        {
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        }

        string safeName = go.name.Replace(" ", "_");
        string path = System.IO.Path.Combine(prefabFolder, safeName + ".prefab").Replace("\\", "/");

        // Save as prefab asset (overwrites if exists)
        var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(go, path, InteractionMode.UserAction);
        if (prefab != null)
        {
            Debug.Log($"Saved prefab: {path}");
            // Select the created prefab asset
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Selection.activeObject = asset;
        }
        else
        {
            Debug.LogWarning($"Failed to save prefab at {path}");
        }
    }
}
