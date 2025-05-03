using UnityEngine;
using System.Collections.Generic;
using System.Linq; // Used for OrderBy/ThenBy

/// <summary>
/// Provides static methods for pathfinding using the A* algorithm on a grid managed by GridManager,
/// considering unit occupancy via GameManager.
/// </summary>
public static class Pathfinder
{
    /// <summary>
    /// Internal helper class representing a node in the pathfinding search space.
    /// </summary>
    private class PathNode
    {
        public Vector3Int gridPosition;
        public int gCost; // Cost from start to this node
        public int hCost; // Heuristic cost from this node to target
        public PathNode parent; // Node came from

        public int fCost => gCost + hCost;

        public PathNode(Vector3Int pos, int g, int h, PathNode p)
        {
            gridPosition = pos;
            gCost = g;
            hCost = h;
            parent = p;
        }
    }

    /// <summary>
    /// Finds the shortest path between two points on the grid using the A* algorithm,
    /// considering terrain walkability and unit occupancy (blocking path *through* units,
    /// but allowing path *to* an occupied target).
    /// </summary>
    /// <param name="start">The starting grid cell position.</param>
    /// <param name="target">The target grid cell position.</param>
    /// <param name="gridManager">Reference to the GridManager for tile data.</param>
    /// <param name="gameManager">Reference to the GameManager to check for unit occupancy.</param>
    /// <returns>A List of Vector3Int representing the path from start to target (inclusive),
    /// or an empty list if no path is found or inputs are invalid.</returns>
    public static List<Vector3Int> FindPath(Vector3Int start, Vector3Int target, GridManager gridManager, GameManager gameManager) // Added gameManager parameter
    {
        // --- Input Validation and Setup ---
        if (gridManager == null || gameManager == null) // Check gameManager too
        {
            Debug.LogError("Pathfinder: GridManager or GameManager reference is null.");
            return new List<Vector3Int>();
        }

        TileData startTileData = gridManager.GetTileDataAt(start);
        TileData targetTileData = gridManager.GetTileDataAt(target); // Target tile walkability check is sometimes skipped if attacking

        if (startTileData == null || !startTileData.isWalkable)
        {
            return new List<Vector3Int>(); // Start point invalid
        }
        // We might allow pathing TO an unwalkable target tile if attacking, depends on game rules.
        // Let's assume for now the target tile itself must be fundamentally valid grid space, even if occupied/unwalkable.
        if (targetTileData == null)
        {
             return new List<Vector3Int>(); // Target tile doesn't exist on map
        }
        // If you require the target tile itself to be walkable (e.g., for movement, not attack):
        // if (targetTileData == null || !targetTileData.isWalkable) return new List<Vector3Int>();


        if (start == target) return new List<Vector3Int> { start };

        var openSet = new List<PathNode>();
        var closedSet = new HashSet<Vector3Int>();

        int startHCost = CalculateHeuristic(start, target);
        var startNode = new PathNode(start, 0, startHCost, null);
        openSet.Add(startNode);

        Vector3Int[] neighbourOffsets = {
            Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right
        };

        // --- A* Main Loop ---
        while (openSet.Count > 0)
        {
            PathNode currentNode = openSet.OrderBy(node => node.fCost).ThenBy(node => node.hCost).First();

            openSet.Remove(currentNode);
            closedSet.Add(currentNode.gridPosition);

            if (currentNode.gridPosition == target)
            {
                return ReconstructPath(currentNode); // Target reached
            }

            // --- Process Neighbors ---
            foreach (var offset in neighbourOffsets)
            {
                Vector3Int neighbourPos = currentNode.gridPosition + offset;

                // Skip if already fully evaluated
                if (closedSet.Contains(neighbourPos)) continue;

                // --- Tile Data Check ---
                TileData neighbourTileData = gridManager.GetTileDataAt(neighbourPos);
                // If neighbor tile doesn't exist on map or isn't walkable (terrain-wise)
                if (neighbourTileData == null || !neighbourTileData.isWalkable)
                {
                    // Mark as closed so we don't try again (avoids repeated GetTileDataAt)
                    closedSet.Add(neighbourPos);
                    continue;
                }

                // --- NEW: Occupancy Check ---
                UnitController occupant = gameManager.GetUnitAt(neighbourPos);
                if (occupant != null) // Tile is occupied
                {
                    // Requirement 2b: If it's occupied BUT it's the target, allow it.
                    // Otherwise, block pathing through it.
                    if (neighbourPos != target)
                    {
                        // Occupied by a unit and it's NOT the target destination. Treat as obstacle.
                        closedSet.Add(neighbourPos); // Add to closed set to block pathing
                        continue; // Skip processing this neighbor further
                    }
                    // If neighbourPos == target, we allow calculating cost to it below.
                }
                // --- End Occupancy Check ---


                // --- Calculate Cost ---
                int movementCostToNeighbour = neighbourTileData.movementCost;
                int tentativeGCost = currentNode.gCost + movementCostToNeighbour;

                // Check if neighbor is already in open set
                PathNode existingNeighbourNode = openSet.FirstOrDefault(node => node.gridPosition == neighbourPos);

                if (existingNeighbourNode == null)
                {
                    // Add new node to open set
                    int hCost = CalculateHeuristic(neighbourPos, target);
                    var newNode = new PathNode(neighbourPos, tentativeGCost, hCost, currentNode);
                    openSet.Add(newNode);
                }
                else if (tentativeGCost < existingNeighbourNode.gCost)
                {
                    // Found shorter path to existing node in open set
                    existingNeighbourNode.gCost = tentativeGCost;
                    existingNeighbourNode.parent = currentNode;
                }
            }
        }

        // --- No Path Found ---
        return new List<Vector3Int>(); // Return empty list
    }

    /// <summary>
    /// Calculates the Manhattan distance heuristic.
    /// </summary>
    private static int CalculateHeuristic(Vector3Int from, Vector3Int to)
    {
        return Mathf.Abs(from.x - to.x) + Mathf.Abs(from.y - to.y);
    }

    /// <summary>
    /// Reconstructs the path from the target node back to the start.
    /// </summary>
    private static List<Vector3Int> ReconstructPath(PathNode targetNode)
    {
        var path = new List<Vector3Int>();
        PathNode currentNode = targetNode;
        while (currentNode != null)
        {
            path.Add(currentNode.gridPosition);
            currentNode = currentNode.parent;
        }
        path.Reverse();
        return path;
    }
}