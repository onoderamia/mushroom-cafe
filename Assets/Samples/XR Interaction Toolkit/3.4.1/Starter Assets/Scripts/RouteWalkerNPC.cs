using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public abstract class RouteWalkerNPC : MonoBehaviour
{
    readonly List<Vector3> m_RoutePoints = new List<Vector3>();
    int m_RouteIndex;
    float m_MoveSpeed = 1.2f;
    bool m_IsMoving;
    bool m_IsWaitingAtDestination;

    [Header("Movement")]
    [SerializeField] float arrivalDistance = 0.08f;
    [SerializeField] float turnSpeed = 8f;

    [Header("Bobbing")]
    [Tooltip("Optional visual root to bob. Leave empty to bob this transform.")]
    [SerializeField] Transform bobTargetOverride;

    Transform m_BobTarget;
    Vector3 m_BaseLocalPosition;
    float m_BobAmplitude;
    float m_BobFrequency;
    float m_BobSeed;

    protected bool IsRouteMoving => m_IsMoving;
    protected bool IsRouteWaitingAtDestination => m_IsWaitingAtDestination;

    protected void InitializeRouteWalker(float bobAmplitude, float bobFrequency)
    {
        m_BobAmplitude = Mathf.Max(0f, bobAmplitude);
        m_BobFrequency = Mathf.Max(0f, bobFrequency);
        m_BobSeed = Random.Range(0f, 100f);

        m_BobTarget = bobTargetOverride != null ? bobTargetOverride : transform;
        m_BaseLocalPosition = m_BobTarget.localPosition;
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
            m_IsMoving = true;
            m_IsWaitingAtDestination = false;
            return;
        }

        CompleteArrival();
    }

    protected void ReverseRoute()
    {
        if (m_RoutePoints.Count < 2)
            return;

        m_RoutePoints.Reverse();
        m_RouteIndex = 1;
        m_IsMoving = true;
        m_IsWaitingAtDestination = false;
        RefreshBobbingBase();
    }

    protected void TickRouteMovement()
    {
        if (!m_IsMoving)
            return;

        if (m_RoutePoints.Count < 2 || m_RouteIndex >= m_RoutePoints.Count)
        {
            CompleteArrival();
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
                CompleteArrival();

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

    protected void TickBobbing()
    {
        if (m_BobTarget == null || m_BobAmplitude <= 0f || m_BobFrequency <= 0f)
            return;

        float bobOffset = Mathf.Sin((Time.time + m_BobSeed) * m_BobFrequency) * m_BobAmplitude;
        Vector3 pos = m_BaseLocalPosition;
        pos.y += bobOffset;
        m_BobTarget.localPosition = pos;
    }

    protected void RefreshBobbingBase()
    {
        if (m_BobTarget != null)
            m_BaseLocalPosition = m_BobTarget.localPosition;
    }

    void CompleteArrival()
    {
        m_IsMoving = false;
        m_IsWaitingAtDestination = true;
        RefreshBobbingBase();
        OnArrivedAtDestination();
    }

    protected abstract void OnArrivedAtDestination();
}