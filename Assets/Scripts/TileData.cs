using UnityEngine;

/// <summary>
/// ScriptableObject to hold data associated with a specific type of tile
/// in a grid-based game (e.g., movement cost, walkability).
/// </summary>
[CreateAssetMenu(fileName = "NewTileData", menuName = "Tactics/Tile Data")]
public class TileData : ScriptableObject
{
    [Tooltip("A descriptive name for the tile type (e.g., Grass, Forest, Water).")]
    public string tileName = "Default Tile"; // Provide a sensible default

    [Tooltip("The cost for a unit to enter or move through this tile.")]
    public int movementCost = 1; // Default movement cost

    [Tooltip("Can units normally end their movement or stand on this tile?")]
    public bool isWalkable = true; // Most tiles are walkable by default

    [Tooltip("(Optional) A sprite that can be used to visually represent this tile type, potentially used by UI or other systems.")]
    public Sprite tileSprite = null; // Optional sprite reference, defaults to null
}