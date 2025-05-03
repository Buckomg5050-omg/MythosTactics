using UnityEngine;

/// <summary>
/// Defines the base characteristics and modifiers associated with a specific race.
/// These values are typically used in conjunction with class data to determine final unit stats.
/// </summary>
[CreateAssetMenu(fileName = "NewRace", menuName = "Tactics/Data/Race Data")]
public class RaceSO : ScriptableObject
{
    [Tooltip("The display name of the race (e.g., Human, Hawkman, Lizardman).")]
    public string raceName = "Default Race";

    [Tooltip("Base movement range for this race before class or other modifiers.")]
    public int baseMovementPoints = 4;

    [Tooltip("Modifier added to a unit's base Health (HP) from its class. Can be positive or negative.")]
    public int baseHealthModifier = 0;

    [Tooltip("Modifier added to a unit's base Attack Power from its class. Can be positive or negative.")]
    public int baseAttackModifier = 0;

    [Tooltip("Modifier added to a unit's base Defense from its class. Can be positive or negative.")]
    public int baseDefenseModifier = 0;

    [Tooltip("Base maximum Magic Points (MP) granted by this race before class or other modifiers.")]
    public int baseMaxMp = 10;

    // Potential future additions:
    // public int baseSpeedModifier = 0;
    // public int baseMagicPowerModifier = 0;
    // public int baseMagicResistanceModifier = 0;
    // public bool canFly = false;
    // public List<TraitSO> racialTraits; // Link to other ScriptableObjects defining special abilities
}
