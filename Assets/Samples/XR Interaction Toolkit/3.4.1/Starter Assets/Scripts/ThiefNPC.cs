using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class ThiefNPC : RouteWalkerNPC
{
    ThiefSpawner m_Spawner;
    int m_StolenLeaves;
    bool m_HasStolenLeaves;
    bool m_Recovered;
    bool m_IsReturning;

    [Header("Rock Hit")]
    [Tooltip("Tag used to identify the rock that can recover stolen leaves.")]
    [SerializeField] string rockTag = "Rock";

    public void Initialize(ThiefSpawner spawner, int stolenLeaves, float bobAmplitude, float bobFrequency)
    {
        m_Spawner = spawner;
        m_StolenLeaves = Mathf.Max(0, stolenLeaves);
        InitializeRouteWalker(bobAmplitude, bobFrequency);
    }

    void Update()
    {
        if (m_Recovered)
            return;

        if (IsRouteMoving)
            TickRouteMovement();
    }

    void LateUpdate()
    {
        if (!IsRouteWaitingAtDestination || m_Recovered)
            return;

        TickBobbing();
    }

    protected override void OnArrivedAtDestination()
    {
        if (m_HasStolenLeaves && m_IsReturning)
        {
            if (m_Spawner != null)
                m_Spawner.NotifyThiefRecovered(this);

            Destroy(gameObject);
            return;
        }

        if (!m_HasStolenLeaves)
        {
            int availableLeaves = GameManager.Instance != null ? Mathf.Max(0, GameManager.Instance.currency) : 0;
            m_StolenLeaves = Mathf.Min(m_StolenLeaves, availableLeaves);
            m_HasStolenLeaves = true;

            if (GameManager.Instance != null && m_StolenLeaves > 0)
                GameManager.Instance.AddCurrency(-Mathf.Abs(m_StolenLeaves));

            m_IsReturning = true;
            ReverseRoute();
        }
    }

    void OnTriggerEnter(Collider other)
    {
        TryRecoverWithRock(other != null ? other.gameObject : null);
    }

    void OnCollisionEnter(Collision collision)
    {
        TryRecoverWithRock(collision != null ? collision.gameObject : null);
    }

    void TryRecoverWithRock(GameObject other)
    {
        if (m_Recovered || other == null)
            return;

        if (!IsRock(other))
            return;

        m_Recovered = true;

        if (m_HasStolenLeaves && GameManager.Instance != null)
            GameManager.Instance.AddCurrency(Mathf.Abs(m_StolenLeaves));

        if (m_Spawner != null)
            m_Spawner.NotifyThiefRecovered(this);

        Destroy(gameObject);
    }

    bool IsRock(GameObject other)
    {
        if (!string.IsNullOrWhiteSpace(rockTag) && other.CompareTag(rockTag))
            return true;

        return other.name.ToLowerInvariant().Contains("rock");
    }
}