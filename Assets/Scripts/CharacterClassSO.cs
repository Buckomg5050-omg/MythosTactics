using UnityEngine;
using System.Collections.Generic; // Ensures List<> is available

/// <summary>
/// Defines the characteristics, stat modifiers, visual representation,
/// and potentially learnable skills associated with a specific character class.
/// These modifiers are applied on top of racial base stats.
/// </summary>
[CreateAssetMenu(fileName = "NewClass", menuName = "Tactics/Data/Class Data")]
public class CharacterClassSO : ScriptableObject
{
    [Tooltip("The display name of the class (e.g., Warrior, Archer, Knight).")]
    public string className = "Default Class";

    [Tooltip("The visual representation (icon/portrait) for this class.")]
    public Sprite classSprite;

    [Header("Stat Modifiers")]
    [Tooltip("Modifier applied to the unit's base Health (HP). This could represent base HP for the class or a bonus.")]
    public int healthStatModifier = 0;

    [Tooltip("Modifier applied to the unit's base Attack Power.")]
    public int attackStatModifier = 0;

    [Tooltip("Modifier applied to the unit's base Defense.")]
    public int defenseStatModifier = 0;

    [Tooltip("Modifier applied to the unit's base Movement Points from their race. Can be positive or negative (e.g., -1 for Knight).")]
    public int movementStatModifier = 0;

    [Tooltip("Modifier applied to the unit's base maximum Magic Points (MP).")]
    public int mpModifier = 0;

    [Header("Skills & Abilities")]
    [Tooltip("List of skills this class inherently possesses or can learn/use.")]
    public List<SkillSO> availableSkills = new List<SkillSO>();

    // --- Placeholders for future additions ---
    // [Header("Other Stat Modifiers")]
    // public int speedStatModifier = 0;
    // public int magicPowerStatModifier = 0;
    // public int magicResistanceStatModifier = 0;

    // [Header("Equipment")]
    // [Tooltip("List of weapon types this class can equip.")]
    // public List<WeaponType> equipableWeaponTypes; // Requires a WeaponType enum or definition
}
