using UnityEngine;

public class TrashCan : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (GameManager.Instance != null)
            GameManager.Instance.UnregisterMushroom(other.gameObject);
        
        Destroy(other.gameObject);
    }
}