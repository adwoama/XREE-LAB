using System;
using System.Linq;
using UnityEngine;

// MetaXRGestureActions
// --------------------
// Higher-level gesture action mapper using built-in Meta hand tracking primitives (OVRHand / OVRSkeleton).
// Keeps FingerCountGesture untouched; this script focuses on actionable gestures for UI / panel control.
// Attach one instance (e.g. on an empty GameObject "GestureRouter") and wire optional public events by
// assigning listeners from other components (WaveformManager, UI managers, etc.).
//
// Gesture set (initial proposal):
// 1. Left wrist flip (rapid supination/pronation angle delta) => Toggle menu (OnMenuToggle)
// 2. Right hand index pinch (tap) => Toggle cursor measurement tool (OnCursorModeToggle)
// 3. Two-hand index pinches held & moving apart/together => Horizontal zoom delta (OnZoomDelta)
// 4. Middle finger pinch hold (long press) on either hand => Freeze/hold trigger (OnFreezeToggle)
// 5. Four fingers extended gesture (cross-check via skeleton if available) OR 3-finger pinch sequence => FFT request (OnFFTRequest)
//    (Uses activeChannel id; can be set externally.)
//
// Notes:
// - Wrist flip detection: compares current wrist up vector against previous and measures angle delta threshold within a time window.
// - Debounce / cooldown fields help limit rapid retriggers.
// - Zoom factor delta is relative; consuming code accumulates it to adjust waveform scale.
// - FFT request currently fires on gesture detection; consumer can decide which buffer to transform.
// - This is a prototype-level implementation; refine thresholds per device tests on Quest.
//
// Integration outline:
// - WaveformManager subscribes to events: gestureActions.OnZoomDelta += mgr.AdjustHorizontalScale;
// - A network layer listens for OnFFTRequest to ask Pi for spectrum or compute locally.
// - UI system listens for OnMenuToggle & OnCursorModeToggle.
//
// Assumptions: Oculus Integration / Interaction SDK provides OVRHand + OVRSkeleton in scene.

namespace XreeLab.Gestures
{
    public class MetaXRGestureActions : MonoBehaviour
    {
        [Header("Hand References (assign or auto-find)")] 
        public OVRHand leftHand;
        public OVRSkeleton leftSkeleton;
        public OVRHand rightHand;
        public OVRSkeleton rightSkeleton;

        [Header("Channel / Context")] 
        [Tooltip("Active channel index for FFT requests.")] public int activeChannel = 0;

        [Header("Wrist Flip Settings")] 
        [Tooltip("Angle delta (degrees) between wrist up vectors to qualify as a flip.")] public float wristFlipAngleThreshold = 95f;
        [Tooltip("Minimum seconds between wrist flip toggles.")] public float wristFlipCooldown = 1.2f;

        [Header("Pinch Settings")] 
        [Tooltip("Pinch strength threshold considered 'pinching'. Range 0..1.")] public float pinchStrengthThreshold = 0.65f;
        [Tooltip("Tap release max duration (seconds) for cursor toggle gesture.")] public float pinchTapMaxDuration = 0.45f;
        [Tooltip("Hold duration (seconds) to treat middle-finger pinch as freeze toggle.")] public float freezeHoldSeconds = 0.9f;

        [Header("Zoom Settings")] 
        [Tooltip("Minimum seconds both hands must be pinching before zoom tracking starts.")] public float zoomActivationDelay = 0.15f;
        [Tooltip("Multiplier applied to distance delta to produce zoom factor delta.")] public float zoomSensitivity = 4.0f;
        [Tooltip("Clamp applied to |zoom delta| per frame.")] public float zoomDeltaClamp = 0.25f;

        [Header("FFT Gesture Settings")] 
        [Tooltip("If true, require 4 extended fingers (index..pinky) on right hand to request FFT.")] public bool useFourFingerExtendedForFFT = true;
        [Tooltip("Minimum seconds between FFT requests.")] public float fftCooldown = 1.5f;

        [Header("Debug / Diagnostics")] 
        public bool verboseLogs = true;

        // Events / Actions --------------------------------------
        public event Action OnMenuToggle;          // Wrist flip left
        public event Action OnCursorModeToggle;    // Right index pinch tap
        public event Action<float> OnZoomDelta;    // Two-hand pinch distance changes
        public event Action OnFreezeToggle;        // Middle finger pinch hold
        public event Action<int> OnFFTRequest;     // Extended fingers gesture

        // Internal state ---------------------------------------
        Vector3 lastLeftWristUp;
        float lastWristFlipTime;

        bool rightIndexPinching; float rightIndexPinchStartTime;
        bool middlePinchHolding; float middlePinchStartTime;

        bool bothIndexPinching; float bothPinchStartTime; float lastPinchDistance;

        float lastFFTTime;

        // Skeleton bone name fragments for tips
        readonly string[] fingerTipNames = { "IndexTip", "MiddleTip", "RingTip", "PinkyTip" };

        void Start()
        {
            AutoFindHandsIfNeeded();
            if (leftSkeleton == null && leftHand != null) leftSkeleton = leftHand.GetComponentInChildren<OVRSkeleton>();
            if (rightSkeleton == null && rightHand != null) rightSkeleton = rightHand.GetComponentInChildren<OVRSkeleton>();
            if (leftSkeleton != null) lastLeftWristUp = leftSkeleton.transform.up; else lastLeftWristUp = Vector3.up;
        }

        void AutoFindHandsIfNeeded()
        {
            if (leftHand == null || rightHand == null)
            {
                var all = FindObjectsOfType<OVRHand>();
                foreach (var h in all)
                {
                    // naive classification by name
                    if (leftHand == null && h.name.ToLower().Contains("left")) leftHand = h;
                    if (rightHand == null && h.name.ToLower().Contains("right")) rightHand = h;
                }
                // fallback: first becomes left, second becomes right if names missing
                if (leftHand == null && all.Length > 0) leftHand = all[0];
                if (rightHand == null && all.Length > 1) rightHand = all[1];
            }
        }

        void Update()
        {
            DetectWristFlip();
            DetectCursorPinchTap();
            DetectFreezeHold();
            DetectZoom();
            DetectFFTGesture();
        }

        // 1. Wrist Flip (left hand)
        void DetectWristFlip()
        {
            if (leftSkeleton == null) return;
            Vector3 currentUp = leftSkeleton.transform.up;
            float angle = Vector3.Angle(lastLeftWristUp, currentUp);
            if (angle >= wristFlipAngleThreshold && (Time.time - lastWristFlipTime) >= wristFlipCooldown)
            {
                lastWristFlipTime = Time.time;
                lastLeftWristUp = currentUp;
                if (verboseLogs) Debug.Log("[Gesture] Wrist flip detected -> Menu toggle");
                OnMenuToggle?.Invoke();
            }
            // Slowly update baseline (so slow rotation doesn't accumulate as flip)
            lastLeftWristUp = Vector3.Slerp(lastLeftWristUp, currentUp, 0.08f);
        }

        // 2. Cursor mode toggle via right index pinch tap
        void DetectCursorPinchTap()
        {
            if (rightHand == null) return;
            bool pinching = rightHand.GetFingerIsPinching(OVRHand.HandFinger.Index) && rightHand.GetFingerPinchStrength(OVRHand.HandFinger.Index) >= pinchStrengthThreshold;
            if (pinching && !rightIndexPinching)
            {
                rightIndexPinching = true;
                rightIndexPinchStartTime = Time.time;
            }
            else if (!pinching && rightIndexPinching)
            {
                float dur = Time.time - rightIndexPinchStartTime;
                rightIndexPinching = false;
                if (dur <= pinchTapMaxDuration)
                {
                    if (verboseLogs) Debug.Log("[Gesture] Index pinch tap -> Cursor mode toggle");
                    OnCursorModeToggle?.Invoke();
                }
            }
        }

        // 3. Freeze toggle via middle finger pinch hold
        void DetectFreezeHold()
        {
            bool middlePinch = false;
            if (leftHand != null)
                middlePinch |= leftHand.GetFingerIsPinching(OVRHand.HandFinger.Middle) && leftHand.GetFingerPinchStrength(OVRHand.HandFinger.Middle) >= pinchStrengthThreshold;
            if (rightHand != null)
                middlePinch |= rightHand.GetFingerIsPinching(OVRHand.HandFinger.Middle) && rightHand.GetFingerPinchStrength(OVRHand.HandFinger.Middle) >= pinchStrengthThreshold;

            if (middlePinch && !middlePinchHolding)
            {
                middlePinchHolding = true;
                middlePinchStartTime = Time.time;
            }
            else if (!middlePinch && middlePinchHolding)
            {
                middlePinchHolding = false; // released before hold threshold
            }
            else if (middlePinchHolding && (Time.time - middlePinchStartTime) >= freezeHoldSeconds)
            {
                middlePinchHolding = false; // trigger once
                if (verboseLogs) Debug.Log("[Gesture] Middle pinch hold -> Freeze toggle");
                OnFreezeToggle?.Invoke();
            }
        }

        // 4. Two-hand index pinch distance for zoom
        void DetectZoom()
        {
            if (leftHand == null || rightHand == null) return;
            bool leftPinch = leftHand.GetFingerIsPinching(OVRHand.HandFinger.Index) && leftHand.GetFingerPinchStrength(OVRHand.HandFinger.Index) >= pinchStrengthThreshold;
            bool rightPinch = rightHand.GetFingerIsPinching(OVRHand.HandFinger.Index) && rightHand.GetFingerPinchStrength(OVRHand.HandFinger.Index) >= pinchStrengthThreshold;

            if (leftPinch && rightPinch)
            {
                if (!bothIndexPinching)
                {
                    bothIndexPinching = true;
                    bothPinchStartTime = Time.time;
                    lastPinchDistance = CurrentIndexTipDistance();
                }
                else if (Time.time - bothPinchStartTime >= zoomActivationDelay)
                {
                    float dist = CurrentIndexTipDistance();
                    float delta = dist - lastPinchDistance;
                    lastPinchDistance = dist;
                    float zoomDelta = Mathf.Clamp(delta * zoomSensitivity, -zoomDeltaClamp, zoomDeltaClamp);
                    if (Mathf.Abs(zoomDelta) > 0.0005f)
                    {
                        OnZoomDelta?.Invoke(zoomDelta);
                        if (verboseLogs) Debug.Log($"[Gesture] Zoom delta {zoomDelta:F3}");
                    }
                }
            }
            else if (bothIndexPinching)
            {
                bothIndexPinching = false;
            }
        }

        float CurrentIndexTipDistance()
        {
            Transform lt = GetFingerTip(leftSkeleton, "IndexTip");
            Transform rt = GetFingerTip(rightSkeleton, "IndexTip");
            if (lt == null || rt == null) return 0f;
            return Vector3.Distance(lt.position, rt.position);
        }

        // 5. FFT gesture (four extended fingers on right hand)
        void DetectFFTGesture()
        {
            if (!useFourFingerExtendedForFFT) return;
            if (rightSkeleton == null) return;
            if (Time.time - lastFFTTime < fftCooldown) return;

            int extended = CountExtendedFingers(rightSkeleton);
            if (extended >= 4)
            {
                lastFFTTime = Time.time;
                if (verboseLogs) Debug.Log($"[Gesture] FFT request (channel {activeChannel})");
                OnFFTRequest?.Invoke(activeChannel);
            }
        }

        int CountExtendedFingers(OVRSkeleton skeleton)
        {
            if (skeleton == null || skeleton.Bones == null) return 0;
            Transform wrist = GetBoneTransform(skeleton, "WristRoot") ?? skeleton.transform;
            int count = 0;
            foreach (var nameFrag in fingerTipNames)
            {
                var tip = GetFingerTip(skeleton, nameFrag);
                if (tip == null) continue;
                float d = Vector3.Distance(tip.position, wrist.position);
                // A heuristic: consider extended if tip is farther than median of all distances * 0.7 OR > absolute minimal threshold
                if (d > 0.05f) count++; // coarse threshold; tune later
            }
            return count;
        }

        Transform GetFingerTip(OVRSkeleton skel, string fragment)
        {
            if (skel == null || skel.Bones == null) return null;
            var bone = skel.Bones.FirstOrDefault(b => b.Transform != null && b.Transform.name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
            return bone?.Transform;
        }

        Transform GetBoneTransform(OVRSkeleton skel, string fragment)
        {
            if (skel == null || skel.Bones == null) return null;
            var bone = skel.Bones.FirstOrDefault(b => b.Transform != null && b.Transform.name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
            return bone?.Transform;
        }
    }
}
