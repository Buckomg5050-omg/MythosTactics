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
    private List<GameObject> activeSkillIndicators  = new List<GameObject>();
    private GameManager gameManagerInstance;

    private void Awake()
    {
        // Singleton setup
        if (Instance == null) Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    void Start()
    {
        gameManagerInstance = FindFirstObjectByType<GameManager>();
        if (gameManagerInstance == null)
            Debug.LogError("UIManager: Could not find GameManager instance in the scene!", this);

        InitializeUI();
    }

    private void InitializeUI()
    {
        HideSelectedUnitInfo();
        HideEndTurnButton();
        HideSkillButton();
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
                gameManagerInstance?.CancelSkillSelection();
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
    public void ShowSelectedUnitInfo(UnitController unit)
    {
        if (selectedInfoText == null) return;

        if (unit == null)
        {
            selectedInfoText.text = "";
            selectedInfoText.gameObject.SetActive(false);
        }
        else
        {
            string info = $"{unit.unitName}\n" +
                          $"HP: {unit.currentHealth} / {unit.maxHealth}\n" +
                          $"MP: {unit.currentMp} / {unit.maxMp}";
            selectedInfoText.text = info;
            selectedInfoText.gameObject.SetActive(true);
        }

        // Update status effect icons for the shown unit (or clear if null)
        UpdateStatusIcons(unit);
    }

    public void UpdateSelectedUnitInfo(UnitController unit)
    {
        Debug.Log($"UIManager: Attempting to update info for {(unit == null ? "NULL" : unit.unitName)}");
        if (selectedInfoText == null) return;

        if (unit == null)
        {
            selectedInfoText.text = "";
        }
        else
        {
            string info = $"{unit.unitName}\n" +
                          $"HP: {unit.currentHealth} / {unit.maxHealth}\n" +
                          $"MP: {unit.currentMp} / {unit.maxMp}";
            selectedInfoText.text = info;
        }

        // Update status effect icons whenever the unit info is refreshed
        UpdateStatusIcons(unit);
    }

    public void HideSelectedUnitInfo()
    {
        if (selectedInfoText == null) return;
        selectedInfoText.text = "";
        selectedInfoText.gameObject.SetActive(false);
        UpdateStatusIcons(null);
    }

    // --- End Turn & Skill Buttons ---
    public void ShowEndTurnButton() => endTurnButton?.SetActive(true);
    public void HideEndTurnButton() => endTurnButton?.SetActive(false);
    public void ShowSkillButton()   => skillButton?.SetActive(true);
    public void HideSkillButton()   => skillButton?.SetActive(false);

    // --- Attack Visualization ---
    public void VisualizeAttackTargets(List<UnitController> targets, GridManager gridManager)
    {
        ClearAttackTargetVisuals();
        if (attackIndicatorPrefab == null || gridManager == null || targets == null) return;
        foreach (var t in targets)
        {
            if (t == null || !t.IsAlive) continue;
            Vector3 pos = gridManager.GridToWorld(t.gridPosition);
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

        // Filter affordable skills
        var affordableSkills = new List<SkillSO>();
        foreach (var skill in skills)
        {
            if (user.currentMp >= skill.mpCost)
                affordableSkills.Add(skill);
        }

        // Clear existing buttons
        foreach (Transform child in skillButtonContainer)
            Destroy(child.gameObject);

        // Instantiate buttons
        foreach (var skill in affordableSkills)
        {
            var buttonInstance = Instantiate(skillButtonTemplatePrefab, skillButtonContainer);
            buttonInstance.transform.SetParent(skillButtonContainer, false);

            // Set button text with optional MP cost
            var buttonTextComp = buttonInstance.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonTextComp != null)
            {
                string btnText = skill.skillName;
                if (skill.mpCost > 0)
                    btnText += $" ({skill.mpCost} MP)";
                buttonTextComp.text = btnText;
            }

            // Hook up click callback
            var buttonComp = buttonInstance.GetComponent<Button>();
            if (buttonComp != null)
            {
                SkillSO captured = skill;
                buttonComp.onClick.RemoveAllListeners();
                buttonComp.onClick.AddListener(() =>
                {
                    HideSkillSelection();
                    onSkillSelectedCallback(captured);
                });
            }

            buttonInstance.SetActive(true);
        }

        skillSelectPanel.SetActive(true);
        Debug.Log("UIManager.ShowSkillSelection: Method finished.");
    }

    public void HideSkillSelection()
    {
        if (skillSelectPanel != null)
            skillSelectPanel.SetActive(false);
    }

    /// <summary>
    /// Clears any existing status‐effect icons and, if a unit is provided,
    /// instantiates a new icon for each active effect using its configured sprite.
    /// </summary>
    private void UpdateStatusIcons(UnitController unit)
    {
        // Validate references
        if (statusIconContainer == null || statusIconTemplatePrefab == null)
        {
            Debug.LogError("UIManager: Status Icon Container or Template Prefab not assigned!");
            return;
        }

        // Clear existing icons
        foreach (Transform child in statusIconContainer)
        {
            Destroy(child.gameObject);
        }

        // If no unit selected, nothing more to do
        if (unit == null) return;

        // If the unit has no active status effects, leave container empty
        if (unit.ActiveStatusEffects == null || unit.ActiveStatusEffects.Count == 0) return;

        // Populate icons for each active status effect
        foreach (var activeEffect in unit.ActiveStatusEffects)
        {
            if (activeEffect.EffectData == null || activeEffect.EffectData.icon == null)
            {
                Debug.LogWarning(
                    $"UIManager: Status effect '{activeEffect.EffectData?.effectName ?? "UNKNOWN"}' on {unit.unitName} is missing data or icon.",
                    unit);
                continue;
            }

            // Instantiate icon prefab under the container
            GameObject iconInstance = Instantiate(statusIconTemplatePrefab, statusIconContainer);

            // Assign the sprite on its Image component
            var iconImage = iconInstance.GetComponent<Image>();
            if (iconImage != null)
            {
                iconImage.sprite = activeEffect.EffectData.icon;
                // Optionally set other properties: iconImage.color = Color.white;
            }
            else
            {
                Debug.LogError("UIManager: StatusIcon template prefab is missing an Image component!", iconInstance);
            }

            iconInstance.SetActive(true);
        }
    }
}
