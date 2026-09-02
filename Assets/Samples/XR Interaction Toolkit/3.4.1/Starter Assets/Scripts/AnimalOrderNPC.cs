using UnityEngine;
using System.Collections.Generic;
using TMPro;

[RequireComponent(typeof(Collider))]
public class AnimalOrderNPC : MonoBehaviour
{
    float m_TimeRemaining;
    float m_MaxTime;
    bool m_IsEnemy;
    int m_RewardLeaves;
    int m_StealLeaves;
    float m_BobAmplitude;
    float m_BobFrequency;
    float m_BobSeed;

    AnimalSpawner m_Spawner;
    Transform m_BobTarget;
    Vector3 m_BaseLocalPosition;
    bool m_Resolved;
    bool m_IsWaitingForOrder = true;
    bool m_IsMovingAnimation;
    Animator m_Animator;
    
    GameObject m_TimerUIRoot;
    TextMeshProUGUI m_TimerText;
    Transform m_TimerTransform;
    GameObject m_OrderDisplayRoot;
    Transform m_OrderDisplayTransform;
    readonly List<MushroomSpawner.MushroomType> m_RequestedMushrooms = new List<MushroomSpawner.MushroomType>();
    readonly List<bool> m_FulfilledMushrooms = new List<bool>();
    readonly List<GameObject> m_OrderVisuals = new List<GameObject>();
    GameObject[] m_MushroomVisualPrefabs;

    readonly List<Vector3> m_RoutePoints = new List<Vector3>();
    int m_RouteIndex;
    float m_MoveSpeed = 1.2f;

    [Header("Movement")]
    [SerializeField] float arrivalDistance = 0.08f;
    [SerializeField] float turnSpeed = 8f;

    [Header("Order Display")]
    [SerializeField] float orderDisplayHeight = 1.45f;
    [SerializeField] float orderVisualSpacing = 0.35f;
    [SerializeField] float orderVisualScale = 0.14f;

    [Header("Bobbing")]
    [Tooltip("Optional visual root to bob. Leave empty to bob this transform.")]
    [SerializeField] Transform bobTargetOverride;

    float leafMoveDelay;
    float leafMoveDuration;
    GameObject leafPrefab;

    struct LeafAnim
    {
        public GameObject leafObj;
        public Vector3 startPos;
        public Vector3 targetPos;
        public float elapsed;
    }

    List<LeafAnim> m_AnimatingLeaves = new List<LeafAnim>();
    float m_LeafDelayTimer;

    public float TimeRemainingNormalized => m_MaxTime <= 0f ? 0f : Mathf.Clamp01(m_TimeRemaining / m_MaxTime);

    public void Initialize(
        AnimalSpawner spawner,
        float countdown,
        List<MushroomSpawner.MushroomType> desiredMushrooms,
        bool isEnemy,
        int rewardLeaves,
        int stealLeaves,
        float bobAmplitude,
        float bobFrequency,
        float leafMoveDelay,
        float leafMoveDuration,
        GameObject leafPrefab,
        GameObject[] mushroomVisualPrefabs)
    {
        m_Spawner = spawner;
        m_MaxTime = Mathf.Max(1f, countdown);
        m_TimeRemaining = m_MaxTime;
        m_IsEnemy = isEnemy;
        m_RewardLeaves = rewardLeaves;
        m_StealLeaves = stealLeaves;
        m_BobAmplitude = Mathf.Max(0f, bobAmplitude);
        m_BobFrequency = Mathf.Max(0f, bobFrequency);
        m_BobSeed = Random.Range(0f, 100f);
        this.leafMoveDelay = leafMoveDelay;
        this.leafMoveDuration = leafMoveDuration;
        this.leafPrefab = leafPrefab;
        m_MushroomVisualPrefabs = mushroomVisualPrefabs;

        SetRequestedMushrooms(desiredMushrooms);

        m_BobTarget = bobTargetOverride != null ? bobTargetOverride : transform;
        m_BaseLocalPosition = m_BobTarget.localPosition;
        m_Animator = GetComponentInChildren<Animator>();
        if (m_Animator != null)
            m_Animator.applyRootMotion = false;

        Collider hitCollider = GetComponent<Collider>();
        if (hitCollider != null)
            hitCollider.isTrigger = true;
    }

    public void BeginRoute(List<Vector3> routePoints, float moveSpeed)
    {
        m_RoutePoints.Clear();

        if (routePoints != null)
        {
            for (int i = 0; i < routePoints.Count; i++)
                m_RoutePoints.Add(routePoints[i]);
        }

        m_MoveSpeed = Mathf.Max(0.01f, moveSpeed);

        if (m_RoutePoints.Count >= 2)
        {
            transform.position = m_RoutePoints[0];
            m_RouteIndex = 1;
            m_IsWaitingForOrder = false;
            SetMovingAnimation(true);
        }
        else
        {
            ArriveAtDestination();
        }
    }

    void Update()
    {
        if (!m_Resolved && !m_IsWaitingForOrder)
        {
            UpdateRouteMovement();
            return;
        }

        if (!m_Resolved && m_IsWaitingForOrder)
        {
            m_TimeRemaining -= Time.deltaTime;
            if (m_TimeRemaining <= 0f)
                HandleTimeout();
        }

        UpdateLeafAnimation();
    }

    void LateUpdate()
    {
        if (m_Resolved || !m_IsWaitingForOrder)
            return;

        AnimateBobbing();
        UpdateTimerUI();
    }

    void UpdateRouteMovement()
    {
        if (m_RoutePoints.Count < 2 || m_RouteIndex >= m_RoutePoints.Count)
        {
            ArriveAtDestination();
            return;
        }

        Vector3 target = m_RoutePoints[m_RouteIndex];
        Vector3 toTarget = target - transform.position;
        float distance = toTarget.magnitude;

        if (distance <= arrivalDistance)
        {
            transform.position = target;
            m_RouteIndex++;

            if (m_RouteIndex >= m_RoutePoints.Count)
            {
                ArriveAtDestination();
            }

            return;
        }

        Vector3 direction = toTarget / distance;
        float step = m_MoveSpeed * Time.deltaTime;
        transform.position += direction * Mathf.Min(step, distance);

        if (direction.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
        }
    }

    void AnimateBobbing()
    {
        if (m_BobTarget == null || m_BobAmplitude <= 0f || m_BobFrequency <= 0f)
            return;

        float bobOffset = Mathf.Sin((Time.time + m_BobSeed) * m_BobFrequency) * m_BobAmplitude;
        Vector3 pos = m_BaseLocalPosition;
        pos.y += bobOffset;
        m_BobTarget.localPosition = pos;
    }

    void CreateTimerUI()
    {
        if (m_TimerUIRoot != null)
            return;

        // Create a world-space canvas for the timer.
        m_TimerUIRoot = new GameObject("TimerUI", typeof(RectTransform), typeof(Canvas));
        Canvas canvas = m_TimerUIRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        m_TimerUIRoot.transform.SetParent(transform);
        m_TimerUIRoot.transform.localPosition = new Vector3(0, 2f, 0);

        RectTransform rootRect = m_TimerUIRoot.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(2f, 1f);

        // Create a TextMeshPro text object.
        GameObject timerTextObj = new GameObject("TimerText", typeof(RectTransform), typeof(TextMeshProUGUI));
        timerTextObj.transform.SetParent(m_TimerUIRoot.transform);
        timerTextObj.transform.localPosition = Vector3.zero;

        m_TimerText = timerTextObj.GetComponent<TextMeshProUGUI>();
        m_TimerText.alignment = TextAlignmentOptions.Center;
        m_TimerText.fontSize = 0.5f;

        RectTransform rectTransform = timerTextObj.GetComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(2, 1);
        
        m_TimerTransform = m_TimerUIRoot.transform;
    }

    void UpdateTimerUI()
    {
        if (m_TimerText == null || m_TimerTransform == null)
            return;

        int seconds = Mathf.CeilToInt(m_TimeRemaining);
        m_TimerText.text = seconds.ToString();

        // Make the timer face the camera
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            m_TimerTransform.LookAt(m_TimerTransform.position + mainCamera.transform.forward);
            if (m_OrderDisplayTransform != null)
                m_OrderDisplayTransform.LookAt(m_OrderDisplayTransform.position + mainCamera.transform.forward);
        }
    }

    void DestroyTimerUI()
    {
        if (m_TimerUIRoot != null)
        {
            Destroy(m_TimerUIRoot);
            m_TimerUIRoot = null;
            m_TimerText = null;
            m_TimerTransform = null;
        }

        DestroyOrderDisplay();
    }

    void CreateOrderDisplay()
    {
        if (m_OrderDisplayRoot != null)
            return;

        m_OrderDisplayRoot = new GameObject("OrderDisplay");
        m_OrderDisplayRoot.transform.SetParent(transform);
        m_OrderDisplayRoot.transform.localPosition = new Vector3(0f, orderDisplayHeight, 0f);
        m_OrderDisplayTransform = m_OrderDisplayRoot.transform;

        float totalWidth = (m_RequestedMushrooms.Count - 1) * orderVisualSpacing;
        for (int i = 0; i < m_RequestedMushrooms.Count; i++)
        {
            GameObject visual = CreateMushroomOrderVisual(m_RequestedMushrooms[i]);
            if (visual == null)
                continue;

            visual.transform.SetParent(m_OrderDisplayRoot.transform);
            visual.transform.localPosition = new Vector3((i * orderVisualSpacing) - (totalWidth * 0.5f), 0f, 0f);
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one * orderVisualScale;
            m_OrderVisuals.Add(visual);
        }
    }

    GameObject CreateMushroomOrderVisual(MushroomSpawner.MushroomType mushroomType)
    {
        GameObject prefab = GetMushroomVisualPrefab(mushroomType);
        GameObject visual = prefab != null
            ? Instantiate(prefab)
            : GameObject.CreatePrimitive(PrimitiveType.Sphere);

        visual.name = "Order_" + mushroomType;
        DisableOrderVisualInteraction(visual);
        return visual;
    }

    GameObject GetMushroomVisualPrefab(MushroomSpawner.MushroomType mushroomType)
    {
        int index = (int)mushroomType;
        if (m_MushroomVisualPrefabs == null || index < 0 || index >= m_MushroomVisualPrefabs.Length)
            return null;

        return m_MushroomVisualPrefabs[index];
    }

    void DisableOrderVisualInteraction(GameObject visual)
    {
        Collider[] colliders = visual.GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = false;

        Rigidbody[] rigidbodies = visual.GetComponentsInChildren<Rigidbody>();
        for (int i = 0; i < rigidbodies.Length; i++)
        {
            rigidbodies[i].isKinematic = true;
            rigidbodies[i].useGravity = false;
        }

        MonoBehaviour[] behaviours = visual.GetComponentsInChildren<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
            behaviours[i].enabled = false;
    }

    void DestroyOrderDisplay()
    {
        if (m_OrderDisplayRoot == null)
            return;

        Destroy(m_OrderDisplayRoot);
        m_OrderDisplayRoot = null;
        m_OrderDisplayTransform = null;
        m_OrderVisuals.Clear();
    }

    void OnTriggerEnter(Collider other)
    {
        if (m_Resolved)
            return;

        MushroomGrowth mushroom = other.GetComponentInParent<MushroomGrowth>();
        if (mushroom == null)
            return;

        if (m_IsEnemy)
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.UnregisterMushroom(other.gameObject);
                GameManager.Instance.AddCurrency(-Mathf.Abs(m_StealLeaves));
                GameManager.Instance.NotifyOrderFailed();
            }

            Destroy(other.gameObject);
            ResolveAndDespawn(Mathf.Max(1, m_StealLeaves));
            return;
        }

        int matchingSlot = FindOpenMatchingSlot(mushroom.mushroomType);
        if (matchingSlot < 0)
            return;

        if (GameManager.Instance != null)
            GameManager.Instance.UnregisterMushroom(mushroom.gameObject);

        m_FulfilledMushrooms[matchingSlot] = true;
        HideOrderVisual(matchingSlot);
        Destroy(mushroom.gameObject);

        if (IsOrderComplete())
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.AddCurrency(Mathf.Max(0, m_RewardLeaves));
                GameManager.Instance.NotifyOrderFulfilled();
            }

            ResolveAndDespawn(Mathf.Max(1, m_RewardLeaves));
        }
    }

    void HandleTimeout()
    {
        if (m_Resolved)
            return;

        if (GameManager.Instance != null)
        {
            if (m_IsEnemy)
                GameManager.Instance.AddCurrency(-Mathf.Abs(m_StealLeaves));

            GameManager.Instance.NotifyOrderFailed();
        }

        ResolveAndDespawn(1);
    }

    void ResolveAndDespawn(int leafDrops)
    {
        if (m_Resolved)
            return;

        m_Resolved = true;
        SetMovingAnimation(false);

        DestroyTimerUI();

        if (m_Spawner != null)
            m_Spawner.NotifyAnimalDespawned(this, leafDrops);

        StartLeafAnimation(leafDrops);
    }

    void StartLeafAnimation(int leafCount)
    {
        if (leafPrefab == null || leafCount <= 0)
        {
            Destroy(gameObject);
            return;
        }

        m_LeafDelayTimer = leafMoveDelay;
        SpawnLeaves(leafCount);
    }

    void SpawnLeaves(int count)
    {
        if (leafPrefab == null)
            return;

        for (int i = 0; i < count; i++)
        {
            GameObject leafObj = Instantiate(leafPrefab, transform.position, Quaternion.identity);

            LeafAnim leaf = new LeafAnim()
            {
                leafObj = leafObj,
                startPos = transform.position + Random.insideUnitSphere * 0.3f,
                targetPos = GetPlayerPosition(),
                elapsed = 0f
            };

            leafObj.transform.position = leaf.startPos;
            m_AnimatingLeaves.Add(leaf);
        }
    }

    void UpdateLeafAnimation()
    {
        if (m_LeafDelayTimer > 0f)
        {
            m_LeafDelayTimer -= Time.deltaTime;
            return;
        }

        for (int i = m_AnimatingLeaves.Count - 1; i >= 0; i--)
        {
            LeafAnim leaf = m_AnimatingLeaves[i];
            leaf.elapsed += Time.deltaTime;

            if (leaf.elapsed >= leafMoveDuration)
            {
                if (leaf.leafObj != null)
                    Destroy(leaf.leafObj);
                m_AnimatingLeaves.RemoveAt(i);
                continue;
            }

            float t = leaf.elapsed / leafMoveDuration;
            float eased = EaseOutCubic(t);
            if (leaf.leafObj != null)
                leaf.leafObj.transform.position = Vector3.Lerp(leaf.startPos, leaf.targetPos, eased);
            m_AnimatingLeaves[i] = leaf;
        }

        if (m_AnimatingLeaves.Count == 0 && m_Resolved)
        {
            Destroy(gameObject);
        }
    }

    Vector3 GetPlayerPosition()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
            return mainCamera.transform.position;
        return transform.position + Vector3.up * 5f;
    }

    float EaseOutCubic(float t)
    {
        float f = t - 1f;
        return f * f * f + 1f;
    }

    void ArriveAtDestination()
    {
        m_IsWaitingForOrder = true;
        SetMovingAnimation(false);

        if (m_BobTarget != null)
            m_BaseLocalPosition = m_BobTarget.localPosition;

        CreateTimerUI();
        CreateOrderDisplay();
    }

    void SetRequestedMushrooms(List<MushroomSpawner.MushroomType> desiredMushrooms)
    {
        m_RequestedMushrooms.Clear();
        m_FulfilledMushrooms.Clear();

        if (desiredMushrooms != null)
        {
            for (int i = 0; i < desiredMushrooms.Count; i++)
                m_RequestedMushrooms.Add(desiredMushrooms[i]);
        }

        for (int i = 0; i < m_RequestedMushrooms.Count; i++)
            m_FulfilledMushrooms.Add(false);
    }

    int FindOpenMatchingSlot(MushroomSpawner.MushroomType mushroomType)
    {
        for (int i = 0; i < m_RequestedMushrooms.Count; i++)
        {
            if (!m_FulfilledMushrooms[i] && m_RequestedMushrooms[i] == mushroomType)
                return i;
        }

        return -1;
    }

    void HideOrderVisual(int slot)
    {
        if (slot < 0 || slot >= m_OrderVisuals.Count)
            return;

        if (m_OrderVisuals[slot] != null)
            Destroy(m_OrderVisuals[slot]);
    }

    bool IsOrderComplete()
    {
        for (int i = 0; i < m_FulfilledMushrooms.Count; i++)
        {
            if (!m_FulfilledMushrooms[i])
                return false;
        }

        return true;
    }

    void SetMovingAnimation(bool moving)
    {
        if (m_Animator == null)
            return;

        if (m_IsMovingAnimation == moving)
            return;

        m_IsMovingAnimation = moving;
        m_Animator.speed = 1f;

        if (moving)
        {
            if (TryCrossFade("Run", 0.1f) || TryCrossFade("Walk", 0.1f))
                return;

            return;
        }

        if (TryCrossFade("Idle_A", 0.15f) || TryCrossFade("Idle", 0.15f))
            return;

        m_Animator.speed = 0f;
    }

    bool TryCrossFade(string stateName, float transitionDuration)
    {
        if (m_Animator == null || m_Animator.runtimeAnimatorController == null)
            return false;

        int stateHash = Animator.StringToHash(stateName);
        for (int layer = 0; layer < m_Animator.layerCount; layer++)
        {
            if (!m_Animator.HasState(layer, stateHash))
                continue;

            m_Animator.CrossFade(stateHash, transitionDuration, layer);
            return true;
        }

        return false;
    }
}
