using UnityEngine;
using TMPro;               // Namespace for TextMeshPro components
using System.Collections.Generic; // Required for Lists
using UnityEngine.UI;     // Required for Button, Image, and UI elements
using System;             // Required for Action delegate

/// <summary>
/// Manages UI elements like turn indicators, selected unit information display,
/// buttons (End Turn, Skill), visualization for attack/skill targets, the skill selection panel,
/// and status effect icons.
/// Requires references to UI components, button GameObjects, indicator Prefabs, and skill selection elements.
/// </summary>
public class UIManager : MonoBehaviour
{
    // --- Singleton Instance ---
    public static UIManager Instance { get; private set; }

    [Header("UI References")]
    [Tooltip("Text element to display the current turn (e.g., 'Player Turn').")]
    public TextMeshProUGUI turnIndicatorText;

    [Tooltip("Text element to display information about the currently selected unit.")]
    public TextMeshProUGUI selectedInfoText;

    [Tooltip("The GameObject containing the End Turn button.")]
    public GameObject endTurnButton;

    // ### ADDED / MODIFIED ###
    [Tooltip("The GameObject containing the Move button.")]
    public GameObject moveButton; // Added field

    [Tooltip("The GameObject containing the Attack button.")]
    public GameObject attackButton; // Added field
    // #######################

    [Tooltip("The GameObject containing the main action Skill button.")]
    public GameObject skillButton;

    [Header("Status Effect Icons")]
    [Tooltip("Container for status effect icons (should have a Horizontal Layout Group).")]
    public Transform statusIconContainer;

    [Tooltip("Prefab for a single status effect icon (should have an Image component).")]
    public GameObject statusIconTemplatePrefab;

    [Header("Skill Selection UI")]
    [Tooltip("The parent panel for the skill selection list.")]
    public GameObject skillSelectPanel;

    [Tooltip("The container (with Vertical Layout Group) where skill buttons will be instantiated.")]
    public Transform skillButtonContainer;

    [Tooltip("The prefab for a single skill button in the selection list.")]
    public GameObject skillButtonTemplatePrefab;

    [Tooltip("Optional: Button to close the skill selection panel without choosing a skill.")]
    public Button cancelSkillButton;

    [Header("Visualization Prefabs")]
    [Tooltip("Prefab to instantiate for indicating attackable targets.")]
    public GameObject attackIndicatorPrefab;

    [Tooltip("Prefab to instantiate for indicating valid skill targets.")]
    public GameObject skillTargetIndicatorPrefab;

    // --- Private Fields ---
    private List<GameObject> activeAttackIndicators = new List<GameObject>();
    private List<GameObject> activeSkillIndicators = new List<GameObject>();
    private GameManager gameManagerInstance; // Keep this if used elsewhere, like cancel button

    // Reference to the movement visualizer IF UIManager handles clearing it
    // public MovementRangeVisualizer movementRangeVisualizer; // Optional: Assign if needed by ClearIndicators


    private void Awake()
    {
        // Singleton setup
        if (Instance == null) Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }
        // Optional: Find the MovementRangeVisualizer if needed
        // movementRangeVisualizer = FindObjectOfType<MovementRangeVisualizer>();
    }

    void Start()
    {
        // Fine to find GameManager here if needed for cancel button etc.
        gameManagerInstance = FindFirstObjectByType<GameManager>();
        if (gameManagerInstance == null)
            Debug.LogError("UIManager: Could not find GameManager instance in the scene!", this);

        InitializeUI();
    }

    private void InitializeUI()
    {
        HideSelectedUnitInfo();
        HideEndTurnButton();
        // HideSkillButton(); // ### MODIFIED: Use ShowActionButtons instead ###
        ShowActionButtons(false); // Start with all action buttons hidden
        HideSkillSelection();
        ClearAttackTargetVisuals();
        ClearSkillVisuals();
        UpdateStatusIcons(null);

        if (cancelSkillButton != null)
        {
            cancelSkillButton.onClick.RemoveAllListeners();
            cancelSkillButton.onClick.AddListener(() =>
            {
                HideSkillSelection();
                // Use the static instance directly if GameManager is also a singleton
                GameManager.Instance?.CancelSkillSelection();
                // Or keep using gameManagerInstance if preferred
                // gameManagerInstance?.CancelSkillSelection();
            });
        }
    }

    // --- Turn Indicator ---
    public void UpdateTurnIndicator(GameManager.Turn currentTurn)
    {
        if (turnIndicatorText != null)
            turnIndicatorText.text = $"{currentTurn} Turn";
    }

    // --- Selected Unit Info ---
    public void ShowSelectedUnitInfo(UnitController unit) // Consider removing this if UpdateSelectedUnitInfo covers all cases
    {
        UpdateSelectedUnitInfo(unit); // Just call the main update method
    }

    public void UpdateSelectedUnitInfo(UnitController unit)
    {
        Debug.Log($"UIManager: Attempting to update info for {(unit == null ? "NULL" : unit.unitName)}");
        if (selectedInfoText == null)
        {
             Debug.LogError("UIManager: selectedInfoText field is not assigned!");
             return;
        }

        if (unit == null)
        {
            selectedInfoText.text = "";
            selectedInfoText.gameObject.SetActive(false); // Also hide the object if empty
        }
        else
        {
            string info = $"{unit.unitName}\n" +
                          $"HP: {unit.currentHealth} / {unit.maxHealth}\n" +
                          $"MP: {unit.currentMp} / {unit.maxMp}";
            selectedInfoText.text = info;
            selectedInfoText.gameObject.SetActive(true); // Ensure object is active when showing info
        }

        // Update status effect icons whenever the unit info is refreshed
        UpdateStatusIcons(unit);
    }

    public void HideSelectedUnitInfo() // Keep for explicit hiding if needed
    {
        if (selectedInfoText == null) return;
        selectedInfoText.text = "";
        selectedInfoText.gameObject.SetActive(false);
        UpdateStatusIcons(null);
    }

    // --- End Turn Button ---
    public void ShowEndTurnButton() => endTurnButton?.SetActive(true);
    public void HideEndTurnButton() => endTurnButton?.SetActive(false);

    // --- Action Button Control (New Method) ---
    // ### NEW METHOD ###
    public void ShowActionButtons(bool visible)
    {
        // Set visibility for all action buttons using SetActive
        if (moveButton != null) moveButton.SetActive(visible);
        if (attackButton != null) attackButton.SetActive(visible);
        if (skillButton != null) skillButton.SetActive(visible);

        Debug.Log($"UIManager: Setting action buttons visibility to {visible}");
    }
    // #################


    // --- REMOVED OBSOLETE METHODS ---
    // public void ShowSkillButton()   => skillButton?.SetActive(true); // Now handled by ShowActionButtons
    // public void HideSkillButton()   => skillButton?.SetActive(false); // Now handled by ShowActionButtons
    // ###############################


    // --- Indicator Clearing (New Method) ---
    // ### NEW METHOD ###
     public void ClearIndicators()
     {
         // Clear movement range visualizer *if* UIManager controls it
         // if (movementRangeVisualizer != null)
         // {
         //      movementRangeVisualizer.ClearRange();
         // }
         // Otherwise, GameManager should handle clearing movement range.

         // Clear attack/skill target visuals managed by UIManager
         ClearAttackTargetVisuals();
         ClearSkillVisuals();
         Debug.Log("UIManager: Cleared target indicators.");
     }
    // ####################


    // --- Attack Visualization ---
    public void VisualizeAttackTargets(List<UnitController> targets, GridManager gridManager)
    {
        ClearAttackTargetVisuals();
        if (attackIndicatorPrefab == null || gridManager == null || targets == null) return;
        foreach (var t in targets)
        {
            if (t == null || !t.IsAlive) continue;
            Vector3 pos = gridManager.GridToWorld(t.gridPosition);
            // Instantiate under this UIManager transform for organization
            var go = Instantiate(attackIndicatorPrefab, pos, Quaternion.identity, transform);
            activeAttackIndicators.Add(go);
        }
    }

    public void ClearAttackTargetVisuals()
    {
        foreach (var go in activeAttackIndicators) if (go) Destroy(go);
        activeAttackIndicators.Clear();
    }

    // --- Skill Visualization ---
    public void VisualizeSkillRange(List<UnitController> validTargets, GridManager gridManager)
    {
        ClearSkillVisuals();
        if (skillTargetIndicatorPrefab == null || gridManager == null || validTargets == null) return;
        foreach (var t in validTargets)
        {
            if (t == null || !t.IsAlive) continue;
            Vector3 pos = gridManager.GridToWorld(t.gridPosition);
             // Instantiate under this UIManager transform for organization
            var go = Instantiate(skillTargetIndicatorPrefab, pos, Quaternion.identity, transform);
            activeSkillIndicators.Add(go);
        }
    }

    public void ClearSkillVisuals()
    {
        foreach (var go in activeSkillIndicators) if (go) Destroy(go);
        activeSkillIndicators.Clear();
    }

    // --- Skill Selection Panel ---
    public void ShowSkillSelection(UnitController user, List<SkillSO> skills, Action<SkillSO> onSkillSelectedCallback)
    {
        Debug.Log("UIManager.ShowSkillSelection: Method entered.");

        if (skillSelectPanel == null || skillButtonContainer == null || skillButtonTemplatePrefab == null || user == null)
        {
            Debug.LogError("UIManager.ShowSkillSelection: Missing reference or user is null.");
            return;
        }

        var affordableSkills = new List<SkillSO>();
        foreach (var skill in skills)
        {
            if (user.currentMp >= skill.mpCost)
                affordableSkills.Add(skill);
        }
        Debug.Log($"UIManager.ShowSkillSelection: Found {affordableSkills.Count} affordable skills. Container: {skillButtonContainer?.name ?? "NULL"}. Panel: {skillSelectPanel?.name ?? "NULL"}");

        foreach (Transform child in skillButtonContainer)
            Destroy(child.gameObject);

        foreach (var skill in affordableSkills)
        {
            var buttonInstance = Instantiate(skillButtonTemplatePrefab, skillButtonContainer);
            buttonInstance.transform.SetParent(skillButtonContainer, false); // Ensure scale is preserved

            var buttonTextComp = buttonInstance.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonTextComp != null)
            {
                string btnText = skill.skillName;
                if (skill.mpCost > 0)
                    btnText += $" ({skill.mpCost} MP)";
                buttonTextComp.text = btnText;
            }

            var buttonComp = buttonInstance.GetComponent<Button>();
            if (buttonComp != null)
            {
                SkillSO captured = skill;
                buttonComp.onClick.RemoveAllListeners();
                buttonComp.onClick.AddListener(() =>
                {
                    // HideSkillSelection(); // Hiding happens in the callback now if needed
                    onSkillSelectedCallback(captured);
                });
            }
            buttonInstance.SetActive(true);
        }
        Debug.Log("UIManager.ShowSkillSelection: Button loop finished. Activating panel...");

        skillSelectPanel.SetActive(true);
        Debug.Log($"UIManager.ShowSkillSelection: Panel '{skillSelectPanel?.name ?? "NULL"}' SetActive(true) called. Panel active state: {skillSelectPanel?.activeSelf}");
    }

    public void HideSkillSelection()
    {
        if (skillSelectPanel != null)
            skillSelectPanel.SetActive(false);
    }

    // --- Status Icon Display ---
    private void UpdateStatusIcons(UnitController unit)
    {
        if (statusIconContainer == null || statusIconTemplatePrefab == null)
        {
            // Don't log error every frame if unassigned, maybe just once in Start/Awake
            // Debug.LogError("UIManager: Status Icon Container or Template Prefab not assigned!");
            return;
        }

        foreach (Transform child in statusIconContainer) Destroy(child.gameObject);
        if (unit == null || unit.ActiveStatusEffects == null || unit.ActiveStatusEffects.Count == 0) return;

        foreach (var activeEffect in unit.ActiveStatusEffects)
        {
            if (activeEffect.EffectData == null || activeEffect.EffectData.icon == null)
            {
                Debug.LogWarning($"UIManager: Status effect '{activeEffect.EffectData?.effectName ?? "UNKNOWN"}' on {unit.unitName} is missing data or icon.", unit);
                continue;
            }
            GameObject iconInstance = Instantiate(statusIconTemplatePrefab, statusIconContainer);
            var iconImage = iconInstance.GetComponent<Image>();
            if (iconImage != null) iconImage.sprite = activeEffect.EffectData.icon;
            else Debug.LogError("UIManager: StatusIcon template prefab is missing an Image component!", iconInstance);
            iconInstance.SetActive(true);
        }
    }
} // --- End of UIManager Class ---