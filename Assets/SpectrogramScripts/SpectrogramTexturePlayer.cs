using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;

public class SpectrogramTexturePlayer : MonoBehaviour
{
    public Texture2D heightTexture;
    public Material instanceMaterial; // should use Spectrogram/HeightDisplace
    public float heightScale = 1f;
    public float fps = 30f;
    public float spacing = 0.06f;
    [Tooltip("Color for low heights (0.0)")]
    public Color colorLow = new Color(0.02f, 0.5f, 0.45f, 1f);
    [Tooltip("Color for high heights (1.0)")]
    public Color colorHigh = new Color(0.0f, 0.9f, 0.75f, 1f);
    [Tooltip("Rim color to accent edges")]
    public Color rimColor = Color.white;
    [Tooltip("Rim power (higher = tighter rim)")]
    public float rimPower = 2.5f;
    public bool autoCreateInstances = true;
    public int frequencyBins = 0; // if 0, read from texture width
    public int gridWidth = 8;
    public int gridHeight = 8;
    [Tooltip("Enable verbose runtime logging to help validate frames, sampling and instance scales.")]
    public bool verboseLogs = true;

    private List<Renderer> instances = new List<Renderer>();
    private float timeAccumulator = 0f;
    private int frameCount = 1;
    private MaterialPropertyBlock mpbShared;

    void Start()
    {
        if (heightTexture == null)
        {
            Debug.LogWarning("SpectrogramTexturePlayer: no heightTexture assigned.");
            return;
        }
        Shader.SetGlobalTexture("_HeightTex", heightTexture);
        // In atlas mode, texture width == gridWidth, texture height == frameCount * gridHeight
        if (gridWidth <= 0) gridWidth = heightTexture.width;
        if (gridHeight <= 0) gridHeight = 1;
        int texW = Mathf.Max(1, heightTexture.width);
        int texH = Mathf.Max(1, heightTexture.height);
        frameCount = Mathf.Max(1, texH / gridHeight);
        if (frequencyBins <= 0) frequencyBins = gridWidth * gridHeight;

        // If the assigned material's shader is unsupported on this platform, fallback to Standard.
        if (instanceMaterial != null && instanceMaterial.shader != null && !instanceMaterial.shader.isSupported)
        {
            Debug.LogWarning("SpectrogramTexturePlayer: assigned material shader not supported on this platform; falling back to Standard shader.");
            instanceMaterial = new Material(Shader.Find("Standard"));
        }

        // Only auto-create instances at runtime to avoid creating persistent editor objects
        if (autoCreateInstances && Application.isPlaying)
        {
            // remove any previously generated children (leftover from edit-time runs)
            RemovePreviouslyGeneratedChildren();
            CreateInstances();
        }

        // instantiate MPB here to avoid creating Unity objects during field initialization
        if (mpbShared == null) mpbShared = new MaterialPropertyBlock();

        if (verboseLogs)
        {
            Debug.Log($"SpectrogramTexturePlayer.Start: tex={heightTexture.name} size={heightTexture.width}x{heightTexture.height} frameCount={frameCount} freqBins={frequencyBins} instances={instances.Count}");
        }
    }

    void CreateInstances()
    {
        ClearInstances();
        // also remove any orphaned children just in case
        RemovePreviouslyGeneratedChildren();
        for (int row = 0; row < gridHeight; row++)
        {
            for (int col = 0; col < gridWidth; col++)
            {
                int idx = row * gridWidth + col;
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"Spec_Col_{col}_{row}";
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(col * spacing, 0f, row * spacing);
                go.transform.localRotation = Quaternion.identity;
                // Start with a thin column; Y-scale will be updated each frame.
                go.transform.localScale = new Vector3(0.05f, 0.05f, 0.05f);
                // remove collider
                Collider colc = go.GetComponent<Collider>();
                if (colc != null)
                {
                    if (Application.isPlaying) Destroy(colc); else DestroyImmediate(colc);
                }

                Renderer r = go.GetComponent<Renderer>();
                if (instanceMaterial != null)
                {
                    r.sharedMaterial = instanceMaterial;
                }

                // enable shadows for depth cues
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;

                // set per-instance freq coord via MPB (kept for potential shader use)
                MaterialPropertyBlock mpb = new MaterialPropertyBlock();
                float freqCoord = (idx + 0.5f) / (float)Mathf.Max(1, gridWidth * gridHeight);
                mpb.SetFloat("_FreqCoord", freqCoord);
                mpb.SetFloat("_HeightScale", heightScale);
                // initial color
                mpb.SetColor("_Color", colorLow);
                mpb.SetColor("_BaseColor", colorLow);
                mpb.SetColor("_RimColor", rimColor);
                mpb.SetFloat("_RimPower", rimPower);
                r.SetPropertyBlock(mpb);
                instances.Add(r);
            }
        }
    }

    // Remove children created previously by this player (name pattern Spec_Col_*)
    void RemovePreviouslyGeneratedChildren()
    {
        List<Transform> toRemove = new List<Transform>();
        for (int i = 0; i < transform.childCount; i++)
        {
            var child = transform.GetChild(i);
            if (child.name.StartsWith("Spec_Col_")) toRemove.Add(child);
        }
        foreach (var t in toRemove)
        {
            if (Application.isPlaying) Destroy(t.gameObject); else DestroyImmediate(t.gameObject);
        }
    }

    void ClearInstances()
    {
        foreach (var r in instances)
        {
            if (r != null) DestroyImmediate(r.gameObject);
        }
        instances.Clear();
    }

    void Update()
    {
        if (heightTexture == null || instances.Count == 0) return;
        timeAccumulator += Time.deltaTime;
        int frame = Mathf.FloorToInt(timeAccumulator * fps) % frameCount;
        float frameNormalized = frame / (float)Mathf.Max(1, frameCount - 1);
        Shader.SetGlobalFloat("_FrameNormalized", frameNormalized);

        // Update per-instance scale by sampling the height texture on CPU.
        // Texture is set readable by the baker. Use GetPixelBilinear for smooth sampling.
        int texW2 = Mathf.Max(1, heightTexture.width);
        int texH2 = Mathf.Max(1, heightTexture.height);
        for (int row = 0; row < gridHeight; row++)
        {
            for (int col = 0; col < gridWidth; col++)
            {
                int idx = row * gridWidth + col;
                int instanceIndex = idx; // matches CreateInstances order
                if (instanceIndex < 0 || instanceIndex >= instances.Count) continue;
                var r = instances[instanceIndex];
                if (r == null) continue;
                GameObject go = r.gameObject;
                // compute UV: u across gridWidth, v across frameCount*gridHeight
                float u = (col + 0.5f) / (float)gridWidth;
                float v = ((frame * gridHeight) + row + 0.5f) / (float)(frameCount * gridHeight);
                float h = 0f;
                try { h = heightTexture.GetPixelBilinear(u, v).r; } catch { h = 0f; }
                Vector3 s = go.transform.localScale;
                s.y = Mathf.Max(0.001f, h * heightScale);
                go.transform.localScale = s;
                // Anchor base at y=0 by moving object up by half its height
                Vector3 p = go.transform.localPosition;
                p.x = col * spacing;
                p.y = s.y * 0.5f;
                p.z = row * spacing;
                go.transform.localPosition = p;
                // Set per-instance color based on height
                mpbShared.Clear();
                float t = Mathf.Clamp01(h);
                Color c = Color.Lerp(colorLow, colorHigh, t);
                mpbShared.SetColor("_Color", c);
                mpbShared.SetColor("_BaseColor", c);
                // rim stronger for taller voxels
                mpbShared.SetColor("_RimColor", Color.Lerp(Color.black, rimColor, Mathf.Clamp01(t * 1.2f)));
                mpbShared.SetFloat("_RimPower", rimPower);
                r.SetPropertyBlock(mpbShared);
            }
        }

        if (verboseLogs && Time.frameCount % 30 == 0)
        {
            // Log a small summary every 30 frames
            int texW = texW2; // reuse outer texture width variable
            float sample0 = heightTexture.GetPixelBilinear(0.5f / texW, frame / (float)Mathf.Max(1, frameCount - 1)).r;
            float sampleFirst = heightTexture.GetPixelBilinear(0.5f / texW, frameNormalized).r;
            float sampleMid = heightTexture.GetPixelBilinear((texW / 2f) / texW, frameNormalized).r;
            Debug.Log($"SpectrogramTexturePlayer.Update: frame={frame}/{frameCount-1} norm={frameNormalized:F3} sampleFirst={sampleFirst:F3} sampleMid={sampleMid:F3} sample0={sample0:F3}");
        }
    }

    private void OnDestroy()
    {
        ClearInstances();
    }
}
