using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using XreeLab.Gestures;

// Binds a world-space Canvas UI to the GestureControlManager.
// Create a Canvas in the scene and add this component; assign UI references.

public class GestureMenuUI : MonoBehaviour
{
    public GestureControlManager manager;

    [Header("UI References")] 
    public Dropdown selectedChannelDropdown;
    public Toggle panelFollowToggle;
    public Slider channelCountSlider;
    public Text channelCountLabel;
    public Toggle[] channelOnToggles; // size up to 4

    void Start()
    {
        if (manager == null) manager = FindObjectOfType<GestureControlManager>();

        if (selectedChannelDropdown != null)
        {
            selectedChannelDropdown.onValueChanged.AddListener(idx => manager.SetSelectedChannel(idx));
        }
        if (panelFollowToggle != null)
        {
            panelFollowToggle.onValueChanged.AddListener(manager.SetPanelFollowing);
        }
        if (channelCountSlider != null)
        {
            channelCountSlider.onValueChanged.AddListener(val => {
                int count = Mathf.RoundToInt(val);
                manager.SetChannelCount(count);
                if (channelCountLabel != null) channelCountLabel.text = $"Channels: {count}";
                RefreshChannelToggles(count);
            });
        }
        RefreshChannelToggles(manager.channelCount);
    }

    void RefreshChannelToggles(int count)
    {
        if (channelOnToggles == null) return;
        for (int i = 0; i < channelOnToggles.Length; i++)
        {
            bool active = i < count;
            var t = channelOnToggles[i];
            if (t == null) continue;
            t.gameObject.SetActive(active);
            int index = i; // capture
            t.onValueChanged.RemoveAllListeners();
            t.isOn = manager.enabledChannels.Contains(index);
            t.onValueChanged.AddListener(on => manager.SetChannelEnabled(index, on));
        }
        if (selectedChannelDropdown != null)
        {
            List<string> options = new List<string>();
            for (int i = 0; i < count; i++) options.Add($"CH{i+1}");
            selectedChannelDropdown.ClearOptions();
            selectedChannelDropdown.AddOptions(options);
            selectedChannelDropdown.value = Mathf.Clamp(manager.selectedChannel, 0, count - 1);
        }
    }
}
