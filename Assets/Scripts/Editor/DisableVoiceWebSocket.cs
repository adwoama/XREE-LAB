using UnityEditor;
using System.Linq;

/// <summary>
/// Adds a scripting define to disable Meta Voice SDK WebSocket code
/// This prevents compile errors from NativeWebSocketWrapper.cs
/// </summary>
[InitializeOnLoad]
public static class DisableVoiceWebSocket
{
    private const string DEFINE = "WIT_DISABLE_WEBSOCKET";

    static DisableVoiceWebSocket()
    {
        var target = EditorUserBuildSettings.selectedBuildTargetGroup;
        var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(target);
        
        if (!defines.Contains(DEFINE))
        {
            if (string.IsNullOrEmpty(defines))
                defines = DEFINE;
            else
                defines += ";" + DEFINE;
            
            PlayerSettings.SetScriptingDefineSymbolsForGroup(target, defines);
            UnityEngine.Debug.Log($"[DisableVoiceWebSocket] Added '{DEFINE}' to scripting defines for {target}");
        }
    }
}
