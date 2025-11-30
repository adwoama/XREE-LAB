using UnityEngine;

// Simple panel follower: keeps assigned panels at a fixed offset/rotation
// relative to the user's head. Enable/disable via GestureControlManager.

public class PanelFollower : MonoBehaviour
{
    [Tooltip("Target to follow (typically the XR camera). If empty, will try to find Camera.main.")]
    public Transform followTarget;

    [Tooltip("Local offset from target (meters).")]
    public Vector3 localOffset = new Vector3(0f, -0.05f, 0.6f);

    [Tooltip("Lerp speed for smooth following.")]
    public float followLerp = 8f;

    [Tooltip("Lock rotation to face user.")]
    public bool faceUser = true;

    void Start()
    {
        if (followTarget == null && Camera.main != null)
            followTarget = Camera.main.transform;
    }

    void LateUpdate()
    {
        if (followTarget == null) return;
        Vector3 desiredPos = followTarget.TransformPoint(localOffset);
        transform.position = Vector3.Lerp(transform.position, desiredPos, Time.deltaTime * followLerp);

        if (faceUser)
        {
            Vector3 dir = (followTarget.position - transform.position);
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(dir.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * followLerp);
            }
        }
    }
}
