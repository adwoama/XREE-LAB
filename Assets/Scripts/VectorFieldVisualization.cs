using System.Collections.Generic;
using UnityEngine;

public class VectorFieldVisualization : MonoBehaviour
{
    [Header("Vector Field Settings")]
    [SerializeField] private GameObject arrowPrefab; // Prefab for individual arrows
    [SerializeField] private Transform arrowParent; // Parent object for arrows
    [SerializeField] private Vector3 fieldDimensions = new Vector3(10, 10, 10); // Dimensions of the vector field
    [SerializeField] private Vector3 arrowSpacing = new Vector3(1, 1, 1); // Spacing between arrows

    private List<GameObject> arrows = new List<GameObject>();

    /// <summary>
    /// Sets the vector field data and updates the arrows.
    /// </summary>
    /// <param name="vectors">3D array of vectors (x, y, z components).</param>
    public void SetData(Vector3[,,] vectors)
    {
        Clear();

        int xCount = vectors.GetLength(0);
        int yCount = vectors.GetLength(1);
        int zCount = vectors.GetLength(2);

        for (int x = 0; x < xCount; x++)
        {
            for (int y = 0; y < yCount; y++)
            {
                for (int z = 0; z < zCount; z++)
                {
                    Vector3 position = new Vector3(x * arrowSpacing.x, y * arrowSpacing.y, z * arrowSpacing.z);
                    GameObject arrow = Instantiate(arrowPrefab, position, Quaternion.identity, arrowParent);
                    arrow.transform.localScale = new Vector3(1, 1, vectors[x, y, z].magnitude);
                    arrow.transform.rotation = Quaternion.LookRotation(vectors[x, y, z]);
                    arrows.Add(arrow);
                }
            }
        }
    }

    /// <summary>
    /// Clears all arrows from the vector field.
    /// </summary>
    public void Clear()
    {
        foreach (GameObject arrow in arrows)
        {
            Destroy(arrow);
        }
        arrows.Clear();
    }
}