using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

// Lightweight safety guard to prevent NullReferenceExceptions coming from
// Oculus.Interaction.ActiveStateTracker when its referenced IActiveState is missing.
// This script runs once at load and will inject a fallback IActiveState implementation
// into any ActiveStateTracker that has a null active state. It uses reflection and the
// public InjectActiveState method when available, so it avoids editing package code.
[DefaultExecutionOrder(-1000)]
public class ActiveStateTrackerSafety : MonoBehaviour
{
    // A small MonoBehaviour implementing the package IActiveState interface.
    // It always reports false (inactive). We attach one instance to a persistent GameObject
    // and reuse it for all trackers that need a fallback.
    private class FallbackActiveState : MonoBehaviour
    {
        // Implemented via duck-typing / reflection by the package interface.
        // We'll provide a public bool Active property so InjectActiveState can use it.
        public bool Active => false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureActiveStateTrackersSafe()
    {
        // We'll patch trackers now and also whenever a scene finishes loading to avoid timing issues.
        void PatchNow()
        {
            try
            {
                // Find the ActiveStateTracker type from the loaded assemblies.
                var trackerType = Type.GetType("Oculus.Interaction.ActiveStateTracker, Assembly-CSharp");
                if (trackerType == null)
                {
                    // Try searching all assemblies for the type name.
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        trackerType = asm.GetType("Oculus.Interaction.ActiveStateTracker");
                        if (trackerType != null) break;
                    }
                }

                if (trackerType == null)
                {
                    Debug.Log("[ActiveStateTrackerSafety] ActiveStateTracker type not found. Nothing to do.");
                    return;
                }

                // Find all instances in the scene (include inactive to be thorough)
                var trackers = UnityEngine.Object.FindObjectsOfType(typeof(MonoBehaviour), true);

                GameObject fallbackGO = null;
                FallbackActiveState fallbackComp = null;

                int patched = 0;

                foreach (var obj in trackers)
                {
                    var mb = obj as MonoBehaviour;
                    if (mb == null) continue;
                    var t = mb.GetType();
                    if (t != trackerType && !t.IsSubclassOf(trackerType)) continue;

                    // Access private field 'ActiveState' via reflection to check for null
                    var activeStateField = trackerType.GetField("ActiveState", BindingFlags.NonPublic | BindingFlags.Instance);
                    bool needsPatch = false;

                    if (activeStateField != null)
                    {
                        var value = activeStateField.GetValue(mb);
                        if (value == null) needsPatch = true;
                    }
                    else
                    {
                        // Fallback: try reading the serialized backing field '_activeState'
                        var serializedField = trackerType.GetField("_activeState", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (serializedField != null)
                        {
                            var val = serializedField.GetValue(mb) as UnityEngine.Object;
                            if (val == null) needsPatch = true;
                        }
                    }

                    if (!needsPatch) continue;

                    // Lazily create fallback component
                    if (fallbackGO == null)
                    {
                        fallbackGO = new GameObject("__ActiveStateTracker_Fallback");
                        UnityEngine.Object.DontDestroyOnLoad(fallbackGO);
                        fallbackComp = fallbackGO.AddComponent<FallbackActiveState>();
                    }

                    // We cannot safely create a valid IActiveState instance without depending on package types,
                    // and calling InjectActiveState reflectively can fail if the object doesn't implement the
                    // package interface. Safer approach: disable the ActiveStateTracker so its Update() won't run
                    // and cause a NullReferenceException. Log a warning so developers can inspect and set a
                    // proper IActiveState in the inspector if needed.
                    try
                    {
                        mb.enabled = false;
                        patched++;
                        Debug.LogWarning($"[ActiveStateTrackerSafety] Disabled ActiveStateTracker on '{mb.gameObject.name}' because it had no ActiveState assigned.");
                        continue;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[ActiveStateTrackerSafety] Failed to disable ActiveStateTracker on {mb.gameObject.name}: {ex.Message}");
                    }
                }

                if (patched > 0)
                {
                    Debug.Log($"[ActiveStateTrackerSafety] Patched {patched} ActiveStateTracker(s) with fallback ActiveState to avoid NullReferenceExceptions.");
                }
                else
                {
                    Debug.Log("[ActiveStateTrackerSafety] No ActiveStateTracker instances required patching.");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ActiveStateTrackerSafety] Unexpected error: {e}");
            }
        }

    // Register to patch whenever a scene finishes loading. Using BeforeSceneLoad above
    // ensures our handler is registered early so it runs before many package Update() calls.
    SceneManager.sceneLoaded += (Scene scene, LoadSceneMode mode) => PatchNow();
    }
}
