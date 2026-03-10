using System;
using System.Collections.Generic;
using UnityEngine;

namespace Prototype7
{
    public sealed class HazardSpawner : MonoBehaviour
    {
        [Header("Spawn")]
        [SerializeField] private float spawnTopPadding = 1.5f;
        [SerializeField] private float spawnSidePadding = 0.6f;

        [Header("Hazard Visuals (Inspector wiring)")]
        [Tooltip("Optional: assign 8 sprites (frames) here for builds. If empty, the editor will auto-load from Assets/Sprites/sootNew/.")]
        [SerializeField] private Sprite[] hazardFramesOverride;

        private GameManager _gm;
        private Camera _cam;

        private float _nextSpawnUnscaled;
        private System.Random _rng;

        private Sprite[] _hazardFrames;

        public void Bind(GameManager gm, Camera cam)
        {
            _gm = gm;
            _cam = cam;
            _rng ??= new System.Random();

            if (hazardFramesOverride != null && hazardFramesOverride.Length > 0)
                _hazardFrames = hazardFramesOverride;

            TryAutoLoadHazardFrames();
        }

        private void Update()
        {
            if (_gm == null || _cam == null) return;
            if (_gm.State != RunState.Playing) return;

            var now = Time.unscaledTime;
            if (now < _nextSpawnUnscaled) return;

            var phase = _gm.CurrentPhase;
            var settings = GetSettings(phase);

            // Phase 3: denser bursts.
            var burst = phase == EruptionPhase.Phase3 ? _rng.Next(settings.burstMin, settings.burstMax + 1) : 1;
            for (int i = 0; i < burst; i++)
            {
                SpawnOne(settings, phase, burstIndex: i, burstCount: burst);
            }

            _nextSpawnUnscaled = now + UnityEngine.Random.Range(settings.spawnIntervalMin, settings.spawnIntervalMax);
        }

        private void SpawnOne(PhaseSettings s, EruptionPhase phase, int burstIndex, int burstCount)
        {
            var go = new GameObject("Hazard");
            go.transform.SetParent(transform, worldPositionStays: true);

            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, worldPositionStays: false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;

            var sr = visual.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 20;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.freezeRotation = true;

            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;

            var hazard = go.AddComponent<Hazard>();

            var camHalfH = _cam.orthographicSize;
            var camHalfW = camHalfH * _cam.aspect;
            var left = _cam.transform.position.x - camHalfW + spawnSidePadding;
            var right = _cam.transform.position.x + camHalfW - spawnSidePadding;
            var top = _cam.transform.position.y + camHalfH + spawnTopPadding;

            var x = UnityEngine.Random.Range(left, right);

            // Slight spacing in bursts to communicate "denser groups" without unreadable overlap.
            if (burstCount > 1)
            {
                var spread = 0.65f;
                var t = burstCount <= 1 ? 0.5f : (burstIndex / (float)(burstCount - 1));
                x = Mathf.Clamp(x + (t - 0.5f) * spread, left, right);
            }

            go.transform.position = new Vector3(x, top, 0f);

            hazard.Init(_gm, _cam, sr, _hazardFrames, s.fallSpeed * 1.10f, s.scale);

            // Spawn sound (phase-dependent): swoosh in phase 1/2, fireball in phase 3.
            _gm.PlayHazardSpawnSfx(phase);
        }

        private PhaseSettings GetSettings(EruptionPhase phase)
        {
            return phase switch
            {
                EruptionPhase.Phase1 => new PhaseSettings
                {
                    fallSpeed = 2.9f,
                    spawnIntervalMin = 0.85f * 0.90f,
                    spawnIntervalMax = 1.25f * 0.90f,
                    // ~30% smaller than before, and clearly smaller than phase 2.
                    scale = 0.42f,
                    burstMin = 1,
                    burstMax = 1,
                },
                EruptionPhase.Phase2 => new PhaseSettings
                {
                    fallSpeed = 4.2f,
                    // Phase 2 now uses Phase 1's spawn rate.
                    spawnIntervalMin = 0.85f * 0.90f,
                    spawnIntervalMax = 1.25f * 0.90f,
                    scale = 0.82f,
                    burstMin = 1,
                    burstMax = 1,
                },
                _ => new PhaseSettings
                {
                    fallSpeed = 6.6f,
                    // Phase 3 now uses Phase 2's spawn rate.
                    spawnIntervalMin = 0.45f * 0.90f,
                    spawnIntervalMax = 0.75f * 0.90f,
                    scale = 1.05f,
                    burstMin = 1,
                    burstMax = 1,
                },
            };
        }

        private void TryAutoLoadHazardFrames()
        {
            if (_hazardFrames != null && _hazardFrames.Length > 0) return;

#if UNITY_EDITOR
            _hazardFrames = EditorOnly_LoadSootNewFrames();
#endif
            if (_hazardFrames == null || _hazardFrames.Length == 0)
                _hazardFrames = new[] { CreateSolidSprite(new Color(0.15f, 0.05f, 0.05f, 1f)) };
        }

        private static Sprite CreateSolidSprite(Color color)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            tex.filterMode = FilterMode.Point;
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), pixelsPerUnit: 1f);
        }

#if UNITY_EDITOR
        private static Sprite[] EditorOnly_LoadSootNewFrames()
        {
            // sootNew is authored as 8 separate textures (soot1..soot8).
            // Some of these textures may contain multiple sliced sprites (e.g. trail fragments);
            // choose the "main" sprite from each texture (largest reasonable slice).
            var paths = new List<string>
            {
                "Assets/Sprites/sootNew/soot1 (1).png",
                "Assets/Sprites/sootNew/soot2 (1).png",
                "Assets/Sprites/sootNew/soot3 (1).png",
                "Assets/Sprites/sootNew/soot4 (1).png",
                "Assets/Sprites/sootNew/soot5 (1).png",
                "Assets/Sprites/sootNew/soot6 (1).png",
                "Assets/Sprites/sootNew/soot7 (1).png",
                "Assets/Sprites/sootNew/soot8 (1).png",
            };

            var frames = new List<Sprite>(capacity: paths.Count);
            foreach (var path in paths)
            {
                var all = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path);
                if (all == null || all.Length == 0) continue;

                Sprite best = null;
                var bestArea = -1f;

                foreach (var a in all)
                {
                    if (a is not Sprite s) continue;
                    var r = s.rect;

                    // Filter out tiny fragments from auto-slicing.
                    if (r.width < 32 || r.height < 32) continue;

                    var area = r.width * r.height;
                    if (area > bestArea)
                    {
                        bestArea = area;
                        best = s;
                    }
                }

                if (best != null) frames.Add(best);
            }

            return frames.ToArray();
        }
#endif

        private struct PhaseSettings
        {
            public float fallSpeed;
            public float spawnIntervalMin;
            public float spawnIntervalMax;
            public float scale;
            public int burstMin;
            public int burstMax;
        }
    }
}

