using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Central manager for gesture-driven oscilloscope actions and menu state.
// Attach to a singleton GameObject (e.g., "GestureRouter").
// Assign references in Inspector.

namespace XreeLab.Gestures
{
    public class GestureControlManager : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Gesture actions source. Assign the MetaXRGestureActions component.")]
        public MetaXRGestureActions gestureActions;

        [Tooltip("TCP oscilloscope client responsible for streaming and FFT commands.")]
        public TcpOscopeClient tcpClient;

        [Tooltip("All panel GameObjects that can be shown/hidden or followed.")]
        public List<GameObject> panels = new List<GameObject>();

        [Tooltip("Optional follower component controlling follow behavior for panels.")]
        public PanelFollower panelFollower;

        [Header("Menu State")] 
        [Tooltip("Currently selected channel index.")]
        public int selectedChannel = 0;

        [Tooltip("Total channel count available.")]
        [Range(1, 4)] public int channelCount = 2;

        [Tooltip("Channels enabled for display/streaming.")]
        public List<int> enabledChannels = new List<int> { 0, 1 };

        [Tooltip("If true, panels follow the user.")]
        public bool panelFollowingEnabled = false;

        [Header("FFT / Gesture Settings")] 
        [Tooltip("Seconds between FFT requests to avoid spamming server.")]
        public float fftGestureCooldown = 1.5f;
        float lastFftTime;
        bool fftEnabled = false; // track FFT toggle state

        void Awake()
        {
            // Keep list sane
            enabledChannels = enabledChannels.Distinct().Where(i => i >= 0 && i < channelCount).ToList();
        }

        void OnEnable()
        {
            if (gestureActions != null)
            {
                gestureActions.OnMenuToggle += HandleMenuToggle;
                gestureActions.OnCursorModeToggle += HandleCursorModeToggle;
                gestureActions.OnZoomDelta += HandleZoomDelta;
                gestureActions.OnFreezeToggle += HandleFreezeToggle;
                gestureActions.OnFFTRequest += HandleFFTRequest;
            }
        }

        void OnDisable()
        {
            if (gestureActions != null)
            {
                gestureActions.OnMenuToggle -= HandleMenuToggle;
                gestureActions.OnCursorModeToggle -= HandleCursorModeToggle;
                gestureActions.OnZoomDelta -= HandleZoomDelta;
                gestureActions.OnFreezeToggle -= HandleFreezeToggle;
                gestureActions.OnFFTRequest -= HandleFFTRequest;
            }
        }

        // Menu toggled via left wrist flip
        void HandleMenuToggle()
        {
            // Show/hide a world-space menu canvas if present.
            var menu = GetComponentInChildren<Canvas>(true);
            if (menu != null)
            {
                bool next = !menu.gameObject.activeSelf;
                menu.gameObject.SetActive(next);
            }
        }

        // Cursor mode toggle via pinch tap (placeholder hook)
        void HandleCursorModeToggle()
        {
            // Could enable a cursor tool; for now just log.
            Debug.Log("[GestureManager] Cursor mode toggle requested");
        }

        // Horizontal zoom gesture: UI-only, manipulate panel visual scale
        void HandleZoomDelta(float delta)
        {
            // Find the panel for selectedChannel and adjust horizontal scale.
            var panel = PanelForChannel(selectedChannel);
            if (panel == null) return;
            var wp = panel.GetComponent<WaveformPanel>();
            if (wp == null) return;

            // Adjust horizontal scale (time axis)
            float current = wp.horizontalScale;
            float next = Mathf.Clamp(current + delta * 0.5f, 0.5f, 3.0f);
            wp.horizontalScale = next;
            if (Mathf.Abs(delta) > 0.001f)
                Debug.Log($"[GestureManager] Horizontal zoom: {next:F2}x");
        }

        // Freeze gesture: signal server to pause streaming, retain last buffer locally
        void HandleFreezeToggle()
        {
            if (tcpClient == null) return;
            int ch = selectedChannel;
            // Toggle freeze state
            bool shouldFreeze = !IsFrozen(ch);
            tcpClient.FreezeChannel(ch, shouldFreeze);
            Debug.Log($"[GestureManager] Freeze {(shouldFreeze ? "ON" : "OFF")} for channel {ch}");
        }

        bool IsFrozen(int channel)
        {
            // Simple state tracking; could expand to per-channel dict if needed
            // For now assume single freeze state per channel
            return false; // placeholder; track in dict if you need persistent state
        }

        // FFT gesture: toggle FFT display for selected channel
        void HandleFFTRequest(int channel)
        {
            if (Time.time - lastFftTime < fftGestureCooldown) return;
            lastFftTime = Time.time;
            if (tcpClient == null) return;
            
            fftEnabled = !fftEnabled;
            if (fftEnabled)
            {
                tcpClient.RequestFFT(selectedChannel, "hann");
                Debug.Log($"[GestureManager] FFT ON for channel {selectedChannel}");
            }
            else
            {
                // Could send a stop_fft command if server supports it
                Debug.Log($"[GestureManager] FFT OFF for channel {selectedChannel}");
            }
        }

        // Public API for UI bindings ---------------------------------
        public void SetSelectedChannel(int index)
        {
            selectedChannel = Mathf.Clamp(index, 0, channelCount - 1);
            if (gestureActions != null) gestureActions.activeChannel = selectedChannel;
        }

        public void SetChannelCount(int count)
        {
            channelCount = Mathf.Clamp(count, 1, 4);
            enabledChannels = enabledChannels.Where(i => i < channelCount).Distinct().ToList();
            UpdatePanelVisibility();
        }

        public void SetChannelEnabled(int index, bool enabled)
        {
            if (index < 0 || index >= channelCount) return;
            if (enabled)
            {
                if (!enabledChannels.Contains(index)) enabledChannels.Add(index);
                // start streaming if tcpClient present
                if (tcpClient != null && tcpClient.IsConnected)
                    tcpClient.StartStreaming(index);
            }
            else
            {
                enabledChannels.Remove(index);
                if (tcpClient != null && tcpClient.IsConnected)
                    tcpClient.StopStreaming(index);
            }
            UpdatePanelVisibility();
        }

        public void SetPanelFollowing(bool enabled)
        {
            panelFollowingEnabled = enabled;
            if (panelFollower != null) panelFollower.enabled = enabled;
        }

        void UpdatePanelVisibility()
        {
            for (int i = 0; i < panels.Count; i++)
            {
                bool on = enabledChannels.Contains(i);
                if (panels[i] != null) panels[i].SetActive(on);
            }
        }

        GameObject PanelForChannel(int index)
        {
            if (index < 0 || index >= panels.Count) return null;
            return panels[index];
        }
    }
}
