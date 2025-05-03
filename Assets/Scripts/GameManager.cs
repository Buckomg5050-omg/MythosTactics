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
    public enum Turn { Player, Enemy }

    private enum SelectionState
    {
        None,
        UnitSelected,
        SelectingSkillFromPanel,
        SelectingSkillTarget,
        ActionPending
    }

    [Header("References")]
    public GridManager gridManager;
    public UIManager uiManager;

    [Header("Unit Prefabs & Spawning")]
    public GameObject playerUnitPrefab;
    public GameObject enemyUnitPrefab;
    public List<Vector3Int> playerStartPositions = new List<Vector3Int> {
        new Vector3Int(0, 0, 0),
        new Vector3Int(0, 1, 0)
    };
    public List<Vector3Int> enemyStartPositions = new List<Vector3Int> {
        new Vector3Int(5, -1, 0),
        new Vector3Int(6, -1, 0),
        new Vector3Int(5, 1, 0)
    };

    [Header("Default Unit Types")]
    public RaceSO defaultPlayerRace;
    public CharacterClassSO defaultPlayerClass;
    public CharacterClassSO specificPlayerClass2;
    public RaceSO defaultEnemyRace;
    public CharacterClassSO defaultEnemyClass;

    [Header("Selection & State")]
    public UnitController selectedUnit { get; private set; }
    public Turn CurrentTurn { get; private set; }

    private PlayerInputActions playerInputActions;
    private List<UnitController> playerUnits = new List<UnitController>();
    private List<UnitController> enemyUnits = new List<UnitController>();
    private List<Vector3Int> reachableTiles = new List<Vector3Int>();
    private MovementRangeVisualizer movementRangeVisualizer;
    private SelectionState currentState = SelectionState.None;
    private bool isEnemyMoving = false;
    private List<UnitController> attackableTargets = new List<UnitController>();
    private bool _clickProcessingPending = false;
    private Vector2 _pendingClickPosition;
    private SkillSO pendingSkill = null;
    private List<Vector3Int> skillTargetableCells = new List<Vector3Int>();
    private List<UnitController> skillValidTargets = new List<UnitController>();
    private bool isGameOver = false;

    public static GameManager Instance { get; private set; }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        playerInputActions = new PlayerInputActions();
        movementRangeVisualizer = GetComponent<MovementRangeVisualizer>();
        if (movementRangeVisualizer == null)
            Debug.LogError("GM: MovementRangeVisualizer missing!", this);

        if (uiManager == null)
            uiManager = FindFirstObjectByType<UIManager>();
        if (uiManager == null)
            Debug.LogError("GM: UIManager missing!", this);

        UnitController.OnUnitDied += HandleUnitDeath;
    }

    void Start()
    {
        isGameOver = false;
        Time.timeScale = 1f;

        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        SpawnPlayerUnits();
        SpawnEnemyUnits();

        CurrentTurn = Turn.Player;
        CurrentTurn = Turn.Enemy; // Force initial setup via EndEnemyTurn
        EndEnemyTurn();
    }

    void OnEnable()
    {
        playerInputActions.Player.Enable();
        playerInputActions.Player.Select.performed += HandleSelectionClick;
    }

    void OnDisable()
    {
        playerInputActions.Player.Select.performed -= HandleSelectionClick;
        playerInputActions.Player.Disable();
        UnitController.OnUnitDied -= HandleUnitDeath;
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

        // --- Ensure World Click Input is Conditional ---
        bool allowWorldClickProcessing = !isEnemyMoving && currentState != SelectionState.SelectingSkillFromPanel;

        if (_clickProcessingPending && allowWorldClickProcessing)
        {
            if (EventSystem.current.IsPointerOverGameObject())
            {
                _clickProcessingPending = false;
            }
            else
            {
                var worldPos = Camera.main.ScreenToWorldPoint(new Vector3(
                    _pendingClickPosition.x,
                    _pendingClickPosition.y,
                    Camera.main.nearClipPlane));
                var clickedCell = gridManager.WorldToGrid(worldPos);

                switch (currentState)
                {
                    case SelectionState.None:
                        HandleClickInNoneState(clickedCell);
                        break;
                    case SelectionState.UnitSelected:
                        HandleClickInUnitSelectedState(clickedCell);
                        break;
                    case SelectionState.SelectingSkillTarget:
                        HandleClickInSelectingSkillTarget(clickedCell);
                        break;
                    case SelectionState.ActionPending:
                        HandleClickInActionPendingState(clickedCell);
                        break;
                    case SelectionState.SelectingSkillFromPanel:
                        // Skip world input entirely while waiting on UI
                        break;
                }
            }

            _clickProcessingPending = false;
        }

        // --- Other per-frame logic can go here ---
    }

    private void HandleSelectionClick(InputAction.CallbackContext ctx)
    {
        if (isGameOver ||
            CurrentTurn != Turn.Player ||
            isEnemyMoving ||
            gridManager == null ||
            Camera.main == null ||
            movementRangeVisualizer == null ||
            uiManager == null ||
            currentState == SelectionState.SelectingSkillFromPanel ||
            EventSystem.current.IsPointerOverGameObject())
        {
            _clickProcessingPending = false;
            return;
        }

        _pendingClickPosition = Mouse.current.position.ReadValue();
        _clickProcessingPending = true;
    }

    private void HandleClickInNoneState(Vector3Int clickedCell)
    {
        var clickedUnit = GetUnitAt(clickedCell);
        if (clickedUnit != null && playerUnits.Contains(clickedUnit))
        {
            if (clickedUnit.HasActedThisTurn)
            {
                Debug.Log($"Unit '{clickedUnit.unitName}' already acted.", clickedUnit);
                return;
            }

            selectedUnit = clickedUnit;
            currentState = SelectionState.UnitSelected;
            ShowActionIndicators(selectedUnit);

            if (UIManager.Instance != null)
                UIManager.Instance.UpdateSelectedUnitInfo(selectedUnit);
        }
    }

    private void HandleClickInUnitSelectedState(Vector3Int clickedCell)
    {
        if (selectedUnit == null) { ResetSelectionState(); return; }

        var target = GetUnitAt(clickedCell);

        if (target != null && attackableTargets.Contains(target))
        {
            PerformAttack(selectedUnit, target);
            selectedUnit.HasActedThisTurn = true;
            ResetSelectionState();
            if (CheckIfAllPlayerUnitsActed()) EndPlayerTurn();
            return;
        }

        var friendly = GetUnitAt(clickedCell);
        if (friendly != null && playerUnits.Contains(friendly) && friendly != selectedUnit)
        {
            ResetSelectionState();
            HandleClickInNoneState(clickedCell);
            return;
        }

        if (reachableTiles.Contains(clickedCell))
        {
            if (clickedCell == selectedUnit.gridPosition)
            {
                ResetSelectionState();
                return;
            }

            var path = Pathfinder.FindPath(selectedUnit.gridPosition, clickedCell, gridManager, this);
            if (path != null && path.Count > 1)
            {
                movementRangeVisualizer.ClearRange();
                uiManager.ClearAttackTargetVisuals();
                uiManager.HideSkillButton();
                uiManager.HideEndTurnButton();
                uiManager.HideSelectedUnitInfo();
                reachableTiles.Clear();
                attackableTargets.Clear();

                var unit = selectedUnit;
                selectedUnit = null;
                currentState = SelectionState.None;
                unit.StartMove(path, gridManager, () => OnUnitMoveComplete(unit));
            }
            else
            {
                Debug.LogWarning($"Pathfinding failed from {selectedUnit.gridPosition} to {clickedCell}");
                ResetSelectionState();
            }
            return;
        }

        ResetSelectionState();
    }

    private void HandleClickInActionPendingState(Vector3Int clickedCell)
    {
        if (selectedUnit == null) { ResetSelectionState(); return; }

        var target = GetUnitAt(clickedCell);
        if (target != null && enemyUnits.Contains(target) && attackableTargets.Contains(target))
        {
            PerformAttack(selectedUnit, target);
            selectedUnit.HasActedThisTurn = true;
            ResetSelectionState();
            if (CheckIfAllPlayerUnitsActed()) EndPlayerTurn();
        }
        else
        {
            ResetSelectionState();
        }
    }

    private void HandleClickInSelectingSkillTarget(Vector3Int clickedCell)
    {
        if (pendingSkill == null || selectedUnit == null)
        {
            Debug.LogError("GM: Click in skill target state invalid!", this);
            ResetSelectionState();
            return;
        }

        var target = GetUnitAt(clickedCell);
        if (target != null && skillValidTargets.Contains(target))
        {
            int cost = pendingSkill.mpCost;
            selectedUnit.currentMp -= cost;
            selectedUnit.currentMp = Mathf.Max(0, selectedUnit.currentMp);

            if (UIManager.Instance != null)
                UIManager.Instance.UpdateSelectedUnitInfo(selectedUnit);

            ExecuteSkillEffect(selectedUnit, target, pendingSkill);

            selectedUnit.HasActedThisTurn = true;
            uiManager.ClearSkillVisuals();
            ResetSelectionState();
            if (CheckIfAllPlayerUnitsActed()) EndPlayerTurn();
        }
        else
        {
            Debug.Log($"GM: Skill '{pendingSkill.skillName}' cancelled - invalid target ({clickedCell}).", selectedUnit.gameObject);
            CancelSkillSelection();
        }
    }

    private void OnUnitMoveComplete(UnitController unit)
    {
        if (isGameOver || unit == null || !playerUnits.Contains(unit) || !unit.IsAlive || uiManager == null)
        {
            ResetSelectionState();
            return;
        }

        selectedUnit = unit;
        currentState = SelectionState.UnitSelected;
        ShowActionIndicators(selectedUnit);
    }

    private void PerformAttack(UnitController attacker, UnitController target)
    {
        if (isGameOver || attacker == null || !attacker.IsAlive || target == null || !target.IsAlive) return;

        int raw = attacker.attackPower;
        int def = target.Defense;
        int dmg = Mathf.Max(1, raw - def);
        Debug.Log($"{attacker.unitName} attacks {target.unitName} for {dmg} damage! ({raw} ATK vs {def} DEF)", attacker.gameObject);
        target.TakeDamage(dmg);
    }

    private void HandleUnitDeath(UnitController dead)
    {
        if (dead == null || isGameOver) return;

        if (playerUnits.Contains(dead)) playerUnits.Remove(dead);
        else if (enemyUnits.Contains(dead)) enemyUnits.Remove(dead);

        if (selectedUnit == dead) ResetSelectionState();
        attackableTargets.Remove(dead);
        skillValidTargets.Remove(dead);

        Destroy(dead.gameObject, 0.1f);
        CheckForGameOver();

        if (!isGameOver && CurrentTurn == Turn.Player && CheckIfAllPlayerUnitsActed())
            EndPlayerTurn();
    }

    private void ResetSelectionState()
    {
        selectedUnit = null;
        uiManager?.UpdateSelectedUnitInfo(null);

        reachableTiles.Clear();
        attackableTargets.Clear();
        pendingSkill = null;
        skillTargetableCells.Clear();
        skillValidTargets.Clear();

        movementRangeVisualizer.ClearRange();
        uiManager.HideSelectedUnitInfo();
        uiManager.HideSkillButton();
        uiManager.ClearAttackTargetVisuals();
        uiManager.ClearSkillVisuals();
        uiManager.HideSkillSelection();

        if (CurrentTurn == Turn.Player && !isGameOver)
            uiManager.ShowEndTurnButton();

        currentState = SelectionState.None;
    }

    private void ShowActionIndicators(UnitController unit)
    {
        if (unit == null || uiManager == null || gridManager == null || movementRangeVisualizer == null) return;

        uiManager.ShowSelectedUnitInfo(unit);
        uiManager.ShowEndTurnButton();

        movementRangeVisualizer.ClearRange();
        uiManager.ClearAttackTargetVisuals();
        uiManager.HideSkillButton();

        reachableTiles = gridManager.CalculateReachableTiles(unit.gridPosition, unit.moveRange, this, unit);
        movementRangeVisualizer.ShowRange(reachableTiles, gridManager);

        attackableTargets = enemyUnits
            .Where(e => e != null && e.IsAlive &&
                        Mathf.Abs(e.gridPosition.x - unit.gridPosition.x) +
                        Mathf.Abs(e.gridPosition.y - unit.gridPosition.y)
                        <= unit.attackRange)
            .ToList();
        uiManager.VisualizeAttackTargets(attackableTargets, gridManager);

        if (unit.unitClass != null && unit.unitClass.availableSkills.Count > 0)
            uiManager.ShowSkillButton();
    }

    private void ExecuteSkillEffect(UnitController caster, UnitController target, SkillSO skill)
    {
        if (skill.skillName == "Power Strike")
        {
            // Power Strike logic (ATK + Power vs DEF)
            int rawDamage = caster.attackPower + skill.basePower;
            int finalDamage = Mathf.Max(1, rawDamage - target.Defense);
            Debug.Log($"   -> Power Strike! (Caster ATK:{caster.attackPower} + Skill Power:{skill.basePower}) = {rawDamage} RAW vs Target DEF:{target.Defense} = {finalDamage} Actual", caster);
            target.TakeDamage(finalDamage);
        }
        else if (skill.isHarmful)
        {
            // General Harmful Skill Logic for ranged/magic attacks
            int rawDamage = caster.attackPower + skill.basePower;
            int finalDamage = Mathf.Max(1, rawDamage - target.Defense);
            Debug.Log($"   -> Harmful Skill '{skill.skillName}'! (Caster ATK:{caster.attackPower} + Skill Power:{skill.basePower}) = {rawDamage} RAW vs Target DEF:{target.Defense} = {finalDamage} Actual", caster);
            target.TakeDamage(finalDamage);
        }
        else
        {
            Debug.Log($"   -> Non-harmful skill '{skill.skillName}' applied.", caster);
        }

        // --- Apply Status Effect ---
        if (skill.statusEffectApplied != null && target != null && target.IsAlive)
        {
            int duration = (skill.statusEffectDuration > 0)
                ? skill.statusEffectDuration
                : skill.statusEffectApplied.baseDuration;

            target.AddStatusEffect(skill.statusEffectApplied, duration);
        }
    }

    public void OnSkillButtonPressed()
    {
        if (CurrentTurn != Turn.Player || isGameOver ||
            currentState != SelectionState.UnitSelected ||
            selectedUnit == null || selectedUnit.HasActedThisTurn)
        {
            Debug.LogWarning("GM: Skill button pressed at invalid time/state.");
            return;
        }

        var skills = selectedUnit.unitClass?.availableSkills;
        if (skills == null || skills.Count == 0)
        {
            Debug.LogError($"GM: Unit '{selectedUnit.unitName}' has no skills.", selectedUnit);
            uiManager.HideSkillButton();
            return;
        }

        movementRangeVisualizer.ClearRange();
        uiManager.ClearAttackTargetVisuals();
        uiManager.HideEndTurnButton();
        uiManager.HideSkillButton();

        if (skills.Count == 1)
        {
            var skill = skills[0];
            if (selectedUnit.currentMp < skill.mpCost)
            {
                Debug.Log($"{selectedUnit.unitName} cannot use {skill.skillName}. Needs {skill.mpCost} MP, has {selectedUnit.currentMp} MP.", selectedUnit);
                return;
            }
            pendingSkill = skill;
            currentState = SelectionState.SelectingSkillTarget;
            ShowSkillTargetingRange(selectedUnit, skill);
            return;
        }

        currentState = SelectionState.SelectingSkillFromPanel;
        UIManager.Instance.ShowSkillSelection(selectedUnit, skills, HandleSkillSelectedFromPanel);
    }

    private void HandleSkillSelectedFromPanel(SkillSO chosenSkill)
    {
        if (currentState != SelectionState.SelectingSkillFromPanel || selectedUnit == null || chosenSkill == null)
        {
            CancelSkillSelection();
            return;
        }

        pendingSkill = chosenSkill;
        currentState = SelectionState.SelectingSkillTarget;
        uiManager.HideSkillSelection();
        ShowSkillTargetingRange(selectedUnit, chosenSkill);
    }

    public void CancelSkillSelection()
    {
        var prev = currentState;
        pendingSkill = null;
        uiManager.ClearSkillVisuals();
        uiManager.HideSkillSelection();

        if ((prev == SelectionState.SelectingSkillFromPanel || prev == SelectionState.SelectingSkillTarget) &&
            selectedUnit != null && !selectedUnit.HasActedThisTurn && CurrentTurn == Turn.Player && !isGameOver)
        {
            currentState = SelectionState.UnitSelected;
            ShowActionIndicators(selectedUnit);
        }
        else
        {
            ResetSelectionState();
        }
    }

    private void ShowSkillTargetingRange(UnitController caster, SkillSO skill)
    {
        uiManager.ClearSkillVisuals();
        skillTargetableCells.Clear();
        skillValidTargets.Clear();

        var pos = caster.gridPosition;
        for (int x = pos.x - skill.range; x <= pos.x + skill.range; x++)
        {
            for (int y = pos.y - skill.range; y <= pos.y + skill.range; y++)
            {
                var cell = new Vector3Int(x, y, pos.z);
                if (Mathf.Abs(cell.x - pos.x) + Mathf.Abs(cell.y - pos.y) <= skill.range)
                {
                    skillTargetableCells.Add(cell);
                    var unit = GetUnitAt(cell);
                    if (unit != null && unit.IsAlive)
                    {
                        bool valid = skill.isHarmful
                            ? enemyUnits.Contains(unit)
                            : playerUnits.Contains(unit);
                        if (valid) skillValidTargets.Add(unit);
                    }
                }
            }
        }
        uiManager.VisualizeSkillRange(skillValidTargets, gridManager);
    }

    public UnitController GetUnitAt(Vector3Int pos)
    {
        foreach (var u in playerUnits) if (u != null && u.IsAlive && u.gridPosition == pos) return u;
        foreach (var u in enemyUnits)  if (u != null && u.IsAlive && u.gridPosition == pos) return u;
        return null;
    }

    private bool CheckIfAllPlayerUnitsActed()
    {
        if (!playerUnits.Any(u => u != null && u.IsAlive)) return true;
        return playerUnits.All(u => u != null && u.IsAlive && u.HasActedThisTurn);
    }

    public void OnEndTurnButtonPressed()
    {
        if (!isGameOver && CurrentTurn == Turn.Player)
            EndPlayerTurn();
    }

    public void EndPlayerTurn()
    {
        ResetSelectionState();
        CurrentTurn = Turn.Enemy;
        uiManager.UpdateTurnIndicator(CurrentTurn);
        StartCoroutine(EnemyTurnCoroutine());
    }

    public void EndEnemyTurn()
    {
        CurrentTurn = Turn.Player;

        // —— Player Turn Tick —— //
        Debug.Log("GM: Ticking status effects for player units...");
        foreach (UnitController playerUnit in playerUnits)
        {
            if (playerUnit != null && playerUnit.IsAlive)
            {
                int effectsBefore = playerUnit.ActiveStatusEffects.Count;
                playerUnit.TickStatusEffects();
                int effectsAfter = playerUnit.ActiveStatusEffects.Count;

                if (playerUnit == selectedUnit && effectsBefore != effectsAfter)
                {
                    if (UIManager.Instance != null)
                    {
                        UIManager.Instance.UpdateSelectedUnitInfo(selectedUnit);
                        Debug.Log($"GM: Triggered UI update for {selectedUnit.unitName} after status tick.");
                    }
                }
            }
        }

        foreach (var u in playerUnits)
            if (u != null && u.IsAlive)
                u.HasActedThisTurn = false;

        uiManager.UpdateTurnIndicator(CurrentTurn);
        ResetSelectionState();
    }

    private IEnumerator EnemyTurnCoroutine()
    {
        yield return new WaitForSeconds(0.5f);
        var enemies = enemyUnits.Where(e => e != null && e.IsAlive).ToList();

        foreach (var enemy in enemies)
        {
            if (!playerUnits.Any(p => p != null && p.IsAlive)) break;

            // —— Enemy Turn Tick —— //
            if (enemy == null || !enemy.IsAlive) continue;
            Debug.Log($"GM: Ticking status effects for {enemy.unitName}...", enemy);
            enemy.TickStatusEffects();

            var target = playerUnits
                .Where(p => p != null && p.IsAlive)
                .OrderBy(p => Mathf.Abs(p.gridPosition.x - enemy.gridPosition.x) +
                              Mathf.Abs(p.gridPosition.y - enemy.gridPosition.y))
                .ThenBy(p => p.currentHealth)
                .FirstOrDefault();

            if (target == null) continue;

            int dist = Mathf.Abs(target.gridPosition.x - enemy.gridPosition.x) +
                       Mathf.Abs(target.gridPosition.y - enemy.gridPosition.y);
            if (dist <= enemy.attackRange)
            {
                PerformAttack(enemy, target);
                yield return new WaitForSeconds(0.75f);
                continue;
            }

            var moveTarget = FindBestMoveTowards(enemy, target.gridPosition);
            if (moveTarget != enemy.gridPosition)
            {
                var path = Pathfinder.FindPath(enemy.gridPosition, moveTarget, gridManager, this);
                if (path != null && path.Count > 1)
                {
                    yield return StartCoroutine(MoveEnemyUnit(enemy, path));
                    dist = Mathf.Abs(target.gridPosition.x - enemy.gridPosition.x) +
                           Mathf.Abs(target.gridPosition.y - enemy.gridPosition.y);
                    if (dist <= enemy.attackRange)
                    {
                        PerformAttack(enemy, target);
                        yield return new WaitForSeconds(0.75f);
                    }
                }
            }

            yield return new WaitForSeconds(0.25f);
        }

        EndEnemyTurn();
    }

    private Vector3Int FindBestMoveTowards(UnitController unit, Vector3Int targetPos)
    {
        var start = unit.gridPosition;
        var reachable = gridManager.CalculateReachableTiles(start, unit.moveRange, this, unit);
        int bestDist = Mathf.Abs(start.x - targetPos.x) + Mathf.Abs(start.y - targetPos.y);
        Vector3Int bestTile = start;

        foreach (var tile in reachable)
        {
            if (GetUnitAt(tile) != null) continue;
            int d = Mathf.Abs(tile.x - targetPos.x) + Mathf.Abs(tile.y - targetPos.y);
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
        bool done = false;
        unit.StartMove(path, gridManager, () => done = true);
        while (!done) yield return null;
    }

    private void CheckForGameOver()
    {
        bool anyPlayer = playerUnits.Any(u => u != null && u.IsAlive);
        bool anyEnemy = enemyUnits.Any(u => u != null && u.IsAlive);

        if (!anyPlayer || !anyEnemy)
        {
            isGameOver = true;
            uiManager.turnIndicatorText.text = !anyPlayer
                ? "GAME OVER (R to Restart)"
                : "VICTORY! (R to Restart)";
            uiManager.HideEndTurnButton();
        }
    }

    private void SpawnPlayerUnits()
    {
        playerUnits.Clear();
        for (int i = 0; i < playerStartPositions.Count; i++)
        {
            Vector3Int pos = playerStartPositions[i];
            GameObject unitGO = Instantiate(playerUnitPrefab, gridManager.GridToWorld(pos), Quaternion.identity);
            UnitController unit = unitGO.GetComponent<UnitController>();
            if (unit != null)
            {
                unit.PlaceUnit(pos, gridManager);
                if (i == 1 && specificPlayerClass2 != null)
                    unit.InitializeStats($"Player {i+1}", defaultPlayerRace, specificPlayerClass2);
                else
                    unit.InitializeStats($"Player {i+1}", defaultPlayerRace, defaultPlayerClass);
                playerUnits.Add(unit);
            }
            else
            {
                Debug.LogError("Spawned player prefab missing UnitController!");
            }
        }
    }

    private void SpawnEnemyUnits()
    {
        enemyUnits.Clear();
        for (int i = 0; i < enemyStartPositions.Count; i++)
        {
            Vector3Int pos = enemyStartPositions[i];
            GameObject unitGO = Instantiate(enemyUnitPrefab, gridManager.GridToWorld(pos), Quaternion.identity);
            UnitController unit = unitGO.GetComponent<UnitController>();
            if (unit != null)
            {
                unit.PlaceUnit(pos, gridManager);
                unit.InitializeStats($"Enemy {i+1}", defaultEnemyRace, defaultEnemyClass);
                enemyUnits.Add(unit);
            }
            else
            {
                Debug.LogError("Spawned enemy prefab missing UnitController!");
            }
        }
    }
}