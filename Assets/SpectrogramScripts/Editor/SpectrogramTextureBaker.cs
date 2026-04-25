using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System;

public class SpectrogramTextureBaker : EditorWindow
{
    private string csvRelativePath = "Assets/data/U1_ctrl_0001.csv";
    private string outputRelativePath = "Assets/Generated/HeightMaps";
    private int targetWidth = 256;
    private int targetHeight = 256;
    private bool normalize = true;
    private int gridWidth = 8;
    private int gridHeight = 8;

    [MenuItem("Spectrogram/Bake Height Texture from CSV")]
    static void ShowWindow()
    {
        GetWindow<SpectrogramTextureBaker>("Spectrogram Baker");
    }

    private void OnGUI()
    {
        GUILayout.Label("CSV -> Height Texture Baker", EditorStyles.boldLabel);
        csvRelativePath = EditorGUILayout.TextField("CSV Path", csvRelativePath);
        outputRelativePath = EditorGUILayout.TextField("Output Folder", outputRelativePath);
        targetWidth = EditorGUILayout.IntField("Target Width", targetWidth);
        targetHeight = EditorGUILayout.IntField("Target Height", targetHeight);
        gridWidth = EditorGUILayout.IntField("Grid Width (cols)", gridWidth);
        gridHeight = EditorGUILayout.IntField("Grid Height (rows)", gridHeight);
        normalize = EditorGUILayout.Toggle("Normalize", normalize);

        if (GUILayout.Button("Bake Height Texture"))
        {
            try
            {
                Bake();
            }
            catch (Exception e)
            {
                Debug.LogError("Bake failed: " + e);
            }
        }
    }

    private void Bake()
    {
        string absCsv = Path.GetFullPath(csvRelativePath);
        if (!File.Exists(absCsv))
        {
            Debug.LogError($"CSV not found: {absCsv}");
            return;
        }

        string[] rawLines = File.ReadAllLines(absCsv);
        // Remove empty lines
        var nonEmpty = rawLines.Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();

        // Heuristically find first data line: the first line where at least
        // half the comma-separated tokens parse as floats.
        int dataStart = 0;
        int foundCols = 0;
        for (int i = 0; i < nonEmpty.Length; i++)
        {
            var tokens = nonEmpty[i].Split(',');
            int good = 0;
            for (int t = 0; t < tokens.Length; t++)
            {
                if (float.TryParse(tokens[t], out _)) good++;
            }
            if (tokens.Length > 0 && good >= Math.Max(1, tokens.Length / 2))
            {
                dataStart = i;
                foundCols = tokens.Length;
                break;
            }
        }

        if (dataStart >= nonEmpty.Length)
        {
            Debug.LogError("No numeric data detected in CSV. Check the file format.");
            return;
        }

        var dataLines = nonEmpty.Skip(dataStart).ToArray();
        int rows = dataLines.Length;
        // Recompute columns as max token count across data lines
        int cols = dataLines.Max(l => l.Split(',').Length);

        float[,] data = new float[rows, cols];
        float min = float.MaxValue;
        float max = float.MinValue;

        for (int r = 0; r < rows; r++)
        {
            string[] parts = dataLines[r].Split(',');
            for (int c = 0; c < cols; c++)
            {
                float v = 0f;
                if (c < parts.Length && float.TryParse(parts[c], out float parsed))
                {
                    v = parsed;
                }
                data[r, c] = v;
                if (v < min) min = v;
                if (v > max) max = v;
            }
        }

        Debug.Log($"SpectrogramTextureBaker: parsed rows={rows} cols={cols} dataStartLine={dataStart} min={min} max={max}");

        // Create an output texture using the requested target size. Large
        // datasets may exceed the platform texture max (reported error), so
        // we downsample from the source CSV to the requested size using
        // bilinear sampling.
        // We'll produce an atlas where each frame occupies `gridHeight` rows
        // and `gridWidth` columns. Atlas width = gridWidth. Atlas height = rows * gridHeight.
        int frameCount = rows;
        int outW = Mathf.Max(1, gridWidth);
        int outH = Mathf.Max(1, gridHeight * frameCount);

        Texture2D tex = new Texture2D(outW, outH, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        // Helper to sample source frequency data linearly across original cols
        Func<float, int, float> sampleFreqForTime = (float freqNorm, int timeIdx) =>
        {
            if (cols <= 1) return 0f;
            float fx = Mathf.Clamp(freqNorm * (cols - 1), 0f, cols - 1);
            int x0 = Mathf.FloorToInt(fx);
            int x1 = Mathf.Min(x0 + 1, cols - 1);
            float tx = fx - x0;
            float v00 = data[timeIdx, x0];
            float v10 = data[timeIdx, x1];
            float v = Mathf.Lerp(v00, v10, tx);
            if (normalize)
            {
                if (Math.Abs(max - min) < 1e-6f) return 0f;
                return Mathf.Clamp01((v - min) / (max - min));
            }
            return v;
        };

        int targetFreqCount = gridWidth * gridHeight;
        for (int fIdx = 0; fIdx < frameCount; fIdx++)
        {
            for (int gy = 0; gy < gridHeight; gy++)
            {
                for (int gx = 0; gx < gridWidth; gx++)
                {
                    int cellIndex = gy * gridWidth + gx; // 0..targetFreqCount-1
                    float freqNorm = (targetFreqCount == 1) ? 0f : (cellIndex / (float)(targetFreqCount - 1));
                    float v = sampleFreqForTime(freqNorm, fIdx);
                    int px = gx;
                    int py = fIdx * gridHeight + gy; // row in atlas
                    Color col = new Color(v, v, v, 1f);
                    tex.SetPixel(px, outH - 1 - py, col);
                }
            }
        }

        tex.Apply();

        // Ensure output folder exists
        string absOut = Path.GetFullPath(outputRelativePath);
        if (!Directory.Exists(absOut)) Directory.CreateDirectory(absOut);

        string baseName = Path.GetFileNameWithoutExtension(csvRelativePath);
        string outFile = Path.Combine(absOut, baseName + "_height.png");
        File.WriteAllBytes(outFile, tex.EncodeToPNG());
        Debug.Log($"Wrote height texture to {outFile}");

        // Refresh AssetDatabase and select the imported texture
        AssetDatabase.Refresh();
        string assetPath = outputRelativePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + "/" + baseName + "_height.png";
        TextureImporter ti = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (ti != null)
        {
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = false;
            ti.mipmapEnabled = false;
            ti.isReadable = true;
            ti.SaveAndReimport();
        }
        UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        Selection.activeObject = asset;
    }
}
