using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class AnimalCustomerOrder : MonoBehaviour
{
    [Header("Order")]
    [Min(1)] public int mushroomTypesAvailable = 4;
    [Min(1)] public int mushroomsWanted = 3;
    public List<MushroomSpawner.MushroomType> requestedMushroomTypes = new List<MushroomSpawner.MushroomType>();

    [Header("Respawn")]
    [Min(0f)] public float minRespawnDelay = 3f;
    [Min(0f)] public float maxRespawnDelay = 8f;

    readonly List<bool> m_FulfilledSlots = new List<bool>();
    readonly HashSet<int> m_ConsumedMushroomIds = new HashSet<int>();
    bool m_OrderComplete;

    void Start()
    {
        GenerateNewOrder();
    }

    public void GenerateNewOrder()
    {
        requestedMushroomTypes.Clear();
        m_FulfilledSlots.Clear();
        m_ConsumedMushroomIds.Clear();

        int typeCount = Mathf.Max(1, mushroomTypesAvailable);
        int orderSize = Mathf.Max(1, mushroomsWanted);

        for (int i = 0; i < orderSize; i++)
        {
            requestedMushroomTypes.Add((MushroomSpawner.MushroomType)Random.Range(0, typeCount));
            m_FulfilledSlots.Add(false);
        }

        m_OrderComplete = false;
        Debug.Log($"{name} wants mushrooms: {string.Join(", ", requestedMushroomTypes)}", this);
    }

    void OnTriggerEnter(Collider other)
    {
        TryAcceptMushroom(other.gameObject);
    }

    void OnCollisionEnter(Collision collision)
    {
        TryAcceptMushroom(collision.gameObject);
    }

    void TryAcceptMushroom(GameObject other)
    {
        if (m_OrderComplete)
            return;

        MushroomGrowth mushroom = other.GetComponentInParent<MushroomGrowth>();
        if (mushroom == null)
            return;

        int mushroomId = mushroom.gameObject.GetInstanceID();
        if (m_ConsumedMushroomIds.Contains(mushroomId))
            return;

        int matchingSlot = FindOpenMatchingSlot(mushroom.mushroomType);
        if (matchingSlot < 0)
            return;

        m_ConsumedMushroomIds.Add(mushroomId);
        m_FulfilledSlots[matchingSlot] = true;

        if (GameManager.Instance != null)
            GameManager.Instance.UnregisterMushroom(mushroom.gameObject);

        Destroy(mushroom.gameObject);

        if (IsOrderFilled())
            StartCoroutine(CompleteOrderThenRespawn());
    }

    int FindOpenMatchingSlot(MushroomSpawner.MushroomType mushroomType)
    {
        for (int i = 0; i < requestedMushroomTypes.Count; i++)
        {
            if (!m_FulfilledSlots[i] && requestedMushroomTypes[i] == mushroomType)
                return i;
        }

        return -1;
    }

    bool IsOrderFilled()
    {
        for (int i = 0; i < m_FulfilledSlots.Count; i++)
        {
            if (!m_FulfilledSlots[i])
                return false;
        }

        return true;
    }

    IEnumerator CompleteOrderThenRespawn()
    {
        m_OrderComplete = true;
        SetVisibleAndCollidable(false);

        float delay = Random.Range(minRespawnDelay, Mathf.Max(minRespawnDelay, maxRespawnDelay));
        yield return new WaitForSeconds(delay);

        GenerateNewOrder();
        SetVisibleAndCollidable(true);
    }

    void SetVisibleAndCollidable(bool value)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].enabled = value;

        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = value;
    }
}
