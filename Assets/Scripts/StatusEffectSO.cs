using UnityEngine;
using UnityEngine.UI;

[CreateAssetMenu(fileName = "NewStatusEffect", menuName = "Tactics/Status Effect")]
public class StatusEffectSO : ScriptableObject
{
    public enum AffectedStat
    {
        None,
        ATK,
        DEF,
        MOV
    }

    [Header("Basic Info")]
    public string effectName = "New Status Effect";
    public AffectedStat statAffected = AffectedStat.None;
    public int modifierValue = 0;    // The amount to add/subtract
    public int baseDuration = 1;     // Default duration in turns
    public bool isBuff = true;       // True for beneficial, False for detrimental

    [TextArea]
    public string description = "";

    [Header("Icon")]
    public Sprite icon = null;
}
