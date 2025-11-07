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

    [Header("Waveform params")]
    public float frequency = 1.0f;
    public float amplitude = 0.8f;
    public float noise = 0.1f;

    [Header("Label")]
    public string channelLabel = "CH?";
    public Color lineColor = Color.green;

    LineRenderer lr;
    float[] buffer;
    float timeAcc;
    TextMesh labelMesh;

    void Awake()
    {
        lr = GetComponent<LineRenderer>();
        lr.positionCount = resolution;
        lr.useWorldSpace = false;
        lr.widthCurve = AnimationCurve.Constant(0,1,0.01f);
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lr.endColor = lineColor;

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
        // advance samples according to updateRate
        float dt = Time.deltaTime;
        timeAcc += dt * updateRate;
        int steps = Mathf.FloorToInt(timeAcc);
        timeAcc -= steps;

        for (int s = 0; s < steps; s++)
        {
            PushSample(GenerateSample(Time.time + s * (1f / updateRate)));
        }

        // update line renderer points
        for (int i = 0; i < resolution; i++)
        {
            float x = Mathf.Lerp(-width/2f, width/2f, (float)i / (resolution-1));
            float y = buffer[i] * height;
            lr.SetPosition(i, new Vector3(x, y, 0f));
        }
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
        if (lr) lr.startColor = lr.endColor = c;
        if (labelMesh) labelMesh.color = Color.white;
    }
}
