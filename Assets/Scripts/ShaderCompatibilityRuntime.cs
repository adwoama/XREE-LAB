using System.Collections.Generic;
using UnityEngine;

// Runtime helper: on startup replace any renderer materials using Meta shaders
// with URP-compatible shader instances to avoid incompatible keyword-space
// errors when the render pipeline is URP. This only affects in-scene instances
// (it creates material instances per-renderer) and does not modify assets.

[DefaultExecutionOrder(-1000)]
public class ShaderCompatibilityRuntime : MonoBehaviour
{
    private const string MetaLitName = "Meta/Lit";
    private const string MetaLitTransparentName = "Meta/Lit Transparent";
    private const string UrpLitName = "Universal Render Pipeline/Lit";

    void Awake()
    {
        TryReplaceMaterials();
    }

    private void TryReplaceMaterials()
    {
        var urp = Shader.Find(UrpLitName);
        if (urp == null)
        {
            Debug.LogWarning($"ShaderCompatibilityRuntime: URP shader '{UrpLitName}' not found. Skipping runtime replacement.");
            return;
        }

        var renderers = FindObjectsOfType<Renderer>(true);
        int totalReplaced = 0;

        foreach (var r in renderers)
        {
            var mats = r.materials; // instance materials
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || m.shader == null) continue;
                var sname = m.shader.name;
                if (sname == MetaLitName || sname == MetaLitTransparentName || sname.Contains("Meta/Lit"))
                {
                    // create a new material instance so we don't mutate shared assets at runtime
                    var newMat = new Material(m);
                    newMat.shader = urp;
                    mats[i] = newMat;
                    changed = true;
                    totalReplaced++;
                }
            }
            if (changed)
            {
                r.materials = mats;
            }
        }

        if (totalReplaced > 0)
            Debug.Log($"ShaderCompatibilityRuntime: Replaced {totalReplaced} material instances to '{UrpLitName}' at runtime.");
    }
}
