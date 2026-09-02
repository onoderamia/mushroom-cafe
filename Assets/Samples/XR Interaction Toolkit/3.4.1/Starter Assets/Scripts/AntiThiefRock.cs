using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class AntiThiefRock : MonoBehaviour
{
    [Tooltip("Optional transform to reset the rock to. If empty, uses the rock's initial position.")]
    public Transform resetTo;

    [Tooltip("Linear speed threshold (m/s) below which the rock is considered at rest.")]
    public float restVelocityThreshold = 0.05f;
    [Tooltip("Angular speed threshold (rad/s) below which the rock is considered at rest.")]
    public float restAngularThreshold = 0.1f;
    [Tooltip("If true, freeze rotation axes to prevent spinning during flight.")]
    public bool freezeRotation = true;
    [Tooltip("How long the rock must remain below thresholds before resetting (seconds).")]
    public float requiredRestTime = 0.5f;

    [Tooltip("Short pause while physics is disabled during teleport to avoid jitter.")]
    public float disablePhysicsDuration = 0.05f;

    Rigidbody rb;
    Vector3 m_StartPos;
    Quaternion m_StartRot;
    float m_RestTimer;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        m_StartPos = resetTo != null ? resetTo.position : transform.position;
        m_StartRot = resetTo != null ? resetTo.rotation : transform.rotation;
        
        if (freezeRotation && rb != null)
        {
            rb.constraints = RigidbodyConstraints.FreezeRotation;
        }
    }

    void FixedUpdate()
    {
        if (rb == null)
            return;

        // consider at rest when both linear and angular velocities are small
        bool nearlyStopped = rb.linearVelocity.sqrMagnitude <= restVelocityThreshold * restVelocityThreshold
            && rb.angularVelocity.sqrMagnitude <= restAngularThreshold * restAngularThreshold;

        if (nearlyStopped)
        {
            m_RestTimer += Time.fixedDeltaTime;
            if (m_RestTimer >= requiredRestTime)
            {
                ReturnToStart();
            }
        }
        else
        {
            m_RestTimer = 0f;
        }
    }

    void ReturnToStart()
    {
        // reset physics and teleport to start position
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            if (freezeRotation)
            {
                rb.constraints = RigidbodyConstraints.FreezeRotation;
            }
        }

        transform.position = m_StartPos;
        transform.rotation = m_StartRot;

        if (rb != null)
        {
            rb.Sleep();
            Invoke(nameof(ReenablePhysics), disablePhysicsDuration);
        }

        m_RestTimer = 0f;
    }

    void ReenablePhysics()
    {
        if (rb == null)
            return;

        rb.isKinematic = false;
        if (freezeRotation)
        {
            rb.constraints = RigidbodyConstraints.FreezeRotation;
        }
        rb.WakeUp();
    }

    void OnDrawGizmosSelected()
    {
        Vector3 pos = (resetTo != null) ? resetTo.position : transform.position;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(pos, 0.12f);
    }
}
