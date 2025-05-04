using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using TacticalRPG;

[RequireComponent(typeof(MovementRangeVisualizer))]
public class GameManager : MonoBehaviour
{
    // MODIFIED: Added ConfirmingAttack and ConfirmingSkill states
    private enum SelectionState
    {
        None,
        UnitSelected,
        SelectingSkillFromPanel,
        SelectingSkillTarget,
        ConfirmingAttack, // New state
        ConfirmingSkill,  // New state
        PlayerMoving,
        ActionPending // Note: Consider if this state is still used/needed with new flow
    }

    [Header("Core References")]
    [Tooltip("Manages grid data and coordinate conversions.")]
    public GridManager gridManager;
    [Tooltip("Manages UI elements like buttons, text, and indicators.")]
    public UIManager uiManager;
    [Tooltip("Visualizes movement range on the grid.")]
    public MovementRangeVisualizer movementRangeVisualizer;

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
    public CharacterClassSO specificPlayerClass2;
    [Tooltip("Default race assigned to enemy units.")]
    public RaceSO defaultEnemyRace;
    [Tooltip("Default class assigned to enemy units.")]
    public CharacterClassSO defaultEnemyClass;

    private const int CT_THRESHOLD = 100;
    private const int DEFAULT_BASE_SPEED = 10;

    // NEW: Turn history tracking
    private List<UnitController> turnHistory = new List<UnitController>();
    private const int MaxTurnHistory = 5; // Store a bit more than needed for UI display flexibility

    public UnitController selectedUnit { get; private set; }
    public UnitController activeUnit { get; private set; }
    private UnitController _targetForConfirmation = null; // NEW: Store target during confirmation

    private PlayerInputActions playerInputActions;
    private List<UnitController> allUnits = new List<UnitController>();
    private List<UnitController> playerUnits = new List<UnitController>();
    private List<UnitController> enemyUnits = new List<UnitController>();
    private List<Vector3Int> _validMovePositions = new List<Vector3Int>();
    private List<UnitController> _validAttackTargets = new List<UnitController>();
    private SkillSO _currentSkill = null;
    private List<UnitController> _validSkillTargets = new List<UnitController>();
    private SelectionState currentState = SelectionState.None;
    private bool isGameOver = false;
    private bool isPlayerActionComplete = false;
    private bool isProcessingClick = false;

    public static GameManager Instance { get; private set; }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        playerInputActions = new PlayerInputActions();
        if (movementRangeVisualizer == null) movementRangeVisualizer = GetComponent<MovementRangeVisualizer>();
        if (movementRangeVisualizer == null) Debug.LogError("GM: MovementRangeVisualizer component missing!", this);
        if (uiManager == null) uiManager = FindFirstObjectByType<UIManager>(); // Modern API
        if (uiManager == null) Debug.LogError("GM: UIManager component missing!", this);
        if (gridManager == null) gridManager = FindFirstObjectByType<GridManager>(); // Modern API
        if (gridManager == null) Debug.LogError("GM: GridManager component missing!", this);

        UnitController.OnUnitDied += HandleUnitDeath;
    }

    void Start()
    {
        isGameOver = false;
        Time.timeScale = 1f;

        SpawnPlayerUnits();
        SpawnEnemyUnits();
        allUnits.AddRange(playerUnits);
        allUnits.AddRange(enemyUnits);

        for (int i = 0; i < allUnits.Count; i++)
        {
            allUnits[i].currentCT = i * 10;
            Debug.Log($"Initialized {allUnits[i].unitName} with CT: {allUnits[i].currentCT}");
        }

        StartCoroutine(BattleLoopCoroutine());
    }

    void OnEnable()
    {
        playerInputActions.Player.Enable();
        playerInputActions.Player.Select.performed += HandleSelectionClick;
        playerInputActions.Player.Cancel.performed += HandleCancelClick;
    }

    void OnDisable()
    {
        playerInputActions.Player.Select.performed -= HandleSelectionClick;
        playerInputActions.Player.Cancel.performed -= HandleCancelClick;
        playerInputActions.Player.Disable();
        UnitController.OnUnitDied -= HandleUnitDeath;
    }

    void Update()
    {
        if (isGameOver && Input.GetKeyDown(KeyCode.R))
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private IEnumerator BattleLoopCoroutine()
    {
        while (!isGameOver)
        {
            var readyUnits = allUnits
                .Where(unit => unit != null && unit.IsAlive && unit.currentCT >= CT_THRESHOLD) // Added null check
                .OrderByDescending(unit => unit.currentCT)
                .ThenByDescending(unit => unit.speed)
                .ToList();

            UnitController nextUnit = readyUnits.FirstOrDefault();
            if (nextUnit != null)
            {
                activeUnit = nextUnit;
                activeUnit.currentCT -= CT_THRESHOLD;
                Debug.Log($"{activeUnit.unitName}'s turn (CT: {activeUnit.currentCT}, Speed: {activeUnit.speed})");

                // NEW: Update UI with turn order display
                if (uiManager != null) // Ensure UIManager exists
                {
                    if (activeUnit != null)
                    {
                        // Predict the next 2 turns (futureTurns[0] is next, futureTurns[1] is after next)
                        List<UnitController> futureTurns = PredictFutureTurns(2);

                        // Get units from history (turnHistory[0] is the *just finished* unit)
                        UnitController unit1TurnAgo = turnHistory.Count >= 2 ? turnHistory[1] : null;
                        UnitController unit2TurnsAgo = turnHistory.Count >= 3 ? turnHistory[2] : null;

                        // Get Sprites (using CharacterClassSO)
                        Sprite sprite2Ago = unit2TurnsAgo?.unitClass?.classSprite;
                        Sprite sprite1Ago = unit1TurnAgo?.unitClass?.classSprite;
                        Sprite spriteActive = activeUnit.unitClass?.classSprite;
                        Sprite spriteNext = futureTurns.Count >= 1 ? futureTurns[0]?.unitClass?.classSprite : null;
                        Sprite spriteAfterNext = futureTurns.Count >= 2 ? futureTurns[1]?.unitClass?.classSprite : null;

                        // Call UIManager to update the display
                        uiManager.UpdateTurnOrderDisplay(sprite2Ago, sprite1Ago, spriteActive, spriteNext, spriteAfterNext);

                        // Update the text indicator
                        uiManager.UpdateTurnIndicatorText($"{activeUnit.unitName}'s Turn");
                    }
                    else
                    {
                        // No unit is active (still charging CT)
                        uiManager.UpdateTurnIndicatorText("Charging CT...");
                        // Clear the turn order display
                        uiManager.UpdateTurnOrderDisplay(null, null, null, null, null);
                    }
                }

                activeUnit.TickStatusEffects(); // Tick effects at start of turn
                if (activeUnit.IsAlive) // Check if still alive after status effects
                {
                    bool isPlayerControlled = playerUnits.Contains(activeUnit);
                    if (isPlayerControlled)
                    {
                        activeUnit.HasActedThisTurn = false;
                        isPlayerActionComplete = false;
                        _targetForConfirmation = null; // Ensure no lingering confirmation target
                        _currentSkill = null; // Ensure no lingering skill
                        Debug.Log($"Starting PLAYER action coroutine for {activeUnit.unitName}");
                        yield return StartCoroutine(WaitForPlayerActionCoroutine(activeUnit));
                    }
                    else
                    {
                        activeUnit.HasActedThisTurn = false;
                        Debug.Log($"Starting AI coroutine for {activeUnit.unitName}");
                        yield return StartCoroutine(ProcessAITurnCoroutine(activeUnit));
                    }

                    // NEW: Add to turn history after unit's action completes
                    if (activeUnit != null) // Check if the turn completed normally
                    {
                        // Add the unit that just acted to the front of the history list
                        turnHistory.Insert(0, activeUnit);
                        // Trim the history list if it exceeds the maximum size
                        while (turnHistory.Count > MaxTurnHistory)
                        {
                            turnHistory.RemoveAt(turnHistory.Count - 1); // Remove the oldest entry from the end
                        }
                    }
                }
                else
                {
                    Debug.Log($"{activeUnit.unitName} died from status effects at turn start.");
                }

                // Cleanup after turn (regardless of player/AI or if died during turn)
                if (uiManager != null)
                {
                    uiManager.HideCombatForecast();
                    uiManager.HideSkillSelection();
                    uiManager.ClearIndicators();
                    uiManager.HideEndTurnButton();
                    uiManager.ShowActionButtons(false);
                    uiManager.UpdateSelectedUnitInfo(null); // Deselect visually
                }
                movementRangeVisualizer.ClearRange();
                ResetSelectionState(); // Ensure state is clean before next turn potentially starts
                activeUnit = null; // Clear active unit reference
            }
            else
            {
                // Advance CT if no unit is ready
                foreach (var unit in allUnits.Where(u => u != null && u.IsAlive)) // Added null check
                {
                    unit.currentCT += unit.speed;
                }
                if (uiManager != null)
                {
                    uiManager.UpdateTurnIndicatorText("Charging CT...");
                    // NEW: Clear turn order display during CT charging
                    uiManager.UpdateTurnOrderDisplay(null, null, null, null, null);
                }
                yield return null; // Wait a frame before checking again
            }

            CheckForGameOver(); // Check game over state each loop iteration
        }
        // End of BattleLoopCoroutine
        Debug.Log("Battle Loop Ended.");
    }

    private IEnumerator WaitForPlayerActionCoroutine(UnitController unit)
    {
        if (unit == null || !unit.IsAlive) yield break;

        selectedUnit = unit; // Set the selected unit for player control
        currentState = SelectionState.UnitSelected;
        isPlayerActionComplete = false;

        // Initial UI setup for the player's turn
        if (uiManager != null)
        {
            uiManager.UpdateSelectedUnitInfo(selectedUnit);
            uiManager.ShowActionButtons(!unit.HasActedThisTurn); // Show if haven't acted
            if (!unit.HasActedThisTurn)
                ShowActionIndicators(selectedUnit); // Show move/attack range
            uiManager.ShowEndTurnButton();
            uiManager.HideCombatForecast(); // Ensure forecast is hidden initially
            uiManager.HideSkillSelection(); // Ensure skill panel is hidden
        }

        // Wait until the player confirms an action or ends their turn
        while (!isPlayerActionComplete && unit.IsAlive && !isGameOver)
        {
            yield return null;
        }

        // Action complete, cleanup UI specifically related to player action selection
        Debug.Log($"Player action complete flag set for {unit.unitName}. Cleaning up action UI.");
        if (uiManager != null)
        {
            uiManager.HideCombatForecast();
            uiManager.HideSkillSelection();
            uiManager.ClearIndicators(); // Clear range/target visuals
            uiManager.ShowActionButtons(false); // Hide action buttons
            uiManager.HideEndTurnButton(); // Hide end turn button
            // Keep selected unit info visible until end of turn potentially
        }
        movementRangeVisualizer.ClearRange();
        currentState = SelectionState.ActionPending; // Or maybe back to None? ActionPending signifies waiting for next turn cycle
        selectedUnit = null; // Deselect unit logically after action
        _targetForConfirmation = null; // Clear confirmation target
        _currentSkill = null; // Clear selected skill
    }

    private void HandleSelectionClick(InputAction.CallbackContext context)
    {
        // Only process clicks if it's a player's turn, they are alive, and game isn't over
        if (activeUnit == null || !playerUnits.Contains(activeUnit) || !activeUnit.IsAlive || isGameOver)
            return;

        StartCoroutine(ProcessClickAfterFrame(context));
    }

    // Coroutine to process click after a frame delay (helps with race conditions/double clicks)
    private IEnumerator ProcessClickAfterFrame(InputAction.CallbackContext context)
    {
        if (isProcessingClick) yield break; // Prevent processing multiple clicks simultaneously
        isProcessingClick = true;
        yield return null; // Wait one frame

        // Ignore clicks on UI elements
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            Debug.Log("Click was on UI element, ignoring world click.");
            isProcessingClick = false;
            yield break;
        }

        if (gridManager == null || Camera.main == null)
        {
            Debug.LogError("Missing GridManager or Main Camera!");
            isProcessingClick = false;
            yield break;
        }

        // Get grid position from mouse click
        Vector2 screenPosition = Mouse.current.position.ReadValue();
        Vector3 worldPosition = Camera.main.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, Camera.main.nearClipPlane));
        Vector3Int gridPos = gridManager.WorldToGrid(worldPosition);
        UnitController clickedUnit = GetUnitAt(gridPos); // Get unit at the clicked position (if any)

        // Route click based on current game state
        switch (currentState)
        {
            case SelectionState.None:
                // This case should technically not be reachable during an active player turn
                // If somehow reached, try selecting the active unit if clicked
                HandleClickInNoneState(clickedUnit);
                break;
            case SelectionState.UnitSelected:
                HandleClickInUnitSelectedState(gridPos, clickedUnit);
                break;
            case SelectionState.SelectingSkillTarget:
                // MODIFIED: Calls updated handler
                HandleClickInSelectingSkillTarget(clickedUnit);
                break;
            case SelectionState.ConfirmingAttack: // NEW: Route to confirmation handler
                HandleClickInConfirmingAttack(clickedUnit);
                break;
            case SelectionState.ConfirmingSkill: // NEW: Route to confirmation handler
                HandleClickInConfirmingSkill(clickedUnit);
                break;
            // PlayerMoving state ignores clicks generally
            // ActionPending state ignores clicks
            // SelectingSkillFromPanel ignores world clicks (handled by UI buttons)
        }

        isProcessingClick = false; // Allow next click processing
    }

    private void HandleClickInNoneState(UnitController clickedUnit)
    {
        // This state is primarily before the first turn or after game over.
        // If we are here during an active turn (edge case), select the active unit if clicked.
        if (clickedUnit != null && playerUnits.Contains(clickedUnit) && clickedUnit == activeUnit && !clickedUnit.HasActedThisTurn)
        {
            Debug.Log($"Unit {clickedUnit.unitName} selected (active unit from None state).");
            selectedUnit = clickedUnit;
            currentState = SelectionState.UnitSelected;
            if (uiManager != null)
            {
                uiManager.UpdateSelectedUnitInfo(selectedUnit);
                uiManager.ShowActionButtons(true);
                uiManager.ShowEndTurnButton();
                ShowActionIndicators(selectedUnit);
            }
        }
        else
        {
            Debug.Log($"Ignoring click in None state. Clicked: {(clickedUnit != null ? clickedUnit.unitName : "Empty")}");
        }
    }

    // MODIFIED: Logic updated for forecast and confirmation state
    private void HandleClickInUnitSelectedState(Vector3Int clickedCell, UnitController clickedUnit)
    {
        if (isGameOver || selectedUnit == null || !selectedUnit.IsAlive || selectedUnit.HasActedThisTurn)
        {
            Debug.LogWarning("Invalid click in UnitSelected state: Game over, unit invalid, or already acted.");
            ResetSelectionState(); // Go back to neutral state
            return;
        }

        // --- Check for Move Action ---
        if (_validMovePositions.Contains(clickedCell) && clickedUnit == null) // Can only move to empty valid cells
        {
            var path = Pathfinder.FindPath(selectedUnit.gridPosition, clickedCell, gridManager, this);
            if (path != null && path.Count > 1) // Path must exist and have more than start node
            {
                Debug.Log($"Moving {selectedUnit.unitName} to {clickedCell}");
                if (uiManager != null)
                {
                    uiManager.ShowActionButtons(false); // Hide buttons during move
                    uiManager.ClearIndicators(); // Clear attack/skill indicators
                    uiManager.HideCombatForecast(); // Ensure forecast is hidden
                }
                movementRangeVisualizer.ClearRange(); // Clear move range visual
                _validMovePositions.Clear(); // Clear cached positions
                _validAttackTargets.Clear(); // Clear cached targets
                currentState = SelectionState.PlayerMoving; // Set state to moving
                selectedUnit.StartMove(path, gridManager, () => OnUnitMoveComplete(selectedUnit)); // Start move coroutine
            }
            else
            {
                Debug.LogWarning($"Pathfinding failed for valid move tile {clickedCell}. Tile possibly became blocked.");
                // Stay in UnitSelected state, action indicators should still be showing
            }
            return; // End processing after handling move attempt
        }

        // --- Check for Attack Action ---
        if (clickedUnit != null && _validAttackTargets.Contains(clickedUnit))
        {
            // Instead of attacking directly, show forecast and enter confirmation state
            Debug.Log($"Player selected attack target: {clickedUnit.unitName}. Calculating forecast.");
            CombatForecastData forecast = CalculateAttackForecast(selectedUnit, clickedUnit);

            if (uiManager != null)
            {
                uiManager.ShowCombatForecast(forecast);
                //uiManager.ClearIndicators(); // Hide range/target visuals, forecast is now primary
                //uiManager.ShowActionButtons(false); // Hide action choice buttons
            }
            movementRangeVisualizer.ClearRange();

            _targetForConfirmation = clickedUnit; // Store the target for confirmation click
            currentState = SelectionState.ConfirmingAttack; // Change state
            return; // End processing after initiating attack confirmation
        }

        // --- Check for clicking self or empty space (Potential Cancel?) ---
        if (clickedUnit == selectedUnit || (clickedUnit == null && !_validMovePositions.Contains(clickedCell)))
        {
            Debug.Log("Clicked self or invalid empty space. Resetting action selection.");
            ResetActionSelection(); // Re-show action indicators, stay in UnitSelected
            return;
        }

        // --- Clicked another unit not in attack range or invalid tile ---
        Debug.Log($"Invalid click target in UnitSelected state: {(clickedUnit != null ? clickedUnit.unitName : "Empty Cell")} at {clickedCell}.");
        // Optionally provide feedback to the player
        ResetActionSelection(); // Re-show action indicators, stay in UnitSelected
    }

    // MODIFIED: Logic updated for forecast and confirmation state
    private void HandleClickInSelectingSkillTarget(UnitController target)
    {
        if (_currentSkill == null || selectedUnit == null || selectedUnit.HasActedThisTurn)
        {
            Debug.LogError("Invalid skill target state: No skill, caster, or caster already acted.");
            CancelSkillSelection(true); // Force cancel back to base state
            return;
        }

        // Check if the clicked target is in the list of valid targets for the current skill
        if (target != null && _validSkillTargets.Contains(target))
        {
            // Instead of executing directly, show forecast and enter confirmation state
            Debug.Log($"Player selected skill target: {target.unitName} for skill '{_currentSkill.skillName}'. Calculating forecast.");

            // Check MP *before* showing forecast, as forecast includes MP cost
            if (selectedUnit.currentMp < _currentSkill.mpCost)
            {
                Debug.LogWarning($"{selectedUnit.unitName} cannot afford skill '{_currentSkill.skillName}'. Needs {_currentSkill.mpCost} MP, has {selectedUnit.currentMp}.");
                // Optionally show UI feedback (e.g., flashing MP cost red)
                // Stay in SelectingSkillTarget state, player needs to choose different target or cancel
                return;
            }

            CombatForecastData forecast = CalculateSkillForecast(selectedUnit, target, _currentSkill);
            Debug.Log($"GameManager: Calculated skill forecast for {selectedUnit.unitName} using '{_currentSkill.skillName}' on {target.unitName}.");

            if (uiManager != null)
            {
                Debug.Log("GameManager: Requesting UIManager to show combat forecast panel.");
                uiManager.ShowCombatForecast(forecast);
                //uiManager.ClearIndicators(); // Hide skill range/target visuals
                //uiManager.HideSkillSelection(); // Ensure skill panel is hidden if it was open
                //uiManager.ShowActionButtons(false); // Hide main action buttons
            }
            movementRangeVisualizer.ClearRange(); // Should be clear already, but ensure it

            _targetForConfirmation = target; // Store target for confirmation
            currentState = SelectionState.ConfirmingSkill; // Change state
        }
        else
        {
            // Clicked on an invalid target or empty space
            Debug.Log($"Invalid skill target clicked: {(target != null ? target.unitName : "Empty Cell")}. Cancelling skill targeting.");
            CancelSkillSelection(false); // Cancel back to UnitSelected state, allowing re-selection of action/skill
        }
    }

    // NEW: Handler for clicks during attack confirmation
    private void HandleClickInConfirmingAttack(UnitController clickedUnit)
    {
        // Check if the click is on the correct target unit stored for confirmation
        if (clickedUnit != null && clickedUnit == _targetForConfirmation)
        {
            Debug.Log($"Attack confirmed: {selectedUnit.unitName} attacking {_targetForConfirmation.unitName}");

            // Execute the attack
            PerformAttack(selectedUnit, _targetForConfirmation);

            // Post-action updates
            selectedUnit.HasActedThisTurn = true;
            isPlayerActionComplete = true; // Signal end of player's action phase for this turn

            // UI Cleanup
            if (uiManager != null)
            {
                uiManager.HideCombatForecast();
                uiManager.ShowActionButtons(false); // Hide action buttons
                uiManager.ClearIndicators(); // Clear any remaining indicators
                uiManager.UpdateSelectedUnitInfo(selectedUnit); // Update info (e.g., HP if damaged by counter) - Counter not implemented yet
            }
            movementRangeVisualizer.ClearRange();

            // State cleanup
            _targetForConfirmation = null;
            currentState = SelectionState.UnitSelected; // Go back to selected state (though action is complete) - or ActionPending? Let's stick to prompt
        }
        else
        {
            // Clicked somewhere else - treat as cancel
            Debug.Log($"Attack confirmation cancelled. Clicked {(clickedUnit != null ? clickedUnit.unitName : "Empty/Other")}.");
            CancelActionConfirmation();
        }
    }

    // NEW: Handler for clicks during skill confirmation
    private void HandleClickInConfirmingSkill(UnitController clickedUnit)
    {
        // Check if the click is on the correct target unit stored for confirmation
        if (clickedUnit != null && clickedUnit == _targetForConfirmation && _currentSkill != null)
        {
            // Double-check MP just before execution (in case state changed unexpectedly)
            if (selectedUnit.currentMp >= _currentSkill.mpCost)
            {
                Debug.Log($"Skill confirmed: {selectedUnit.unitName} using '{_currentSkill.skillName}' on {_targetForConfirmation.unitName}");

                // Execute the skill
                selectedUnit.currentMp -= _currentSkill.mpCost; // Deduct MP
                selectedUnit.currentMp = Mathf.Max(0, selectedUnit.currentMp); // Ensure MP doesn't go negative
                ExecuteSkillEffect(selectedUnit, _targetForConfirmation, _currentSkill);

                // Post-action updates
                selectedUnit.HasActedThisTurn = true;
                isPlayerActionComplete = true; // Signal end of player's action phase

                // UI Cleanup
                if (uiManager != null)
                {
                    Debug.Log("GameManager: Requesting UIManager to hide combat forecast panel after skill execution.");
                    uiManager.HideCombatForecast();
                    uiManager.ShowActionButtons(false);
                    uiManager.ClearIndicators();
                    uiManager.UpdateSelectedUnitInfo(selectedUnit); // Update info (MP cost, potential HP changes)
                }
                movementRangeVisualizer.ClearRange();

                // State cleanup
                _targetForConfirmation = null;
                _currentSkill = null; // Clear the selected skill
                currentState = SelectionState.UnitSelected; // Go back to selected state
            }
            else
            {
                // Should have been caught earlier, but handle as a safeguard
                Debug.LogError($"Insufficient MP for skill '{_currentSkill.skillName}' at confirmation! Needs {_currentSkill.mpCost}, has {selectedUnit.currentMp}. Cancelling.");
                CancelActionConfirmation();
            }
        }
        else
        {
            // Clicked somewhere else - treat as cancel
            Debug.Log($"Skill confirmation cancelled. Clicked {(clickedUnit != null ? clickedUnit.unitName : "Empty/Other")}.");
            CancelActionConfirmation();
        }
    }

    // --- Combat Forecast Calculation Methods ---

    /// <summary>
    /// Calculates the predicted outcome of a basic attack.
    /// </summary>
    /// <param name="attacker">The attacking unit.</param>
    /// <param name="target">The target unit.</param>
    /// <returns>CombatForecastData containing predicted results.</returns>
    public CombatForecastData CalculateAttackForecast(UnitController attacker, UnitController target)
    {
        if (attacker == null || target == null)
        {
            Debug.LogError("Cannot calculate attack forecast: attacker or target is null.");
            // Return default/empty data to avoid null reference errors downstream
            return new CombatForecastData(0, "Unknown", "Unknown", 0, new List<CombatForecastData.StatusEffectForecast>());
        }

        // Damage calculation mirroring PerformAttack
        int predictedDamage = Mathf.Max(1, attacker.attackPower - target.Defense);

        // Create forecast data
        return new CombatForecastData(
            predictedDamage,
            attacker.unitName,
            target.unitName,
            0, // Basic attacks cost 0 MP
            new List<CombatForecastData.StatusEffectForecast>() // Basic attacks apply no status effects here
        );
    }

    /// <summary>
    /// Calculates the predicted outcome of using a skill.
    /// </summary>
    /// <param name="caster">The unit using the skill.</param>
    /// <param name="target">The unit targeted by the skill.</param>
    /// <param name="skill">The skill being used.</param>
    /// <returns>CombatForecastData containing predicted results.</returns>
    public CombatForecastData CalculateSkillForecast(UnitController caster, UnitController target, SkillSO skill)
    {
        if (caster == null || target == null || skill == null)
        {
            Debug.LogError("Cannot calculate skill forecast: caster, target, or skill is null.");
            return new CombatForecastData(0, "Unknown", "Unknown", 0, new List<CombatForecastData.StatusEffectForecast>());
        }

        int predictedDamage = 0;

        // Damage calculation mirroring ExecuteSkillEffect
        if (skill.isHarmful)
        {
            // Use caster's attack power + skill base power for harmful skills, similar to ExecuteSkillEffect logic
            int rawDamage = caster.attackPower + skill.basePower;
            predictedDamage = Mathf.Max(1, rawDamage - target.Defense);
        }
        // Note: Non-harmful skills currently deal 0 damage in forecast. Healing/buff amounts aren't forecasted here.

        // Status Effect Forecasting
        List<CombatForecastData.StatusEffectForecast> statusEffects = new List<CombatForecastData.StatusEffectForecast>();
        if (skill.statusEffectApplied != null)
        {
            // Use skill's specific duration if set, otherwise fallback to effect's base duration
            int duration = (skill.statusEffectDuration > 0) ? skill.statusEffectDuration : skill.statusEffectApplied.baseDuration;

            statusEffects.Add(new CombatForecastData.StatusEffectForecast(
                skill.statusEffectApplied.effectName,
                duration,
                skill.statusEffectApplied.icon // Ensure icon is assigned in the SO
            ));
        }

        // Create forecast data
        return new CombatForecastData(
            predictedDamage,
            caster.unitName,
            target.unitName,
            skill.mpCost,
            statusEffects
        );
    }

    // --- End Combat Forecast Calculation Methods ---

    // NEW: Predict future turns based on CT and speed
    private List<UnitController> PredictFutureTurns(int numberOfTurns)
    {
        List<UnitController> predictedTurns = new List<UnitController>();
        if (numberOfTurns <= 0) return predictedTurns;

        // Get all living units EXCEPT the currently active one (if any)
        List<UnitController> potentialActors = GetAllUnits()
                                                .Where(u => u != null && u.IsAlive && u != activeUnit)
                                                .ToList();

        if (potentialActors.Count == 0) return predictedTurns;

        // Store simulated CT values. Start with current real CT.
Dictionary<UnitController, float> simulatedCT = potentialActors.ToDictionary(u => u, u => (float)u.currentCT);
        // Predict the required number of turns
        for (int i = 0; i < numberOfTurns && potentialActors.Count > 0; i++)
        {
            UnitController nextUnit = null;
            float minTicksToThreshold = float.MaxValue;

            // Determine which unit reaches the threshold next based on time = (Threshold - CurrentCT) / Speed
            foreach (var unit in potentialActors)
            {
                // Ensure the unit hasn't already been predicted in this simulation run
                if (predictedTurns.Contains(unit)) continue;

                float currentSimCT = simulatedCT[unit];
                // Handle cases where unit might already be >= threshold in simulation state
                if (currentSimCT >= CT_THRESHOLD)
                {
                    // This unit acts immediately in this simulation step
                    minTicksToThreshold = 0f;
                    nextUnit = unit;
                    break; // Found an immediate actor
                }

                float speed = unit.speed > 0 ? unit.speed : 1; // Prevent division by zero
                float ticksNeeded = (CT_THRESHOLD - currentSimCT) / speed;

                if (nextUnit == null || ticksNeeded < minTicksToThreshold)
                {
                    minTicksToThreshold = ticksNeeded;
                    nextUnit = unit;
                }
                // Tie-breaker: Higher speed acts first if time is equal
                else if (Mathf.Approximately(ticksNeeded, minTicksToThreshold))
                {
                    if (unit.speed > nextUnit.speed)
                    {
                        nextUnit = unit;
                    }
                }
            }

            // If a next unit was determined for this prediction step
            if (nextUnit != null)
            {
                predictedTurns.Add(nextUnit);

                // If we predicted based on minimum time > 0, advance other units' simulated CT
                if (minTicksToThreshold > 0)
                {
                    float timeElapsed = minTicksToThreshold;
                    foreach (var unit in potentialActors)
                    {
                        // Don't advance the unit that just "acted" in the simulation yet
                        if (unit != nextUnit)
                        {
                            simulatedCT[unit] += unit.speed * timeElapsed;
                        }
                    }
                }
                // Now update the CT of the unit that "acted" - subtract threshold
                simulatedCT[nextUnit] -= CT_THRESHOLD;
                if (simulatedCT[nextUnit] < 0) simulatedCT[nextUnit] = 0; // Clamp CT floor
            }
            else
            {
                // No unit could be predicted
                break;
            }
        }

        return predictedTurns;
    }

    private IEnumerator ProcessAITurnCoroutine(UnitController enemyUnit)
    {
        if (!enemyUnit.IsAlive) yield break;
        Debug.Log($"Processing AI for {enemyUnit.unitName}");
        yield return new WaitForSeconds(0.5f); // AI thinking time

        var livingPlayers = playerUnits.Where(p => p != null && p.IsAlive).ToList();
        if (!livingPlayers.Any())
        {
            Debug.Log($"{enemyUnit.unitName}: No living players found, ending turn.");
            enemyUnit.HasActedThisTurn = true; // Ensure turn ends even if no action taken
            yield break;
        }

        // Simple AI: Target lowest HP player, then closest
        UnitController target = livingPlayers
            .OrderBy(p => p.currentHp)
            .ThenBy(p => Vector3Int.Distance(p.gridPosition, enemyUnit.gridPosition))
            .FirstOrDefault();

        if (target == null)
        {
            Debug.Log($"{enemyUnit.unitName} could not find a valid target.");
            enemyUnit.HasActedThisTurn = true; // End turn
            yield break;
        }

        Debug.Log($"{enemyUnit.unitName} targeting {target.unitName}");

        // --- AI Skill Logic ---
        var skills = enemyUnit.unitClass?.availableSkills?.Where(s => s.isHarmful && enemyUnit.currentMp >= s.mpCost).ToList(); // Only consider affordable harmful skills
        bool usedSkill = false;
        if (skills != null && skills.Any() && Random.value < 0.4f) // Increased chance to use skill
        {
            var skill = skills[Random.Range(0, skills.Count)];
            int dist = Mathf.Abs(target.gridPosition.x - enemyUnit.gridPosition.x) +
                       Mathf.Abs(target.gridPosition.y - enemyUnit.gridPosition.y);

            if (dist <= skill.range)
            {
                Debug.Log($"{enemyUnit.unitName} using skill '{skill.skillName}' on {target.unitName}.");
                enemyUnit.currentMp -= skill.mpCost;
                enemyUnit.currentMp = Mathf.Max(0, enemyUnit.currentMp);
                Debug.Log($"{enemyUnit.unitName} MP reduced to {enemyUnit.currentMp}.");
                ExecuteSkillEffect(enemyUnit, target, skill); // Execute directly
                enemyUnit.HasActedThisTurn = true;
                usedSkill = true;
                yield return new WaitForSeconds(0.75f); // Wait after skill
            }
        }

        // --- AI Attack/Move Logic (if skill not used) ---
        if (!usedSkill)
        {
            int attackDist = Mathf.Abs(target.gridPosition.x - enemyUnit.gridPosition.x) +
                             Mathf.Abs(target.gridPosition.y - enemyUnit.gridPosition.y);

            // Attack if in range
            if (attackDist <= enemyUnit.attackRange)
            {
                Debug.Log($"{enemyUnit.unitName} is in range to attack {target.unitName}.");
                PerformAttack(enemyUnit, target); // Attack directly
                enemyUnit.HasActedThisTurn = true;
                yield return new WaitForSeconds(0.75f); // Wait after attack
            }
            // Move if not in attack range
            else
            {
                var moveTargetCell = FindBestMoveTowards(enemyUnit, target.gridPosition);
                if (moveTargetCell != enemyUnit.gridPosition) // Found a valid move target closer to player
                {
                    var path = Pathfinder.FindPath(enemyUnit.gridPosition, moveTargetCell, gridManager, this);
                    if (path != null && path.Count > 1)
                    {
                        Debug.Log($"{enemyUnit.unitName} moving towards target via {moveTargetCell}.");
                        yield return StartCoroutine(MoveEnemyUnit(enemyUnit, path)); // Wait for move to complete

                        // Check if now in attack range *after* moving
                        if (target != null && target.IsAlive) // Target might have died during move effects? Unlikely now.
                        {
                            attackDist = Mathf.Abs(target.gridPosition.x - enemyUnit.gridPosition.x) +
                                         Mathf.Abs(target.gridPosition.y - enemyUnit.gridPosition.y);
                            if (attackDist <= enemyUnit.attackRange)
                            {
                                Debug.Log($"{enemyUnit.unitName} attacking {target.unitName} after moving.");
                                PerformAttack(enemyUnit, target); // Attack directly
                                enemyUnit.HasActedThisTurn = true;
                                yield return new WaitForSeconds(0.75f); // Wait after attack
                            }
                            else
                            {
                                Debug.Log($"{enemyUnit.unitName} moved but still not in range to attack.");
                                enemyUnit.HasActedThisTurn = true; // Mark action as complete even if only moved
                            }
                        }
                        else
                        {
                            enemyUnit.HasActedThisTurn = true; // Target died or invalid, action complete
                        }
                    }
                    else
                    {
                        Debug.Log($"{enemyUnit.unitName} couldn't find path to move target {moveTargetCell}. Ending turn.");
                        enemyUnit.HasActedThisTurn = true; // Cannot move, end turn
                    }
                }
                else
                {
                    Debug.Log($"{enemyUnit.unitName} cannot move closer to target {target.unitName}. Ending turn.");
                    enemyUnit.HasActedThisTurn = true; // Cannot move, end turn
                }
            }
        }

        // Ensure HasActedThisTurn is set if somehow missed
        if (!enemyUnit.HasActedThisTurn)
        {
            Debug.LogWarning($"{enemyUnit.unitName} finished AI turn without HasActedThisTurn being set. Setting now.");
            enemyUnit.HasActedThisTurn = true;
        }

        yield return new WaitForSeconds(0.25f); // Brief pause at end of AI turn
    }

    public void OnMoveButtonPressed()
    {
        if (isGameOver || activeUnit == null || activeUnit != selectedUnit || selectedUnit.HasActedThisTurn || currentState != SelectionState.UnitSelected)
        {
            Debug.LogWarning("Move button pressed at invalid time.");
            return;
        }
        // Simply ensure the indicators are showing (they should be already if in UnitSelected)
        ShowActionIndicators(selectedUnit);
        // Hide forecast if it was somehow visible
        if (uiManager != null) uiManager.HideCombatForecast();
    }

    public void OnAttackButtonPressed()
    {
        if (isGameOver || activeUnit == null || activeUnit != selectedUnit || selectedUnit.HasActedThisTurn || currentState != SelectionState.UnitSelected)
        {
            Debug.LogWarning("Attack button pressed at invalid time.");
            return;
        }
        // Ensure indicators are showing
        ShowActionIndicators(selectedUnit);
        // Hide forecast if it was somehow visible
        if (uiManager != null) uiManager.HideCombatForecast();
    }

    public void OnSkillButtonPressed()
    {
        if (isGameOver || activeUnit == null || activeUnit != selectedUnit || selectedUnit.HasActedThisTurn || currentState != SelectionState.UnitSelected)
        {
            Debug.LogWarning("Skill button pressed at invalid time.");
            return;
        }

        var skills = selectedUnit.unitClass?.availableSkills;
        if (skills == null || skills.Count == 0)
        {
            Debug.LogWarning($"Unit '{selectedUnit.unitName}' has no skills defined.");
            // Provide UI feedback?
            return;
        }

        // Clear existing indicators and hide action buttons before showing skill UI/range
        if (uiManager != null)
        {
            uiManager.ClearIndicators();
            uiManager.ShowActionButtons(false);
            uiManager.HideCombatForecast(); // Ensure forecast hidden
        }
        movementRangeVisualizer.ClearRange();
        _validAttackTargets.Clear(); // Clear attack targets when entering skill selection
        _validMovePositions.Clear(); // Clear move positions

        // Check if affordable skills exist first
        bool canAffordAny = skills.Any(s => s != null && selectedUnit.currentMp >= s.mpCost); // Added null check for skill SO
        if (!canAffordAny)
        {
            Debug.Log($"{selectedUnit.unitName} cannot afford any skills.");
            // Provide UI feedback?
            // Revert UI back to showing action buttons/indicators
            ResetActionSelection();
            return;
        }

        // If only one *affordable* skill, directly enter targeting mode for it
        var affordableSkills = skills.Where(s => s != null && selectedUnit.currentMp >= s.mpCost).ToList();
        if (affordableSkills.Count == 1)
        {
            var skill = affordableSkills[0];
            Debug.Log($"Only one affordable skill '{skill.skillName}'. Entering targeting mode.");
            _currentSkill = skill;
            currentState = SelectionState.SelectingSkillTarget;
            ShowSkillTargetingRange(selectedUnit, skill);
        }
        // If multiple affordable skills, show the selection panel
        else
        {
            Debug.Log($"Multiple affordable skills available. Showing skill selection panel.");
            currentState = SelectionState.SelectingSkillFromPanel;
            if (uiManager != null)
                uiManager.ShowSkillSelection(selectedUnit, skills, HandleSkillSelectedFromPanel); // ShowSkillSelection should handle filtering affordable ones internally now
        }
    }

    private void HandleSkillSelectedFromPanel(SkillSO chosenSkill)
    {
        // Ensure we are in the right state and have valid data
        if (currentState != SelectionState.SelectingSkillFromPanel || selectedUnit == null || chosenSkill == null)
        {
            Debug.LogWarning($"Invalid skill selection attempt. State: {currentState}, Unit: {selectedUnit?.unitName}, Skill: {chosenSkill?.skillName}");
            CancelSkillSelection(true); // Force cancel back to base state
            return;
        }

        // Check MP cost again just in case
        if (selectedUnit.currentMp < chosenSkill.mpCost)
        {
            Debug.LogWarning($"{selectedUnit.unitName} cannot afford selected skill '{chosenSkill.skillName}'. Needs {chosenSkill.mpCost}, has {selectedUnit.currentMp}.");
            // Keep panel open? Or close and revert? Let's close and revert.
            CancelSkillSelection(false); // Cancel back to unit selected
            return;
        }

        Debug.Log($"Skill selected from panel: '{chosenSkill.skillName}' for {selectedUnit.unitName}.");
        _currentSkill = chosenSkill;
        currentState = SelectionState.SelectingSkillTarget; // Move to targeting state

        // Hide the skill selection panel and show the targeting visuals
        if (uiManager != null)
            uiManager.HideSkillSelection();

        ShowSkillTargetingRange(selectedUnit, chosenSkill); // Display target indicators for the chosen skill
    }

    public void OnEndTurnButtonPressed()
    {
        if (isGameOver || activeUnit == null || !playerUnits.Contains(activeUnit) || currentState == SelectionState.PlayerMoving)
        {
            Debug.LogWarning($"End turn button pressed at invalid time. State: {currentState}");
            return;
        }
        Debug.Log($"{activeUnit.unitName} chose to WAIT/End Turn.");
        isPlayerActionComplete = true; // Signal action completion
        // Cleanup handled by BattleLoopCoroutine when isPlayerActionComplete becomes true
    }

    private void OnUnitMoveComplete(UnitController unit)
    {
        if (isGameOver || unit == null || !playerUnits.Contains(unit) || !unit.IsAlive)
        {
            Debug.LogWarning("OnUnitMoveComplete called for invalid unit or after game over.");
            return; // Avoid further processing if state is invalid
        }

        Debug.Log($"{unit.unitName} finished moving to {unit.gridPosition}.");
        // After moving, the unit is still selected and can potentially act (if HasActedThisTurn is false)
        selectedUnit = unit; // Ensure unit remains selected
        currentState = SelectionState.UnitSelected; // Return to the main selection state

        // Update UI to reflect post-move options
        if (uiManager != null)
        {
            uiManager.UpdateSelectedUnitInfo(selectedUnit); // Refresh unit info panel
            uiManager.ShowActionButtons(!selectedUnit.HasActedThisTurn); // Show action buttons IF the unit hasn't acted yet
            if (!selectedUnit.HasActedThisTurn)
            {
                ShowActionIndicators(selectedUnit); // Show available attack targets from new position
            }
            else
            {
                uiManager.ClearIndicators(); // If unit already acted, clear indicators
                movementRangeVisualizer.ClearRange();
            }
            uiManager.ShowEndTurnButton(); // End turn is always available after move (or action)
        }
    }

    // Actual execution methods (unchanged core logic)
    private void PerformAttack(UnitController attacker, UnitController target)
    {
        if (isGameOver || attacker == null || !attacker.IsAlive || target == null || !target.IsAlive) return;

        int rawDamage = attacker.attackPower;
        int defense = target.Defense;
        int finalDamage = Mathf.Max(1, rawDamage - defense); // Ensure at least 1 damage

        Debug.Log($"{attacker.unitName} attacks {target.unitName} for {finalDamage} damage! (Raw: {rawDamage}, Def: {defense})");
        target.TakeDamage(finalDamage);

        // TODO: Add counter-attack logic?
        // TODO: Add visual/audio feedback for attack
    }

    private void ExecuteSkillEffect(UnitController caster, UnitController target, SkillSO skill)
    {
        if (isGameOver || caster == null || !caster.IsAlive || target == null || skill == null)
        {
            Debug.LogError($"ExecuteSkillEffect failed: Invalid parameters. Caster: {caster?.unitName}, Target: {target?.unitName}, Skill: {skill?.skillName}");
            return;
        }
        // Check target validity based on skill type AFTER ensuring target isn't null
        bool isTargetValidForSkill = (skill.isHarmful && enemyUnits.Contains(target)) || (!skill.isHarmful && playerUnits.Contains(target));
        if (!isTargetValidForSkill && target != caster) // Allow self-targeting for non-harmful skills? Assume yes for now.
        {
            // This check should ideally happen before calling ExecuteSkillEffect, but added as safeguard.
            Debug.LogWarning($"ExecuteSkillEffect: Target {target.unitName} is not valid for skill '{skill.skillName}' type (Harmful: {skill.isHarmful}). Aborting effect.");
            return;
        }

        Debug.Log($"Applying effect of '{skill.skillName}' from {caster.unitName} to {target.unitName}...");

        // Apply Damage (if harmful)
        if (skill.isHarmful && target.IsAlive) // Check target is alive before dealing damage
        {
            int rawDamage = caster.attackPower + skill.basePower; // Consistent with forecast
            int finalDamage = Mathf.Max(1, rawDamage - target.Defense);
            Debug.Log($"Harmful Skill '{skill.skillName}'! (CasterATK:{caster.attackPower} + SkillPower:{skill.basePower}) = {rawDamage} vs TargetDEF:{target.Defense} = {finalDamage} damage.");
            target.TakeDamage(finalDamage);
        }
        else if (!skill.isHarmful) // Handle Buffs/Healing (Example)
        {
            // Placeholder: Add logic for healing or applying buffs based on skill.basePower or other properties
            if (skill.basePower > 0) // Example: Treat basePower as heal amount
            {
                int healAmount = skill.basePower; // Simple heal for now
                Debug.Log($"Non-harmful skill '{skill.skillName}' healing {target.unitName} for {healAmount}.");
                target.Heal(healAmount);
            }
            else
            {
                Debug.Log($"Non-harmful skill '{skill.skillName}' applied (no direct heal/damage).");
            }
        }

        // Apply Status Effect (if any and target still alive)
        if (skill.statusEffectApplied != null && target != null && target.IsAlive)
        {
            int duration = (skill.statusEffectDuration > 0) ? skill.statusEffectDuration : skill.statusEffectApplied.baseDuration;
            Debug.Log($"Applying status effect '{skill.statusEffectApplied.effectName}' to {target.unitName} for {duration} turns.");
            target.AddStatusEffect(skill.statusEffectApplied, duration);
        }
        // TODO: Add visual/audio feedback for skill
    }

    // Shows Movement Range and Attack Targets from current position
    private void ShowActionIndicators(UnitController unit)
    {
        if (unit == null || unit.HasActedThisTurn || uiManager == null || gridManager == null || movementRangeVisualizer == null) return;

        Debug.Log($"Showing action indicators for {unit.unitName}");

        // Clear previous visuals
        movementRangeVisualizer.ClearRange();
        if (uiManager != null) uiManager.ClearIndicators(); // Clears attack/skill target visuals
        if (uiManager != null) uiManager.HideCombatForecast(); // Ensure forecast hidden

        // Show Movement Range
        _validMovePositions = gridManager.CalculateReachableTiles(unit.gridPosition, unit.moveRange, this, unit);
        movementRangeVisualizer.ShowRange(_validMovePositions, gridManager);

        // Show Attack Targets
        _validAttackTargets = GetAllUnits() // Use helper to get current list
            .Where(e => e != null && e.IsAlive && // Target must be alive
                        !playerUnits.Contains(e) && // Target must be an enemy (basic attack)
                        IsUnitInManhattanRange(unit.gridPosition, e.gridPosition, unit.attackRange)) // Check range
            .ToList();

        if (uiManager != null) uiManager.VisualizeAttackTargets(_validAttackTargets, gridManager);
        Debug.Log($"Found {_validMovePositions.Count} move tiles and {_validAttackTargets.Count} attack targets for {unit.unitName}.");
    }

    // Shows potential targets for a specific skill
    private void ShowSkillTargetingRange(UnitController caster, SkillSO skill)
    {
        if (uiManager == null || gridManager == null || caster == null || skill == null)
        {
            Debug.LogError("Cannot show skill targeting range: Missing references or data.");
            return;
        }

        // Clear previous visuals
        uiManager.ClearIndicators(); // Clear attack/skill visuals
        movementRangeVisualizer.ClearRange(); // Clear move visuals
        uiManager.HideCombatForecast(); // Ensure forecast hidden

        _validSkillTargets.Clear(); // Reset the list of valid targets

        var potentialTargets = GetAllUnits(); // Get all currently active units

        foreach (var potentialTarget in potentialTargets)
        {
            if (potentialTarget == null || !potentialTarget.IsAlive) continue; // Skip dead/null units

            // Check Manhattan distance for range
            if (IsUnitInManhattanRange(caster.gridPosition, potentialTarget.gridPosition, skill.range))
            {
                // Check team validity based on skill type
                bool isTargetValidTeam = (skill.isHarmful && enemyUnits.Contains(potentialTarget)) ||
                                         (!skill.isHarmful && playerUnits.Contains(potentialTarget)) ||
                                         (!skill.isHarmful && potentialTarget == caster); // Allow self-target for non-harmful?

                if (isTargetValidTeam)
                {
                    _validSkillTargets.Add(potentialTarget);
                }
            }
        }

        Debug.Log($"Skill '{skill.skillName}': Found {_validSkillTargets.Count} valid targets in range {skill.range}.");
        uiManager.VisualizeSkillRange(_validSkillTargets, gridManager); // Update UI
    }

    // Helper to check Manhattan distance
    private bool IsUnitInManhattanRange(Vector3Int pos1, Vector3Int pos2, int range)
    {
        return Mathf.Abs(pos1.x - pos2.x) + Mathf.Abs(pos1.y - pos2.y) <= range;
    }

    // Helper to get a combined list of living units
    public List<UnitController> GetAllUnits()
    {
        // Consider caching this if called very frequently, but recreating is safer if units die often
        return playerUnits.Concat(enemyUnits).Where(u => u != null && u.IsAlive).ToList();
    }

    public UnitController GetUnitAt(Vector3Int pos)
    {
        // Perf optimization: Check grid manager first if it stores unit locations?
        // Otherwise, iterate through combined list
        return GetAllUnits().FirstOrDefault(u => u.gridPosition == pos);
    }

    // MODIFIED: Added 'forceReset' parameter
    /// <summary>
    /// Cancels the current skill selection process.
    /// </summary>
    /// <param name="forceReset">If true, resets state fully to None. If false, resets to UnitSelected.</param>
    public void CancelSkillSelection(bool forceReset = false)
    {
        Debug.Log($"Skill selection cancelled. Force Reset: {forceReset}");
        _currentSkill = null;
        _validSkillTargets.Clear();
        _targetForConfirmation = null; // Ensure confirmation target cleared

        if (uiManager != null)
        {
            uiManager.HideSkillSelection();
            uiManager.ClearIndicators(); // Clear skill visuals
            uiManager.HideCombatForecast(); // Hide forecast if shown
        }

        if (forceReset || selectedUnit == null || !selectedUnit.IsAlive)
        {
            ResetSelectionState(); // Full reset if forced or unit invalid
        }
        else
        {
            // Revert to UnitSelected state, show base action indicators again
            currentState = SelectionState.UnitSelected;
            if (uiManager != null)
            {
                uiManager.ShowActionButtons(!selectedUnit.HasActedThisTurn);
                if (!selectedUnit.HasActedThisTurn) ShowActionIndicators(selectedUnit);
                uiManager.UpdateSelectedUnitInfo(selectedUnit); // Ensure info panel is correct
            }
        }
    }

    // NEW: Cancels out of a confirmation state back to UnitSelected
    private void CancelActionConfirmation()
    {
        Debug.Log("Action confirmation cancelled.");
        _targetForConfirmation = null;
        // _currentSkill remains if cancelling skill confirm, cleared if attack confirm (was null anyway)

        if (uiManager != null)
        {
            uiManager.HideCombatForecast();
        }

        // Decide which state to return to
        if (_currentSkill != null) // We were confirming a skill
        {
            currentState = SelectionState.SelectingSkillTarget; // Go back to selecting target for the *same* skill
            ShowSkillTargetingRange(selectedUnit, _currentSkill); // Re-show skill range
        }
        else // We were confirming an attack
        {
            currentState = SelectionState.UnitSelected; // Go back to general action selection
            if (selectedUnit != null && !selectedUnit.HasActedThisTurn)
            {
                ShowActionIndicators(selectedUnit); // Re-show move/attack options
                if (uiManager != null) uiManager.ShowActionButtons(true);
            }
        }
        // Ensure unit info panel is up to date
        if (uiManager != null && selectedUnit != null) uiManager.UpdateSelectedUnitInfo(selectedUnit);
    }

    // NEW: Resets selection indicators and buttons but stays in UnitSelected state
    private void ResetActionSelection()
    {
        Debug.Log("Resetting action selection visuals.");
        if (selectedUnit != null && currentState == SelectionState.UnitSelected && !selectedUnit.HasActedThisTurn)
        {
            if (uiManager != null)
            {
                uiManager.ClearIndicators();
                uiManager.HideCombatForecast();
                uiManager.ShowActionButtons(true);
                uiManager.UpdateSelectedUnitInfo(selectedUnit);
            }
            movementRangeVisualizer.ClearRange();
            ShowActionIndicators(selectedUnit); // Re-display move/attack ranges
        }
        else
        {
            // If not in a state where resetting actions makes sense, do a full reset
            ResetSelectionState();
        }
        _targetForConfirmation = null; // Ensure confirmation target cleared
        // _currentSkill = null; // Should we clear current skill here too? Maybe not if user just misclicked.
    }

    // Resets state completely, deselecting unit and clearing UI.
    private void ResetSelectionState()
    {
        Debug.Log($"Resetting selection state fully. Previous state: {currentState}");
        selectedUnit = null; // Deselect unit logically
        _currentSkill = null;
        _targetForConfirmation = null;
        _validMovePositions.Clear();
        _validAttackTargets.Clear();
        _validSkillTargets.Clear();

        // Reset UI elements
        if (uiManager != null)
        {
            uiManager.UpdateSelectedUnitInfo(null); // Clear selection info panel
            uiManager.ClearIndicators(); // Clear all range/target visuals
            uiManager.HideSkillSelection(); // Hide skill panel
            uiManager.HideCombatForecast(); // Hide forecast panel
            uiManager.ShowActionButtons(false); // Hide action buttons
            // Hide end turn button unless it's specifically an active player turn (handled in BattleLoop/WaitForPlayerAction)
            if (activeUnit == null || !playerUnits.Contains(activeUnit))
                uiManager.HideEndTurnButton();
        }
        movementRangeVisualizer.ClearRange(); // Clear move range visuals

        currentState = SelectionState.None; // Set state back to neutral
    }

    private Vector3Int FindBestMoveTowards(UnitController unit, Vector3Int targetPos)
    {
        var start = unit.gridPosition;
        // Use unit's actual move range for calculation
        var reachable = gridManager.CalculateReachableTiles(start, unit.moveRange, this, unit);

        if (!reachable.Any()) return start; // Cannot move anywhere

        int bestDist = int.MaxValue; // Initialize with max value
        Vector3Int bestTile = start; // Default to staying put

        // Check current distance first
        bestDist = Mathf.Abs(start.x - targetPos.x) + Mathf.Abs(start.y - targetPos.y);

        // Iterate through reachable tiles to find one closer to the target
        foreach (var tile in reachable)
        {
            // Ensure the tile is actually empty (AI shouldn't try to move onto occupied tiles)
            if (GetUnitAt(tile) != null && tile != start) continue;

            int d = Mathf.Abs(tile.x - targetPos.x) + Mathf.Abs(tile.y - targetPos.y);

            // Found a closer tile
            if (d < bestDist)
            {
                bestDist = d;
                bestTile = tile;
            }
            // Optional: If distance is equal, prefer tiles that aren't the start tile?
            else if (d == bestDist && bestTile == start)
            {
                bestTile = tile;
            }
        }
        return bestTile; // Return the best tile found (could be the start tile if no closer tile is reachable)
    }

    private IEnumerator MoveEnemyUnit(UnitController unit, List<Vector3Int> path)
    {
        bool moveComplete = false;
        unit.StartMove(path, gridManager, () => { moveComplete = true; });
        yield return new WaitUntil(() => moveComplete);
        Debug.Log($"{unit.unitName} finished AI move to {unit.gridPosition}.");
    }

    private void HandleUnitDeath(UnitController deadUnit)
    {
        if (deadUnit == null || isGameOver) return;
        Debug.Log($"'{deadUnit.unitName}' died.");

        // Remove from team lists and main list
        bool removedPlayer = playerUnits.Remove(deadUnit);
        bool removedEnemy = enemyUnits.Remove(deadUnit);
        allUnits.Remove(deadUnit); // Remove from the combined list used for turn order

        // If the currently selected/active/targeted unit died, adjust state
        if (selectedUnit == deadUnit) selectedUnit = null;
        if (activeUnit == deadUnit) activeUnit = null; // Active unit died, BattleLoop will handle turn advancement
        if (_targetForConfirmation == deadUnit) _targetForConfirmation = null; // Target died before confirmation

        // Clean up lists used for targeting visuals
        _validAttackTargets.Remove(deadUnit);
        _validSkillTargets.Remove(deadUnit);

        // Remove from turn history
        turnHistory.Remove(deadUnit);

        // If the death occurred during player action, potentially end the action prematurely
        if (removedPlayer && activeUnit == deadUnit) // Check if the active player unit died
        {
            Debug.Log($"Active player unit {deadUnit.unitName} died. Ending player action.");
            isPlayerActionComplete = true;
            ResetSelectionState(); // Reset UI and state
        }
        else if (currentState == SelectionState.ConfirmingAttack || currentState == SelectionState.ConfirmingSkill)
        {
            // If confirming action and either attacker or target dies, cancel confirmation
            if (selectedUnit == null || !selectedUnit.IsAlive || _targetForConfirmation == null || !_targetForConfirmation.IsAlive)
            {
                Debug.Log("Unit involved in confirmation died. Cancelling action.");
                CancelActionConfirmation();
            }
        }
        else if (currentState == SelectionState.SelectingSkillTarget)
        {
            // If selecting skill target and caster dies, cancel fully
            if (selectedUnit == null || !selectedUnit.IsAlive)
            {
                CancelSkillSelection(true);
            }
        }

        CheckForGameOver(); // Check if the death ended the game
    }

    private void CheckForGameOver()
    {
        if (isGameOver) return; // Don't check again if already over

        // Check if any units remain on each team
        bool anyPlayerLeft = playerUnits.Any(u => u != null && u.IsAlive);
        bool anyEnemyLeft = enemyUnits.Any(u => u != null && u.IsAlive);

        if (!anyPlayerLeft)
        {
            isGameOver = true;
            Debug.Log("GAME OVER - Player Defeated!");
            if (uiManager != null)
            {
                uiManager.UpdateTurnIndicatorText("GAME OVER (R to Restart)");
                uiManager.HideEndTurnButton();
                uiManager.ShowActionButtons(false);
                uiManager.HideCombatForecast();
                uiManager.HideSkillSelection();
                uiManager.ClearIndicators();
            }
            Time.timeScale = 0f; // Pause game
        }
        else if (!anyEnemyLeft)
        {
            isGameOver = true;
            Debug.Log("VICTORY! - Enemies Defeated!");
            if (uiManager != null)
            {
                uiManager.UpdateTurnIndicatorText("VICTORY! (R to Restart)");
                uiManager.HideEndTurnButton();
                uiManager.ShowActionButtons(false);
                uiManager.HideCombatForecast();
                uiManager.HideSkillSelection();
                uiManager.ClearIndicators();
            }
            // Optionally keep time scale at 1f for victory animation? Or pause. Let's pause.
            // Time.timeScale = 0f;
        }
    }

    // --- Unit Spawning Methods ---
    private void SpawnPlayerUnits()
    {
        playerUnits.Clear();
        HashSet<Vector3Int> occupied = new HashSet<Vector3Int>();
        for (int i = 0; i < playerStartPositions.Count; i++)
        {
            Vector3Int pos = playerStartPositions[i];
            if (!gridManager.IsWalkable(pos) || occupied.Contains(pos))
            {
                Debug.LogError($"Invalid or occupied spawn position for player unit at {pos}");
                continue;
            }
            occupied.Add(pos);
            GameObject unitGO = Instantiate(playerUnitPrefab, gridManager.GridToWorld(pos), Quaternion.identity);
            UnitController unit = unitGO.GetComponent<UnitController>();
            if (unit != null)
            {
                unit.PlaceUnit(pos, gridManager);
                string unitName = $"Player {i + 1}";
                // Assign specific class to second unit if available
                CharacterClassSO classToAssign = (i == 1 && specificPlayerClass2 != null) ? specificPlayerClass2 : defaultPlayerClass;
                RaceSO raceToAssign = defaultPlayerRace; // Assuming same race for now

                if (classToAssign == null || raceToAssign == null)
                {
                    Debug.LogError($"Missing default Race or Class SO for Player {i+1}. Cannot initialize stats.");
                    Destroy(unitGO);
                    continue;
                }

                unit.InitializeStats(unitName, raceToAssign, classToAssign);
                // Calculate speed based on race and class modifiers
                int baseSpeed = raceToAssign.baseSpeed; // Use race base speed
                unit.speed = Mathf.Max(1, baseSpeed + classToAssign.speedModifier); // Add class mod, ensure minimum speed of 1
                if (unit.speed <= 0) // Double check just in case
                {
                    Debug.LogError($"Unit {unitName} has invalid speed ({unit.speed}). Setting to 1.");
                    unit.speed = 1;
                }
                unit.currentCT = 0; // Initialize CT
                unitGO.name = unitName; // Set GameObject name
                playerUnits.Add(unit);
            }
            else
            {
                Debug.LogError("Spawned player prefab missing UnitController!");
                Destroy(unitGO);
            }
        }
        Debug.Log($"Spawned {playerUnits.Count} player units.");
    }

    private void SpawnEnemyUnits()
    {
        enemyUnits.Clear();
        HashSet<Vector3Int> occupied = new HashSet<Vector3Int>();
        for (int i = 0; i < enemyStartPositions.Count; i++)
        {
            Vector3Int pos = enemyStartPositions[i];
            if (!gridManager.IsWalkable(pos) || occupied.Contains(pos))
            {
                Debug.LogError($"Invalid or occupied spawn position for enemy unit at {pos}");
                continue;
            }
            occupied.Add(pos);
            GameObject unitGO = Instantiate(enemyUnitPrefab, gridManager.GridToWorld(pos), Quaternion.identity);
            UnitController unit = unitGO.GetComponent<UnitController>();
            if (unit != null)
            {
                unit.PlaceUnit(pos, gridManager);
                string unitName = $"Enemy {i + 1}";
                CharacterClassSO classToAssign = defaultEnemyClass;
                RaceSO raceToAssign = defaultEnemyRace;

                if (classToAssign == null || raceToAssign == null)
                {
                    Debug.LogError($"Missing default Race or Class SO for Enemy {i+1}. Cannot initialize stats.");
                    Destroy(unitGO);
                    continue;
                }

                unit.InitializeStats(unitName, raceToAssign, classToAssign);
                int baseSpeed = raceToAssign.baseSpeed;
                unit.speed = Mathf.Max(1, baseSpeed + classToAssign.speedModifier);
                if (unit.speed <= 0)
                {
                    Debug.LogError($"Unit {unitName} has invalid speed ({unit.speed}). Setting to 1.");
                    unit.speed = 1;
                }
                unit.currentCT = 0;
                unitGO.name = unitName;
                enemyUnits.Add(unit);
            }
            else
            {
                Debug.LogError("Spawned enemy prefab missing UnitController!");
                Destroy(unitGO);
            }
        }
        Debug.Log($"Spawned {enemyUnits.Count} enemy units.");
    }

    // Handle cancel input (e.g., right-click, Escape)
    private void HandleCancelClick(InputAction.CallbackContext context)
    {
        if (isGameOver || activeUnit == null || !playerUnits.Contains(activeUnit)) return;

        switch (currentState)
        {
            case SelectionState.ConfirmingAttack:
            case SelectionState.ConfirmingSkill:
                CancelActionConfirmation();
                break;
            case SelectionState.SelectingSkillTarget:
            case SelectionState.SelectingSkillFromPanel:
                CancelSkillSelection(false);
                break;
            case SelectionState.UnitSelected:
                ResetActionSelection();
                break;
            default:
                Debug.Log($"Cancel ignored in state: {currentState}");
                break;
        }
    }
}