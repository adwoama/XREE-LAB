using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using XreeLab.Gestures;
using TMPro;

// Binds a world-space Canvas UI to the GestureControlManager.
// Create a Canvas in the scene and add this component; assign UI references.

public class GestureMenuUI : MonoBehaviour
{
    public GestureControlManager manager;

    [Header("UI References")] 
    [Header("Unity UI (non-TMP)")]
    public Dropdown selectedChannelDropdown;
    public Toggle panelFollowToggle;
    public Slider channelCountSlider;
    public Text channelCountLabel;
    [Header("TextMeshPro (TMP)")]
    public TMP_Dropdown selectedChannelDropdownTMP;
    public TMP_Text channelCountLabelTMP;
    public Toggle[] channelOnToggles; // size up to 4

    void Start()
    {
        if (manager == null) manager = FindObjectOfType<GestureControlManager>();

        // Wire dropdown (Unity UI or TMP)
        if (selectedChannelDropdown != null)
        {
            selectedChannelDropdown.onValueChanged.AddListener(idx => manager.SetSelectedChannel(idx));
        }
        else if (selectedChannelDropdownTMP != null)
        {
            selectedChannelDropdownTMP.onValueChanged.AddListener(idx => manager.SetSelectedChannel(idx));
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
                if (channelCountLabelTMP != null) channelCountLabelTMP.text = $"Channels: {count}";
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
        // Populate dropdown options (Unity UI or TMP)
        List<string> options = new List<string>();
        for (int i = 0; i < count; i++) options.Add($"CH{i+1}");

        if (selectedChannelDropdown != null)
        {
            selectedChannelDropdown.ClearOptions();
            selectedChannelDropdown.AddOptions(options);
            selectedChannelDropdown.value = Mathf.Clamp(manager.selectedChannel, 0, count - 1);
        }
        else if (selectedChannelDropdownTMP != null)
        {
            selectedChannelDropdownTMP.ClearOptions();
            var tmpOptions = new List<TMP_Dropdown.OptionData>();
            foreach (var o in options) tmpOptions.Add(new TMP_Dropdown.OptionData(o));
            selectedChannelDropdownTMP.options = tmpOptions;
            selectedChannelDropdownTMP.value = Mathf.Clamp(manager.selectedChannel, 0, count - 1);
            selectedChannelDropdownTMP.RefreshShownValue();
        }
    }
}
