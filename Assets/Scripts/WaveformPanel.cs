using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Renders a continuously-updating waveform using a LineRenderer.
/// Provides a simple label and API to configure frequency/amplitude/noise.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class WaveformPanel : MonoBehaviour
{
    [Header("Graph settings")]
    public int resolution = 256;
    public float width = 0.9f;
    public float height = 0.45f;
    public float updateRate = 60f; // samples per second
    [Tooltip("Thickness of the waveform line in meters (local space)")] 
    public float lineThickness = 0.01f;
    [Tooltip("If true, auto adjust thickness based on distance to camera for readability")] 
    public bool scaleThicknessWithDistance = true;
    [Tooltip("Multiplier applied when auto-scaling thickness")] 
    public float distanceThicknessFactor = 0.0025f;
    [Tooltip("Amplitude scaling factor (0-1); reduces waveform vertical extent to fit better in panel")]
    [Range(0.1f, 1.0f)]
    public float amplitudeScale = 0.8f;

    [Header("Waveform params")]
    public float frequency = 1.0f;
    public float amplitude = 0.8f;
    public float noise = 0.1f;

    [Header("Streaming Mode")]
    [Tooltip("If true, internal synthetic waveform generation is disabled and only external SetSamples() updates are shown.")]
    public bool useExternalData = false;

    [Header("Label")]
    public string channelLabel = "CH?";
    public Color lineColor = Color.green;

    LineRenderer lr;
    float[] buffer;
    float timeAcc;
    TextMesh labelMesh;
    Gradient cachedGradient;

    void Awake()
    {
        lr = GetComponent<LineRenderer>();
        lr.positionCount = resolution;
        lr.useWorldSpace = false;
        // Width curve single constant; we will adjust widthMultiplier in Update for distance scaling.
        lr.widthCurve = AnimationCurve.Constant(0,1,1f);
        // Prefer URP Unlit if available for crisp lines; fallback to Sprites/Default.
        Shader s = Shader.Find("Universal Render Pipeline/Unlit");
        if (s == null) s = Shader.Find("Sprites/Default");
        lr.material = new Material(s);
        lr.material.enableInstancing = true;
        BuildGradient();
        ApplyLineColor();

        buffer = new float[resolution];

        // create a simple 3D text label
        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(transform, false);
        labelGO.transform.localPosition = new Vector3(-width*0.48f, height*0.55f, 0.01f);
        labelMesh = labelGO.AddComponent<TextMesh>();
        labelMesh.text = channelLabel;
        labelMesh.characterSize = 0.08f;
        labelMesh.anchor = TextAnchor.UpperLeft;
        labelMesh.color = Color.white;
    }

    void Start()
    {
        // initialize buffer
        for (int i = 0; i < buffer.Length; i++) buffer[i] = 0f;
    }

    void Update()
    {
        if (!useExternalData)
        {
            // advance samples according to updateRate only if generating internally
            float dt = Time.deltaTime;
            timeAcc += dt * updateRate;
            int steps = Mathf.FloorToInt(timeAcc);
            timeAcc -= steps;
            for (int s = 0; s < steps; s++)
            {
                PushSample(GenerateSample(Time.time + s * (1f / updateRate)));
            }
        }

        // update line renderer points
        for (int i = 0; i < resolution; i++)
        {
            float x = Mathf.Lerp(-width/2f, width/2f, (float)i / (resolution-1));
            float y = buffer[i] * height * amplitudeScale;
            lr.SetPosition(i, new Vector3(x, y, 0f));
        }

        // Thickness handling
        float thickness = lineThickness;
        if (scaleThicknessWithDistance && Camera.main != null)
        {
            float d = Vector3.Distance(Camera.main.transform.position, transform.position);
            // Simple linear scaling; clamp for stability
            thickness = Mathf.Clamp(lineThickness + d * distanceThicknessFactor, lineThickness, lineThickness * 6f);
        }
        lr.widthMultiplier = thickness;
    }

    float GenerateSample(float t)
    {
        // basic sine + noise; can be extended
        float s = Mathf.Sin(2f * Mathf.PI * frequency * t) * amplitude;
        s += (Random.value * 2f - 1f) * noise;
        return Mathf.Clamp(s, -1f, 1f);
    }

    void PushSample(float sample)
    {
        // shift left and append at end
        for (int i = 0; i < buffer.Length - 1; i++) buffer[i] = buffer[i+1];
        buffer[buffer.Length - 1] = sample;
    }

    // Public API
    public void SetLabel(string label)
    {
        channelLabel = label;
        if (labelMesh) labelMesh.text = label;
    }

    public void SetColor(Color c)
    {
        lineColor = c;
        ApplyLineColor();
        if (labelMesh) labelMesh.color = Color.white;
    }

    public void SetThickness(float t)
    {
        lineThickness = Mathf.Max(0.0005f, t);
    }

    void BuildGradient()
    {
        // Simple flat gradient; could extend to fade ends.
        cachedGradient = new Gradient();
        cachedGradient.SetKeys(
            new [] { new GradientColorKey(lineColor, 0f), new GradientColorKey(lineColor, 1f) },
            new [] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
        );
        if (lr) lr.colorGradient = cachedGradient;
    }

    void ApplyLineColor()
    {
        if (!lr) return;
        // Update gradient if color changed
        if (cachedGradient == null || cachedGradient.colorKeys.Length == 0 || cachedGradient.colorKeys[0].color != lineColor)
        {
            BuildGradient();
        }

        // Fallback to start/end colors for non-gradient shaders
        lr.startColor = lineColor;
        lr.endColor = lineColor;

        if (lr.material != null)
        {
            // Try common property names
            if (lr.material.HasProperty("_BaseColor")) lr.material.SetColor("_BaseColor", lineColor);
            else if (lr.material.HasProperty("_Color")) lr.material.SetColor("_Color", lineColor);
        }
    }

    /// <summary>
    /// Directly sets the internal buffer from an external sample array.
    /// Automatically resamples if lengths differ. Input expected in [-1,1].
    /// Call this when streaming real oscilloscope data.
    /// </summary>
    public void SetSamples(float[] samples)
    {
        if (samples == null || samples.Length == 0) return;
        useExternalData = true; // switch to external data mode upon first injection
        if (buffer == null || buffer.Length != resolution) buffer = new float[resolution];

        int srcLen = samples.Length;
        if (srcLen == resolution)
        {
            // Fast path copy
            for (int i = 0; i < resolution; i++) buffer[i] = Mathf.Clamp(samples[i], -1f, 1f);
        }
        else
        {
            // Linear resample
            for (int i = 0; i < resolution; i++)
            {
                float srcIndex = (float)i * (srcLen - 1) / (resolution - 1);
                int i0 = (int)srcIndex;
                int i1 = Mathf.Min(i0 + 1, srcLen - 1);
                float t = srcIndex - i0;
                float v = Mathf.Lerp(samples[i0], samples[i1], t);
                buffer[i] = Mathf.Clamp(v, -1f, 1f);
            }
        }
    }
}
