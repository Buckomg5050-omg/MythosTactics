using UnityEngine;

[System.Serializable]
public class ActiveStatusEffect
{
    public StatusEffectSO EffectData;      // Reference to the status effect definition
    public int RemainingDuration;         // Number of turns left before this effect expires

    public ActiveStatusEffect(StatusEffectSO effectData, int duration)
    {
        EffectData = effectData;
        RemainingDuration = duration;
    }
}
