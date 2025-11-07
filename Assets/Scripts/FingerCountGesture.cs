using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

// Simple finger-count gesture detector for Meta/Quest using OVRSkeleton tip positions.
// Attach this component to a GameObject in the scene. Assign the OVRSkeleton (left or right)
// for the hand you want to monitor (you can drag the LeftHandSkeleton/RightHandSkeleton from the scene),
// and optionally a UI Text to show the detected gesture.
//
// Algorithm (simple, robust): we consider a finger "up/extended" when its tip is sufficiently
// far from the wrist. This is fast and works reasonably well for simple gestures like
// counting 1..4 fingers.

public class FingerCountGesture : MonoBehaviour
{
    [Tooltip("Assign the OVRSkeleton for the hand to observe (left or right). If empty, the script will try to find one in the scene.")]
    public OVRSkeleton skeleton;
    [Tooltip("Optional: a reference to the OVRHand for additional info (pinch strength, confidence).")]
    public OVRHand ovrHand;

    [Tooltip("Optional UI Text to show the current gesture in-scene.")]
    public Text uiText;

    [Tooltip("Distance (meters) from wrist to fingertip considered as 'extended'. Tune per project / scale.")]
    public float extendedDistance = 0.07f;

    [Tooltip("Debounce time (seconds) to avoid flicker when hand is moving")]
    public float debounceSeconds = 0.18f;

    [Tooltip("Colors to show for gestures 0..4. Provide at least 5 colors to map directly.")]
    public Color[] gestureColors = new Color[] { Color.gray, Color.green, Color.cyan, Color.yellow, Color.magenta };

    // internal
    int lastCount = -1;
    float lastChange = 0f;

    // bone name hints used to find the tip transforms in OVRSkeleton bones
    readonly string[] tipNames = new string[] { "IndexTip", "MiddleTip", "RingTip", "PinkyTip" };

    void Start()
    {
        if (skeleton == null)
        {
            // try to auto-find a skeleton (prefer a child named LeftHand or RightHand)
            var sks = FindObjectsOfType<OVRSkeleton>();
            if (sks != null && sks.Length > 0)
                skeleton = sks[0];
        }
        if (ovrHand == null && skeleton != null)
        {
            // try to find an OVRHand component on the same root
            var root = skeleton.transform.root;
            ovrHand = root.GetComponentInChildren<OVRHand>();
        }
        UpdateUI(0);
    }

    void Update()
    {
        if (skeleton == null || !skeleton.IsDataValid)
        {
            // try to find a skeleton if the assigned one isn't valid yet
            if (skeleton == null) Start();
            return;
        }

        var wrist = GetBoneTransformByName("WristRoot") ?? GetBoneTransformByName("Wrist") ?? skeleton.transform;
        if (wrist == null) return;

        int count = 0;
        foreach (var tip in tipNames)
        {
            var t = GetBoneTransformByName(tip);
            if (t == null) continue;
            float d = Vector3.Distance(t.position, wrist.position);
            if (d >= extendedDistance) count++;
        }

        // clamp to 0..4
        count = Mathf.Clamp(count, 0, 4);

        if (count != lastCount && Time.time - lastChange >= debounceSeconds)
        {
            lastCount = count;
            lastChange = Time.time;
            OnGestureChanged(count);
        }
    }

    Transform GetBoneTransformByName(string nameHint)
    {
        if (skeleton == null || skeleton.Bones == null) return null;
        var b = skeleton.Bones.FirstOrDefault(x => x.Transform != null && x.Transform.name.IndexOf(nameHint, StringComparison.OrdinalIgnoreCase) >= 0);
        return b != null ? b.Transform : null;
    }

    void OnGestureChanged(int count)
    {
        // in-scene UI
        UpdateUI(count);

        // Debug log for MQDH / adb logcat
        if (useAnsiColorInLogs)
        {
            try
            {
                var col = (gestureColors != null && gestureColors.Length > count) ? gestureColors[count] : Color.white;
                string ansi = AnsiColor(col);
                Debug.Log($"{ansi}[Gesture] FingerCount={count}{AnsiReset()}");
            }
            catch
            {
                Debug.Log($"[Gesture] FingerCount={count}");
            }
        }
        else
        {
            Debug.Log($"[Gesture] FingerCount={count}");
        }
    }

    [Header("Logging")]
    [Tooltip("If true, attempt to emit ANSI colored logs (RGB) which some log viewers (MQDH) may render.")]
    public bool useAnsiColorInLogs = true;

    string AnsiColor(Color c)
    {
        int r = Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
        int g = Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
        int b = Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
        // 24-bit foreground color
        return $"\u001b[38;2;{r};{g};{b}m";
    }

    string AnsiReset()
    {
        return "\u001b[0m";
    }

    void UpdateUI(int count)
    {
        if (uiText != null)
        {
            uiText.text = $"Gesture: {count}";
            if (gestureColors != null && gestureColors.Length > count)
                uiText.color = gestureColors[count];
        }
    }
}
