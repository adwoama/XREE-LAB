using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Draws an oscilloscope-style grid (graticule) with voltage and time scale labels.
/// Attach to the same GameObject as WaveformPanel or as a child.
/// </summary>
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
    public float gridLineThickness = 0.008f;
    [Tooltip("Grid line color (usually subtle gray)")]
    public Color gridColor = new Color(0.4f, 0.4f, 0.4f, 1.0f);
    [Tooltip("Major division line color (brighter)")]
    public Color majorGridColor = new Color(0.6f, 0.6f, 0.6f, 1.0f);

    [Header("Scale Labels")]
    [Tooltip("Volts per division (e.g., 0.5V, 1V, 2V)")]
    public float voltsPerDiv = 1.0f;
    [Tooltip("Time per division in microseconds (e.g., 10µs, 100µs, 1ms)")]
    public float timePerDivUS = 100f;
    [Tooltip("Show scale labels")]
    public bool showLabels = true;
    [Tooltip("Label text size")]
    public float labelSize = 0.05f;

    private List<LineRenderer> gridLines = new List<LineRenderer>();
    private GameObject gridParent;
    private GameObject labelsParent;
    private TextMesh voltLabel;
    private TextMesh timeLabel;
    private Material gridMaterial;

    void Awake()
    {
        // Setup shared material for all grid lines
        Shader s = Shader.Find("Universal Render Pipeline/Unlit");
        if (s == null) s = Shader.Find("Sprites/Default");
        gridMaterial = new Material(s);
        
        if (gridMaterial.HasProperty("_BaseColor")) 
            gridMaterial.SetColor("_BaseColor", gridColor);
        else if (gridMaterial.HasProperty("_Color")) 
            gridMaterial.SetColor("_Color", gridColor);

        BuildGrid();
        
        if (showLabels)
        {
            CreateLabels();
        }
    }

    void BuildGrid()
    {
        // Clear existing grid
        if (gridParent != null)
        {
            DestroyImmediate(gridParent);
            gridLines.Clear();
        }

        gridParent = new GameObject("GridLines");
        gridParent.transform.SetParent(transform, false);

        float halfWidth = gridWidth / 2f;
        float halfHeight = gridHeight / 2f;

        // Draw vertical lines (time divisions)
        for (int i = 0; i <= horizontalDivisions; i++)
        {
            float x = Mathf.Lerp(-halfWidth, halfWidth, (float)i / horizontalDivisions);
            CreateLine($"VLine_{i}", 
                new Vector3(x, -halfHeight, -0.015f), 
                new Vector3(x, halfHeight, -0.015f));
        }

        // Draw horizontal lines (voltage divisions)
        for (int i = 0; i <= verticalDivisions; i++)
        {
            float y = Mathf.Lerp(-halfHeight, halfHeight, (float)i / verticalDivisions);
            CreateLine($"HLine_{i}", 
                new Vector3(-halfWidth, y, -0.015f), 
                new Vector3(halfWidth, y, -0.015f));
        }
    }

    void CreateLine(string name, Vector3 start, Vector3 end)
    {
        GameObject lineObj = new GameObject(name);
        lineObj.transform.SetParent(gridParent.transform, false);
        
        LineRenderer lr = lineObj.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        lr.useWorldSpace = false;
        lr.widthMultiplier = gridLineThickness;
        lr.material = gridMaterial;
        lr.startColor = gridColor;
        lr.endColor = gridColor;
        lr.sortingOrder = 100; // Render on top
        
        gridLines.Add(lr);
    }

    void CreateLabels()
    {
        labelsParent = new GameObject("GridLabels");
        labelsParent.transform.SetParent(transform, false);

        // Voltage scale label (bottom left)
        var voltLabelGO = new GameObject("VoltLabel");
        voltLabelGO.transform.SetParent(labelsParent.transform, false);
        voltLabelGO.transform.localPosition = new Vector3(-gridWidth * 0.48f, -gridHeight * 0.6f, -0.06f);
        voltLabel = voltLabelGO.AddComponent<TextMesh>();
        voltLabel.characterSize = labelSize;
        voltLabel.anchor = TextAnchor.UpperLeft;
        voltLabel.color = new Color(1f, 1f, 0f, 0.9f); // Yellow
        UpdateVoltLabel();

        // Time scale label (bottom right)
        var timeLabelGO = new GameObject("TimeLabel");
        timeLabelGO.transform.SetParent(labelsParent.transform, false);
        timeLabelGO.transform.localPosition = new Vector3(gridWidth * 0.48f, -gridHeight * 0.6f, -0.06f);
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
        if (gridParent != null && Application.isPlaying)
        {
            BuildGrid();
            UpdateVoltLabel();
            UpdateTimeLabel();
        }
    }
}
