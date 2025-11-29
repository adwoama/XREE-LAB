using UnityEngine;

// PanelFollowTether: keeps a panel near the user and preserves an offset established
// when the panel is released. Prevents panels from drifting off infinitely by clamping
// distance and optionally smoothing corrections. Designed to coexist with Interaction SDK
// grab mechanics (Grabbable / HandGrabInteractable). You can manually call OnGrabStart/OnGrabEnd
// from grab interaction events if available, or let the script attempt auto-detection.

public class PanelFollowTether : MonoBehaviour
{
    [Header("User Reference")]
    [Tooltip("Head/camera transform (e.g. MainCamera inside XR Rig). Auto-filled if null.")] 
    public Transform userHead;

    [Header("Distance Constraints")] 
    [Tooltip("Maximum allowed distance from user before we start pulling back (meters).")]
    public float maxDistance = 1.6f;
    [Tooltip("Hard clamp distance (panel will never exceed this). Should be >= maxDistance.")] 
    public float hardClampDistance = 2.0f;
    [Tooltip("Speed used to gently correct position when outside maxDistance.")] 
    public float pullBackSpeed = 3.0f;

    [Header("Offset Preservation")] 
    [Tooltip("Maintain offset to user after release so panels 'follow' user movement.")] 
    public bool maintainOffset = true;
    [Tooltip("Smoothing (seconds) for following user movement.")] 
    public float followSmoothTime = 0.25f;
    [Tooltip("Extra smoothing for head position to reduce micro-jitter (seconds). 0 = no extra smoothing")] 
    public float headSmoothingSeconds = 0.05f;
    [Tooltip("Ignore tiny target movements under this distance (meters) to avoid oscillation")] 
    public float positionDeadzone = 0.005f;

    [Header("Physics Stabilization")] 
    [Tooltip("Drag to apply to Rigidbody when not grabbed.")] 
    public float idleDrag = 3f;
    [Tooltip("Angular drag to apply when not grabbed.")] 
    public float idleAngularDrag = 3f;
    [Tooltip("Drag to apply while grabbed (lower lets user move it freely).")]
    public float grabbedDrag = 0.2f;
    [Tooltip("Angular drag while grabbed.")] 
    public float grabbedAngularDrag = 0.2f;
    [Tooltip("When not grabbed, set Rigidbody to kinematic to avoid fighting physics while we reposition via Transform.")] 
    public bool makeKinematicWhenIdle = true;

    [Header("Optional Axis Constraints")] 
    [Tooltip("Lock vertical movement relative to user head (panel stays at similar height).")]
    public bool lockYRelative = false;
    [Tooltip("Maximum vertical delta from original offset if lockYRelative is true.")] 
    public float maxYDelta = 0.25f;
    [Tooltip("Clamp the stored vertical offset relative to head on release (prevents drifting too high/low).")]
    public bool clampOffsetY = false;
    [Tooltip("Minimum Y offset relative to head when clamping.")]
    public float minOffsetY = -0.2f;
    [Tooltip("Maximum Y offset relative to head when clamping.")]
    public float maxOffsetY = 0.35f;

    [Header("Debug")] 
    [Tooltip("Emit diagnostic logs.")] 
    public bool debugLogs = false;

    Vector3 storedOffset; // offset from userHead when released
    Vector3 followVelocity; // smoothing state
    Vector3 smoothedHead; // filtered head position to reduce jitter

    Rigidbody rb;

    // Simple grabbed state tracking (can be overridden by events)
    bool isGrabbed;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Start()
    {
        if (userHead == null)
        {
            var cam = Camera.main;
            if (cam != null) userHead = cam.transform;
        }
        if (userHead != null)
        {
            storedOffset = transform.position - userHead.position;
            smoothedHead = userHead.position;
        }
        // Improve visual smoothness when physics is used
        if (rb != null)
        {
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }
        ApplyIdlePhysics();
    }

    void Update()
    {
        // Intentionally keep logic in LateUpdate to follow final HMD pose.
    }

    // Call this from grab start event (HandGrabInteractable / GrabInteractable) if available
    public void OnGrabStart()
    {
        isGrabbed = true;
        ApplyGrabbedPhysics();
        if (rb != null && makeKinematicWhenIdle) rb.isKinematic = false;
        if (debugLogs) Debug.Log("[PanelTether] Grab start");
    }

    // Call this from grab end event
    public void OnGrabEnd()
    {
        isGrabbed = false;
        if (userHead != null)
        {
            storedOffset = transform.position - userHead.position; // update offset after reposition
            if (clampOffsetY)
            {
                storedOffset.y = Mathf.Clamp(storedOffset.y, minOffsetY, maxOffsetY);
            }
        }
        ApplyIdlePhysics();
        if (rb != null && makeKinematicWhenIdle) rb.isKinematic = true;
        if (debugLogs) Debug.Log("[PanelTether] Grab end; storedOffset=" + storedOffset);
    }

    void ApplyIdlePhysics()
    {
        if (rb == null) return;
        rb.linearDamping = idleDrag;
        rb.angularDamping = idleAngularDrag;
    }

    void ApplyGrabbedPhysics()
    {
        if (rb == null) return;
        rb.linearDamping = grabbedDrag;
        rb.angularDamping = grabbedAngularDrag;
    }

    // Optional auto-detection (attempts to infer grabbed state each frame). If you wire events, you can disable this block.
    void LateUpdate()
    {
        if (userHead == null) return;

        // Head smoothing to reduce perceived jitter
        if (headSmoothingSeconds > 0f)
        {
            float alpha = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.0001f, headSmoothingSeconds));
            smoothedHead = Vector3.Lerp(smoothedHead, userHead.position, alpha);
        }
        else
        {
            smoothedHead = userHead.position;
        }

        // Maintain offset if not grabbed
        if (!isGrabbed && maintainOffset)
        {
            Vector3 target = smoothedHead + storedOffset;
            if (lockYRelative)
            {
                float desiredY = smoothedHead.y + storedOffset.y;
                float currentY = transform.position.y;
                float clampedY = Mathf.Clamp(desiredY, currentY - maxYDelta, currentY + maxYDelta);
                target.y = clampedY;
            }

            // Deadzone to avoid micro-oscillation
            if ((target - transform.position).sqrMagnitude > positionDeadzone * positionDeadzone)
            {
                transform.position = Vector3.SmoothDamp(transform.position, target, ref followVelocity, followSmoothTime);
            }
        }

        // Distance tether correction (use smoothed head)
        Vector3 toPanel = transform.position - smoothedHead;
        float dist = toPanel.magnitude;

        if (dist > hardClampDistance)
        {
            Vector3 clamped = smoothedHead + toPanel.normalized * hardClampDistance;
            if (debugLogs) Debug.Log($"[PanelTether] Hard clamp from {dist:F2} to {hardClampDistance:F2}");
            transform.position = clamped;
            dist = hardClampDistance;
        }
        else if (dist > maxDistance && !isGrabbed)
        {
            Vector3 target = smoothedHead + toPanel.normalized * maxDistance;
            transform.position = Vector3.MoveTowards(transform.position, target, pullBackSpeed * Time.deltaTime);
            if (debugLogs) Debug.Log($"[PanelTether] Pulling panel back (dist {dist:F2} > {maxDistance:F2})");
        }
    }
}
