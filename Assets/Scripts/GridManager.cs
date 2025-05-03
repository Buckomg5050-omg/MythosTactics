using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic; // Required for List, Queue, Dictionary, HashSet

/// <summary>
/// Manages conversions between world positions and grid cell positions
/// for a specified Tilemap, provides access to tile data, and calculates movement ranges.
/// </summary>
public class GridManager : MonoBehaviour
{
    /// <summary>
    /// Public reference to the Tilemap component used for grid calculations.
    /// Assign this in the Unity Inspector.
    /// </summary>
    [Tooltip("The Tilemap used for ground grid calculations, data retrieval, and pathfinding.")]
    public Tilemap groundTilemap;

    // --- Coordinate Conversion & Tile Data --- (Keep WorldToGrid, GridToWorld, GetTileDataAt as they are)

    public Vector3Int WorldToGrid(Vector3 worldPos)
    {
        if (groundTilemap == null) { /* ... error log ... */ return Vector3Int.zero; }
        return groundTilemap.WorldToCell(worldPos);
    }

    public Vector3 GridToWorld(Vector3Int gridPos)
    {
        if (groundTilemap == null) { /* ... error log ... */ return Vector3.zero; }
        return groundTilemap.GetCellCenterWorld(gridPos);
    }

    public TileData GetTileDataAt(Vector3Int gridPosition)
    {
        if (groundTilemap == null) { /* ... error log ... */ return null; }
        TileBase tileBase = groundTilemap.GetTile(gridPosition);
        if (tileBase == null) { return null; }
        ScriptableTile scriptableTile = tileBase as ScriptableTile;
        if (scriptableTile != null) { return scriptableTile.tileData; } // Simplified return, assumes null check is done in SO or user handles null
        return null; // Not a ScriptableTile
    }


    // --- Movement Range Calculation (MODIFIED) ---

    /// <summary>
    /// Calculates all reachable tile positions from a starting position within a given movement range,
    /// considering tile movement costs, walkability, and unit occupancy. Uses Breadth-First Search.
    /// </summary>
    /// <param name="startPos">The starting grid cell position.</param>
    /// <param name="moveRange">The maximum movement points available.</param>
    /// <param name="gameManager">Reference to the GameManager to check for occupied tiles.</param>
    /// <param name="movingUnit">The specific unit whose range is being calculated (to avoid self-blocking).</param>
    /// <returns>A List of Vector3Int containing all reachable grid positions, including the start position.</returns>
    public List<Vector3Int> CalculateReachableTiles(Vector3Int startPos, int moveRange, GameManager gameManager, UnitController movingUnit)
    {
        // --- Initialization & Edge Case Checks ---
        var reachableTiles = new List<Vector3Int>();
        if (groundTilemap == null || gameManager == null || movingUnit == null || moveRange < 0)
        {
            Debug.LogError($"GridManager: Cannot CalculateReachableTiles. Invalid inputs provided. " +
                           $"Tilemap: {groundTilemap!=null}, GameManager: {gameManager!=null}, MovingUnit: {movingUnit!=null}, Range: {moveRange}", this);
            return reachableTiles; // Return empty list if setup is invalid
        }

        var queue = new Queue<Vector3Int>();
        var costSoFar = new Dictionary<Vector3Int, int>();
        var visited = new HashSet<Vector3Int>(); // Tracks tiles added to queue/processed

        // Check the starting tile itself (walkability)
        TileData startTileData = GetTileDataAt(startPos);
        // No need to check occupancy for start tile using GetUnitAt here, as BFS starts from it.
        if (startTileData == null || !startTileData.isWalkable)
        {
             Debug.LogWarning($"GridManager: Start position {startPos} is invalid or not walkable for movement calculation.", this);
             return reachableTiles;
        }

        // Initialize BFS
        queue.Enqueue(startPos);
        visited.Add(startPos);
        costSoFar[startPos] = 0;

        Vector3Int[] neighbourOffsets = {
            Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right
        };

        // --- BFS Loop ---
        while (queue.Count > 0)
        {
            Vector3Int currentPos = queue.Dequeue();

            // Process neighbors
            foreach (var offset in neighbourOffsets)
            {
                Vector3Int neighbourPos = currentPos + offset;

                // --- Boundary/Visited Checks ---
                // Skip if already processed efficiently
                if (visited.Contains(neighbourPos)) continue;

                // --- Tile Data Checks ---
                TileData neighbourTileData = GetTileDataAt(neighbourPos);
                if (neighbourTileData == null || !neighbourTileData.isWalkable)
                {
                    continue; // Skip invalid or unwalkable terrain tiles
                }

                // --- NEW: Occupancy Check ---
                UnitController occupant = gameManager.GetUnitAt(neighbourPos);
                if (occupant != null && occupant != movingUnit) // Check if occupied by ANOTHER unit
                {
                    // Treat tile as blocked for pathing if occupied by someone else
                    visited.Add(neighbourPos); // Mark as visited so we don't check it again via another path
                    continue; // Skip this neighbor entirely, cannot path through it
                }
                // --- End Occupancy Check ---


                // --- Cost and Range Checks ---
                int currentCost = costSoFar[currentPos];
                int moveCostToNeighbour = neighbourTileData.movementCost;
                int newCost = currentCost + moveCostToNeighbour;

                if (newCost <= moveRange)
                {
                    // If within range and not blocked, add to queue and store cost
                    costSoFar[neighbourPos] = newCost;
                    queue.Enqueue(neighbourPos);
                    visited.Add(neighbourPos); // Mark as visited/queued
                }
                else
                {
                    // If out of range via this path, still mark visited to avoid reprocessing
                    // from potentially longer paths later in the BFS.
                    visited.Add(neighbourPos);
                }
            }
        }

        // --- Final Result ---
        // The keys of costSoFar are all the tiles reached within range *and* not blocked
        reachableTiles.AddRange(costSoFar.Keys);
        return reachableTiles;
    }
}