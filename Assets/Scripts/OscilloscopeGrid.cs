using UnityEngine;

/// <summary>
/// Draws an oscilloscope-style grid (graticule) with voltage and time scale labels.
/// Attach to the same GameObject as WaveformPanel or as a child.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class OscilloscopeGrid : MonoBehaviour
{
    [Header("Grid Settings")]
    [Tooltip("Number of horizontal divisions (typically 10 for oscilloscopes)")]
    public int horizontalDivisions = 10;
    [Tooltip("Number of vertical divisions (typically 8 for oscilloscopes)")]
    public int verticalDivisions = 8;
    [Tooltip("Width of the grid area (should match WaveformPanel width)")]
    public float gridWidth = 0.9f;
    [Tooltip("Height of the grid area (should match WaveformPanel height)")]
    public float gridHeight = 0.45f;
    [Tooltip("Grid line thickness")]
    public float gridLineThickness = 0.002f;
    [Tooltip("Grid line color (usually subtle gray)")]
    public Color gridColor = new Color(0.3f, 0.3f, 0.3f, 0.6f);
    [Tooltip("Major division line color (brighter)")]
    public Color majorGridColor = new Color(0.5f, 0.5f, 0.5f, 0.8f);

    [Header("Scale Labels")]
    [Tooltip("Volts per division (e.g., 0.5V, 1V, 2V)")]
    public float voltsPerDiv = 1.0f;
    [Tooltip("Time per division in microseconds (e.g., 10µs, 100µs, 1ms)")]
    public float timePerDivUS = 100f;
    [Tooltip("Show scale labels")]
    public bool showLabels = true;
    [Tooltip("Label text size")]
    public float labelSize = 0.05f;

    private LineRenderer lr;
    private GameObject labelsParent;
    private TextMesh voltLabel;
    private TextMesh timeLabel;

    void Awake()
    {
        lr = GetComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.widthMultiplier = gridLineThickness;
        
        // Setup material (use URP Unlit or Sprites/Default)
        Shader s = Shader.Find("Universal Render Pipeline/Unlit");
        if (s == null) s = Shader.Find("Sprites/Default");
        lr.material = new Material(s);
        lr.startColor = gridColor;
        lr.endColor = gridColor;
        
        if (lr.material.HasProperty("_BaseColor")) 
            lr.material.SetColor("_BaseColor", gridColor);
        else if (lr.material.HasProperty("_Color")) 
            lr.material.SetColor("_Color", gridColor);

        BuildGrid();
        
        if (showLabels)
        {
            CreateLabels();
        }
    }

    void BuildGrid()
    {
        // Calculate total line segments needed
        // Vertical lines: horizontalDivisions + 1
        // Horizontal lines: verticalDivisions + 1
        // Each line needs 2 points
        int verticalLines = horizontalDivisions + 1;
        int horizontalLines = verticalDivisions + 1;
        int totalSegments = verticalLines + horizontalLines;
        int pointsNeeded = totalSegments * 3; // 2 points per line + 1 separator (NaN jump)

        lr.positionCount = pointsNeeded;
        int idx = 0;

        float halfWidth = gridWidth / 2f;
        float halfHeight = gridHeight / 2f;

        // Draw vertical lines (time divisions)
        for (int i = 0; i <= horizontalDivisions; i++)
        {
            float x = Mathf.Lerp(-halfWidth, halfWidth, (float)i / horizontalDivisions);
            lr.SetPosition(idx++, new Vector3(x, -halfHeight, 0.01f));
            lr.SetPosition(idx++, new Vector3(x, halfHeight, 0.01f));
            // Separator point (disconnected segment)
            if (i < horizontalDivisions)
                lr.SetPosition(idx++, new Vector3(float.NaN, float.NaN, float.NaN));
        }

        // Draw horizontal lines (voltage divisions)
        for (int i = 0; i <= verticalDivisions; i++)
        {
            float y = Mathf.Lerp(-halfHeight, halfHeight, (float)i / verticalDivisions);
            lr.SetPosition(idx++, new Vector3(-halfWidth, y, 0.01f));
            lr.SetPosition(idx++, new Vector3(halfWidth, y, 0.01f));
            if (i < verticalDivisions)
                lr.SetPosition(idx++, new Vector3(float.NaN, float.NaN, float.NaN));
        }
    }

    void CreateLabels()
    {
        labelsParent = new GameObject("GridLabels");
        labelsParent.transform.SetParent(transform, false);

        // Voltage scale label (bottom left)
        var voltLabelGO = new GameObject("VoltLabel");
        voltLabelGO.transform.SetParent(labelsParent.transform, false);
        voltLabelGO.transform.localPosition = new Vector3(-gridWidth * 0.48f, -gridHeight * 0.6f, 0.02f);
        voltLabel = voltLabelGO.AddComponent<TextMesh>();
        voltLabel.characterSize = labelSize;
        voltLabel.anchor = TextAnchor.UpperLeft;
        voltLabel.color = new Color(1f, 1f, 0f, 0.9f); // Yellow
        UpdateVoltLabel();

        // Time scale label (bottom right)
        var timeLabelGO = new GameObject("TimeLabel");
        timeLabelGO.transform.SetParent(labelsParent.transform, false);
        timeLabelGO.transform.localPosition = new Vector3(gridWidth * 0.48f, -gridHeight * 0.6f, 0.02f);
        timeLabel = timeLabelGO.AddComponent<TextMesh>();
        timeLabel.characterSize = labelSize;
        timeLabel.anchor = TextAnchor.UpperRight;
        timeLabel.color = new Color(1f, 1f, 0f, 0.9f); // Yellow
        UpdateTimeLabel();
    }

    void UpdateVoltLabel()
    {
        if (voltLabel != null)
        {
            voltLabel.text = $"{voltsPerDiv:F2}V/div";
        }
    }

    void UpdateTimeLabel()
    {
        if (timeLabel != null)
        {
            string unit = "µs";
            float value = timePerDivUS;
            
            // Auto-scale units
            if (timePerDivUS >= 1000f)
            {
                value = timePerDivUS / 1000f;
                unit = "ms";
            }
            if (timePerDivUS >= 1000000f)
            {
                value = timePerDivUS / 1000000f;
                unit = "s";
            }
            
            timeLabel.text = $"{value:F1}{unit}/div";
        }
    }

    // Public API to update scales dynamically
    public void SetVoltsPerDiv(float volts)
    {
        voltsPerDiv = volts;
        UpdateVoltLabel();
    }

    public void SetTimePerDiv(float microseconds)
    {
        timePerDivUS = microseconds;
        UpdateTimeLabel();
    }

    void OnValidate()
    {
        // Rebuild grid when parameters change in Inspector
        if (lr != null && Application.isPlaying)
        {
            BuildGrid();
            UpdateVoltLabel();
            UpdateTimeLabel();
        }
    }
}
