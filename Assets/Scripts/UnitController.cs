using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System; // Required for Action
using TacticalRPG;

[RequireComponent(typeof(SpriteRenderer))]
public class UnitController : MonoBehaviour
{
    public static Action<UnitController> OnUnitDied;

    [Header("Unit Definition")]
    public RaceSO unitRace;
    public CharacterClassSO unitClass;

    [Header("Unit Identity & Calculated Stats")]
    public string unitName = "Unit";
    public int maxHealth;
    public int currentHealth;
    public int currentHp => currentHealth;
    public int maxMp;
    public int currentMp;
    public int moveRange;
    public int attackRange = 1;
    public int attackPower;
    public int baseDefense;
    public int speed;
    public int currentCT = 0;

    [Header("Movement")]
    public float moveSpeed = 5.0f;
    public bool isMoving { get; private set; } = false;

    [Header("Grid Position")]
    public Vector3Int gridPosition;

    [Header("Status Effects")]
    public List<ActiveStatusEffect> ActiveStatusEffects = new List<ActiveStatusEffect>();

    public bool IsAlive => currentHealth > 0;
    public bool HasActedThisTurn { get; set; } = false;

    // Effective defense including status modifiers
    public int Defense
    {
        get
        {
            int effectiveDefense = baseDefense;
            foreach (ActiveStatusEffect effect in ActiveStatusEffects)
            {
                if (effect.EffectData.statAffected == StatusEffectSO.AffectedStat.DEF)
                {
                    effectiveDefense += effect.EffectData.modifierValue;
                }
            }
            return Mathf.Max(0, effectiveDefense);
        }
    }

    private const float DISTANCE_TOLERANCE = 0.01f;
    private Coroutine _moveCoroutine = null;
    private bool _isDying = false;
    private SpriteRenderer spriteRenderer;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
            Debug.LogError($"UnitController ({gameObject.name}): Missing SpriteRenderer!", this); // Keep: Error
    }

    public void InitializeStats(string name, RaceSO race, CharacterClassSO charClass)
    {
        if (race == null || charClass == null)
        {
            Debug.LogError($"UnitController ({name}): RaceSO or CharacterClassSO is null!", this); // Keep: Error
            return;
        }

        unitRace  = race;
        unitClass = charClass;
        unitName  = name;

        // Sprite
        if (spriteRenderer != null && unitClass.classSprite != null)
            spriteRenderer.sprite = unitClass.classSprite;
        else
            Debug.LogWarning($"UnitController ({name}): Class '{unitClass.className}' missing sprite.", this); // Keep: Warning

        // HP
        const int BASE_HP = 80;
        maxHealth     = BASE_HP + race.baseHealthModifier + charClass.healthStatModifier;
        currentHealth = maxHealth;

        // MP
        maxMp     = Mathf.Max(0, race.baseMaxMp + charClass.mpModifier);
        currentMp = maxMp;

        // ATK / DEF
        const int BASE_ATK = 8, BASE_DEF = 3;
        attackPower = BASE_ATK + race.baseAttackModifier + charClass.attackStatModifier;
        baseDefense = BASE_DEF + race.baseDefenseModifier + charClass.defenseStatModifier;

        // MOV
        moveRange = Mathf.Max(1, race.baseMovementPoints + charClass.movementStatModifier);

        // SPD
        speed = Mathf.Max(1, race.baseSpeed + charClass.speedModifier);

        _isDying = false;
        HasActedThisTurn = false;

        // Removed detailed stat init log for clarity
    }

    public void PlaceUnit(Vector3Int startPosition, GridManager gridManager)
    {
        if (gridManager == null)
        {
            Debug.LogError($"UnitController ({unitName}): GridManager null.", this);
            return;
        }
        gridPosition = startPosition;
        transform.position = gridManager.GridToWorld(startPosition);
    }

    public void StartMove(List<Vector3Int> path, GridManager gridManager, Action onMoveComplete = null)
    {
        if (!IsAlive || isMoving) return;
        if (path == null || path.Count <= 1) { onMoveComplete?.Invoke(); return; }
        if (gridManager == null)    { Debug.LogError($"UnitController ({unitName}): GridManager null.", this); onMoveComplete?.Invoke(); return; }
        if (moveSpeed <= 0)          { Debug.LogWarning($"UnitController ({unitName}): Invalid move speed.", this); onMoveComplete?.Invoke(); return; }

        if (_moveCoroutine != null) StopCoroutine(_moveCoroutine);
        _moveCoroutine = StartCoroutine(MoveAlongPath(path, gridManager, onMoveComplete));
    }

    /// <summary>
    /// Applies damage, checks for death, and updates UI if this unit is currently selected.
    /// </summary>
    public void TakeDamage(int damageAmount)
    {
        if (!IsAlive || _isDying) return;

        int damageToApply = Mathf.Max(0, damageAmount);
        currentHealth -= damageToApply;
        Debug.Log($"Unit '{unitName}' takes {damageToApply} damage. Current Health: {currentHealth}/{maxHealth}", this); // Keep: HP change

        currentHealth = Mathf.Max(0, currentHealth);
        if (currentHealth <= 0)
        {
            Die();
        }
        else
        {
            // Refresh UI if this unit is the one displayed
            if (GameManager.Instance != null && GameManager.Instance.selectedUnit == this)
            {
                UIManager.Instance?.UpdateSelectedUnitInfo(this);
            }
        }
        // Only essential HP log kept above. No extra logs needed.
    }

    private void Die()
    {
        if (_isDying) return;
        _isDying = true;
        Debug.Log($"Unit '{unitName}' defeated!", this); // Keep: Unit defeat

        if (_moveCoroutine != null)
        {
            StopCoroutine(_moveCoroutine);
            _moveCoroutine = null;
            isMoving = false;
        }

        OnUnitDied?.Invoke(this);
    }

    private IEnumerator MoveAlongPath(List<Vector3Int> path, GridManager gridManager, Action onMoveComplete)
    {
        isMoving = true;
        for (int i = 1; i < path.Count; i++)
        {
            if (!IsAlive || _isDying) { isMoving = false; _moveCoroutine = null; yield break; }
            var targetWorldPos = gridManager.GridToWorld(path[i]);
            while (Vector3.Distance(transform.position, targetWorldPos) > DISTANCE_TOLERANCE)
            {
                if (!IsAlive || _isDying) { isMoving = false; _moveCoroutine = null; yield break; }
                transform.position = Vector3.MoveTowards(transform.position, targetWorldPos, moveSpeed * Time.deltaTime);
                yield return null;
            }
            transform.position = targetWorldPos;
            gridPosition = path[i];
        }
        isMoving = false;
        _moveCoroutine = null;
        if (IsAlive && !_isDying) onMoveComplete?.Invoke();
    }

    /// <summary>
    /// Adds a new status effect to this unit.
    /// </summary>
    public void AddStatusEffect(StatusEffectSO effectSO, int duration)
    {
        if (effectSO == null)
        {
            Debug.LogWarning($"UnitController ({unitName}): Tried to add a null StatusEffectSO.", this); // Keep: Warning
            return;
        }

        var newActiveEffect = new ActiveStatusEffect(effectSO, duration);
        ActiveStatusEffects.Add(newActiveEffect);
        Debug.Log($"{unitName} gained status effect: {effectSO.effectName} for {duration} turns.", this); // Keep: status applied
        // Only essential log for status application kept.
    }

    /// <summary>
    /// Decrements and removes expired status effects each turn,
    /// and applies damage-over-time for detrimental, non-stat effects.
    /// </summary>
    public void TickStatusEffects()
    {
        for (int i = ActiveStatusEffects.Count - 1; i >= 0; i--)
        {
            var effect = ActiveStatusEffects[i];

            // --- Apply Damage Over Time effects ---
            // Check if it's a detrimental effect not directly modifying a base stat (our convention for DoT)
            if (!effect.EffectData.isBuff
                && effect.EffectData.statAffected == StatusEffectSO.AffectedStat.None
                && effect.EffectData.modifierValue > 0)
            {
                int dotDamage = effect.EffectData.modifierValue;
                Debug.Log($"{unitName} takes {dotDamage} damage from '{effect.EffectData.effectName}'.", this); // Keep: DoT
                TakeDamage(dotDamage);
            }

            // Decrement remaining duration
            effect.RemainingDuration--;
            // Only keep DoT and expiration logs. Removed per-tick duration log.

            // Remove if expired
            if (effect.RemainingDuration <= 0)
            {
                Debug.Log($"{unitName}'s effect '{effect.EffectData.effectName}' expired.", this); // Keep: status expired
                ActiveStatusEffects.RemoveAt(i);
            }
        }
    }
    public void Heal(int amount)
{
    if (amount <= 0 || !IsAlive) return; // Ignore non-positive healing or healing dead units

    currentHealth += amount;
    currentHealth = Mathf.Min(currentHealth, maxHealth); // Clamp to max health

    Debug.Log($"{unitName} healed for {amount}. Current HP: {currentHealth}/{maxHealth}");
    // TODO: Add healing visual effect/number popup?
    // TODO: Trigger OnHeal event if needed?
}
}
