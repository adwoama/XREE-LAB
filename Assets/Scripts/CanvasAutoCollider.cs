using UnityEngine;

/// <summary>
/// Adds / updates a BoxCollider to match a world-space Canvas RectTransform so Interaction SDK ray/poke
/// surfaces can use 'Collider' mode reliably. Place on the Canvas GameObject.
/// </summary>
[ExecuteAlways]
public class CanvasAutoCollider : MonoBehaviour
{
    [Tooltip("Extra padding added to collider size in meters.")] public Vector2 padding = new Vector2(0.01f, 0.01f);
    [Tooltip("Z thickness of collider (meters).")]
    public float depth = 0.02f;
    [Tooltip("Refresh collider every frame in edit mode.")] public bool continuousUpdate = true;

    RectTransform rt;
    BoxCollider box;

    void OnEnable()
    {
        rt = GetComponent<RectTransform>();
        if (rt == null) { enabled = false; return; }
        box = GetComponent<BoxCollider>();
        if (box == null) box = gameObject.AddComponent<BoxCollider>();
        UpdateCollider();
    }

    void Update()
    {
        if (!continuousUpdate) return;
        UpdateCollider();
    }

    void OnValidate() => UpdateCollider();

    void UpdateCollider()
    {
        if (rt == null || box == null) return;
        // Canvas in world space: width/height come from rect * local scale
        var scale = rt.lossyScale; // world scaling
        float w = rt.rect.width * scale.x + padding.x * 2f;
        float h = rt.rect.height * scale.y + padding.y * 2f;
        box.size = new Vector3(w, h, depth);
        box.center = Vector3.zero; // assumes pivot centered; adjust if pivot differs
    }
}
