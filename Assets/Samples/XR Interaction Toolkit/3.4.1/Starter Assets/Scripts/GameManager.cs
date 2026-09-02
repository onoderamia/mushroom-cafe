using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using TMPro;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    [System.Serializable]
    public class CafeDayConfig
    {
        public string dayName = "Day 1";
        [Min(1)] public int ordersToClear = 3;
        [Min(1)] public int orderSize = 1;
        [Min(1)] public int maxConcurrentAnimals = 1;
        [Min(1f)] public float animalCountdown = 60f;
        [Min(0.1f)] public float minSpawnDelay = 4f;
        [Min(0.1f)] public float maxSpawnDelay = 6f;
        [Min(0f)] public float dayDuration = 75f;
        [Range(1, 4)] public int mushroomTypesAvailable = 4;
        public bool allowDuplicateMushrooms = false;
        public AudioClip musicClip;
    }

    public static GameManager Instance;
    
    public int currency = 0;
    public List<GameObject> availableMushrooms = new List<GameObject>();

    [Header("Day Progression")]
    public bool enableDaySystem = true;
    public List<CafeDayConfig> dayConfigs = new List<CafeDayConfig>();
    public AnimalSpawner animalSpawner;

    [Header("Progress Board")]
    public Transform progressBoardAnchor;
    public string progressBoardAnchorName = "gamedescui";
    public Vector3 progressBoardWorldOffset = new Vector3(0.75f, 0f, 0f);
    public Vector2 progressBoardSize = new Vector2(420f, 560f);

    [Header("Audio")]
    public AudioClip daySuccessClip;
    public AudioClip dayFailClip;
    [Range(0f, 1f)] public float effectsVolume = 0.85f;
    [Range(0f, 1f)] public float musicVolume = 0.25f;
    [Range(0f, 1f)] public float effectsSpatialBlend = 0.7f;
    [Min(1f)] public float effectsMaxDistance = 40f;

    [Header("Level Clear Feedback")]
    public Transform feedbackAnchor;
    public Vector3 feedbackWorldOffset = new Vector3(0f, 0.05f, -0.08f);
    
    // global upgrade tracking
    private int totalUpgradesDone = 0;
    private float baseCost = 10f;
    private float exponent = 1.5f; // how fast cost grows

    static int s_RequestedStartDay = -1;

    int m_CurrentDayIndex;
    int m_OrdersFulfilledToday;
    int m_OrdersFailedToday;
    float m_DayTimeRemaining;
    bool m_DayRunning;
    bool m_IsTransitioning;

    GameObject m_ProgressBoardRoot;
    TextMeshProUGUI m_DayText;
    TextMeshProUGUI m_OrdersText;
    TextMeshProUGUI m_TimerText;
    TextMeshProUGUI m_RankText;
    TextMeshProUGUI m_MessageText;
    GameObject m_RestartButtonRoot;
    GameObject m_QuitButtonRoot;
    GameObject m_NextDayButtonRoot;
    GameObject m_FinalDayButtonRoot;
    AudioSource m_EffectsSource;
    AudioSource m_MusicSource;
    AudioClip m_GeneratedSuccessClip;
    AudioClip m_GeneratedFailClip;
    DayStartSnapshot m_DayStartSnapshot;
    readonly Dictionary<int, AudioClip> m_GeneratedDayMusic = new Dictionary<int, AudioClip>();

    public bool UsesDaySystem => enableDaySystem;

    class DayStartSnapshot
    {
        public int currency;
        public int totalUpgradesDone;
        public readonly Dictionary<MushroomSpawner, int> spawnerCapacityLevels = new Dictionary<MushroomSpawner, int>();
        public readonly Dictionary<MushroomUpgradeManager, int> upgradeLevels = new Dictionary<MushroomUpgradeManager, int>();
    }
    
    void Awake()
    {
        if (Instance == null)
            Instance = this;
    }

    void Start()
    {
        if (!enableDaySystem)
            return;

        EnsureDefaultDayConfigs();
        ApplyRequiredDayTuning();

        if (animalSpawner == null)
            animalSpawner = FindObjectOfType<AnimalSpawner>();

        CreateProgressBoardIfNeeded();
        SetupAudioSources();

        m_CurrentDayIndex = s_RequestedStartDay >= 0
            ? Mathf.Clamp(s_RequestedStartDay, 0, dayConfigs.Count - 1)
            : 0;
        s_RequestedStartDay = -1;

        BeginDay(m_CurrentDayIndex);
    }

    void Update()
    {
        if (!enableDaySystem || !m_DayRunning || m_IsTransitioning)
            return;

        CafeDayConfig day = CurrentDay;
        if (day != null && day.dayDuration > 0f)
        {
            m_DayTimeRemaining -= Time.deltaTime;
            if (m_DayTimeRemaining <= 0f)
            {
                m_DayTimeRemaining = 0f;
                StartCoroutine(EndDayRoutine(m_OrdersFulfilledToday >= day.ordersToClear));
            }
        }

        UpdateProgressBoard();
    }
    
    public int GetNextUpgradeCost()
    {
        return Mathf.RoundToInt(baseCost * Mathf.Pow(exponent, totalUpgradesDone));
    }
    
    public bool TryUpgrade()
    {
        int cost = GetNextUpgradeCost();
        if (currency >= cost)
        {
            currency -= cost;
            totalUpgradesDone++;
            return true;
        }
        return false;
    }
    
    public void AddCurrency(int amount)
    {
        currency = Mathf.Max(0, currency + amount);
    }

    public void NotifyOrderFulfilled()
    {
        if (!enableDaySystem || !m_DayRunning || m_IsTransitioning)
            return;

        m_OrdersFulfilledToday++;
        UpdateProgressBoard();

        CafeDayConfig day = CurrentDay;
        if (day != null && m_OrdersFulfilledToday >= day.ordersToClear)
            StartCoroutine(EndDayRoutine(true));
    }

    public void NotifyOrderFailed()
    {
        if (!enableDaySystem || !m_DayRunning || m_IsTransitioning)
            return;

        m_OrdersFailedToday++;
        UpdateProgressBoard();
    }

    public void RestartCurrentDay()
    {
        RestoreDayStartSnapshot();
        BeginDay(m_CurrentDayIndex);
    }

    public void QuitGame()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    public void ContinueToNextDay()
    {
        int nextDayIndex = Mathf.Min(m_CurrentDayIndex + 1, dayConfigs.Count - 1);
        ContinueToDay(nextDayIndex);
    }

    public void JumpToFinalDay()
    {
        ContinueToDay(dayConfigs.Count - 1);
    }
    
    public void RegisterMushroom(GameObject mushroom)
    {
        if (!availableMushrooms.Contains(mushroom))
            availableMushrooms.Add(mushroom);
    }
    
    public void UnregisterMushroom(GameObject mushroom)
    {
        availableMushrooms.Remove(mushroom);
    }
    
    public List<GameObject> GetMushroomsOfType(int mushroomLevel)
    {
        return GetMushroomsOfType((MushroomSpawner.MushroomType)mushroomLevel);
    }

    public List<GameObject> GetMushroomsOfType(MushroomSpawner.MushroomType mushroomType)
    {
        List<GameObject> result = new List<GameObject>();
        foreach (GameObject mushroom in availableMushrooms)
        {
            MushroomGrowth growth = mushroom.GetComponent<MushroomGrowth>();
            if (growth != null && growth.mushroomType == mushroomType)
                result.Add(mushroom);
        }
        return result;
    }
    
    public bool HasMushroomOfType(int mushroomLevel)
    {
        return HasMushroomOfType((MushroomSpawner.MushroomType)mushroomLevel);
    }

    public bool HasMushroomOfType(MushroomSpawner.MushroomType mushroomType)
    {
        return GetMushroomsOfType(mushroomType).Count > 0;
    }

    CafeDayConfig CurrentDay
    {
        get
        {
            if (dayConfigs == null || dayConfigs.Count == 0)
                return null;

            return dayConfigs[Mathf.Clamp(m_CurrentDayIndex, 0, dayConfigs.Count - 1)];
        }
    }

    void BeginDay(int dayIndex)
    {
        if (dayConfigs == null || dayConfigs.Count == 0)
            return;

        m_CurrentDayIndex = Mathf.Clamp(dayIndex, 0, dayConfigs.Count - 1);
        CaptureDayStartSnapshot();

        CafeDayConfig day = CurrentDay;
        m_OrdersFulfilledToday = 0;
        m_OrdersFailedToday = 0;
        m_DayTimeRemaining = day.dayDuration;
        m_DayRunning = true;
        m_IsTransitioning = false;

        if (animalSpawner != null)
        {
            animalSpawner.ConfigureForDay(
                day.maxConcurrentAnimals,
                day.minSpawnDelay,
                day.maxSpawnDelay,
                day.animalCountdown,
                day.orderSize,
                day.mushroomTypesAvailable,
                day.allowDuplicateMushrooms);
            animalSpawner.ClearActiveAnimals();
            animalSpawner.SetSpawningEnabled(true);
        }

        PlayDayMusic(day);

        if (m_MessageText != null)
            m_MessageText.text = $"Serve {day.ordersToClear} orders to clear the day.";

        if (m_RankText != null)
            m_RankText.text = "";

        SetRouteButtonsVisible(false, false);
        SetRestartAndQuitButtonsVisible(true);
        UpdateProgressBoard();
    }

    IEnumerator EndDayRoutine(bool cleared)
    {
        if (m_IsTransitioning)
            yield break;

        m_IsTransitioning = true;
        m_DayRunning = false;

        if (animalSpawner != null)
        {
            animalSpawner.SetSpawningEnabled(false);
            animalSpawner.ClearActiveAnimals();
        }

        string rank = CalculateRank();
        if (m_RankText != null)
            m_RankText.text = "Rank: " + rank;

        if (cleared)
        {
            PlayEffect(daySuccessClip != null ? daySuccessClip : GetGeneratedSuccessClip());
        }
        else
        {
            PlayEffect(dayFailClip != null ? dayFailClip : GetGeneratedFailClip());
        }

        string nextMessage;
        bool finalDay = m_CurrentDayIndex >= dayConfigs.Count - 1;
        if (cleared && !finalDay)
        {
            nextMessage = $"Day complete. Rank {rank}.\nChoose your next route.";
        }
        else if (cleared)
        {
            nextMessage = $"Seven days complete. Final rank {rank}.\nThe cafe is cleared!";
        }
        else
        {
            nextMessage = "Day failed. Restart this day and try again.";
        }

        if (m_MessageText != null)
            m_MessageText.text = nextMessage;

        UpdateProgressBoard();

        if (cleared && !finalDay)
        {
            bool finalRouteIsDifferent = m_CurrentDayIndex + 1 < dayConfigs.Count - 1;
            SetRouteButtonsVisible(true, finalRouteIsDifferent);
            SetRestartAndQuitButtonsVisible(true);
        }
        else
        {
            SetRouteButtonsVisible(false, false);
            SetRestartAndQuitButtonsVisible(true);
        }

        yield break;
    }

    string CalculateRank()
    {
        CafeDayConfig day = CurrentDay;
        if (day == null)
            return "D";

        float ratio = m_OrdersFulfilledToday / (float)Mathf.Max(1, day.ordersToClear);
        if (ratio >= 1f)
            return m_OrdersFailedToday == 0 ? "S" : "A";
        if (ratio >= 0.85f)
            return "A";
        if (ratio >= 0.7f)
            return "B";
        if (ratio >= 0.5f)
            return "C";
        return "D";
    }

    void UpdateProgressBoard()
    {
        CafeDayConfig day = CurrentDay;
        if (day == null)
            return;

        if (m_DayText != null)
            m_DayText.text = $"{day.dayName} / {dayConfigs.Count}";

        if (m_OrdersText != null)
            m_OrdersText.text = $"Orders: {m_OrdersFulfilledToday}/{day.ordersToClear}\nMissed: {m_OrdersFailedToday}";

        if (m_TimerText != null)
        {
            if (day.dayDuration > 0f)
                m_TimerText.text = "Time: " + FormatTime(m_DayTimeRemaining);
            else
                m_TimerText.text = "Time: No limit";
        }
    }

    string FormatTime(float seconds)
    {
        int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(seconds));
        int minutes = totalSeconds / 60;
        int remainder = totalSeconds % 60;
        return $"{minutes:00}:{remainder:00}";
    }

    void EnsureDefaultDayConfigs()
    {
        if (dayConfigs != null && dayConfigs.Count > 0)
            return;

        dayConfigs = new List<CafeDayConfig>()
        {
            CreateDay("Day 1", 1, 1, 1, 60f, 4f, 6f, 75f, false),
            CreateDay("Day 2", 1, 2, 1, 60f, 3.5f, 5.5f, 75f, false),
            CreateDay("Day 3", 1, 3, 1, 55f, 3f, 5f, 75f, true),
            CreateDay("Day 4", 3, 3, 2, 50f, 2.75f, 4.25f, 75f, true),
            CreateDay("Day 5", 3, 3, 3, 45f, 2.25f, 3.75f, 75f, true),
            CreateDay("Day 6", 5, 3, 3, 45f, 1.75f, 3f, 75f, true),
            CreateDay("Day 7", 5, 3, 3, 30f, 1.25f, 2.5f, 75f, true),
        };
    }

    void ApplyRequiredDayTuning()
    {
        if (dayConfigs == null)
            return;

        if (dayConfigs.Count >= 6 && dayConfigs[5] != null)
            dayConfigs[5].animalCountdown = 45f;

        if (dayConfigs.Count >= 7 && dayConfigs[6] != null)
            dayConfigs[6].ordersToClear = 5;
    }

    CafeDayConfig CreateDay(string name, int ordersToClear, int orderSize, int maxAnimals, float countdown, float minDelay, float maxDelay, float duration, bool duplicates)
    {
        return new CafeDayConfig()
        {
            dayName = name,
            ordersToClear = ordersToClear,
            orderSize = orderSize,
            maxConcurrentAnimals = maxAnimals,
            animalCountdown = countdown,
            minSpawnDelay = minDelay,
            maxSpawnDelay = maxDelay,
            dayDuration = duration,
            mushroomTypesAvailable = 4,
            allowDuplicateMushrooms = duplicates
        };
    }

    void CreateProgressBoardIfNeeded()
    {
        if (m_ProgressBoardRoot != null)
            return;

        if (progressBoardAnchor == null)
            progressBoardAnchor = FindTransformByName(progressBoardAnchorName);

        m_ProgressBoardRoot = new GameObject("DayProgressBoard", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        SetUiLayer(m_ProgressBoardRoot);

        Vector2 boardSize = new Vector2(Mathf.Max(progressBoardSize.x, 420f), Mathf.Max(progressBoardSize.y, 560f));
        RectTransform rootRect = m_ProgressBoardRoot.GetComponent<RectTransform>();
        rootRect.sizeDelta = boardSize;

        Canvas canvas = m_ProgressBoardRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        CanvasScaler scaler = m_ProgressBoardRoot.GetComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 1f;

        AddTrackedDeviceGraphicRaycaster(m_ProgressBoardRoot);

        if (progressBoardAnchor != null)
        {
            m_ProgressBoardRoot.transform.position =
                progressBoardAnchor.position +
                progressBoardAnchor.right * progressBoardWorldOffset.x +
                progressBoardAnchor.up * progressBoardWorldOffset.y +
                progressBoardAnchor.forward * progressBoardWorldOffset.z;
            m_ProgressBoardRoot.transform.rotation = progressBoardAnchor.rotation;
            m_ProgressBoardRoot.transform.localScale = progressBoardAnchor.lossyScale;
        }
        else
        {
            m_ProgressBoardRoot.transform.position = new Vector3(0.85f, 1.35f, 2f);
            m_ProgressBoardRoot.transform.localScale = Vector3.one * 0.002f;
        }

        Image background = CreatePanel(m_ProgressBoardRoot.transform, "BoardBackground", new Color(0.1f, 0.08f, 0.06f, 0.92f), boardSize);
        background.raycastTarget = true;

        m_DayText = CreateText("DayText", "First Day / 7", 34f, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -42f), new Vector2(-32f, 56f), TextAlignmentOptions.Center);
        m_OrdersText = CreateText("OrdersText", "Orders: 0/3", 26f, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -122f), new Vector2(-44f, 90f), TextAlignmentOptions.Center);
        m_TimerText = CreateText("TimerText", "Time: 03:00", 26f, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -200f), new Vector2(-44f, 48f), TextAlignmentOptions.Center);
        m_RankText = CreateText("RankText", "", 30f, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -262f), new Vector2(-44f, 54f), TextAlignmentOptions.Center);
        m_MessageText = CreateText("MessageText", "", 20f, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 220f), new Vector2(-44f, 92f), TextAlignmentOptions.Center);

        m_RestartButtonRoot = CreateButton("RestartButton", "Restart", new Vector2(-86f, 42f), RestartCurrentDay);
        m_QuitButtonRoot = CreateButton("QuitButton", "Quit", new Vector2(86f, 42f), QuitGame);
        m_NextDayButtonRoot = CreateButton("NextDayButton", "Next Day", new Vector2(-86f, 126f), ContinueToNextDay);
        m_FinalDayButtonRoot = CreateButton("FinalDayButton", "Final Day", new Vector2(86f, 126f), JumpToFinalDay);
        SetRouteButtonsVisible(false, false);
    }

    TextMeshProUGUI CreateText(string name, string text, float fontSize, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 sizeDelta, TextAlignmentOptions alignment)
    {
        GameObject textObj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        SetUiLayer(textObj);
        textObj.transform.SetParent(m_ProgressBoardRoot.transform, false);

        RectTransform rect = textObj.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        TextMeshProUGUI tmp = textObj.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = alignment;
        tmp.color = new Color(0.98f, 0.92f, 0.78f, 1f);
        tmp.enableWordWrapping = true;
        tmp.raycastTarget = false;
        return tmp;
    }

    Image CreatePanel(Transform parent, string name, Color color, Vector2 size)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        SetUiLayer(panel);
        panel.transform.SetParent(parent, false);

        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;

        Image image = panel.GetComponent<Image>();
        image.color = color;
        return image;
    }

    GameObject CreateButton(string name, string label, Vector2 anchoredPosition, UnityEngine.Events.UnityAction action)
    {
        GameObject buttonObj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        SetUiLayer(buttonObj);
        buttonObj.transform.SetParent(m_ProgressBoardRoot.transform, false);

        RectTransform rect = buttonObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(140f, 56f);

        Image image = buttonObj.GetComponent<Image>();
        image.color = new Color(0.45f, 0.3f, 0.14f, 0.95f);

        Button button = buttonObj.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);

        GameObject labelObj = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        SetUiLayer(labelObj);
        labelObj.transform.SetParent(buttonObj.transform, false);

        RectTransform labelRect = labelObj.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        TextMeshProUGUI tmp = labelObj.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 22f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;

        return buttonObj;
    }

    void SetRestartAndQuitButtonsVisible(bool visible)
    {
        if (m_RestartButtonRoot != null)
            m_RestartButtonRoot.SetActive(visible);
        if (m_QuitButtonRoot != null)
            m_QuitButtonRoot.SetActive(visible);
    }

    void SetRouteButtonsVisible(bool showNextDay, bool showFinalDay)
    {
        if (m_NextDayButtonRoot != null)
            m_NextDayButtonRoot.SetActive(showNextDay);
        if (m_FinalDayButtonRoot != null)
            m_FinalDayButtonRoot.SetActive(showFinalDay);
    }

    void ContinueToDay(int dayIndex)
    {
        if (!enableDaySystem || dayConfigs == null || dayConfigs.Count == 0)
            return;

        SetRouteButtonsVisible(false, false);
        m_IsTransitioning = false;
        BeginDay(Mathf.Clamp(dayIndex, 0, dayConfigs.Count - 1));
    }

    void CaptureDayStartSnapshot()
    {
        DayStartSnapshot snapshot = new DayStartSnapshot()
        {
            currency = currency,
            totalUpgradesDone = totalUpgradesDone
        };

        MushroomSpawner[] spawners = FindObjectsOfType<MushroomSpawner>(true);
        for (int i = 0; i < spawners.Length; i++)
        {
            if (spawners[i] != null && !snapshot.spawnerCapacityLevels.ContainsKey(spawners[i]))
                snapshot.spawnerCapacityLevels.Add(spawners[i], spawners[i].CurrentCapacityLevel);
        }

        MushroomUpgradeManager[] upgradeManagers = FindObjectsOfType<MushroomUpgradeManager>(true);
        for (int i = 0; i < upgradeManagers.Length; i++)
        {
            if (upgradeManagers[i] != null && !snapshot.upgradeLevels.ContainsKey(upgradeManagers[i]))
                snapshot.upgradeLevels.Add(upgradeManagers[i], upgradeManagers[i].CurrentUpgradeLevel);
        }

        m_DayStartSnapshot = snapshot;
    }

    void RestoreDayStartSnapshot()
    {
        SetRouteButtonsVisible(false, false);
        availableMushrooms.Clear();

        if (animalSpawner != null)
        {
            animalSpawner.SetSpawningEnabled(false);
            animalSpawner.ClearActiveAnimals();
        }

        if (m_DayStartSnapshot == null)
        {
            currency = 0;
            totalUpgradesDone = 0;
            return;
        }

        currency = m_DayStartSnapshot.currency;
        totalUpgradesDone = m_DayStartSnapshot.totalUpgradesDone;

        foreach (KeyValuePair<MushroomSpawner, int> spawnerState in m_DayStartSnapshot.spawnerCapacityLevels)
        {
            if (spawnerState.Key != null)
                spawnerState.Key.RestoreCapacityLevel(spawnerState.Value);
        }

        foreach (KeyValuePair<MushroomUpgradeManager, int> upgradeState in m_DayStartSnapshot.upgradeLevels)
        {
            if (upgradeState.Key != null)
                upgradeState.Key.RestoreUpgradeLevel(upgradeState.Value);
        }
    }

    void SetUiLayer(GameObject target)
    {
        int uiLayer = LayerMask.NameToLayer("UI");
        if (target != null && uiLayer >= 0)
            target.layer = uiLayer;
    }

    Transform FindTransformByName(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
            return null;

        Transform[] transforms = FindObjectsOfType<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i] != null && transforms[i].name == objectName)
                return transforms[i];
        }

        return null;
    }

    void AddTrackedDeviceGraphicRaycaster(GameObject target)
    {
        System.Type raycasterType = System.Type.GetType("UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
        if (raycasterType != null && target.GetComponent(raycasterType) == null)
            target.AddComponent(raycasterType);
    }

    void SetupAudioSources()
    {
        if (m_ProgressBoardRoot != null && m_EffectsSource == null)
        {
            m_EffectsSource = GetFeedbackAnchor().gameObject.AddComponent<AudioSource>();
            m_EffectsSource.playOnAwake = false;
            m_EffectsSource.spatialBlend = effectsSpatialBlend;
            m_EffectsSource.rolloffMode = AudioRolloffMode.Linear;
            m_EffectsSource.minDistance = 0.5f;
            m_EffectsSource.maxDistance = effectsMaxDistance;
            m_EffectsSource.volume = effectsVolume;
        }

        if (m_MusicSource == null)
        {
            m_MusicSource = gameObject.AddComponent<AudioSource>();
            m_MusicSource.playOnAwake = false;
            m_MusicSource.loop = true;
            m_MusicSource.spatialBlend = 0f;
            m_MusicSource.volume = musicVolume;
        }
    }

    void PlayDayMusic(CafeDayConfig day)
    {
        if (m_MusicSource == null || day == null)
            return;

        AudioClip clip = day.musicClip != null ? day.musicClip : GetGeneratedDayMusic(m_CurrentDayIndex);
        if (m_MusicSource.clip == clip && m_MusicSource.isPlaying)
            return;

        m_MusicSource.Stop();
        m_MusicSource.clip = clip;
        m_MusicSource.pitch = 1f + (m_CurrentDayIndex * 0.04f);
        m_MusicSource.Play();
    }

    void PlayEffect(AudioClip clip)
    {
        if (m_EffectsSource == null || clip == null)
            return;

        Transform anchor = GetFeedbackAnchor();
        m_EffectsSource.transform.position = GetFeedbackPosition(anchor);
        m_EffectsSource.PlayOneShot(clip, effectsVolume);
    }

    Transform GetFeedbackAnchor()
    {
        if (feedbackAnchor != null)
            return feedbackAnchor;

        if (m_ProgressBoardRoot != null)
            return m_ProgressBoardRoot.transform;

        Camera mainCamera = Camera.main;
        return mainCamera != null ? mainCamera.transform : transform;
    }

    Vector3 GetFeedbackPosition(Transform anchor)
    {
        if (anchor == null)
            return transform.position;

        return anchor.position +
            anchor.right * feedbackWorldOffset.x +
            anchor.up * feedbackWorldOffset.y +
            anchor.forward * feedbackWorldOffset.z;
    }

    AudioClip GetGeneratedSuccessClip()
    {
        if (m_GeneratedSuccessClip == null)
            m_GeneratedSuccessClip = CreateToneClip("GeneratedDaySuccess", new float[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.7f, 0.55f);
        return m_GeneratedSuccessClip;
    }

    AudioClip GetGeneratedFailClip()
    {
        if (m_GeneratedFailClip == null)
            m_GeneratedFailClip = CreateToneClip("GeneratedDayFail", new float[] { 220f, 174.61f, 146.83f }, 0.72f, 0.5f);
        return m_GeneratedFailClip;
    }

    AudioClip GetGeneratedDayMusic(int dayIndex)
    {
        if (m_GeneratedDayMusic.TryGetValue(dayIndex, out AudioClip clip))
            return clip;

        float baseFrequency = 174.61f + (dayIndex * 18f);
        clip = CreateToneClip("GeneratedDayMusic" + (dayIndex + 1), new float[] { baseFrequency, baseFrequency * 1.5f }, 2f, 0.12f);
        m_GeneratedDayMusic[dayIndex] = clip;
        return clip;
    }

    AudioClip CreateToneClip(string clipName, float[] frequencies, float duration, float amplitude = 0.35f)
    {
        int sampleRate = 44100;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        int sectionLength = Mathf.Max(1, sampleCount / Mathf.Max(1, frequencies.Length));

        for (int i = 0; i < sampleCount; i++)
        {
            int section = Mathf.Clamp(i / sectionLength, 0, frequencies.Length - 1);
            float frequency = frequencies[section];
            float t = i / (float)sampleRate;
            float fadeIn = Mathf.Clamp01(i / (sampleRate * 0.02f));
            float fadeOut = Mathf.Clamp01((sampleCount - i) / (sampleRate * 0.04f));
            samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * amplitude * fadeIn * fadeOut;
        }

        AudioClip clip = AudioClip.Create(clipName, sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
