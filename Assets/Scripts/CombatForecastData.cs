// CombatForecastData.cs

using System.Collections.Generic;
using UnityEngine; // Required for Sprite

/// <summary>
/// Holds the predicted results of a potential combat action.
/// Used for UI display before the player commits to an action.
/// </summary>
[System.Serializable] // Add if you want to see this in the Unity Inspector (though structs aren't ideal for Inspector editing)
public readonly struct CombatForecastData
{
    /// <summary>
    /// Expected damage to be dealt to the target.
    /// </summary>
    public readonly int PredictedDamage;

    /// <summary>
    /// Name of the unit performing the action.
    /// </summary>
    public readonly string AttackerName;

    /// <summary>
    /// Name of the unit targeted by the action.
    /// </summary>
    public readonly string TargetName;

    /// <summary>
    /// The MP cost required to perform the action (0 for basic attacks or actions with no cost).
    /// </summary>
    public readonly int MpCost;

    /// <summary>
    /// A list detailing the status effects predicted to be applied by the action.
    /// Note: This is a readonly reference to a list; the list itself *can* be modified after creation
    /// if not handled carefully elsewhere. Consider using IReadOnlyList if strict immutability is paramount.
    /// </summary>
    public readonly List<StatusEffectForecast> StatusEffects;

    /// <summary>
    /// Initializes a new instance of the <see cref="CombatForecastData"/> struct.
    /// </summary>
    /// <param name="predictedDamage">Expected damage.</param>
    /// <param name="attackerName">Attacker's name.</param>
    /// <param name="targetName">Target's name.</param>
    /// <param name="mpCost">MP cost of the action.</param>
    /// <param name="statusEffects">List of predicted status effects. If null, an empty list is created.</param>
    public CombatForecastData(int predictedDamage, string attackerName, string targetName, int mpCost, List<StatusEffectForecast> statusEffects)
    {
        this.PredictedDamage = predictedDamage;
        this.AttackerName = attackerName ?? "Unknown Attacker"; // Basic null check
        this.TargetName = targetName ?? "Unknown Target";     // Basic null check
        this.MpCost = mpCost;
        // Ensure we always have a list instance, even if it's empty.
        this.StatusEffects = statusEffects ?? new List<StatusEffectForecast>();
    }

    // Overload constructor for cases with no status effects for convenience
     public CombatForecastData(int predictedDamage, string attackerName, string targetName, int mpCost)
        : this(predictedDamage, attackerName, targetName, mpCost, null) // Calls the main constructor
    {
    }


    /// <summary>
    /// Holds forecast information for a single status effect application.
    /// </summary>
    [System.Serializable] // Add if needed for Inspector visibility within a list
    public readonly struct StatusEffectForecast
    {
        /// <summary>
        /// Display name of the status effect.
        /// </summary>
        public readonly string EffectName;

        /// <summary>
        /// Predicted duration of the status effect in turns.
        /// </summary>
        public readonly int Duration;

        /// <summary>
        /// UI Icon representing the status effect.
        /// </summary>
        public readonly Sprite Icon; // Note: Sprite is a Unity class (reference type)

        /// <summary>
        /// Initializes a new instance of the <see cref="StatusEffectForecast"/> struct.
        /// </summary>
        /// <param name="effectName">Name of the effect.</param>
        /// <param name="duration">Duration in turns.</param>
        /// <param name="icon">UI icon.</param>
        public StatusEffectForecast(string effectName, int duration, Sprite icon)
        {
            this.EffectName = effectName ?? "Unknown Effect"; // Basic null check
            this.Duration = duration;
            this.Icon = icon; // Sprites can be null in Unity, handle this in UI logic
        }
    }
}