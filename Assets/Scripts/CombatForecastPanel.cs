// CombatForecastPanel.cs

using UnityEngine;
using UnityEngine.UI; // Required for Image
using TMPro;          // Required for TextMeshProUGUI
using System.Text;    // Used for potential StringBuilder optimization (optional but good practice)

namespace TacticalRPG
{
    /// <summary>
    /// Manages the UI Panel that displays the forecast of a potential combat action.
    /// </summary>
    public class CombatForecastPanel : MonoBehaviour
    {
        [Header("UI Element References")]
        [SerializeField]
        private TextMeshProUGUI attackerNameText;

        [SerializeField]
        private TextMeshProUGUI targetNameText;

        [SerializeField]
        private TextMeshProUGUI damageText;

        [SerializeField]
        private TextMeshProUGUI mpCostText;

        [Header("Status Effect Display")]
        [SerializeField]
        private Transform statusIconContainer; // Parent object for status icons (ideally with a Layout Group)

        [SerializeField]
        private GameObject statusIconPrefab; // Prefab for status icons (must have an Image component)

        // Optional: Use StringBuilder for minor performance gain if updating text frequently
        private readonly StringBuilder _stringBuilder = new StringBuilder();

        void Awake()
        {
            // Basic validation to catch setup errors early
            if (attackerNameText == null) Debug.LogError("CombatForecastPanel: Attacker Name Text not assigned!");
            if (targetNameText == null) Debug.LogError("CombatForecastPanel: Target Name Text not assigned!");
            if (damageText == null) Debug.LogError("CombatForecastPanel: Damage Text not assigned!");
            if (mpCostText == null) Debug.LogError("CombatForecastPanel: MP Cost Text not assigned!");
            if (statusIconContainer == null) Debug.LogError("CombatForecastPanel: Status Icon Container not assigned!");
            if (statusIconPrefab == null) Debug.LogError("CombatForecastPanel: Status Icon Prefab not assigned!");
            else if (statusIconPrefab.GetComponent<Image>() == null) Debug.LogError("CombatForecastPanel: Status Icon Prefab is missing an Image component!");

            // Start hidden by default
            Hide();
        }

        /// <summary>
        /// Updates the panel with the provided combat forecast data and makes the panel visible.
        /// </summary>
        /// <param name="data">The combat forecast data to display.</param>
        public void UpdateForecast(CombatForecastData data)
        {
            if (!AreReferencesValid())
            {
                Debug.LogError("CombatForecastPanel cannot update - one or more UI references are missing.");
                Hide();
                return;
            }
            // Log input data and panel state
            var rt = GetComponent<RectTransform>();
            Debug.Log($"CombatForecastPanel.UpdateForecast called. Data: Attacker='{data.AttackerName}', Target='{data.TargetName}', Damage={data.PredictedDamage}, MpCost={data.MpCost}");
            if (rt != null)
                Debug.Log($"RectTransform: position={rt.position}, anchoredPosition={rt.anchoredPosition}, localScale={rt.localScale}");
            Debug.Log($"Panel active state before update: {gameObject.activeSelf}");

            // --- Update Basic Info ---
            attackerNameText.text = data.AttackerName;
            Debug.Log($"Set attackerNameText to '{attackerNameText.text}'");
            targetNameText.text = data.TargetName;
            Debug.Log($"Set targetNameText to '{targetNameText.text}'");

            // Using StringBuilder for efficiency
            _stringBuilder.Clear();
            _stringBuilder.Append("Damage: ").Append(data.PredictedDamage);
            damageText.text = _stringBuilder.ToString();
            Debug.Log($"Set damageText to '{damageText.text}'");

            // --- Update MP Cost (Show/Hide) ---
            if (data.MpCost > 0)
            {
                _stringBuilder.Clear().Append("MP Cost: ").Append(data.MpCost);
                mpCostText.text = _stringBuilder.ToString();
                mpCostText.gameObject.SetActive(true);
                Debug.Log($"Set mpCostText to '{mpCostText.text}', activeSelf={mpCostText.gameObject.activeSelf}");
            }
            else
            {
                mpCostText.gameObject.SetActive(false);
                Debug.Log($"mpCostText activeSelf after hide: {mpCostText.gameObject.activeSelf}");
            }

            // --- Update Status Effects ---
            Debug.Log($"StatusIconContainer child count before clear: {statusIconContainer.childCount}");
            foreach (Transform child in statusIconContainer)
                Destroy(child.gameObject);
            Debug.Log($"StatusIconContainer child count after clear: {statusIconContainer.childCount}");

            if (data.StatusEffects != null && statusIconPrefab != null)
            {
                foreach (var effectForecast in data.StatusEffects)
                {
                    GameObject iconInstance = Instantiate(statusIconPrefab, statusIconContainer);
                    Debug.Log($"Instantiated status icon for effect '{effectForecast.EffectName}'");
                    Image iconImage = iconInstance.GetComponent<Image>();
                    if (iconImage != null)
                        iconImage.sprite = effectForecast.Icon;
                    TextMeshProUGUI effectText = iconInstance.GetComponentInChildren<TextMeshProUGUI>();
                    if (effectText != null)
                        effectText.text = $"{effectForecast.EffectName} ({effectForecast.Duration}t)";
                }
            }
            Debug.Log($"StatusIconContainer final child count: {statusIconContainer.childCount}");

            // --- Make Panel Visible ---
            Debug.Log("CombatForecastPanel: Activating panel");
            gameObject.SetActive(true);
            Debug.Log($"Panel active state after update: {gameObject.activeSelf}");
        }

        /// <summary>
        /// Hides the combat forecast panel.
        /// </summary>
        public void Hide()
        {
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Helper to check if essential references are assigned in the Inspector.
        /// </summary>
        private bool AreReferencesValid()
        {
            return attackerNameText != null &&
                   targetNameText != null &&
                   damageText != null &&
                   mpCostText != null &&
                   statusIconContainer != null &&
                   statusIconPrefab != null;
        }
    }
}