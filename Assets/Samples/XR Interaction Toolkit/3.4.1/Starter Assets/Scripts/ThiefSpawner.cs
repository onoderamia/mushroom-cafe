using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ThiefSpawner : MonoBehaviour
{
    [System.Serializable]
    public class ThiefTypeConfig
    {
        public string thiefName = "Thief";
        public GameObject thiefPrefab;
        [Tooltip("Leaves stolen when this thief reaches the destination.")]
        public int stolenLeaves = 3;
        [Min(0.01f)]
        public float spawnWeight = 1f;
    }

    [System.Serializable]
    public class ThiefRouteConfig
    {
        public string routeName = "Thief Route";
        [Tooltip("Final waiting point where the thief steals leaves.")]
        public Transform destinationPoint;
        [Tooltip("Optional control points used to shape a curved path.")]
        public List<Transform> controlPoints = new List<Transform>();
        [Min(2)] public int samplesPerSegment = 6;
        [Min(0.01f)] public float moveSpeed = 1.2f;
    }

    [Header("Spawn Setup")]
    public List<ThiefTypeConfig> thiefTypes = new List<ThiefTypeConfig>();
    [Tooltip("Single spawn location used by all thieves.")]
    public Transform spawnPoint;
    [Tooltip("Thieves will follow one randomly picked route toward its destination.")]
    public List<ThiefRouteConfig> thiefRoutes = new List<ThiefRouteConfig>();
    [Min(1)] public int maxAliveThieves = 1;
    [Min(0f)] public float firstSpawnDelay = 2f;
    [Min(0.1f)] public float minSpawnDelay = 6f;
    [Min(0.1f)] public float maxSpawnDelay = 12f;

    [Header("Shared NPC Tuning")]
    public float bobAmplitude = 0.06f;
    public float bobFrequency = 1.6f;

    readonly List<ThiefNPC> m_ActiveThieves = new List<ThiefNPC>();
    Coroutine m_SpawnRoutine;
    bool m_SpawningEnabled = true;

    void Start()
    {
        SetSpawningEnabled(true);
    }

    IEnumerator SpawnRoutine()
    {
        if (firstSpawnDelay > 0f)
            yield return new WaitForSeconds(firstSpawnDelay);

        while (enabled && m_SpawningEnabled)
        {
            CleanupNullThieves();

            if (m_ActiveThieves.Count < maxAliveThieves)
                SpawnOneThief();

            float wait = Random.Range(minSpawnDelay, Mathf.Max(minSpawnDelay, maxSpawnDelay));
            yield return new WaitForSeconds(wait);
        }
    }

    void SpawnOneThief()
    {
        ThiefTypeConfig config = PickRandomThiefType();
        if (config == null || config.thiefPrefab == null)
            return;

        Transform fixedSpawn = spawnPoint != null ? spawnPoint : transform;
        ThiefRouteConfig route = PickRandomRoute();
        if (route == null || route.destinationPoint == null)
            return;

        GameObject instance = Instantiate(config.thiefPrefab, fixedSpawn.position, fixedSpawn.rotation);
        if (instance == null)
        {
            Debug.LogWarning($"ThiefSpawner failed to instantiate prefab for type '{config.thiefName}'.", this);
            return;
        }

        EnsureThiefCollider(instance);

        ThiefNPC npc = instance.GetComponent<ThiefNPC>();
        if (npc == null)
            npc = instance.AddComponent<ThiefNPC>();

        if (npc == null)
        {
            Debug.LogWarning($"ThiefSpawner could not find or add ThiefNPC on spawned thief '{instance.name}'.", instance);
            Destroy(instance);
            return;
        }

        npc.Initialize(this, Mathf.Max(0, config.stolenLeaves), bobAmplitude, bobFrequency);

        List<Vector3> sampledPath = RoutePathUtility.BuildSampledPath(fixedSpawn.position, route.destinationPoint, route.controlPoints, route.samplesPerSegment);
        npc.BeginRoute(sampledPath, route.moveSpeed);

        m_ActiveThieves.Add(npc);
        Debug.Log($"Spawned thief: {config.thiefName}", instance);
    }

    ThiefTypeConfig PickRandomThiefType()
    {
        if (thiefTypes == null || thiefTypes.Count == 0)
            return null;

        float totalWeight = 0f;
        for (int i = 0; i < thiefTypes.Count; i++)
        {
            ThiefTypeConfig type = thiefTypes[i];
            if (type != null && type.thiefPrefab != null)
                totalWeight += Mathf.Max(0.01f, type.spawnWeight);
        }

        if (totalWeight <= 0f)
            return null;

        float pick = Random.value * totalWeight;
        float cumulative = 0f;

        for (int i = 0; i < thiefTypes.Count; i++)
        {
            ThiefTypeConfig type = thiefTypes[i];
            if (type == null || type.thiefPrefab == null)
                continue;

            cumulative += Mathf.Max(0.01f, type.spawnWeight);
            if (pick <= cumulative)
                return type;
        }

        return thiefTypes[thiefTypes.Count - 1];
    }

    ThiefRouteConfig PickRandomRoute()
    {
        if (thiefRoutes == null || thiefRoutes.Count == 0)
            return null;

        List<ThiefRouteConfig> validRoutes = new List<ThiefRouteConfig>();
        for (int i = 0; i < thiefRoutes.Count; i++)
        {
            ThiefRouteConfig route = thiefRoutes[i];
            if (route != null && route.destinationPoint != null)
                validRoutes.Add(route);
        }

        if (validRoutes.Count == 0)
            return null;

        int index = Random.Range(0, validRoutes.Count);
        Debug.Log("Picked thief route: " + validRoutes[index].routeName);
        return validRoutes[index];
    }

    void CleanupNullThieves()
    {
        for (int i = m_ActiveThieves.Count - 1; i >= 0; i--)
        {
            if (m_ActiveThieves[i] == null)
            {
                m_ActiveThieves.RemoveAt(i);
            }
        }
    }

    void EnsureThiefCollider(GameObject thief)
    {
        if (thief == null || thief.GetComponent<Collider>() != null)
            return;

        CapsuleCollider collider = thief.AddComponent<CapsuleCollider>();
        collider.isTrigger = true;
        collider.radius = 0.35f;
        collider.height = 1.1f;
        collider.center = new Vector3(0f, 0.55f, 0f);
    }

    public void SetSpawningEnabled(bool shouldSpawn)
    {
        m_SpawningEnabled = shouldSpawn;

        if (m_SpawningEnabled)
        {
            if (m_SpawnRoutine == null && isActiveAndEnabled)
                m_SpawnRoutine = StartCoroutine(SpawnRoutine());
        }
        else if (m_SpawnRoutine != null)
        {
            StopCoroutine(m_SpawnRoutine);
            m_SpawnRoutine = null;
        }
    }

    public void ClearActiveThieves()
    {
        for (int i = m_ActiveThieves.Count - 1; i >= 0; i--)
        {
            ThiefNPC thief = m_ActiveThieves[i];
            if (thief != null)
                Destroy(thief.gameObject);
        }

        m_ActiveThieves.Clear();
    }

    public void NotifyThiefRecovered(ThiefNPC npc)
    {
        m_ActiveThieves.Remove(npc);
    }

    void OnDisable()
    {
        if (m_SpawnRoutine != null)
        {
            StopCoroutine(m_SpawnRoutine);
            m_SpawnRoutine = null;
        }
    }
}