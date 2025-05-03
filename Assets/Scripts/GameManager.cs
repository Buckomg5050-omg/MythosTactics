using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(MovementRangeVisualizer))]
public class GameManager : MonoBehaviour
{
    // Enums define the possible states for turns and unit selection/actions.
    public enum Turn { Player, Enemy }
    private enum SelectionState { None, UnitSelected, SelectingSkillFromPanel, SelectingSkillTarget, PlayerMoving, ActionPending } // Added PlayerMoving explicitly

    [Header("Core References")]
    [Tooltip("Manages grid data and coordinate conversions.")]
    public GridManager gridManager;
    [Tooltip("Manages UI elements like buttons, text, and indicators.")]
    public UIManager uiManager;
    [Tooltip("Visualizes movement range on the grid.")]
    public MovementRangeVisualizer movementRangeVisualizer; // Made public for clarity

    [Header("Unit Setup")]
    [Tooltip("Prefab used for player units.")]
    public GameObject playerUnitPrefab;
    [Tooltip("Prefab used for enemy units.")]
    public GameObject enemyUnitPrefab;
    [Tooltip("Starting positions for player units.")]
    public List<Vector3Int> playerStartPositions = new List<Vector3Int> { new Vector3Int(0, 0, 0), new Vector3Int(0, 1, 0) };
    [Tooltip("Starting positions for enemy units.")]
    public List<Vector3Int> enemyStartPositions = new List<Vector3Int> { new Vector3Int(5, -1, 0), new Vector3Int(6, -1, 0), new Vector3Int(5, 1, 0) };

    [Header("Default Unit Data")]
    [Tooltip("Default race assigned to player units.")]
    public RaceSO defaultPlayerRace;
    [Tooltip("Default class assigned to player unit 1.")]
    public CharacterClassSO defaultPlayerClass;
    [Tooltip("Specific class assigned to player unit 2.")]
    public CharacterClassSO specificPlayerClass2; // For Archer etc.
    [Tooltip("Default race assigned to enemy units.")]
    public RaceSO defaultEnemyRace;
    [Tooltip("Default class assigned to enemy units.")]
    public CharacterClassSO defaultEnemyClass;

    // --- Public Properties ---
    [Header("Current State Info")]
    [Tooltip("The currently selected unit, if any.")]
    public UnitController selectedUnit { get; private set; }
    [Tooltip("Indicates whose turn it currently is.")]
    public Turn CurrentTurn { get; private set; }

    // --- Private State Variables ---
    private PlayerInputActions playerInputActions;
    private List<UnitController> playerUnits = new List<UnitController>();
    private List<UnitController> enemyUnits = new List<UnitController>();
    private List<Vector3Int> _validMovePositions = new List<Vector3Int>();
    private List<UnitController> _validAttackTargets = new List<UnitController>();
    private SkillSO _currentSkill = null; // Renamed from pendingSkill for clarity
    private List<UnitController> _validSkillTargets = new List<UnitController>();
    private SelectionState currentState = SelectionState.None;
    private bool isGameOver = false;
    // Removed _clickProcessingPending and _pendingClickPosition - handle input directly in callback

    // --- Singleton ---
    public static GameManager Instance { get; private set; }

    // --- Unity Lifecycle Methods ---
    void Awake()
    {
        // Singleton Setup
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        // Input Setup
        playerInputActions = new PlayerInputActions();

        // Component Check / Find
        if (movementRangeVisualizer == null) movementRangeVisualizer = GetComponent<MovementRangeVisualizer>();
        if (movementRangeVisualizer == null) Debug.LogError("GM: MovementRangeVisualizer component missing!", this);
        if (uiManager == null) uiManager = FindFirstObjectByType<UIManager>();
        if (uiManager == null) Debug.LogError("GM: UIManager component missing!", this);
        if (gridManager == null) gridManager = FindFirstObjectByType<GridManager>();
        if (gridManager == null) Debug.LogError("GM: GridManager component missing!", this);

        // Event Subscription
        UnitController.OnUnitDied += HandleUnitDeath;
    }

    void Start()
    {
        isGameOver = false;
        Time.timeScale = 1f; // Ensure game runs normally

        SpawnPlayerUnits();
        SpawnEnemyUnits();

        // Start the game with the first Player Turn
        CurrentTurn = Turn.Player;
        // We call EndEnemyTurn to correctly set up the first player turn
        EndEnemyTurn(); // This will set turn to Player and reset units
    }

    void OnEnable()
    {
        playerInputActions.Player.Enable();
        // Use 'performed' for potentially more reliable event firing after UI processing
        playerInputActions.Player.Select.performed += HandleSelectionClick;
    }

    void OnDisable()
    {
        playerInputActions.Player.Select.performed -= HandleSelectionClick;
        playerInputActions.Player.Disable();
        UnitController.OnUnitDied -= HandleUnitDeath; // Unsubscribe
    }

    void Update()
    {
        // Keep existing game-over and restart logic
        if (isGameOver)
        {
            if (Input.GetKeyDown(KeyCode.R))
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            return;
        }
        // Click processing is now handled entirely within the callback coroutine.
        // Other per-frame logic (like animations, timers?) could go here if needed.
    }

    // --- Input Handling ---
    private void HandleSelectionClick(InputAction.CallbackContext context)
    {
        // Start the coroutine to process click after a frame delay
        StartCoroutine(ProcessClickAfterFrame(context));
    }

    private IEnumerator ProcessClickAfterFrame(InputAction.CallbackContext context)
    {
        // Wait one frame - helps ensure UI events are processed first
        yield return null;

        // --- Initial Checks after delay ---
        if (isGameOver || CurrentTurn != Turn.Player) yield break; // Exit if game ended or not player turn

        if (EventSystem.current.IsPointerOverGameObject())
        {
            Debug.Log("ProcessClickAfterFrame: Click was on UI element, ignoring world click.");
            yield break; // Exit coroutine if click started on UI
        }

        // Make sure critical components are present
        if (gridManager == null || Camera.main == null)
        {
            Debug.LogError("ProcessClickAfterFrame: Missing GridManager or Main Camera!");
            yield break;
        }

        // --- Calculate Position ---
        // ** WRONG: Vector2 screenPosition = context.ReadValue<Vector2>(); // Cannot read Vector2 from Button context **
        // ** CORRECT: Get current mouse position directly **
        Vector2 screenPosition = Mouse.current.position.ReadValue();

        // Convert screen position to world position
        Vector3 worldPosition = Camera.main.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, Camera.main.nearClipPlane));

        // Convert world position to grid cell
        Vector3Int gridPos = gridManager.WorldToGrid(worldPosition); // Use correct GridManager method

        Debug.Log($"ProcessClickAfterFrame: Processing delayed click at Grid Pos: {gridPos} in State: {currentState}");

        // --- State-Based Click Delegation ---
        switch (currentState)
        {
            case SelectionState.None: // Player turn, nothing selected
                HandleClickInNoneState(gridPos);
                break;

            case SelectionState.UnitSelected: // Unit selected, waiting for move/attack/skill target or deselect
                HandleClickInUnitSelectedState(gridPos);
                break;

            case SelectionState.SelectingSkillTarget: // Skill chosen, waiting for skill target
                HandleClickInSelectingSkillTarget(gridPos);
                break;

            // States that ignore world clicks here (input comes from elsewhere or disallowed)
            case SelectionState.SelectingSkillFromPanel:
            case SelectionState.PlayerMoving:
            case SelectionState.ActionPending: // If used, might need specific handling or ignore
                Debug.Log($"ProcessClickAfterFrame: Ignoring click in state {currentState}");
                break;

            default:
                Debug.LogError($"ProcessClickAfterFrame: Unhandled state: {currentState}");
                break;
        }
    }

    // --- State-Specific Click Handlers ---

    private void HandleClickInNoneState(Vector3Int clickedCell)
    {
        var clickedUnit = GetUnitAt(clickedCell);
        if (clickedUnit != null && playerUnits.Contains(clickedUnit)) // Clicked a player unit
        {
            selectedUnit = clickedUnit; // Select the unit

            if (UIManager.Instance != null)
                UIManager.Instance.UpdateSelectedUnitInfo(selectedUnit); // Update UI immediately

            if (selectedUnit.HasActedThisTurn)
            {
                // Unit already acted, just show info, no action buttons
                Debug.Log($"GM.Select: Unit {selectedUnit.unitName} already acted. Hiding action buttons.");
                currentState = SelectionState.UnitSelected; // Still enter selected state
                if (UIManager.Instance != null)
                {
                    UIManager.Instance.ShowActionButtons(false);
                    UIManager.Instance.ShowEndTurnButton();
                }
            }
            else
            {
                // Unit can act
                Debug.Log($"GM.Select: Unit {selectedUnit.unitName} can act. Showing action buttons.");
                currentState = SelectionState.UnitSelected; // Enter selected state
                if (UIManager.Instance != null)
                {
                    UIManager.Instance.ShowActionButtons(true); // Show action buttons
                    UIManager.Instance.ShowEndTurnButton();
                }
                ShowActionIndicators(selectedUnit); // Show move/attack ranges
            }
        }
        else
        {
            // Clicked empty space or enemy - do nothing in this state
            Debug.Log("HandleClickInNoneState: Clicked empty space or non-player unit.");
            ResetSelectionState(); // Ensure deselection if clicking empty space
        }
    }

    private void HandleClickInUnitSelectedState(Vector3Int clickedCell)
    {
        if (selectedUnit == null) { ResetSelectionState(); return; } // Safety check

        // 1. Check for Valid Move Target
        if (_validMovePositions.Contains(clickedCell) && clickedCell != selectedUnit.gridPosition)
        {
            var path = Pathfinder.FindPath(selectedUnit.gridPosition, clickedCell, gridManager, this);
            if (path != null && path.Count > 1)
            {
                Debug.Log($"GM: Moving {selectedUnit.unitName} to {clickedCell}");
                if (UIManager.Instance != null)
                {
                    UIManager.Instance.ShowActionButtons(false); // Hide buttons on commit
                    UIManager.Instance.ClearIndicators();
                }
                movementRangeVisualizer.ClearRange(); // Clear move range explicitly
                 _validMovePositions.Clear(); // Clear lists
                 _validAttackTargets.Clear();

                currentState = SelectionState.PlayerMoving; // Change state during move
                selectedUnit.StartMove(path, gridManager, () => OnUnitMoveComplete(selectedUnit)); // Start move, provide callback
            }
            else { Debug.LogWarning("Pathfinding failed despite valid tile?"); ResetSelectionState(); } // Should not happen if reachable
            return; // Exit after handling move
        }

        // 2. Check for Valid Attack Target
        var targetUnit = GetUnitAt(clickedCell);
        if (targetUnit != null && _validAttackTargets.Contains(targetUnit))
        {
            Debug.Log($"GM: {selectedUnit.unitName} attacking {targetUnit.unitName}");
            PerformAttack(selectedUnit, targetUnit); // Execute attack
            selectedUnit.HasActedThisTurn = true;    // Mark as acted

            if (UIManager.Instance != null)
            {
                UIManager.Instance.UpdateSelectedUnitInfo(selectedUnit); // Update UI (HP/MP changes if any)
                UIManager.Instance.ShowActionButtons(false);             // Hide action buttons
                UIManager.Instance.ClearIndicators();                  // Clear indicators
            }
             movementRangeVisualizer.ClearRange(); // Explicitly clear move range too
             _validMovePositions.Clear();
             _validAttackTargets.Clear();

            currentState = SelectionState.UnitSelected; // Stay selected, but buttons hidden

            if (CheckIfAllPlayerUnitsActed()) EndPlayerTurn(); // Check if turn ends
            return; // Exit after handling attack
        }

        // 3. Check for Click on Self or Another Friendly Unit
        var clickedUnit = GetUnitAt(clickedCell);
        if (clickedUnit != null && playerUnits.Contains(clickedUnit))
        {
            if (clickedUnit == selectedUnit) // Clicked self
            {
                // Option 1: Do nothing (keep selected)
                 Debug.Log("Clicked selected unit again.");
                 // Option 2: Deselect
                 // ResetSelectionState();
            }
            else // Clicked another friendly unit
            {
                Debug.Log($"Switching selection from {selectedUnit.unitName} to {clickedUnit.unitName}");
                // Reset first, then handle the click as if starting from None state
                ResetSelectionState();
                HandleClickInNoneState(clickedCell);
            }
            return;
        }

        // 4. Clicked Invalid Space - Deselect
        Debug.Log("HandleClickInUnitSelectedState: Invalid click, deselecting.");
        ResetSelectionState();
    }


    private void HandleClickInSelectingSkillTarget(Vector3Int clickedCell)
    {
        if (_currentSkill == null || selectedUnit == null)
        {
            Debug.LogError("GM: Click in skill target state invalid (No skill/unit)!", this);
            ResetSelectionState();
            return;
        }

        var target = GetUnitAt(clickedCell);

        // SUCCESS PATH
        if (target != null && _validSkillTargets.Contains(target))
        {
            Debug.Log($"GM: Executing skill '{_currentSkill.skillName}' from {selectedUnit.unitName} on {target.unitName}");

            // Deduct MP
            int cost = _currentSkill.mpCost;
            selectedUnit.currentMp -= cost;
            selectedUnit.currentMp = Mathf.Max(0, selectedUnit.currentMp);

            // Update UI immediately AFTER cost deduction
            if (UIManager.Instance != null)
                UIManager.Instance.UpdateSelectedUnitInfo(selectedUnit);

            // Execute Skill Logic
            ExecuteSkillEffect(selectedUnit, target, _currentSkill);

            // Mark unit as acted AFTER effect
            selectedUnit.HasActedThisTurn = true;

             // Cleanup UI
            Debug.Log($"GameManager: Unit {selectedUnit.unitName} finished SKILL action. Disabling action buttons.");
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ClearIndicators(); // Clear skill targets AND attack/move if any were left
                UIManager.Instance.ShowActionButtons(false); // Hide action buttons
            }
            movementRangeVisualizer.ClearRange(); // Also clear move range
            _validMovePositions.Clear();
            _validAttackTargets.Clear();
            _validSkillTargets.Clear();
            _currentSkill = null; // Clear the skill reference

            currentState = SelectionState.UnitSelected; // Remain selected

            // Check for auto-end turn
            if (CheckIfAllPlayerUnitsActed()) EndPlayerTurn();
        }
        // FAILURE / CANCELLATION PATH
        else
        {
            Debug.Log($"GM: Skill '{_currentSkill.skillName}' cancelled - invalid target ({clickedCell}).", selectedUnit?.gameObject);
            CancelSkillSelection(); // Handles cleanup and state reset
        }
    }

    // HandleClickInActionPendingState might not be needed if move->action isn't a distinct state pause
    private void HandleClickInActionPendingState(Vector3Int clickedCell)
    {
        Debug.LogWarning($"HandleClickInActionPendingState called for {clickedCell}. This state might be unused or needs specific logic.");
        // Example: If this state was waiting for a post-move attack click:
        // var targetUnit = GetUnitAt(clickedCell);
        // if (targetUnit != null && _validAttackTargets.Contains(targetUnit)) { ... handle attack ... } else { ResetSelectionState(); }
        ResetSelectionState(); // Default to deselect if state logic is unclear
    }


    // --- Action Callbacks & Helpers ---

    private void OnUnitMoveComplete(UnitController unit)
    {
        if (isGameOver || unit == null || !playerUnits.Contains(unit) || !unit.IsAlive)
        {
            // If the moving unit died mid-move or game ended, reset state.
            if (selectedUnit == unit) ResetSelectionState();
            return;
        }

        Debug.Log($"{unit.unitName} finished moving to {unit.gridPosition}.");

        // Unit remains selected after moving
        selectedUnit = unit;
        currentState = SelectionState.UnitSelected; // Now back to selected state

        if (UIManager.Instance != null)
             UIManager.Instance.UpdateSelectedUnitInfo(selectedUnit);

        if (selectedUnit.HasActedThisTurn)
        {
            // If move was the *only* action (e.g., no attack/skill after), ensure buttons stay hidden
             Debug.Log($"GM.MoveComplete: Unit {selectedUnit.unitName} already acted. Hiding action buttons.");
             if (UIManager.Instance != null) UIManager.Instance.ShowActionButtons(false);
        }
        else
        {
             // Unit moved but can still act (e.g., attack or skill)
             Debug.Log($"GM.MoveComplete: Unit {selectedUnit.unitName} can still act. Showing buttons/indicators.");
             if (UIManager.Instance != null) UIManager.Instance.ShowActionButtons(true);
             ShowActionIndicators(selectedUnit); // Re-show indicators for potential post-move actions
        }
         if (UIManager.Instance != null) UIManager.Instance.ShowEndTurnButton();
    }

    private void PerformAttack(UnitController attacker, UnitController target)
    {
        if (isGameOver || attacker == null || !attacker.IsAlive || target == null || !target.IsAlive) return;

        // Use attackPower directly
        int raw = attacker.attackPower;
        int def = target.Defense; // Use Defense property
        int dmg = Mathf.Max(1, raw - def); // Ensure minimum damage of 1
        Debug.Log($"{attacker.unitName} attacks {target.unitName} for {dmg} damage! ({raw} ATK vs {def} DEF)", attacker.gameObject);
        target.TakeDamage(dmg);
    }

    private void ExecuteSkillEffect(UnitController caster, UnitController target, SkillSO skill)
    {
        Debug.Log($"GM: Applying effect of '{skill.skillName}' from {caster.unitName} to {target.unitName}...", caster);

        // --- Damage/Healing Logic ---
        if (skill.skillName == "Power Strike")
        {
            int rawDamage = caster.attackPower + skill.basePower;
            int finalDamage = Mathf.Max(1, rawDamage - target.Defense);
            Debug.Log($"   -> Power Strike! (Caster ATK:{caster.attackPower} + Skill Power:{skill.basePower}) = {rawDamage} RAW vs Target DEF:{target.Defense} = {finalDamage} Actual", caster);
            target.TakeDamage(finalDamage);
        }
        else if (skill.isHarmful) // Handles Shoot Arrow, Poison Arrow, etc.
        {
            int rawDamage = caster.attackPower + skill.basePower;
            int finalDamage = Mathf.Max(1, rawDamage - target.Defense);
            Debug.Log($"   -> Harmful Skill '{skill.skillName}'! (Caster ATK:{caster.attackPower} + Skill Power:{skill.basePower}) = {rawDamage} RAW vs Target DEF:{target.Defense} = {finalDamage} Actual", caster);
            target.TakeDamage(finalDamage);
        }
        // Add 'else if (!skill.isHarmful && skill.basePower > 0)' for HEALING skills later
        else // Non-harmful, non-healing skills (like Defend)
        {
            Debug.Log($"   -> Non-harmful skill '{skill.skillName}' applied (no direct damage/heal).", caster);
        }

        // --- Apply Status Effect ---
        if (skill.statusEffectApplied != null)
        {
             if (target != null && target.IsAlive) // Check target validity again after potential damage
             {
                int duration = (skill.statusEffectDuration > 0) ? skill.statusEffectDuration : skill.statusEffectApplied.baseDuration;
                target.AddStatusEffect(skill.statusEffectApplied, duration);
             } else {
                 Debug.LogWarning($"ExecuteSkillEffect: Target died or became invalid before status effect '{skill.statusEffectApplied.effectName}' could be applied.");
             }
        }
    }


    private void HandleUnitDeath(UnitController deadUnit)
    {
        if (deadUnit == null || isGameOver) return;
        Debug.Log($"'{deadUnit.unitName}' received death event.");

        bool wasPlayer = playerUnits.Contains(deadUnit);
        bool wasEnemy = enemyUnits.Contains(deadUnit);

        if (wasPlayer) playerUnits.Remove(deadUnit);
        if (wasEnemy) enemyUnits.Remove(deadUnit);

        // Clear selection if the dead unit was selected
        if (selectedUnit == deadUnit)
        {
            ResetSelectionState();
        }

        // Remove from potential target lists
        _validAttackTargets.Remove(deadUnit);
        _validSkillTargets.Remove(deadUnit);

        // Optionally destroy GameObject after a small delay for death animation/sound
        // Destroy(deadUnit.gameObject, 0.1f); // Already handled in UnitController.Die() ? Check consistency.

        // Check win/loss condition
        CheckForGameOver();

        // If a player unit died during player turn, check if turn should auto-end
        if (!isGameOver && wasPlayer && CurrentTurn == Turn.Player && CheckIfAllPlayerUnitsActed())
        {
            EndPlayerTurn();
        }
    }

    private void ResetSelectionState()
    {
        Debug.Log($"Resetting selection state. Previous state was: {currentState}");
        selectedUnit = null;
        _currentSkill = null;
        _validMovePositions.Clear();
        _validAttackTargets.Clear();
        _validSkillTargets.Clear();

        if (uiManager != null)
        {
            uiManager.UpdateSelectedUnitInfo(null); // Clears info text and status icons
            uiManager.ClearIndicators(); // Clears attack/skill target indicators
            uiManager.HideSkillSelection(); // Hides the skill choice panel
            // uiManager.ShowActionButtons(false); // Action buttons are now hidden by selection/action logic, not deselection
        }
        if (movementRangeVisualizer != null)
        {
            movementRangeVisualizer.ClearRange(); // Clear move range visuals
        }

        // Ensure End Turn button visibility is correct for the current turn state
        if (CurrentTurn == Turn.Player && !isGameOver)
        {
             if (uiManager != null) uiManager.ShowEndTurnButton();
        }
        else
        {
            if (uiManager != null) uiManager.HideEndTurnButton();
        }
         // Hide action buttons explicitly on reset, as no unit is selected
         if (uiManager != null) uiManager.ShowActionButtons(false);


        currentState = SelectionState.None; // Return to base state
    }

    // Shows tile indicators for move/attack ranges
    private void ShowActionIndicators(UnitController unit)
    {
        if (unit == null || unit.HasActedThisTurn || uiManager == null || gridManager == null || movementRangeVisualizer == null) return;

        Debug.Log($"Showing action indicators for {unit.unitName}");

        // Clear previous visuals first
        if (movementRangeVisualizer != null) movementRangeVisualizer.ClearRange();
        if (uiManager != null) uiManager.ClearIndicators();

        // Calculate and show Move range
        _validMovePositions = gridManager.CalculateReachableTiles(unit.gridPosition, unit.moveRange, this, unit);
        if (movementRangeVisualizer != null) movementRangeVisualizer.ShowRange(_validMovePositions, gridManager);

        // Calculate and show Attack targets
        _validAttackTargets = enemyUnits
            .Where(e => e != null && e.IsAlive &&
                        (Mathf.Abs(e.gridPosition.x - unit.gridPosition.x) +
                         Mathf.Abs(e.gridPosition.y - unit.gridPosition.y)) <= unit.attackRange)
            .ToList();
        if (uiManager != null) uiManager.VisualizeAttackTargets(_validAttackTargets, gridManager);

        // *** NO CALLS TO ShowActionButtons() HERE *** Button visibility is handled by selection logic.
    }

    // --- Skill Button & Panel Logic ---

    public void OnSkillButtonPressed()
    {
         // --- Initial Checks ---
         if (CurrentTurn != Turn.Player || isGameOver || selectedUnit == null)
         {
             Debug.LogWarning("GM: Skill button pressed at invalid time/state (Turn/Game Over/No Unit).");
             return;
         }
         // Check state *after* confirming unit exists
          if (currentState != SelectionState.UnitSelected)
         {
             Debug.LogWarning($"GM: Skill button pressed in unexpected state: {currentState}");
              return;
          }
          if (selectedUnit.HasActedThisTurn)
          {
               Debug.LogWarning($"GM: Unit {selectedUnit.unitName} has already acted.", selectedUnit);
               return;
          }


        var skills = selectedUnit.unitClass?.availableSkills;
        if (skills == null || skills.Count == 0)
        {
            Debug.LogError($"GM: Unit '{selectedUnit.unitName}' has no skills defined in its class.", selectedUnit);
            return; // Nothing to do
        }

        // --- Prepare for Skill Action ---
        // Hide indicators and main action buttons as we transition to skill selection/targeting
        if (movementRangeVisualizer != null) movementRangeVisualizer.ClearRange();
        if (uiManager != null)
        {
            uiManager.ClearIndicators();
            uiManager.ShowActionButtons(false);
            // Keep End Turn visible for now, maybe hide later? uiManager.HideEndTurnButton();
        }

        // --- Handle Single vs Multiple Skills ---
        if (skills.Count == 1)
        {
            var skill = skills[0];
            // Check MP affordability
            if (selectedUnit.currentMp < skill.mpCost)
            {
                Debug.Log($"{selectedUnit.unitName} cannot use {skill.skillName}. Needs {skill.mpCost} MP, has {selectedUnit.currentMp} MP.", selectedUnit);
                // If MP is insufficient, revert UI state
                if (uiManager != null) uiManager.ShowActionButtons(true); // Show buttons again
                ShowActionIndicators(selectedUnit); // Show indicators again
                return; // Exit, don't change state
            }

            // Proceed directly to targeting
            Debug.Log($"GM: Preparing single skill '{skill.skillName}' for targeting.");
            _currentSkill = skill;
            currentState = SelectionState.SelectingSkillTarget;
            ShowSkillTargetingRange(selectedUnit, skill);
        }
        else // Multiple skills
        {
            // Check if *any* skill is affordable first (optional, prevents empty panel)
             bool canAffordAny = skills.Any(s => selectedUnit.currentMp >= s.mpCost);
             if (!canAffordAny)
             {
                 Debug.Log($"GM: {selectedUnit.unitName} cannot afford any skills.", selectedUnit);
                 if (uiManager != null) uiManager.ShowActionButtons(true); // Show buttons again
                 ShowActionIndicators(selectedUnit); // Show indicators again
                 return; // Exit, don't change state
             }

            // Proceed to show selection panel
            Debug.Log($"GM: Unit {selectedUnit.unitName} has multiple skills. Showing selection panel.");
            currentState = SelectionState.SelectingSkillFromPanel; // Set state BEFORE showing panel
            if (uiManager != null)
            {
                uiManager.ShowSkillSelection(selectedUnit, skills, HandleSkillSelectedFromPanel); // UIManager shows the panel
            } else {
                 Debug.LogError("UIManager instance is null, cannot show skill selection!");
                 currentState = SelectionState.UnitSelected; // Revert state on error
            }
        }
    }

    // Callback from UIManager when a skill button in the panel is clicked
    private void HandleSkillSelectedFromPanel(SkillSO chosenSkill)
    {
        if (currentState != SelectionState.SelectingSkillFromPanel || selectedUnit == null || chosenSkill == null)
        {
             Debug.LogWarning($"HandleSkillSelectedFromPanel called in unexpected state ({currentState}) or with null data.");
             CancelSkillSelection(); // Reset if something went wrong
            return;
        }

        Debug.Log($"GM: Skill selected from panel: '{chosenSkill.skillName}' for {selectedUnit.unitName}.", selectedUnit);

        _currentSkill = chosenSkill;
        currentState = SelectionState.SelectingSkillTarget; // Transition to targeting state

        if (uiManager != null)
        {
             uiManager.HideSkillSelection(); // Hide the panel
        }

        ShowSkillTargetingRange(selectedUnit, chosenSkill); // Show targets for the chosen skill
    }

    // Called by UI Cancel button or when skill targeting fails
    public void CancelSkillSelection()
    {
        Debug.Log("GM: Cancelling skill selection or targeting.");
        SelectionState previousState = currentState; // Remember where we were

        // Clear skill-related temp data
        _currentSkill = null;
        _validSkillTargets.Clear();
        // skillTargetableCells.Clear(); // Only needed if visualizing non-targetable range cells

        if (uiManager != null)
        {
             uiManager.ClearSkillVisuals();
             uiManager.HideSkillSelection();
        }

        // If we were selecting/targeting AND the unit can still act, return to UnitSelected state
        if ((previousState == SelectionState.SelectingSkillFromPanel || previousState == SelectionState.SelectingSkillTarget) &&
            selectedUnit != null && !selectedUnit.HasActedThisTurn && CurrentTurn == Turn.Player && !isGameOver)
        {
            Debug.Log("GM.Cancel: Returning to UnitSelected state.");
            currentState = SelectionState.UnitSelected;
            if (UIManager.Instance != null) UIManager.Instance.ShowActionButtons(true); // Show buttons again
            ShowActionIndicators(selectedUnit); // Show indicators again
             if (UIManager.Instance != null) UIManager.Instance.ShowEndTurnButton(); // Ensure end turn is visible
        }
        else // Otherwise (e.g. cancelling after already acting, or unit died), fully reset
        {
             Debug.Log("GM.Cancel: Resetting selection fully.");
             ResetSelectionState();
        }
    }

    // Calculates and displays skill target indicators
    private void ShowSkillTargetingRange(UnitController caster, SkillSO skill)
    {
        if (uiManager == null || gridManager == null || caster == null || skill == null) return;

        uiManager.ClearSkillVisuals(); // Clear previous
        // skillTargetableCells.Clear(); // Only needed if visualizing range itself
        _validSkillTargets.Clear();

        var casterPos = caster.gridPosition;
        List<Vector3Int> potentialRangeCells = new List<Vector3Int>(); // Optional: for range vis

        // Iterate through a square bounding box around the caster up to the skill range
        for (int x = casterPos.x - skill.range; x <= casterPos.x + skill.range; x++)
        {
            for (int y = casterPos.y - skill.range; y <= casterPos.y + skill.range; y++)
            {
                var cell = new Vector3Int(x, y, casterPos.z);
                // Check Manhattan distance for actual range
                if (Mathf.Abs(cell.x - casterPos.x) + Mathf.Abs(cell.y - casterPos.y) <= skill.range)
                {
                    // Optional: Add to potentialRangeCells if visualizing full range area
                    // potentialRangeCells.Add(cell);

                    // Check for units at this cell
                    var unitAtCell = GetUnitAt(cell);
                    if (unitAtCell != null && unitAtCell.IsAlive)
                    {
                        // Determine if the unit is a valid target based on skill type
                        bool isTargetValid = false;
                        if (skill.isHarmful)
                        {
                            // Harmful skills target enemies
                            isTargetValid = enemyUnits.Contains(unitAtCell);
                        }
                        else
                        {
                            // Non-harmful skills target allies (including self if range is 0 or more)
                            isTargetValid = playerUnits.Contains(unitAtCell);
                        }

                        if (isTargetValid)
                        {
                            _validSkillTargets.Add(unitAtCell);
                        }
                    }
                }
            }
        }
        Debug.Log($"Skill '{skill.skillName}': Found {_validSkillTargets.Count} valid targets in range {skill.range}.");
        // Visualize only the valid targets
        uiManager.VisualizeSkillRange(_validSkillTargets, gridManager);
        // Optional: Visualize full range area using potentialRangeCells
        // uiManager.VisualizeSkillArea(potentialRangeCells, gridManager);
    }

    // --- Utility Methods ---

    public UnitController GetUnitAt(Vector3Int pos)
    {
        // Optimized slightly to check player list first if it's player turn, etc.
        if (CurrentTurn == Turn.Player)
        {
             foreach (var u in playerUnits) if (u != null && u.IsAlive && u.gridPosition == pos) return u;
             foreach (var u in enemyUnits)  if (u != null && u.IsAlive && u.gridPosition == pos) return u;
        } else {
             foreach (var u in enemyUnits)  if (u != null && u.IsAlive && u.gridPosition == pos) return u;
             foreach (var u in playerUnits) if (u != null && u.IsAlive && u.gridPosition == pos) return u;
        }
        return null;
    }

    private bool CheckIfAllPlayerUnitsActed()
    {
        // A turn ends if there are no living player units, or if all living player units have acted.
        List<UnitController> livingPlayers = playerUnits.Where(u => u != null && u.IsAlive).ToList();
        if (!livingPlayers.Any()) return true; // No players left
        return livingPlayers.All(u => u.HasActedThisTurn);
    }

    // --- Turn Management ---

    public void OnEndTurnButtonPressed()
    {
        if (!isGameOver && CurrentTurn == Turn.Player)
        {
             Debug.Log("Player manually ended turn.");
             EndPlayerTurn();
        }
    }

    private void EndPlayerTurn()
    {
        Debug.Log("===== Enemy Turn Starts =====");
        ResetSelectionState(); // Clear selection before enemy turn
        CurrentTurn = Turn.Enemy;
        if (uiManager != null) uiManager.UpdateTurnIndicator(CurrentTurn);
        StartCoroutine(EnemyTurnCoroutine());
    }

    public void EndEnemyTurn() // Should be private, called only by coroutine
    {
         Debug.Log("===== Player Turn Starts =====");
         CurrentTurn = Turn.Player;

         // --- Status Effect Ticking for Player Units ---
         Debug.Log("GM: Ticking status effects for player units...");
         List<UnitController> playersToTick = playerUnits.Where(p => p != null && p.IsAlive).ToList(); // Get living units first
         foreach (UnitController playerUnit in playersToTick)
         {
             int effectsBefore = playerUnit.ActiveStatusEffects.Count;
             playerUnit.TickStatusEffects();
             int effectsAfter = playerUnit.ActiveStatusEffects.Count;

             // If the currently selected unit had effects expire, update its UI
             if (playerUnit == selectedUnit && effectsBefore != effectsAfter)
             {
                 if (UIManager.Instance != null)
                 {
                     UIManager.Instance.UpdateSelectedUnitInfo(selectedUnit);
                     Debug.Log($"GM: Triggered UI update for {selectedUnit.unitName} after status tick.");
                 }
             }
         }

         // --- Reset Action Flags ---
         Debug.Log("Resetting 'HasActedThisTurn' for player units.");
         foreach (var u in playerUnits)
             if (u != null && u.IsAlive)
                 u.HasActedThisTurn = false;

         if (uiManager != null) uiManager.UpdateTurnIndicator(CurrentTurn);
         ResetSelectionState(); // Reset selection at start of player turn
         // Re-enable End Turn button explicitly if ResetSelectionState didn't
         if (uiManager != null && !isGameOver) uiManager.ShowEndTurnButton();

         // Check if player has any units left / any possible moves, could trigger game over here too
         if (!playerUnits.Any(p => p != null && p.IsAlive))
         {
              CheckForGameOver(); // Check immediately if player was wiped out
         }
    }

    // --- Enemy AI ---
    private IEnumerator EnemyTurnCoroutine()
    {
        Debug.Log("Starting Enemy Turn Coroutine...");
        yield return new WaitForSeconds(0.5f); // Brief pause at start of turn

        var livingEnemies = enemyUnits.Where(e => e != null && e.IsAlive).ToList();
        var livingPlayers = playerUnits.Where(p => p != null && p.IsAlive).ToList();

        if (!livingPlayers.Any()) // No players left, end turn immediately
        {
             Debug.Log("No living players found, ending enemy turn.");
             EndEnemyTurn();
             yield break;
        }

        foreach (var enemyUnit in livingEnemies)
        {
             if (!enemyUnit.IsAlive) continue; // Extra safety check
             if (!playerUnits.Any(p => p != null && p.IsAlive)) break; // Check if players wiped mid-turn

             Debug.Log($"Processing AI for {enemyUnit.unitName}");

             // --- Enemy Status Effect Ticking ---
             Debug.Log($"GM: Ticking status effects for {enemyUnit.unitName}...", enemyUnit);
             enemyUnit.TickStatusEffects();
             if (!enemyUnit.IsAlive) continue; // Check if DoT killed it


             // --- AI Targeting --- Find closest living player
             UnitController target = livingPlayers
                 .Where(p => p != null && p.IsAlive) // Ensure target is still alive
                 .OrderBy(p => Vector3Int.Distance(p.gridPosition, enemyUnit.gridPosition)) // Simple distance sort
                 // .ThenBy(p => p.currentHealth) // Optional: prioritize weaker units
                 .FirstOrDefault();

             if (target == null) {
                  Debug.Log($"{enemyUnit.unitName} could not find a valid target.");
                  continue; // No targets left for this enemy
             }
             Debug.Log($"{enemyUnit.unitName} targeting {target.unitName}.");


             // --- AI Action: Attack or Move ---
             int dist = Mathf.Abs(target.gridPosition.x - enemyUnit.gridPosition.x) +
                        Mathf.Abs(target.gridPosition.y - enemyUnit.gridPosition.y);

             if (dist <= enemyUnit.attackRange) // Target in attack range
             {
                 Debug.Log($"{enemyUnit.unitName} is in range to attack {target.unitName}.");
                 PerformAttack(enemyUnit, target);
                 yield return new WaitForSeconds(0.75f); // Pause after attacking
             }
             else // Target not in range, try to move closer
             {
                 Debug.Log($"{enemyUnit.unitName} needs to move closer to {target.unitName}.");
                 var moveTargetCell = FindBestMoveTowards(enemyUnit, target.gridPosition);

                 if (moveTargetCell != enemyUnit.gridPosition) // Found a valid move
                 {
                     var path = Pathfinder.FindPath(enemyUnit.gridPosition, moveTargetCell, gridManager, this);
                     if (path != null && path.Count > 1)
                     {
                         Debug.Log($"{enemyUnit.unitName} moving towards target via {moveTargetCell}.");
                         yield return StartCoroutine(MoveEnemyUnit(enemyUnit, path)); // Wait for move coroutine

                         // Check again if target is in range after moving
                         if (target != null && target.IsAlive) // Re-check target validity
                         {
                              dist = Mathf.Abs(target.gridPosition.x - enemyUnit.gridPosition.x) +
                                     Mathf.Abs(target.gridPosition.y - enemyUnit.gridPosition.y);
                              if (dist <= enemyUnit.attackRange)
                              {
                                   Debug.Log($"{enemyUnit.unitName} attacking {target.unitName} after moving.");
                                   PerformAttack(enemyUnit, target);
                                   yield return new WaitForSeconds(0.75f); // Pause after post-move attack
                              }
                              else { Debug.Log($"{enemyUnit.unitName} still not in range after moving."); }
                         } else { Debug.Log($"{enemyUnit.unitName}'s target died before post-move attack."); }
                     } else { Debug.Log($"{enemyUnit.unitName} pathfinding failed to {moveTargetCell}."); }
                 } else { Debug.Log($"{enemyUnit.unitName} cannot move closer to target."); }
             }

             yield return new WaitForSeconds(0.25f); // Brief pause between enemy actions
        }

         Debug.Log("Finished processing all enemy units.");
         EndEnemyTurn();
    }


    private Vector3Int FindBestMoveTowards(UnitController unit, Vector3Int targetPos)
    {
        var start = unit.gridPosition;
        var reachable = gridManager.CalculateReachableTiles(start, unit.moveRange, this, unit);
        int bestDist = int.MaxValue; // Initialize with max value
        Vector3Int bestTile = start; // Default to staying put

        // Check current distance first
        bestDist = Mathf.Abs(start.x - targetPos.x) + Mathf.Abs(start.y - targetPos.y);


        foreach (var tile in reachable)
        {
            // Don't move onto occupied tiles
            if (GetUnitAt(tile) != null) continue;

            int d = Mathf.Abs(tile.x - targetPos.x) + Mathf.Abs(tile.y - targetPos.y);
            // Find the reachable tile that minimizes distance to the target
            if (d < bestDist)
            {
                bestDist = d;
                bestTile = tile;
            }
        }
        return bestTile;
    }

    private IEnumerator MoveEnemyUnit(UnitController unit, List<Vector3Int> path)
    {
        // Simple coroutine just waits for the unit's own move to finish
        bool moveComplete = false;
        unit.StartMove(path, gridManager, () => { moveComplete = true; });
        yield return new WaitUntil(() => moveComplete);
         Debug.Log($"{unit.unitName} finished AI move via coroutine.");
    }

    // --- Game Over ---
    private void CheckForGameOver()
    {
        // Only proceed if game isn't already over
        if (isGameOver) return;

        bool anyPlayerLeft = playerUnits.Any(u => u != null && u.IsAlive);
        bool anyEnemyLeft = enemyUnits.Any(u => u != null && u.IsAlive);

        if (!anyPlayerLeft) // Player team wiped out
        {
            isGameOver = true;
             Debug.Log("GAME OVER - Player Defeated!");
             if (uiManager != null)
             {
                uiManager.turnIndicatorText.text = "GAME OVER (R to Restart)";
                uiManager.HideEndTurnButton();
                uiManager.ShowActionButtons(false); // Hide action buttons
             }
             Time.timeScale = 0f; // Optional: Pause game time
        }
        else if (!anyEnemyLeft) // Enemy team wiped out
        {
            isGameOver = true;
            Debug.Log("VICTORY! - Enemies Defeated!");
             if (uiManager != null)
             {
                uiManager.turnIndicatorText.text = "VICTORY! (R to Restart)";
                uiManager.HideEndTurnButton();
                uiManager.ShowActionButtons(false); // Hide action buttons
             }
             // Don't pause time on victory? Or maybe pause after short delay/animation
        }
    }

    // --- Unit Spawning ---
    private void SpawnPlayerUnits()
    {
        playerUnits.Clear(); // Clear any existing units
        for (int i = 0; i < playerStartPositions.Count; i++)
        {
            Vector3Int pos = playerStartPositions[i];
            GameObject unitGO = Instantiate(playerUnitPrefab, gridManager.GridToWorld(pos), Quaternion.identity); // Use center
            UnitController unit = unitGO.GetComponent<UnitController>();
            if (unit != null)
            {
                unit.PlaceUnit(pos, gridManager);
                // Assign specific class to second unit if available
                if (i == 1 && specificPlayerClass2 != null)
                    unit.InitializeStats($"Player {i + 1}", defaultPlayerRace, specificPlayerClass2);
                else
                    unit.InitializeStats($"Player {i + 1}", defaultPlayerRace, defaultPlayerClass);
                playerUnits.Add(unit);
            }
            else { Debug.LogError("Spawned player prefab missing UnitController!"); Destroy(unitGO); }
        }
         Debug.Log($"GM: Spawned {playerUnits.Count} player units.");
    }

    private void SpawnEnemyUnits()
    {
        enemyUnits.Clear(); // Clear any existing units
        for (int i = 0; i < enemyStartPositions.Count; i++)
        {
            Vector3Int pos = enemyStartPositions[i];
            GameObject unitGO = Instantiate(enemyUnitPrefab, gridManager.GridToWorld(pos), Quaternion.identity); // Use center
            UnitController unit = unitGO.GetComponent<UnitController>();
            if (unit != null)
            {
                unit.PlaceUnit(pos, gridManager);
                unit.InitializeStats($"Enemy {i + 1}", defaultEnemyRace, defaultEnemyClass);
                enemyUnits.Add(unit);
            }
            else { Debug.LogError("Spawned enemy prefab missing UnitController!"); Destroy(unitGO); }
        }
         Debug.Log($"GM: Spawned {enemyUnits.Count} enemy units.");
    }
} // --- End of GameManager Class ---