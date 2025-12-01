using UnityEngine;
using UnityEngine.UI;
using XreeLab.Gestures;

/// <summary>
/// Simple test button to trigger freeze/pause directly (bypassing gesture).
/// Attach to a Button GameObject and assign references in Inspector.
/// Used to A/B test whether crash is gesture-specific or freeze-path-wide.
/// </summary>
public class PauseTestButton : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Assign your GestureControlManager (on GestureRouter).")]
    public GestureControlManager gestureManager;

    [Tooltip("Assign the Button component.")]
    public Button button;

    [Header("Test Channel")]
    [Tooltip("Which channel to freeze (0-based UI index, will map to server channel+1).")]
    public int testChannelIndex = 0;

    private void Start()
    {
        if (button == null) button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.AddListener(OnPauseButtonClick);
        }
        if (gestureManager == null)
        {
            gestureManager = FindObjectOfType<GestureControlManager>();
        }
    }

    private void OnPauseButtonClick()
    {
        if (gestureManager == null)
        {
            Debug.LogError("[PauseTestButton] GestureControlManager not assigned.");
            return;
        }

        Debug.Log($"[PauseTestButton] Triggering freeze toggle for channel {testChannelIndex} via button (time={Time.time:F3})");
        
        // Directly invoke the same path as the gesture
        // This mimics the gesture's OnFreezeToggle event
        try
        {
            // Set selected channel and trigger freeze
            gestureManager.SetSelectedChannel(testChannelIndex);
            
            // Access the private HandleFreezeToggle via reflection or expose a public test method
            // Simpler: just call the TCP client directly (same as gesture path)
            if (gestureManager.tcpClient != null)
            {
                int serverCh = testChannelIndex + 1;
                bool currentlyFrozen = IsFrozen(serverCh);
                bool next = !currentlyFrozen;
                
                Debug.Log($"[PauseTestButton] Calling FreezeChannel({serverCh}, {next})");
                gestureManager.tcpClient.FreezeChannel(serverCh, next);
            }
            else
            {
                Debug.LogError("[PauseTestButton] TcpOscopeClient not assigned on manager.");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[PauseTestButton] Exception during freeze: {ex.Message}\n{ex.StackTrace}");
        }
    }

    // Helper to check if channel is frozen (duplicates manager's private state; you may want to expose this)
    private System.Collections.Generic.HashSet<int> frozenStates = new System.Collections.Generic.HashSet<int>();
    private bool IsFrozen(int serverChannel)
    {
        // For now, just return false; button will always toggle to frozen first
        // You can extend this to track state if needed
        return frozenStates.Contains(serverChannel);
    }
}
