using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class AnimalSpawner : MonoBehaviour
{
    [System.Serializable]
    public class AnimalTypeConfig
    {
        public string animalName = "Animal";
        public GameObject animalPrefab;
        [Tooltip("How long this animal waits before getting mad.")]
        public float orderCountdown = 30f;
        [HideInInspector]
        public int desiredMushroomIndex = -1;
        [Tooltip("The fixed three-mushroom order this animal wants.")]
        public List<MushroomSpawner.MushroomType> desiredMushrooms = new List<MushroomSpawner.MushroomType>();
        public bool isEnemy = false;
        [Tooltip("Positive leaves when this order succeeds.")]
        public int rewardLeaves = 3;
        [Tooltip("Leaves stolen when enemy succeeds or when timer runs out.")]
        public int stealLeaves = 2;
        [Min(0.01f)]
        public float spawnWeight = 1f;
    }

    [System.Serializable]
    public class AnimalRouteConfig
    {
        public string routeName = "Route";
        [Tooltip("Final waiting point where the animal requests an order.")]
        public Transform destinationPoint;
        [Tooltip("Optional control points used to shape a curved path.")]
        public List<Transform> controlPoints = new List<Transform>();
        [Min(2)] public int samplesPerSegment = 6;
        [Min(0.01f)] public float moveSpeed = 1.2f;
    }

    [Header("Spawn Setup")]
    public List<AnimalTypeConfig> animalTypes = new List<AnimalTypeConfig>();
    [Tooltip("Single spawn location used by all animals.")]
    public Transform spawnPoint;
    [Tooltip("Animals will follow one randomly picked route toward its destination.")]
    public List<AnimalRouteConfig> arrivalRoutes = new List<AnimalRouteConfig>();
    [Min(1)] public int maxAliveAnimals = 2;
    [Min(0f)] public float firstSpawnDelay = 1f;
    [Min(0.1f)] public float minSpawnDelay = 3f;
    [Min(0.1f)] public float maxSpawnDelay = 7f;

    [Header("Shared NPC Tuning")]
    public float bobAmplitude = 0.08f;
    public float bobFrequency = 1.6f;

    [Header("Leaf Animation")]
    [Min(0f)] public float leafMoveDelay = 2f;
    [Min(0.1f)] public float leafMoveDuration = 1.5f;
    [Tooltip("Optional visual prefab for animating leaves. No leaf animation plays if this is empty.")]
    public GameObject leafAnimationPrefab;

    [Header("Leaf Drops")]
    [Tooltip("Optional visual prefab spawned when animals despawn.")]
    public GameObject leafDropPrefab;
    [Min(0)] public int defaultLeafDropCount = 3;
    [Min(0f)] public float leafDropScatterRadius = 0.35f;
    [Min(0.1f)] public float leafDropLifetime = 5f;

    readonly List<AnimalOrderNPC> m_ActiveAnimals = new List<AnimalOrderNPC>();
    readonly Dictionary<AnimalOrderNPC, AnimalRouteConfig> m_AssignedRoutes = new Dictionary<AnimalOrderNPC, AnimalRouteConfig>();
    readonly GameObject[] m_MushroomVisualPrefabs = new GameObject[4];
    Coroutine m_SpawnRoutine;
    bool m_SpawningEnabled = true;
    bool m_UseDynamicLevelOrders;
    int m_DynamicOrderSize = 3;
    int m_DynamicMushroomTypesAvailable = 4;
    bool m_DynamicAllowDuplicateMushrooms = true;
    float m_DynamicCountdown = 30f;

    void Start()
    {
        CacheMushroomVisualPrefabs();
        SetSpawningEnabled(true);
    }

    IEnumerator SpawnRoutine()
    {
        if (firstSpawnDelay > 0f)
            yield return new WaitForSeconds(firstSpawnDelay);

        while (enabled && m_SpawningEnabled)
        {
            CleanupNullAnimals();

            if (m_ActiveAnimals.Count < maxAliveAnimals)
                SpawnOneAnimal();

            float wait = Random.Range(minSpawnDelay, Mathf.Max(minSpawnDelay, maxSpawnDelay));
            yield return new WaitForSeconds(wait);
        }
    }

    void SpawnOneAnimal()
    {
        AnimalTypeConfig config = PickRandomAnimalType();
        if (config == null || config.animalPrefab == null)
            return;

        Transform fixedSpawn = spawnPoint != null ? spawnPoint : transform;
        AnimalRouteConfig route = PickRandomRoute();
        if (route == null || route.destinationPoint == null)
            return;

        Vector3 spawnPos = fixedSpawn.position;
        Quaternion spawnRot = fixedSpawn.rotation;

        GameObject instance = Instantiate(config.animalPrefab, spawnPos, spawnRot);
        if (instance == null)
        {
            Debug.LogWarning($"AnimalSpawner failed to instantiate prefab for type '{config.animalName}'.", this);
            return;
        }

        EnsureAnimalCollider(instance);

        AnimalOrderNPC npc = instance.GetComponent<AnimalOrderNPC>();
        if (npc == null)
            npc = instance.AddComponent<AnimalOrderNPC>();

        if (npc == null)
        {
            Debug.LogWarning($"AnimalSpawner could not find or add AnimalOrderNPC on spawned animal '{instance.name}'.", instance);
            Destroy(instance);
            return;
        }

        float countdown = m_UseDynamicLevelOrders ? m_DynamicCountdown : config.orderCountdown;
        npc.Initialize(this, countdown, GetDesiredMushrooms(config), config.isEnemy, config.rewardLeaves, config.stealLeaves, bobAmplitude, bobFrequency, leafMoveDelay, leafMoveDuration, leafAnimationPrefab, m_MushroomVisualPrefabs);

        List<Vector3> sampledPath = RoutePathUtility.BuildSampledPath(fixedSpawn.position, route.destinationPoint, route.controlPoints, route.samplesPerSegment);
        npc.BeginRoute(sampledPath, route.moveSpeed);

        m_ActiveAnimals.Add(npc);
        m_AssignedRoutes[npc] = route;
        Debug.Log($"Spawned animal: {config.animalName}", instance);
    }

    AnimalTypeConfig PickRandomAnimalType()
    {
        if (animalTypes == null || animalTypes.Count == 0)
            return null;

        float totalWeight = 0f;
        for (int i = 0; i < animalTypes.Count; i++)
        {
            AnimalTypeConfig type = animalTypes[i];
            if (type != null && type.animalPrefab != null)
                totalWeight += Mathf.Max(0.01f, type.spawnWeight);
        }

        if (totalWeight <= 0f)
            return null;

        float pick = Random.value * totalWeight;
        float cumulative = 0f;

        for (int i = 0; i < animalTypes.Count; i++)
        {
            AnimalTypeConfig type = animalTypes[i];
            if (type == null || type.animalPrefab == null)
                continue;

            cumulative += Mathf.Max(0.01f, type.spawnWeight);
            if (pick <= cumulative)
                return type;
        }

        return animalTypes[animalTypes.Count - 1];
    }

    AnimalRouteConfig PickRandomRoute()
    {
        if (arrivalRoutes == null || arrivalRoutes.Count == 0)
            return null;

        List<AnimalRouteConfig> validRoutes = new List<AnimalRouteConfig>();
        for (int i = 0; i < arrivalRoutes.Count; i++)
        {
            AnimalRouteConfig route = arrivalRoutes[i];
            if (route != null && route.destinationPoint != null && !IsRouteOccupied(route))
                validRoutes.Add(route);
        }

        if (validRoutes.Count == 0)
            return null;

        int index = Random.Range(0, validRoutes.Count);
        Debug.Log("Picked route: " + validRoutes[index].routeName);
        return validRoutes[index];
    }

    void CleanupNullAnimals()
    {
        for (int i = m_ActiveAnimals.Count - 1; i >= 0; i--)
        {
            if (m_ActiveAnimals[i] == null)
            {
                m_AssignedRoutes.Remove(m_ActiveAnimals[i]);
                m_ActiveAnimals.RemoveAt(i);
            }
        }
    }

    public void NotifyAnimalDespawned(AnimalOrderNPC npc, int leafDrops)
    {
        m_ActiveAnimals.Remove(npc);
        m_AssignedRoutes.Remove(npc);
        SpawnLeafDrops(npc != null ? npc.transform.position : transform.position, leafDrops);
    }

    bool IsRouteOccupied(AnimalRouteConfig route)
    {
        foreach (AnimalRouteConfig assignedRoute in m_AssignedRoutes.Values)
        {
            if (assignedRoute == route)
                return true;
        }

        return false;
    }

    void EnsureAnimalCollider(GameObject animal)
    {
        if (animal == null || animal.GetComponent<Collider>() != null)
            return;

        CapsuleCollider collider = animal.AddComponent<CapsuleCollider>();
        collider.isTrigger = true;
        collider.radius = 0.35f;
        collider.height = 1.1f;
        collider.center = new Vector3(0f, 0.55f, 0f);
    }

    List<MushroomSpawner.MushroomType> GetDesiredMushrooms(AnimalTypeConfig config)
    {
        if (m_UseDynamicLevelOrders)
            return GenerateDynamicOrder();

        if (config.desiredMushrooms != null && config.desiredMushrooms.Count > 0)
            return new List<MushroomSpawner.MushroomType>(config.desiredMushrooms);

        MushroomSpawner.MushroomType fallbackType = config.desiredMushroomIndex < 0
            ? MushroomSpawner.MushroomType.Leccinum
            : (MushroomSpawner.MushroomType)Mathf.Clamp(config.desiredMushroomIndex, 0, 3);

        return new List<MushroomSpawner.MushroomType>()
        {
            fallbackType,
            fallbackType,
            fallbackType
        };
    }

    public void ConfigureForDay(int dayMaxAliveAnimals, float dayMinSpawnDelay, float dayMaxSpawnDelay, float dayCountdown, int orderSize, int mushroomTypesAvailable, bool allowDuplicateMushrooms)
    {
        maxAliveAnimals = Mathf.Max(1, dayMaxAliveAnimals);
        minSpawnDelay = Mathf.Max(0.1f, dayMinSpawnDelay);
        maxSpawnDelay = Mathf.Max(minSpawnDelay, dayMaxSpawnDelay);
        m_DynamicCountdown = Mathf.Max(1f, dayCountdown);
        m_DynamicOrderSize = Mathf.Max(1, orderSize);
        m_DynamicMushroomTypesAvailable = Mathf.Clamp(mushroomTypesAvailable, 1, 4);
        m_DynamicAllowDuplicateMushrooms = allowDuplicateMushrooms;
        m_UseDynamicLevelOrders = true;
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

    public void ClearActiveAnimals()
    {
        for (int i = m_ActiveAnimals.Count - 1; i >= 0; i--)
        {
            AnimalOrderNPC npc = m_ActiveAnimals[i];
            if (npc != null)
                Destroy(npc.gameObject);
        }

        m_ActiveAnimals.Clear();
        m_AssignedRoutes.Clear();
    }

    List<MushroomSpawner.MushroomType> GenerateDynamicOrder()
    {
        List<MushroomSpawner.MushroomType> order = new List<MushroomSpawner.MushroomType>();
        int typeCount = Mathf.Clamp(m_DynamicMushroomTypesAvailable, 1, 4);
        int orderSize = Mathf.Max(1, m_DynamicOrderSize);

        if (m_DynamicAllowDuplicateMushrooms)
        {
            for (int i = 0; i < orderSize; i++)
                order.Add((MushroomSpawner.MushroomType)Random.Range(0, typeCount));
            return order;
        }

        List<MushroomSpawner.MushroomType> pool = new List<MushroomSpawner.MushroomType>();
        for (int i = 0; i < typeCount; i++)
            pool.Add((MushroomSpawner.MushroomType)i);

        while (order.Count < orderSize)
        {
            if (pool.Count == 0)
            {
                order.Add((MushroomSpawner.MushroomType)Random.Range(0, typeCount));
                continue;
            }

            int index = Random.Range(0, pool.Count);
            order.Add(pool[index]);
            pool.RemoveAt(index);
        }

        return order;
    }

    void CacheMushroomVisualPrefabs()
    {
        MushroomSpawner[] mushroomSpawners = FindObjectsOfType<MushroomSpawner>();
        for (int i = 0; i < mushroomSpawners.Length; i++)
        {
            MushroomSpawner mushroomSpawner = mushroomSpawners[i];
            if (mushroomSpawner == null || mushroomSpawner.mushroomPrefabs == null || mushroomSpawner.mushroomPrefabs.Count == 0)
                continue;

            int typeIndex = (int)mushroomSpawner.mushroomType;
            if (typeIndex < 0 || typeIndex >= m_MushroomVisualPrefabs.Length)
                continue;

            m_MushroomVisualPrefabs[typeIndex] = mushroomSpawner.mushroomPrefabs[0];
        }
    }

    void SpawnLeafDrops(Vector3 atPosition, int count)
    {
        if (leafDropPrefab == null)
            return;

        int drops = Mathf.Max(defaultLeafDropCount, count);
        for (int i = 0; i < drops; i++)
        {
            Vector2 circle = Random.insideUnitCircle * leafDropScatterRadius;
            Vector3 pos = atPosition + new Vector3(circle.x, 0.15f, circle.y);
            GameObject drop = Instantiate(leafDropPrefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            Destroy(drop, leafDropLifetime);
        }
    }
}
