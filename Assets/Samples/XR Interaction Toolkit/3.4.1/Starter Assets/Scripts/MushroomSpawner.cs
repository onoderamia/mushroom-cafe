using UnityEngine;
using System.Collections.Generic;

public class MushroomSpawner : MonoBehaviour
{
    public enum MushroomType
    {
        Leccinum = 0,
        Champignon = 1,
        Amanita = 2,
        Agaricales = 3
    }

    public MushroomType mushroomType;
    public List<GameObject> mushroomPrefabs;
    public List<Transform> spawnPoints;
    public List<GameObject> grownMushrooms = new List<GameObject>();
    [Min(1)] public int maxUnlockedSpawnPoints = 5;
    private List<GameObject> currentMushrooms = new List<GameObject>();
    private int currentCapacityLevel = 0;
    
    public float[] growthTimes = { 30f, 20f, 10f, 5f };

    void Start()
    {
        SpawnUnlockedMushrooms();
    }

    public int CurrentCapacityLevel => currentCapacityLevel;

    public void SpawnMushroomAt(Transform point)
    {
        if (mushroomPrefabs.Count > 0 && IsSpawnPointUnlocked(point))
        {
            GameObject mushroom = Instantiate(mushroomPrefabs[0], point.position, point.rotation);
            mushroom.transform.parent = transform;
            currentMushrooms.Add(mushroom);
            
            // make kinematic so gravity doesn't pull it off the log
            Rigidbody rb = mushroom.GetComponent<Rigidbody>();
            if (rb != null)
                rb.isKinematic = true;
            
            MushroomGrowth growth = mushroom.GetComponent<MushroomGrowth>();
            growth.spawnPoint = point;
            growth.growthTime = GetCurrentGrowthTime();
            growth.mushroomType = mushroomType;
            growth.mushroomLevel = (int)mushroomType;
        }
    }

    public void OnMushroomGrown(GameObject mushroom)
    {
        if (!grownMushrooms.Contains(mushroom))
            grownMushrooms.Add(mushroom);
    }

    public void OnMushroomGrabbed(GameObject mushroom)
    {
        grownMushrooms.Remove(mushroom);
        currentMushrooms.Remove(mushroom);
        Transform point = mushroom.GetComponent<MushroomGrowth>().spawnPoint;

        if (IsSpawnPointUnlocked(point))
            StartCoroutine(RespawnAt(point));
    }

    private System.Collections.IEnumerator RespawnAt(Transform point)
    {
        yield return new WaitForSeconds(2f);
        SpawnMushroomAt(point);
    }

    public void UpgradeMushroomType()
    {
        int maxCapacityLevel = Mathf.Min(maxUnlockedSpawnPoints, spawnPoints.Count) - 1;
        if (currentCapacityLevel < maxCapacityLevel)
        {
            currentCapacityLevel++;
            UpdateCurrentGrowthTimes();

            Transform newlyUnlockedPoint = spawnPoints[currentCapacityLevel];
            if (!HasMushroomAt(newlyUnlockedPoint))
                SpawnMushroomAt(newlyUnlockedPoint);
        }
    }

    public void RestoreCapacityLevel(int capacityLevel)
    {
        StopAllCoroutines();
        ClearAllOwnedMushrooms();

        int maxCapacityLevel = Mathf.Min(maxUnlockedSpawnPoints, spawnPoints.Count) - 1;
        currentCapacityLevel = Mathf.Clamp(capacityLevel, 0, Mathf.Max(0, maxCapacityLevel));
        SpawnUnlockedMushrooms();
    }

    public void SetGrowthTime(float newGrowthTime)
    {
        foreach (GameObject mushroom in currentMushrooms)
        {
            MushroomGrowth growth = mushroom.GetComponent<MushroomGrowth>();
            if (growth != null)
                growth.growthTime = newGrowthTime;
        }
    }

    void SpawnUnlockedMushrooms()
    {
        int unlockedCount = GetUnlockedSpawnCount();
        for (int i = 0; i < unlockedCount; i++)
            SpawnMushroomAt(spawnPoints[i]);
    }

    int GetUnlockedSpawnCount()
    {
        return Mathf.Clamp(currentCapacityLevel + 1, 0, Mathf.Min(maxUnlockedSpawnPoints, spawnPoints.Count));
    }

    bool IsSpawnPointUnlocked(Transform point)
    {
        if (point == null)
            return false;

        int index = spawnPoints.IndexOf(point);
        return index >= 0 && index < GetUnlockedSpawnCount();
    }

    bool HasMushroomAt(Transform point)
    {
        for (int i = 0; i < currentMushrooms.Count; i++)
        {
            GameObject mushroom = currentMushrooms[i];
            if (mushroom == null)
                continue;

            MushroomGrowth growth = mushroom.GetComponent<MushroomGrowth>();
            if (growth != null && growth.spawnPoint == point)
                return true;
        }

        return false;
    }

    float GetCurrentGrowthTime()
    {
        if (growthTimes == null || growthTimes.Length == 0)
            return 10f;

        int index = Mathf.Clamp(currentCapacityLevel, 0, growthTimes.Length - 1);
        return growthTimes[index];
    }

    void UpdateCurrentGrowthTimes()
    {
        float growthTime = GetCurrentGrowthTime();
        for (int i = 0; i < currentMushrooms.Count; i++)
        {
            GameObject mushroom = currentMushrooms[i];
            if (mushroom == null)
                continue;

            MushroomGrowth growth = mushroom.GetComponent<MushroomGrowth>();
            if (growth != null)
                growth.growthTime = growthTime;
        }
    }

    void ClearAllOwnedMushrooms()
    {
        MushroomGrowth[] mushrooms = FindObjectsOfType<MushroomGrowth>(true);
        for (int i = mushrooms.Length - 1; i >= 0; i--)
        {
            MushroomGrowth mushroom = mushrooms[i];
            if (mushroom == null || !spawnPoints.Contains(mushroom.spawnPoint))
                continue;

            if (GameManager.Instance != null)
                GameManager.Instance.UnregisterMushroom(mushroom.gameObject);

            Destroy(mushroom.gameObject);
        }

        currentMushrooms.Clear();
        grownMushrooms.Clear();
    }
}
