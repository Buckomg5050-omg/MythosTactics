// UIManager.cs

using UnityEngine;
using TMPro;
using System.Collections.Generic;
using UnityEngine.UI;
using System;
using TacticalRPG; // Added namespace for CombatForecastPanel/Data
using System.Text; // Already using StringBuilder elsewhere, ensure it's imported

namespace TacticalRPG // Added namespace
{
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [Header("UI References")]
        [Tooltip("Text element to display the current turn state (e.g., 'Unit X's Turn').")]
        public TextMeshProUGUI turnIndicatorText;

        [Tooltip("Text element to display information about the currently selected unit.")]
        public TextMeshProUGUI selectedInfoText;

        [Tooltip("The GameObject containing the End Turn button.")]
        public GameObject endTurnButton;

        [Tooltip("The GameObject containing the Move button.")]
        public GameObject moveButton;

        [Tooltip("The GameObject containing the Attack button.")]
        public GameObject attackButton;

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

        // --- Combat Forecast ---
        [Header("Combat Forecast")]
        [Tooltip("Reference to the Combat Forecast UI Panel component.")]
        [SerializeField]
        private CombatForecastPanel combatForecastPanel; // Added Field

        [Header("Visualization Prefabs")]
        [Tooltip("Prefab to instantiate for indicating attackable targets.")]
        public GameObject attackIndicatorPrefab;

        [Tooltip("Prefab to instantiate for indicating valid skill targets.")]
        public GameObject skillTargetIndicatorPrefab;

        private List<GameObject> activeAttackIndicators = new List<GameObject>();
        private List<GameObject> activeSkillIndicators = new List<GameObject>();
        private GameManager gameManagerInstance;
        // Using StringBuilder for minor performance gain
        private readonly StringBuilder _stringBuilder = new StringBuilder();

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else
            {
                Destroy(gameObject);
                return;
            }

            // --- Validation for new field ---
            if (combatForecastPanel == null)
            {
                Debug.LogWarning("UIManager: Combat Forecast Panel reference is not assigned in the inspector.", this);
                // Attempt to find it if not assigned, as a fallback (optional, but can be helpful)
                combatForecastPanel = UnityEngine.Object.FindFirstObjectByType<CombatForecastPanel>(FindObjectsInactive.Include); // Include inactive objects in search
                 if (combatForecastPanel == null)
                    Debug.LogError("UIManager: Could not find CombatForecastPanel instance in the scene!", this);
                 else
                    Debug.Log("UIManager: Found CombatForecastPanel automatically.", this);
            }
            // --- End Validation ---
        }

        void Start()
        {
            // Cache GameManager instance (moved from original code for slight optimization)
            gameManagerInstance = FindFirstObjectByType<GameManager>();
            if (gameManagerInstance == null)
                Debug.LogError("UIManager: Could not find GameManager instance in the scene!", this);


            InitializeUI();
        }

        private void InitializeUI()
        {
            HideSelectedUnitInfo();
            HideEndTurnButton();
            ShowActionButtons(false);
            HideSkillSelection();
            ClearIndicators(); // This now also hides the forecast
            UpdateStatusIcons(null);
            // HideCombatForecast(); // Explicitly hide on init as well - redundant if ClearIndicators called

            if (cancelSkillButton != null)
            {
                cancelSkillButton.onClick.RemoveAllListeners();
                cancelSkillButton.onClick.AddListener(() =>
                {
                    HideSkillSelection();
                    // Check if gameManagerInstance is valid before using
                    if (gameManagerInstance != null) gameManagerInstance.CancelSkillSelection();
                    else if(GameManager.Instance != null) GameManager.Instance.CancelSkillSelection(); // Fallback to singleton access
                    else Debug.LogError("UIManager: Cannot call CancelSkillSelection - GameManager not found!");

                    HideCombatForecast(); // Hide forecast when cancelling skill selection
                });
            }
        }

        public void UpdateTurnIndicatorText(string turnInfo)
        {
            if (turnIndicatorText != null)
                turnIndicatorText.text = turnInfo;
        }

        public void UpdateSelectedUnitInfo(UnitController unit)
        {
            if (selectedInfoText == null)
            {
                Debug.LogError("UIManager: selectedInfoText field is not assigned!");
                return;
            }

            if (unit == null)
            {
                HideSelectedUnitInfo(); // Calls the method which also hides forecast
            }
            else
            {
                 // Use StringBuilder for efficiency
                _stringBuilder.Clear();
                _stringBuilder.AppendLine(unit.unitName);
                _stringBuilder.Append("HP: ").Append(unit.currentHealth).Append(" / ").Append(unit.maxHealth).AppendLine();
                _stringBuilder.Append("MP: ").Append(unit.currentMp).Append(" / ").Append(unit.maxMp);
                selectedInfoText.text = _stringBuilder.ToString();

                selectedInfoText.gameObject.SetActive(true);
                UpdateStatusIcons(unit);
                // Note: Selecting a unit doesn't automatically show a forecast.
                // Forecast is shown when a *specific action* is considered.
                 HideCombatForecast(); // Hide forecast when selecting a new unit initially
            }
        }

        public void HideSelectedUnitInfo()
        {
            if (selectedInfoText != null) {
                selectedInfoText.text = "";
                selectedInfoText.gameObject.SetActive(false);
            }
            UpdateStatusIcons(null);
            HideCombatForecast(); // Hide forecast when deselecting unit
        }

        public void ShowEndTurnButton()
        {
            endTurnButton?.SetActive(true);
        }

        public void HideEndTurnButton()
        {
            endTurnButton?.SetActive(false);
        }

        public void ShowActionButtons(bool visible)
        {
            if (moveButton != null) moveButton.SetActive(visible);
            if (attackButton != null) attackButton.SetActive(visible);
            if (skillButton != null) skillButton.SetActive(visible);

            // Hide forecast when action buttons are hidden (usually means cancelling/completing action)
            if (!visible) {
                 HideCombatForecast();
            }
        }

        public void ClearIndicators()
        {
            ClearAttackTargetVisuals();
            ClearSkillVisuals();
            HideCombatForecast(); // Hide forecast when clearing indicators
        }

        public void VisualizeAttackTargets(List<UnitController> targets, GridManager gridManager)
        {
            ClearAttackTargetVisuals();
            if (attackIndicatorPrefab == null || gridManager == null || targets == null) return;
            foreach (var t in targets)
            {
                if (t == null || !t.IsAlive) continue;
                Vector3 pos = gridManager.GridToWorld(t.gridPosition);
                // Ensure transform is cached or use 'this.transform' for clarity if needed.
                var go = Instantiate(attackIndicatorPrefab, pos, Quaternion.identity, transform);
                activeAttackIndicators.Add(go);
            }
        }

        public void ClearAttackTargetVisuals()
        {
            foreach (var go in activeAttackIndicators) if (go) Destroy(go);
            activeAttackIndicators.Clear();
        }

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

        public void ShowSkillSelection(UnitController user, List<SkillSO> skills, Action<SkillSO> onSkillSelectedCallback)
        {
            if (skillSelectPanel == null || skillButtonContainer == null || skillButtonTemplatePrefab == null || user == null)
            {
                Debug.LogError("UIManager.ShowSkillSelection: Missing reference or user is null.");
                return;
            }

             HideCombatForecast(); // Hide forecast when showing skill selection list

            // Filter affordable skills (already present, good)
            var affordableSkills = new List<SkillSO>();
            foreach (var skill in skills)
            {
                 if (skill != null && user.currentMp >= skill.mpCost) // Added null check for skill
                    affordableSkills.Add(skill);
            }
            Debug.Log($"UIManager.ShowSkillSelection: Found {affordableSkills.Count} affordable skills for {user.unitName}.");

            // Clear existing buttons (already present, good)
            foreach (Transform child in skillButtonContainer)
                Destroy(child.gameObject);

            // Populate buttons (already present, good)
            foreach (var skill in affordableSkills)
            {
                var buttonInstance = Instantiate(skillButtonTemplatePrefab, skillButtonContainer);
                 // Correct parenting: Instantiate with parent already set
                // buttonInstance.transform.SetParent(skillButtonContainer, false); // Redundant if instantiated with parent

                var buttonTextComp = buttonInstance.GetComponentInChildren<TextMeshProUGUI>();
                if (buttonTextComp != null)
                {
                     // Use StringBuilder for efficiency
                    _stringBuilder.Clear();
                    _stringBuilder.Append(skill.skillName);
                    if (skill.mpCost > 0)
                        _stringBuilder.Append(" (").Append(skill.mpCost).Append(" MP)");
                    buttonTextComp.text = _stringBuilder.ToString();
                }

                var buttonComp = buttonInstance.GetComponent<Button>();
                if (buttonComp != null)
                {
                    SkillSO captured = skill; // Capture loop variable correctly
                    buttonComp.onClick.RemoveAllListeners();
                    buttonComp.onClick.AddListener(() =>
                    {
                        onSkillSelectedCallback(captured);
                        // HideSkillSelection(); // Typically called by GameManager after skill processing
                    });
                }
                buttonInstance.SetActive(true);
            }

            skillSelectPanel.SetActive(true);
        }

        public void HideSkillSelection()
        {
            if (skillSelectPanel != null)
                skillSelectPanel.SetActive(false);
            // Consider hiding forecast here too, although Cancel button and skill selection initiation already do.
            // HideCombatForecast();
        }

        // --- Combat Forecast Methods ---

        /// <summary>
        /// Shows the Combat Forecast panel and updates it with the provided data.
        /// </summary>
        /// <param name="data">The forecast data to display.</param>
        public void ShowCombatForecast(CombatForecastData data)
        {
            if (combatForecastPanel == null)
            {
                Debug.LogError("UIManager: Cannot show combat forecast - CombatForecastPanel reference is missing!", this);
                return;
            }
            // Log hierarchy to detect disabled parents
            Transform t = combatForecastPanel.transform;
            string hierarchy = t.name;
            while (t.parent != null) { t = t.parent; hierarchy = t.name + "/" + hierarchy; }
            Debug.Log($"UIManager: CombatForecastPanel hierarchy: {hierarchy}, parent active states: {GetParentActiveStates(combatForecastPanel.transform)}");
            Debug.Log($"UIManager: Showing combat forecast panel for {data.AttackerName} vs {data.TargetName}.");
            Debug.Log($"UIManager: Panel active state before show: {combatForecastPanel.gameObject.activeSelf}");

            // Debug RectTransform and child UI components
            var panelRt = combatForecastPanel.GetComponent<RectTransform>();
            Debug.Log($"UIManager: CombatForecastPanel RectTransform anchoredPosition={panelRt.anchoredPosition}, sizeDelta={panelRt.sizeDelta}, localScale={panelRt.localScale}");
            var texts = combatForecastPanel.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (var txt in texts)
                Debug.Log($"UIManager: Text '{txt.name}' active={txt.gameObject.activeSelf}, text='{txt.text}'");
            var images = combatForecastPanel.GetComponentsInChildren<Image>(true);
            foreach (var img in images)
                Debug.Log($"UIManager: Image '{img.name}' active={img.gameObject.activeSelf}, sprite={img.sprite}");

            combatForecastPanel.UpdateForecast(data); // UpdateForecast handles making the panel visible
            // Safeguard: force activation if still inactive
            if (!combatForecastPanel.gameObject.activeSelf)
            {
                Debug.LogWarning("UIManager: Panel still inactive after UpdateForecast. Forcing activation.", this);
                combatForecastPanel.gameObject.SetActive(true);
            }
            Debug.Log($"UIManager: Panel active state after show: {combatForecastPanel.gameObject.activeSelf}");
        }

        /// <summary>
        /// Hides the Combat Forecast panel.
        /// </summary>
        public void HideCombatForecast()
        {
            Debug.Log($"UIManager: Hiding combat forecast panel.");
            Debug.Log($"UIManager: Panel active state before hide: {combatForecastPanel?.gameObject.activeSelf}");
            combatForecastPanel?.Hide(); // Use null-conditional operator for brevity
            Debug.Log($"UIManager: Panel active state after hide: {combatForecastPanel?.gameObject.activeSelf}");
        }

        // Helper: logs active state of all ancestors
        private string GetParentActiveStates(Transform tf)
        {
            List<string> states = new List<string>();
            Transform cur = tf;
            while (cur != null)
            {
                states.Add($"{cur.name}:{cur.gameObject.activeSelf}");
                cur = cur.parent;
            }
            return string.Join(", ", states);
        }

        // --- End Combat Forecast Methods ---


        private void UpdateStatusIcons(UnitController unit)
        {
            if (statusIconContainer == null || statusIconTemplatePrefab == null)
            {
                // Removed redundant return; covered by the check below
                // return;
            }

            // Clear existing icons
            if (statusIconContainer != null) {
                 foreach (Transform child in statusIconContainer) Destroy(child.gameObject);
            } else {
                 Debug.LogWarning("UIManager: Status Icon Container not assigned, cannot update icons.");
                 return; // Cannot proceed without container
            }


            // Check if unit exists and has effects
            if (unit == null || unit.ActiveStatusEffects == null || unit.ActiveStatusEffects.Count == 0)
            {
                 // No unit or no effects, leave container empty
                return;
            }

             if (statusIconTemplatePrefab == null) {
                Debug.LogError("UIManager: Status Icon Template Prefab not assigned, cannot display icons.");
                return; // Cannot proceed without prefab
            }

            // Populate new icons
            foreach (var activeEffect in unit.ActiveStatusEffects)
            {
                if (activeEffect.EffectData == null || activeEffect.EffectData.icon == null)
                {
                    Debug.LogWarning($"UIManager: Status effect '{activeEffect.EffectData?.effectName ?? "UNKNOWN"}' on {unit.unitName} is missing data or icon.");
                    continue; // Skip this effect
                }

                GameObject iconInstance = Instantiate(statusIconTemplatePrefab, statusIconContainer);
                var iconImage = iconInstance.GetComponent<Image>();
                if (iconImage != null)
                {
                     iconImage.sprite = activeEffect.EffectData.icon;
                     iconImage.enabled = true; // Ensure image is enabled
                }
                else
                {
                    Debug.LogError("UIManager: StatusIcon template prefab is missing an Image component!");
                     Destroy(iconInstance); // Clean up improperly configured instance
                     continue;
                }
                iconInstance.SetActive(true); // Ensure instance is active
            }
        }
    }
} // End namespace