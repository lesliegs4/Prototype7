using UnityEngine;
using UnityEngine.SceneManagement;

namespace Prototype7
{
    /// <summary>
    /// Auto-builds a playable 2D prototype at runtime (no manual scene wiring).
    /// </summary>
    public static class PrototypeBootstrapper
    {
        private const string RootName = "__Prototype7_Root";
        private const string BackgroundName = "Background";
        private const string SpawnerName = "Spawner";
        private const string BgMusicName = "BGMusic";

        private static int _lastBootstrappedSceneHandle = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneLoadHook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootstrapAfterInitialLoad()
        {
            BootstrapForScene(SceneManager.GetActiveScene());
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            BootstrapForScene(scene);
        }

        private static void BootstrapForScene(Scene scene)
        {
            if (scene.IsValid() && scene.handle == _lastBootstrappedSceneHandle) return;
            if (scene.IsValid()) _lastBootstrappedSceneHandle = scene.handle;

            // If the scene is wired via Inspector, don't auto-build anything.
            if (HasSceneWiring()) return;

            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                cam = camGo.AddComponent<Camera>();
                camGo.tag = "MainCamera";
                cam.orthographic = true;
                cam.orthographicSize = 5f;
                cam.transform.position = new Vector3(0f, 0f, -10f);
            }

            EnsureRoot(out var root);

            // Remove common template UI objects (no text allowed and not needed here).
            var canvas = GameObject.Find("Canvas");
            if (canvas != null) Object.Destroy(canvas);
            var eventSystem = GameObject.Find("EventSystem");
            if (eventSystem != null) Object.Destroy(eventSystem);

            // Clean up any stray placed hazard from the template scene.
            var straySoot = GameObject.Find("soot1");
            if (straySoot != null)
            {
                // Use soot1's CircleCollider2D as the "hitbox template" reference, then remove it.
                if (!HazardColliderTemplate.HasTemplate)
                {
                    var col = straySoot.GetComponent<CircleCollider2D>();
                    var sr = straySoot.GetComponent<SpriteRenderer>();
                    HazardColliderTemplate.SetFrom(col, sr != null ? sr.sprite : null);
                }

                Object.Destroy(straySoot);
            }

            var gm = Object.FindFirstObjectByType<GameManager>();
            if (gm == null)
            {
                var gmGo = new GameObject("GameManager");
                gmGo.transform.SetParent(root.transform, worldPositionStays: true);
                gm = gmGo.AddComponent<GameManager>();
            }

            EnsureBackground(root.transform, gm, cam);
            EnsurePlayer(root.transform, gm, cam);
            EnsureSpawner(root.transform, gm, cam);
            EnsureBgMusic(root.transform);

            gm.BeginRun();
        }

        private static bool HasSceneWiring()
        {
            // Include inactive objects so a disabled wiring object still prevents auto-bootstrap.
            var all = Object.FindObjectsOfType(typeof(Prototype7SceneWiring), includeInactive: true);
            return all != null && all.Length > 0;
        }

        private static void EnsureRoot(out GameObject root)
        {
            root = GameObject.Find(RootName);
            if (root != null) return;

            root = new GameObject(RootName);
        }

        private static void EnsureBackground(Transform root, GameManager gm, Camera cam)
        {
            var bg = GameObject.Find(BackgroundName);
            if (bg == null)
            {
                bg = new GameObject(BackgroundName);
                bg.transform.SetParent(root, worldPositionStays: true);
                bg.transform.position = new Vector3(0f, 0f, 10f);
            }

            var sr = bg.GetComponent<SpriteRenderer>();
            if (sr == null) sr = bg.AddComponent<SpriteRenderer>();
            sr.sortingOrder = -100;
            sr.color = Color.white;

            gm.BindBackground(sr);
            FitBackgroundToCamera(sr, cam);
        }

        private static void FitBackgroundToCamera(SpriteRenderer sr, Camera cam)
        {
            if (sr.sprite == null) return;

            var camHeight = cam.orthographicSize * 2f;
            var camWidth = camHeight * cam.aspect;
            var spriteSize = sr.sprite.bounds.size;
            if (spriteSize.x <= 0f || spriteSize.y <= 0f) return;

            var scale = Mathf.Max(camWidth / spriteSize.x, camHeight / spriteSize.y);
            sr.transform.localScale = new Vector3(scale, scale, 1f);
            sr.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 10f);
        }

        private static void EnsurePlayer(Transform root, GameManager gm, Camera cam)
        {
            var player = GameObject.Find("Player");
            if (player == null)
            {
                player = new GameObject("Player");
                player.transform.SetParent(root, worldPositionStays: true);
            }

            var sr = player.GetComponent<SpriteRenderer>();
            if (sr == null) sr = player.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 10;
            if (sr.sprite == null)
            {
                sr.sprite = CreateSolidSprite();
            }

            var rb = player.GetComponent<Rigidbody2D>();
            if (rb == null) rb = player.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.freezeRotation = true;

            var col = player.GetComponent<Collider2D>();
            if (col == null) col = player.AddComponent<BoxCollider2D>();
            col.isTrigger = false;

            var mover = player.GetComponent<PlayerMovement>();
            if (mover == null) mover = player.AddComponent<PlayerMovement>();
            mover.Bind(gm, cam, rb, col);

            // Place near bottom center.
            var bottomY = cam.transform.position.y - cam.orthographicSize;
            var safeY = bottomY + 0.9f;
            player.transform.position = new Vector3(cam.transform.position.x, safeY, 0f);

            // Ensure a reasonable on-screen size (template scene had extreme scaling).
            if (player.transform.localScale.x > 5f || player.transform.localScale.y > 5f)
                player.transform.localScale = Vector3.one;
            if (player.transform.localScale.x < 0.05f)
                player.transform.localScale = Vector3.one;

            // Make it obviously controllable (high-contrast tint).
            sr.color = new Color(0.25f, 0.9f, 1f, 1f);
        }

        private static Sprite CreateSolidSprite()
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            tex.filterMode = FilterMode.Point;
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), pixelsPerUnit: 1f);
        }

        private static void EnsureSpawner(Transform root, GameManager gm, Camera cam)
        {
            var spawnerGo = GameObject.Find(SpawnerName);
            if (spawnerGo == null)
            {
                spawnerGo = new GameObject(SpawnerName);
                spawnerGo.transform.SetParent(root, worldPositionStays: true);
            }

            var spawner = spawnerGo.GetComponent<HazardSpawner>();
            if (spawner == null) spawner = spawnerGo.AddComponent<HazardSpawner>();
            spawner.Bind(gm, cam);
        }

        private static void EnsureBgMusic(Transform root)
        {
            // Prefer an existing scene object if the user already added one.
            var go = GameObject.Find("bgmusic") ?? GameObject.Find(BgMusicName);
            if (go == null)
            {
                go = new GameObject(BgMusicName);
                go.transform.SetParent(root, worldPositionStays: true);
                go.transform.position = Vector3.zero;
            }

            var src = go.GetComponent<AudioSource>();
            if (src == null) src = go.AddComponent<AudioSource>();

            src.playOnAwake = true;
            src.loop = true;
            src.spatialBlend = 0f; // 2D
            src.volume = 0.22f; // keep SFX above music

            if (src.clip == null)
                src.clip = TryLoadMusicClip();

            if (src.clip != null && !src.isPlaying)
                src.Play();
        }

        private static AudioClip TryLoadMusicClip()
        {
            // Build-friendly if you move the mp3 to Assets/Resources/Audio/ and name it without extension.
            var clip = Resources.Load<AudioClip>("Audio/ihatetuesdays-jungle-ish-beat-for-video-games-314073");
            if (clip != null) return clip;

#if UNITY_EDITOR
            // Editor fallback: load directly from Assets/Audio.
            const string path = "Assets/Audio/ihatetuesdays-jungle-ish-beat-for-video-games-314073.mp3";
            clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip != null) return clip;
#endif

            return null;
        }
    }
}

