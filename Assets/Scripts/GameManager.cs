using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Prototype7
{
    public enum RunState
    {
        Idle = 0,
        Playing = 1,
        Won = 2,
        Lost = 3,
    }

    public enum EruptionPhase
    {
        Phase1 = 0,
        Phase2 = 1,
        Phase3 = 2,
    }

    public sealed class GameManager : MonoBehaviour
    {
        [Header("Timing")]
        [SerializeField] private float totalDurationSeconds = 60f;
        [SerializeField] private float phaseDurationSeconds = 20f;

        [Header("Restart")]
        [SerializeField] private float restartDelaySeconds = 0.6f;

        [Header("Audio (optional)")]
        [SerializeField] private AudioClip swooshForward;
        [SerializeField] private AudioClip swooshBack;
        [SerializeField] private AudioClip fireballShot;
        [SerializeField] private AudioClip loseClip;
        [SerializeField] private AudioClip winClip;

        [SerializeField, Range(0f, 1f)] private float swooshVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float fireballVolume = 0.765f; // 10% lower than 0.85
        [SerializeField, Range(0f, 1f)] private float loseVolume = 0.80f; // slightly lower
        [SerializeField, Range(0f, 1f)] private float winVolume = 1f;

        [Header("Game Over Shake (optional)")]
        [SerializeField] private float gameOverShakeDurationSeconds = 0.22f;
        [SerializeField] private float gameOverShakeMagnitude = 0.10f;

        private RunState _state = RunState.Idle;
        private float _elapsedUnscaled;

        private SpriteRenderer _background;
        private Sprite _phase1;
        private Sprite _phase2;
        private Sprite _phase3;

        private SpriteRenderer _flash;
        private Coroutine _flashRoutine;

        private AudioSource _sfx;
        private bool _attemptedAutoLoadAudio;

        public RunState State => _state;

        public float ElapsedSecondsUnscaled => _elapsedUnscaled;
        public float RemainingSecondsUnscaled => Mathf.Max(0f, totalDurationSeconds - _elapsedUnscaled);

        public EruptionPhase CurrentPhase
        {
            get
            {
                if (_elapsedUnscaled < phaseDurationSeconds) return EruptionPhase.Phase1;
                if (_elapsedUnscaled < phaseDurationSeconds * 2f) return EruptionPhase.Phase2;
                return EruptionPhase.Phase3;
            }
        }

        public void BindBackground(SpriteRenderer background)
        {
            _background = background;
            TryAutoLoadBackgroundSprites();
        }

        public void BeginRun()
        {
            if (_state == RunState.Playing) return;
            _state = RunState.Playing;
            _elapsedUnscaled = 0f;

            EnsureFlashOverlay();
            SetBackgroundForPhase(EruptionPhase.Phase1, hardSwap: true);
        }

        private void Awake()
        {
            EnsureSfx();
            TryAutoLoadAudioClips();
        }

        private void Update()
        {
            // Restart (R) from any state (useful for quick iteration).
            if (IsRestartPressed())
            {
                ReloadScene();
                return;
            }

            if (_state != RunState.Playing)
            {
                // Keep the end-screen overlay sized correctly if the Game view changes.
                EnsureFlashOverlay();
                FitOverlayToCamera(_flash, Camera.main);
                return;
            }

            // Cheat (C): skip to phase 3 for testing.
            if (IsCheatPhase3Pressed())
            {
                _elapsedUnscaled = phaseDurationSeconds * 2f + 0.01f;
                SetBackgroundForPhase(EruptionPhase.Phase3, hardSwap: false);
            }

            _elapsedUnscaled += Time.unscaledDeltaTime;

            var phase = CurrentPhase;
            SetBackgroundForPhase(phase, hardSwap: false);

            if (_elapsedUnscaled >= totalDurationSeconds)
                Win();
        }

        public void Lose(Vector3 hitWorldPos)
        {
            if (_state != RunState.Playing) return;
            _state = RunState.Lost;

            PlayLoseSfx();

            if (_flashRoutine != null) StopCoroutine(_flashRoutine);
            _flashRoutine = StartCoroutine(LoseSequence(hitWorldPos));
        }

        public void Win()
        {
            if (_state != RunState.Playing) return;
            _state = RunState.Won;

            PlayWinSfx();

            if (_flashRoutine != null) StopCoroutine(_flashRoutine);
            _flashRoutine = StartCoroutine(WinSequence());
        }

        public void PlayRandomSwoosh()
        {
            EnsureSfx();
            TryAutoLoadAudioClips();

            if (_sfx == null) return;
            if (swooshForward == null && swooshBack == null) return;

            var clip = (UnityEngine.Random.value < 0.5f ? swooshForward : swooshBack) ?? (swooshForward != null ? swooshForward : swooshBack);
            if (clip == null) return;
            _sfx.PlayOneShot(clip, swooshVolume);
        }

        public void PlayHazardSpawnSfx(EruptionPhase phase)
        {
            EnsureSfx();
            TryAutoLoadAudioClips();
            if (_sfx == null) return;

            if (phase == EruptionPhase.Phase3)
            {
                if (fireballShot == null) return;
                _sfx.PlayOneShot(fireballShot, fireballVolume);
                return;
            }

            PlayRandomSwoosh();
        }

        private IEnumerator LoseSequence(Vector3 hitWorldPos)
        {
            // Clear, readable loss cue: impact flash then hold a full-screen "game over" tint.
            yield return Flash(new Color(1f, 0.2f, 0.2f, 0.95f), 0.10f);

            EnsureFlashOverlay();
            FitOverlayToCamera(_flash, Camera.main);
            if (_flash != null) _flash.color = new Color(0.75f, 0.05f, 0.05f, 0.92f);

            if (gameOverShakeDurationSeconds > 0f && gameOverShakeMagnitude > 0f)
                yield return ShakeCamera(gameOverShakeDurationSeconds, gameOverShakeMagnitude);

            yield return new WaitForSecondsRealtime(restartDelaySeconds);
        }

        private IEnumerator WinSequence()
        {
            // Clear, readable win cue: brighten + calm fade.
            yield return Flash(new Color(1f, 1f, 1f, 0.65f), 0.18f);
            yield return Flash(new Color(1f, 1f, 1f, 0.25f), 0.20f);
        }

        private void ReloadScene()
        {
            var scene = SceneManager.GetActiveScene();
            // Loading by buildIndex fails if the scene isn't in Build Settings (buildIndex == -1).
            // Loading by path/name works in-editor and in builds.
            if (!string.IsNullOrEmpty(scene.path))
                SceneManager.LoadScene(scene.path);
            else
                SceneManager.LoadScene(scene.name);
        }

        private void EnsureFlashOverlay()
        {
            if (_flash != null) return;

            var go = new GameObject("ScreenFlash");
            go.transform.SetParent(transform, worldPositionStays: true);
            go.transform.position = new Vector3(0f, 0f, -1f);

            _flash = go.AddComponent<SpriteRenderer>();
            _flash.sortingOrder = 10000;
            _flash.sprite = CreateSolidSprite();
            _flash.color = new Color(1f, 1f, 1f, 0f);

            FitOverlayToCamera(_flash, Camera.main);
        }

        private void EnsureSfx()
        {
            if (_sfx != null) return;
            _sfx = GetComponent<AudioSource>();
            if (_sfx == null) _sfx = gameObject.AddComponent<AudioSource>();

            _sfx.playOnAwake = false;
            _sfx.loop = false;
            _sfx.spatialBlend = 0f; // 2D
        }

        private void PlayLoseSfx()
        {
            EnsureSfx();
            TryAutoLoadAudioClips();
            if (_sfx == null || loseClip == null) return;
            _sfx.PlayOneShot(loseClip, loseVolume);
        }

        private void PlayWinSfx()
        {
            EnsureSfx();
            TryAutoLoadAudioClips();
            if (_sfx == null || winClip == null) return;
            _sfx.PlayOneShot(winClip, winVolume);
        }

        private void TryAutoLoadAudioClips()
        {
            if (_attemptedAutoLoadAudio) return;
            _attemptedAutoLoadAudio = true;

            // Build-friendly option: if clips are placed under Assets/Resources/Audio/.
            swooshForward ??= Resources.Load<AudioClip>("Audio/swoosh_forward");
            swooshBack ??= Resources.Load<AudioClip>("Audio/swoosh_back");
            fireballShot ??= Resources.Load<AudioClip>("Audio/fireball_shot");
            loseClip ??= Resources.Load<AudioClip>("Audio/lose");
            winClip ??= Resources.Load<AudioClip>("Audio/player_win");

#if UNITY_EDITOR
            // Editor fallback: load directly from Assets/Audio without manual wiring.
            swooshForward ??= EditorOnly_LoadAudioClipByName("swoosh_forward");
            swooshBack ??= EditorOnly_LoadAudioClipByName("swoosh_back");
            fireballShot ??= EditorOnly_LoadAudioClipByName("fireball_shot");
            loseClip ??= EditorOnly_LoadAudioClipByName("lose");
            winClip ??= EditorOnly_LoadAudioClipByName("player_win");
#endif
        }

        private IEnumerator ShakeCamera(float durationSeconds, float magnitude)
        {
            var cam = Camera.main;
            if (cam == null) yield break;

            var t = 0f;
            var startPos = cam.transform.position;

            while (t < durationSeconds)
            {
                t += Time.unscaledDeltaTime;
                var o = UnityEngine.Random.insideUnitCircle * magnitude;
                cam.transform.position = startPos + new Vector3(o.x, o.y, 0f);

                // Keep the overlay centered on the camera while shaking.
                if (_flash != null)
                    _flash.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, cam.transform.position.z + 1f);

                yield return null;
            }

            cam.transform.position = startPos;
            if (_flash != null)
                _flash.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, cam.transform.position.z + 1f);
        }

#if UNITY_EDITOR
        private static AudioClip EditorOnly_LoadAudioClipByName(string clipNameNoExt)
        {
            var guids = UnityEditor.AssetDatabase.FindAssets($"{clipNameNoExt} t:AudioClip", new[] { "Assets/Audio" });
            if (guids == null || guids.Length == 0) return null;

            foreach (var guid in guids)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null) return clip;
            }

            return null;
        }
#endif

        private static void FitOverlayToCamera(SpriteRenderer sr, Camera cam)
        {
            if (sr == null || cam == null) return;

            var camHeight = cam.orthographicSize * 2f;
            var camWidth = camHeight * cam.aspect;
            sr.transform.localScale = new Vector3(camWidth, camHeight, 1f);
            sr.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, cam.transform.position.z + 1f);
        }

        private static Sprite CreateSolidSprite()
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            tex.filterMode = FilterMode.Point;
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), pixelsPerUnit: 1f);
        }

        private IEnumerator Flash(Color color, float seconds)
        {
            EnsureFlashOverlay();
            if (_flash == null) yield break;

            _flash.color = color;
            yield return new WaitForSecondsRealtime(seconds);
            _flash.color = new Color(color.r, color.g, color.b, 0f);
        }

        private static bool IsRestartPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb.rKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.R);
#endif
        }

        private static bool IsCheatPhase3Pressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb.cKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.C);
#endif
        }

        private void SetBackgroundForPhase(EruptionPhase phase, bool hardSwap)
        {
            if (_background == null) return;

            Sprite target = phase switch
            {
                EruptionPhase.Phase1 => _phase1,
                EruptionPhase.Phase2 => _phase2,
                _ => _phase3
            };

            if (target != null && _background.sprite != target)
            {
                _background.sprite = target;
                // The bootstrapper sizes the background once; if sprite changes, it can drift.
                // Refit cheaply on swap.
                var cam = Camera.main;
                if (cam != null)
                {
                    var camHeight = cam.orthographicSize * 2f;
                    var camWidth = camHeight * cam.aspect;
                    var spriteSize = target.bounds.size;
                    if (spriteSize.x > 0f && spriteSize.y > 0f)
                    {
                        var scale = Mathf.Max(camWidth / spriteSize.x, camHeight / spriteSize.y);
                        _background.transform.localScale = new Vector3(scale, scale, 1f);
                        _background.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 10f);
                    }
                }

                if (!hardSwap)
                {
                    // Subtle phase-change pulse without any text/UI.
                    if (_flashRoutine == null || _state == RunState.Playing)
                        StartCoroutine(Flash(new Color(1f, 1f, 1f, 0.12f), 0.08f));
                }
            }
        }

        private void TryAutoLoadBackgroundSprites()
        {
            if (_phase1 != null && _phase2 != null && _phase3 != null) return;

#if UNITY_EDITOR
            _phase1 ??= TryFindSpriteByGuidOrName(nameContains: "Phase1");
            _phase2 ??= TryFindSpriteByGuidOrName(nameContains: "Phase2");
            _phase3 ??= TryFindSpriteByGuidOrName(nameContains: "Phase3");
#endif
        }

#if UNITY_EDITOR
        private static Sprite TryFindSpriteByGuidOrName(string nameContains)
        {
            // Editor-only: locate sprites without requiring manual inspector wiring.
            var guids = UnityEditor.AssetDatabase.FindAssets($"{nameContains} t:Sprite", new[] { "Assets/Sprites" });
            foreach (var guid in guids)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null) return sprite;
            }

            return null;
        }
#endif
    }
}

