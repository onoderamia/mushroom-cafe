using UnityEngine;
using TMPro;

public class MushroomUpgradeManager : MonoBehaviour
{
    public MushroomSpawner spawner;
    public TextMeshProUGUI currencyText;
    public TextMeshProUGUI costText;
    public TextMeshProUGUI upgradeText;
    
    private int currentUpgradeLevel = 0;
    private int maxUpgradeLevel = 4;

    public int CurrentUpgradeLevel => currentUpgradeLevel;

    void Start()
    {
        UpdateUI();
    }

    public void OnUpgradeButtonPoked()
    {
        if (currentUpgradeLevel >= maxUpgradeLevel) return;

        if (GameManager.Instance.TryUpgrade())
        {
            currentUpgradeLevel++;
            spawner.UpgradeMushroomType();
            UpdateUI();
        }
    }

    public void RestoreUpgradeLevel(int upgradeLevel)
    {
        currentUpgradeLevel = Mathf.Clamp(upgradeLevel, 0, maxUpgradeLevel);
        UpdateUI();
    }

    void Update()
    {
        UpdateUI();
    }

    void UpdateUI()
    {
        // Debug.Log("Currency is: " + GameManager.Instance.currency);
        if (currencyText != null)
            currencyText.text = "Leaves: " + GameManager.Instance.currency;

        // always show current mushroom
        if (upgradeText != null)
            upgradeText.text = "Mushrooms: " + (currentUpgradeLevel + 1) + "/5";

        // only show cost if not max level
        if (currentUpgradeLevel < maxUpgradeLevel)
        {
            if (costText != null)
                costText.text = "Upgrade: " + GameManager.Instance.GetNextUpgradeCost() + " leaves";
        }
        else
        {
            if (costText != null)
                costText.text = "Max Level!";
        }
    }
}
