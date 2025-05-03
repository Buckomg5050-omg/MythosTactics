using UnityEngine;

[CreateAssetMenu(fileName = "NewSkill", menuName = "Tactics/Data/Skill Data")]
public class SkillSO : ScriptableObject
{
    [Tooltip("The display name of the skill (e.g., Power Strike, Fireball, Heal).")]
    public string skillName = "Default Skill";

    [Tooltip("A brief description of the skill's effect, suitable for UI tooltips.")]
    [TextArea(3, 5)]
    public string description = "Skill description goes here.";

    [Tooltip("The maximum range (in tiles) from which this skill can be targeted.")]
    public int range = 1;

    [Tooltip("Does this skill primarily target enemies (true) or allies/self (false)? Affects targeting logic.")]
    public bool isHarmful = true;

    [Tooltip("Base damage or healing amount for the skill. How this is used depends on the skill's implementation.")]
    public int basePower = 5;

    [Tooltip("MP cost required to use this skill.")]
    public int mpCost = 0;

    [Header("Status Effect (Optional)")]
    [Tooltip("Status effect applied by the skill, if any.")]
    public StatusEffectSO statusEffectApplied = null;

    [Tooltip("Duration (in turns) of the status effect; 0 to use the base duration from the StatusEffectSO.")]
    public int statusEffectDuration = 0;

    // --- Placeholders for future additions ---

    // [Header("Advanced Properties")]
    // [Tooltip("Radius of the area of effect (0 for single target).")]
    // public int areaOfEffect = 0;

    // [Tooltip("Resource cost to use the skill (e.g., MP, TP).")]
    // public int cost = 0;
    // public ResourceType costType = ResourceType.MP; // Example enum needed

    // [Tooltip("Name of an animation trigger to play when the skill is used.")]
    // public string animationTrigger = "";

    // public ElementType skillElement = ElementType.Physical; // Example enum needed
    // public SkillCategory skillCategory = SkillCategory.Attack; // Example enum needed
}
