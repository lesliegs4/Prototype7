using UnityEngine;

namespace Prototype7
{
    /// <summary>
    /// Build-safe (no AssetDatabase) scene wiring using Inspector references.
    /// Add this to any GameObject in your scene and assign fields as needed.
    /// If present, the runtime auto-bootstrapper is disabled.
    /// </summary>
    public sealed class Prototype7SceneWiring : MonoBehaviour
    {
        [Header("Core refs (optional if auto-find works)")]
        [SerializeField] private Camera cameraOverride;
        [SerializeField] private GameManager gameManager;
        [SerializeField] private SpriteRenderer background;
        [SerializeField] private HazardSpawner spawner;

        [Header("Player refs (optional if PlayerMovement can be found)")]
        [SerializeField] private PlayerMovement playerMovement;
        [SerializeField] private Rigidbody2D playerRigidbody;
        [SerializeField] private Collider2D playerCollider;

        [Header("Optional collider template (recommended)")]
        [SerializeField] private CircleCollider2D hazardColliderTemplate;
        [SerializeField] private SpriteRenderer hazardColliderTemplateSprite;

        [Header("BG Music (optional)")]
        [SerializeField] private GameObject bgmusicGameObject;
        [SerializeField] private AudioClip bgMusicClip;
        [SerializeField, Range(0f, 1f)] private float bgMusicVolume = 0.22f;

        private void Awake()
        {
            var cam = cameraOverride != null ? cameraOverride : Camera.main;
            if (cam == null) cam = FindFirstObjectByType<Camera>();

            gameManager ??= FindFirstObjectByType<GameManager>();
            spawner ??= FindFirstObjectByType<HazardSpawner>();

            background ??= GameObject.Find("Background")?.GetComponent<SpriteRenderer>();

            playerMovement ??= FindFirstObjectByType<PlayerMovement>();
            if (playerMovement != null)
            {
                if (playerRigidbody == null) playerRigidbody = playerMovement.GetComponent<Rigidbody2D>();
                if (playerCollider == null) playerCollider = playerMovement.GetComponent<Collider2D>();
            }

            if (hazardColliderTemplate != null && hazardColliderTemplateSprite != null && !HazardColliderTemplate.HasTemplate)
            {
                HazardColliderTemplate.SetFrom(hazardColliderTemplate, hazardColliderTemplateSprite.sprite);
            }

            if (gameManager != null && background != null)
            {
                gameManager.BindBackground(background);
                if (cam != null) FitBackgroundToCamera(background, cam);
            }

            if (gameManager != null && cam != null && spawner != null)
                spawner.Bind(gameManager, cam);

            if (playerMovement != null && gameManager != null && cam != null && playerRigidbody != null && playerCollider != null)
                playerMovement.Bind(gameManager, cam, playerRigidbody, playerCollider);

            SetupBgMusic();

            if (gameManager != null)
                gameManager.BeginRun();
        }

        private static void FitBackgroundToCamera(SpriteRenderer sr, Camera cam)
        {
            if (sr == null || cam == null) return;
            if (sr.sprite == null) return;

            sr.sortingOrder = -100;
            sr.color = Color.white;

            var camHeight = cam.orthographicSize * 2f;
            var camWidth = camHeight * cam.aspect;
            var spriteSize = sr.sprite.bounds.size;
            if (spriteSize.x <= 0f || spriteSize.y <= 0f) return;

            var scale = Mathf.Max(camWidth / spriteSize.x, camHeight / spriteSize.y);
            sr.transform.localScale = new Vector3(scale, scale, 1f);
            sr.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 10f);
        }

        private void SetupBgMusic()
        {
            var go = bgmusicGameObject ?? GameObject.Find("bgmusic") ?? GameObject.Find("BGMusic");
            if (go == null) return;

            var src = go.GetComponent<AudioSource>();
            if (src == null) src = go.AddComponent<AudioSource>();

            if (bgMusicClip != null) src.clip = bgMusicClip;
            if (src.clip == null) return;

            src.loop = true;
            src.playOnAwake = true;
            src.spatialBlend = 0f;
            src.volume = bgMusicVolume;

            if (!src.isPlaying) src.Play();
        }
    }
}

