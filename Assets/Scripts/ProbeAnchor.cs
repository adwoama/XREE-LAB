using UnityEngine;

public class ProbeAnchor : MonoBehaviour
{
    [Header("Probe Settings")]
    [SerializeField] private string probeName = "Probe"; // Name of the probe
    [SerializeField] private Vector3 probePosition; // Position of the probe
    [SerializeField] private GameObject markerPrefab; // Prefab for the probe marker

    private GameObject markerInstance;

    void Start()
    {
        // Instantiate the marker at the probe position
        if (markerPrefab != null)
        {
            markerInstance = Instantiate(markerPrefab, probePosition, Quaternion.identity, transform);
            UpdateMarkerLabel();
        }
        else
        {
            Debug.LogWarning("ProbeAnchor: No marker prefab assigned.");
        }
    }

    /// <summary>
    /// Updates the probe position and marker.
    /// </summary>
    /// <param name="position">New position of the probe.</param>
    public void SetPosition(Vector3 position)
    {
        probePosition = position;
        if (markerInstance != null)
        {
            markerInstance.transform.position = probePosition;
        }
    }

    /// <summary>
    /// Updates the probe name and marker label.
    /// </summary>
    /// <param name="name">New name of the probe.</param>
    public void SetName(string name)
    {
        probeName = name;
        UpdateMarkerLabel();
    }

    private void UpdateMarkerLabel()
    {
        if (markerInstance != null)
        {
            TextMesh label = markerInstance.GetComponentInChildren<TextMesh>();
            if (label != null)
            {
                label.text = probeName;
            }
        }
    }
}