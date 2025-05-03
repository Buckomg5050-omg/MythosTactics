using UnityEngine;
using UnityEngine.Tilemaps; // Required for TileBase, ITilemap, and the TileData struct

/// <summary>
/// A custom TileBase class that allows linking Tile assets in the editor
/// directly to a TileData ScriptableObject. This separates the visual
/// representation (the Tile asset) from the gameplay data (the TileData asset).
/// </summary>
[CreateAssetMenu(fileName = "NewScriptableTile", menuName = "Tactics/Scriptable Tile")]
public class ScriptableTile : TileBase
{
    [Tooltip("Reference to the ScriptableObject containing the gameplay data for this tile type.")]
    public TileData tileData; // Reference to our TileData ScriptableObject

    /// <summary>
    /// This method is called by the Tilemap system to get the rendering data for the tile
    /// at the specified position. We use it to set the tile's sprite based on the
    /// linked TileData ScriptableObject.
    /// </summary>
    /// <param name="position">The cell position of the tile within the tilemap.</param>
    /// <param name="tilemap">The tilemap the tile belongs to.</param>
    /// <param name="targetTileData">A struct (UnityEngine.Tilemaps.TileData) to be populated with rendering information (sprite, color, etc.).
    /// THIS is the parameter we need to modify. Note the different type from our field 'this.tileData'.</param>
    public override void GetTileData(Vector3Int position, ITilemap tilemap, ref UnityEngine.Tilemaps.TileData targetTileData)
    {
        // Set default values first in the struct we were passed
        targetTileData.sprite = null; // Default to no sprite
        targetTileData.color = Color.white;
        targetTileData.transform = Matrix4x4.identity;
        targetTileData.gameObject = null;
        targetTileData.flags = TileFlags.LockTransform; // Common default flag
        targetTileData.colliderType = Tile.ColliderType.None; // Default to no collider, change if needed

        // Check if our custom TileData ScriptableObject reference is assigned
        if (this.tileData != null)
        {
            // If assigned, use the sprite defined within our ScriptableObject
            // to set the sprite property of the struct parameter.
            targetTileData.sprite = this.tileData.tileSprite;

            // You could potentially set other properties based on this.tileData here too:
            // if (!this.tileData.isWalkable)
            // {
            //     targetTileData.color = new Color(0.8f, 0.8f, 0.8f, 1f); // Example: slightly greyed out
            // }
        }
        else
        {
            // Optional: Log a warning if the TileData SO is missing.
            // Debug.LogWarning($"ScriptableTile at {position} ({this.name}) is missing its TileData reference.", this);
        }
    }

    /// <summary>
    /// This method is called by the Tilemap system to check if the tile has animation data.
    /// For this basic implementation, we return false as we are not using tile animations.
    /// </summary>
    /// <param name="position">The cell position of the tile within the tilemap.</param>
    /// <param name="tilemap">The tilemap the tile belongs to.</param>
    /// <param name="tileAnimationData">A struct to be populated with animation information if returning true.</param>
    /// <returns>True if the tile has animation data, false otherwise.</returns>
    public override bool GetTileAnimationData(Vector3Int position, ITilemap tilemap, ref TileAnimationData tileAnimationData)
    {
        // We don't have animation data in this basic version
        return false;
    }
}