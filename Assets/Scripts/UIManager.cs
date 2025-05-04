using UnityEngine;
using TMPro;
using System.Collections.Generic;
using UnityEngine.UI;
using System;
using TacticalRPG;
using System.Text;

namespace TacticalRPG
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

        [Header("Combat Forecast")]
        [Tooltip("Reference to the Combat Forecast UI Panel component.")]
        [SerializeField]
        private CombatForecastPanel combatForecastPanel;

        [Header("Visualization Prefabs")]
        [Tooltip("Prefab to instantiate for indicating attackable targets.")]
        public GameObject attackIndicatorPrefab;

        [Tooltip("Prefab to instantiate for indicating valid skill targets.")]
        public GameObject skillTargetIndicatorPrefab;

        [Header("Turn Order UI")]
        [Tooltip("Icon representing the unit from two turns ago.")]
        public Image turnOrderIcon0;

        [Tooltip("Icon representing the unit from one turn ago.")]
        public Image turnOrderIcon1;

        [Tooltip("Icon representing the currently active unit.")]
        public Image turnOrderIcon2;

        [Tooltip("Icon representing the next unit in turn order.")]
        public Image turnOrderIcon3;

        [Tooltip("Icon representing the unit after the next in turn order.")]
        public Image turnOrderIcon4;

        private List<GameObject> activeAttackIndicators = new List<GameObject>();
        private List<GameObject> activeSkillIndicators = new List<GameObject>();
        private GameManager gameManagerInstance;
        private readonly StringBuilder _stringBuilder = new StringBuilder();

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else
            {
                Destroy(gameObject);
                return;
            }

            if (combatForecastPanel == null)
            {
                Debug.LogWarning("UIManager: Combat Forecast Panel reference is not assigned in the inspector.", this);
                combatForecastPanel = UnityEngine.Object.FindFirstObjectByType<CombatForecastPanel>(FindObjectsInactive.Include);
                if (combatForecastPanel == null)
                    Debug.LogError("UIManager: Could not find CombatForecastPanel instance in the scene!", this);
                else
                    Debug.Log("UIManager: Found CombatForecastPanel automatically.", this);
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
            ShowActionButtons(false);
            HideSkillSelection();
            ClearIndicators();
            UpdateStatusIcons(null);
            ClearTurnOrderDisplay(); // Initialize turn order UI

            if (cancelSkillButton != null)
            {
                cancelSkillButton.onClick.RemoveAllListeners();
                cancelSkillButton.onClick.AddListener(() =>
                {
                    HideSkillSelection();
                    if (gameManagerInstance != null) gameManagerInstance.CancelSkillSelection();
                    else if (GameManager.Instance != null) GameManager.Instance.CancelSkillSelection();
                    else Debug.LogError("UIManager: Cannot call CancelSkillSelection - GameManager not found!");
                    HideCombatForecast();
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
                HideSelectedUnitInfo();
            }
            else
            {
                _stringBuilder.Clear();
                _stringBuilder.AppendLine(unit.unitName);
                _stringBuilder.Append("HP: ").Append(unit.currentHealth).Append(" / ").Append(unit.maxHealth).AppendLine();
                _stringBuilder.Append("MP: ").Append(unit.currentMp).Append(" / ").Append(unit.maxMp);
                selectedInfoText.text = _stringBuilder.ToString();

                selectedInfoText.gameObject.SetActive(true);
                UpdateStatusIcons(unit);
                HideCombatForecast();
            }
        }

        public void HideSelectedUnitInfo()
        {
            if (selectedInfoText != null)
            {
                selectedInfoText.text = "";
                selectedInfoText.gameObject.SetActive(false);
            }
            UpdateStatusIcons(null);
            HideCombatForecast();
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
            if (!visible)
            {
                HideCombatForecast();
            }
        }

        public void ClearIndicators()
        {
            ClearAttackTargetVisuals();
            ClearSkillVisuals();
            HideCombatForecast();
        }

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

            HideCombatForecast();

            var affordableSkills = new List<SkillSO>();
            foreach (var skill in skills)
            {
                if (skill != null && user.currentMp >= skill.mpCost)
                    affordableSkills.Add(skill);
            }
            Debug.Log($"UIManager.ShowSkillSelection: Found {affordableSkills.Count} affordable skills for {user.unitName}.");

            foreach (Transform child in skillButtonContainer)
                Destroy(child.gameObject);

            foreach (var skill in affordableSkills)
            {
                var buttonInstance = Instantiate(skillButtonTemplatePrefab, skillButtonContainer);
                var buttonTextComp = buttonInstance.GetComponentInChildren<TextMeshProUGUI>();
                if (buttonTextComp != null)
                {
                    _stringBuilder.Clear();
                    _stringBuilder.Append(skill.skillName);
                    if (skill.mpCost > 0)
                        _stringBuilder.Append(" (").Append(skill.mpCost).Append(" MP)");
                    buttonTextComp.text = _stringBuilder.ToString();
                }

                var buttonComp = buttonInstance.GetComponent<Button>();
                if (buttonComp != null)
                {
                    SkillSO captured = skill;
                    buttonComp.onClick.RemoveAllListeners();
                    buttonComp.onClick.AddListener(() =>
                    {
                        onSkillSelectedCallback(captured);
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
        }

        public void ShowCombatForecast(CombatForecastData data)
        {
            if (combatForecastPanel == null)
            {
                Debug.LogError("UIManager: Cannot show combat forecast - CombatForecastPanel reference is missing!", this);
                return;
            }
            Transform t = combatForecastPanel.transform;
            string hierarchy = t.name;
            while (t.parent != null) { t = t.parent; hierarchy = t.name + "/" + hierarchy; }
            Debug.Log($"UIManager: CombatForecastPanel hierarchy: {hierarchy}, parent active states: {GetParentActiveStates(combatForecastPanel.transform)}");
            Debug.Log($"UIManager: Showing combat forecast panel for {data.AttackerName} vs {data.TargetName}.");
            Debug.Log($"UIManager: Panel active state before show: {combatForecastPanel.gameObject.activeSelf}");

            var panelRt = combatForecastPanel.GetComponent<RectTransform>();
            Debug.Log($"UIManager: CombatForecastPanel RectTransform anchoredPosition={panelRt.anchoredPosition}, sizeDelta={panelRt.sizeDelta}, localScale={panelRt.localScale}");
            var texts = combatForecastPanel.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (var txt in texts)
                Debug.Log($"UIManager: Text '{txt.name}' active={txt.gameObject.activeSelf}, text='{txt.text}'");
            var images = combatForecastPanel.GetComponentsInChildren<Image>(true);
            foreach (var img in images)
                Debug.Log($"UIManager: Image '{img.name}' active={img.gameObject.activeSelf}, sprite={img.sprite}");

            combatForecastPanel.UpdateForecast(data);
            if (!combatForecastPanel.gameObject.activeSelf)
            {
                Debug.LogWarning("UIManager: Panel still inactive after UpdateForecast. Forcing activation.", this);
                combatForecastPanel.gameObject.SetActive(true);
            }
            Debug.Log($"UIManager: Panel active state after show: {combatForecastPanel.gameObject.activeSelf}");
        }

        public void HideCombatForecast()
        {
            Debug.Log($"UIManager: Hiding combat forecast panel.");
            Debug.Log($"UIManager: Panel active state before hide: {combatForecastPanel?.gameObject.activeSelf}");
            combatForecastPanel?.Hide();
            Debug.Log($"UIManager: Panel active state after hide: {combatForecastPanel?.gameObject.activeSelf}");
        }

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

        private void UpdateStatusIcons(UnitController unit)
        {
            if (statusIconContainer == null)
            {
                Debug.LogWarning("UIManager: Status Icon Container not assigned, cannot update icons.");
                return;
            }

            foreach (Transform child in statusIconContainer) Destroy(child.gameObject);

            if (unit == null || unit.ActiveStatusEffects == null || unit.ActiveStatusEffects.Count == 0)
            {
                return;
            }

            if (statusIconTemplatePrefab == null)
            {
                Debug.LogError("UIManager: Status Icon Template Prefab not assigned, cannot display icons.");
                return;
            }

            foreach (var activeEffect in unit.ActiveStatusEffects)
            {
                if (activeEffect.EffectData == null || activeEffect.EffectData.icon == null)
                {
                    Debug.LogWarning($"UIManager: Status effect '{activeEffect.EffectData?.effectName ?? "UNKNOWN"}' on {unit.unitName} is missing data or icon.");
                    continue;
                }

                GameObject iconInstance = Instantiate(statusIconTemplatePrefab, statusIconContainer);
                var iconImage = iconInstance.GetComponent<Image>();
                if (iconImage != null)
                {
                    iconImage.sprite = activeEffect.EffectData.icon;
                    iconImage.enabled = true;
                }
                else
                {
                    Debug.LogError("UIManager: StatusIcon template prefab is missing an Image component!");
                    Destroy(iconInstance);
                    continue;
                }
                iconInstance.SetActive(true);
            }
        }

        public void UpdateTurnOrderDisplay(Sprite sprite2TurnsAgo, Sprite sprite1TurnAgo, Sprite activeUnitSprite, Sprite spriteNextTurn, Sprite spriteAfterNext)
        {
            if (turnOrderIcon0 != null)
            {
                turnOrderIcon0.enabled = sprite2TurnsAgo != null;
                if (sprite2TurnsAgo != null) turnOrderIcon0.sprite = sprite2TurnsAgo;
            }
            if (turnOrderIcon1 != null)
            {
                turnOrderIcon1.enabled = sprite1TurnAgo != null;
                if (sprite1TurnAgo != null) turnOrderIcon1.sprite = sprite1TurnAgo;
            }
            if (turnOrderIcon2 != null)
            {
                turnOrderIcon2.enabled = activeUnitSprite != null;
                if (activeUnitSprite != null) turnOrderIcon2.sprite = activeUnitSprite;
            }
            if (turnOrderIcon3 != null)
            {
                turnOrderIcon3.enabled = spriteNextTurn != null;
                if (spriteNextTurn != null) turnOrderIcon3.sprite = spriteNextTurn;
            }
            if (turnOrderIcon4 != null)
            {
                turnOrderIcon4.enabled = spriteAfterNext != null;
                if (spriteAfterNext != null) turnOrderIcon4.sprite = spriteAfterNext;
            }
        }

        public void ClearTurnOrderDisplay()
        {
            if (turnOrderIcon0 != null)
            {
                turnOrderIcon0.sprite = null;
                turnOrderIcon0.enabled = false;
            }
            if (turnOrderIcon1 != null)
            {
                turnOrderIcon1.sprite = null;
                turnOrderIcon1.enabled = false;
            }
            if (turnOrderIcon2 != null)
            {
                turnOrderIcon2.sprite = null;
                turnOrderIcon2.enabled = false;
            }
            if (turnOrderIcon3 != null)
            {
                turnOrderIcon3.sprite = null;
                turnOrderIcon3.enabled = false;
            }
            if (turnOrderIcon4 != null)
            {
                turnOrderIcon4.sprite = null;
                turnOrderIcon4.enabled = false;
            }
        }
    }
}