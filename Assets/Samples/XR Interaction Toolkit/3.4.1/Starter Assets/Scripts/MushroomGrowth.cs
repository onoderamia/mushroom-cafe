using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class MushroomGrowth : MonoBehaviour
{
    public float growthTime = 10f;
    public Transform spawnPoint;
    public MushroomSpawner.MushroomType mushroomType;
    public int mushroomLevel = 0;
    
    [Header("Sounds")]
    public AudioClip grabSound;

    private float timer = 0f;
    private bool isGrown = false;
    private bool isGrabbed = false;
    private Vector3 targetScale;
    private MushroomSpawner spawner;
    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grabInteractable;
    private AudioSource audioSource;

    void Start()
    {
        targetScale = transform.localScale;
        transform.localScale = Vector3.zero;
        spawner = GetComponentInParent<MushroomSpawner>();
        
        grabInteractable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        if (grabInteractable != null)
        {
            grabInteractable.enabled = false;
            grabInteractable.selectEntered.AddListener(OnGrabbed);
            grabInteractable.selectExited.AddListener(OnReleased);
        }

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.spatialBlend = 1f;
    }

    void Update()
    {
        if (!isGrown)
        {
            timer += Time.deltaTime;
            float progress = timer / growthTime;
            
            float easedProgress;
            if (progress < 0.7f)
                easedProgress = (progress / 0.7f) * 0.6f;
            else
                easedProgress = 0.6f + ((progress - 0.7f) / 0.3f) * 0.4f;
            easedProgress = Mathf.Clamp01(easedProgress);
            transform.localScale = Vector3.Lerp(Vector3.zero, targetScale, easedProgress);

            if (progress >= 1f)
            {
                isGrown = true;
                transform.localScale = targetScale;
                
                if (grabInteractable != null)
                    grabInteractable.enabled = true;
                
                if (spawner != null)
                    spawner.OnMushroomGrown(gameObject);
                if (GameManager.Instance != null)
                    GameManager.Instance.RegisterMushroom(gameObject);
            }
        }
    }

    void OnGrabbed(SelectEnterEventArgs args)
    {
        if (isGrabbed) return;
        isGrabbed = true;

        if (grabSound != null)
            audioSource.PlayOneShot(grabSound);

        if (GameManager.Instance != null)
            GameManager.Instance.UnregisterMushroom(gameObject);

        MushroomSpawner savedSpawner = spawner;
        transform.parent = null;
        if (savedSpawner != null)
            savedSpawner.OnMushroomGrabbed(gameObject);
    }

    void OnReleased(SelectExitEventArgs args)
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
        }
    }

    public bool IsGrown() { return isGrown; }
}
