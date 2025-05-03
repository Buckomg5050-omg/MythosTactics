using UnityEngine;
using System.Collections.Generic; // Required for using List<>

/// <summary>
/// Responsible for visually displaying a range of tiles on the grid
/// by instantiating indicator prefabs.
/// </summary>
public class MovementRangeVisualizer : MonoBehaviour
{
    [Tooltip("The prefab used to visually indicate a tile within range.")]
    public GameObject indicatorPrefab;

    // Private list to keep track of the indicators we've created
    private List<GameObject> activeIndicators = new List<GameObject>();

    /// <summary>
    /// Displays visual indicators on the specified tiles using the GridManager for positioning.
    /// Clears any previously shown range first.
    /// </summary>
    /// <param name="rangeTiles">A list of grid positions (Vector3Int) to highlight.</param>
    /// <param name="gridManager">Reference to the GridManager for coordinate conversion.</param>
    public void ShowRange(List<Vector3Int> rangeTiles, GridManager gridManager)
    {
        // 1. Clear any existing indicators before showing the new range
        ClearRange();

        // Safety check for required components before proceeding
        if (indicatorPrefab == null)
        {
            Debug.LogError("MovementRangeVisualizer: Indicator Prefab is not assigned!", this);
            return; // Stop if prefab is missing
        }
        if (gridManager == null)
        {
            Debug.LogError("MovementRangeVisualizer: GridManager reference is null!", this);
            return; // Stop if gridManager is missing
        }

        // 2. Iterate through the provided tile positions
        foreach (Vector3Int gridPos in rangeTiles)
        {
            // 3. Convert grid position to world position using the GridManager
            Vector3 worldPosition = gridManager.GridToWorld(gridPos);

            // 4. Instantiate the indicator prefab at the calculated world position
            GameObject instance = Instantiate(indicatorPrefab, worldPosition, Quaternion.identity);

            // 5. Add the new indicator instance to our tracking list
            activeIndicators.Add(instance);

            // 6. (Optional) Parent the indicator to this object for hierarchy organization
            instance.transform.SetParent(this.transform);
        }

        // Optional Log
        // Debug.Log($"MovementRangeVisualizer: Displaying {activeIndicators.Count} range indicators.");
    }

    /// <summary>
    /// Destroys all currently active range indicators and clears the tracking list.
    /// </summary>
    public void ClearRange()
    {
        // 1. Iterate through the list of active indicator GameObjects
        foreach (GameObject indicator in activeIndicators)
        {
            // Check if not already destroyed (important if ClearRange is called rapidly)
            if (indicator != null)
            {
                 // 2. Destroy the GameObject
                Destroy(indicator);
            }
        }

        // 3. Clear the list itself, removing all (now potentially null) references
        activeIndicators.Clear();

        // Optional Log
        // Debug.Log("MovementRangeVisualizer: Cleared range indicators.");
    }

    // Optional: Ensure cleanup if this object itself is destroyed while indicators are active
    /*
    void OnDestroy()
    {
        ClearRange(); // Ensure indicators are cleaned up when the visualizer is destroyed
    }
    */
}