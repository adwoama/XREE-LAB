using System.Linq;
using UnityEditor;
using UnityEngine;

// Simple Editor utility to find Materials that reference legacy Meta shaders
// and replace them with URP-compatible shader variants. This operates only
// on assets under the Assets/ folder (does not modify package cache files).
// Usage: Window -> Shader Fixer -> Replace Meta/Lit -> URP Lit

public static class ShaderCompatibilityFix
{
    private const string MetaLitName = "Meta/Lit";
    private const string MetaLitTransparentName = "Meta/Lit Transparent";
    private const string UrpLitName = "Universal Render Pipeline/Lit";

    [MenuItem("Tools/Shader Fixer/Replace Meta/Lit -> URP Lit (Assets)")]
    public static void ReplaceMetaLitInAssets()
    {
        var urp = Shader.Find(UrpLitName);
        if (urp == null)
        {
            Debug.LogError($"Could not find URP shader '{UrpLitName}'. Make sure Universal RP package is installed and a URP shader with that name exists.");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
        int replaced = 0;

        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;

            var sname = mat.shader != null ? mat.shader.name : "";
            if (sname == MetaLitName || sname == MetaLitTransparentName || sname.Contains("Meta/Lit"))
            {
                Undo.RecordObject(mat, "Replace Meta shader with URP Lit");
                mat.shader = urp;
                EditorUtility.SetDirty(mat);
                replaced++;
            }
        }

        if (replaced > 0)
        {
            AssetDatabase.SaveAssets();
            Debug.Log($"ShaderCompatibilityFix: Replaced {replaced} materials to '{UrpLitName}' in Assets/.");
        }
        else
        {
            Debug.Log("ShaderCompatibilityFix: No materials referencing 'Meta/Lit' were found under Assets/.");
        }
    }

    [MenuItem("Tools/Shader Fixer/Scan for Meta/Lit usages (Assets)")]
    public static void ScanForMetaLit()
    {
        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
        int found = 0;

        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;
            var sname = mat.shader != null ? mat.shader.name : "";
            if (sname == MetaLitName || sname == MetaLitTransparentName || sname.Contains("Meta/Lit"))
            {
                found++;
                Debug.Log($"Found Material: {path} (shader: {sname})");
            }
        }

        Debug.Log($"ShaderCompatibilityFix: Scan complete. Found {found} materials referencing Meta/Lit under Assets/.");
    }
}
