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

            var sr = go.AddComponent<SpriteRenderer>();
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

            // Drift + tilt range are used inside Hazard.Init to lock a readable trajectory (tilt matches travel direction).
            hazard.Init(_gm, _cam, _hazardFrames, s.fallSpeed, s.driftAbsMax, s.spinAbsMaxDeg, s.scale);

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
                    spawnIntervalMin = 0.85f,
                    spawnIntervalMax = 1.25f,
                    // ~30% smaller than before, and clearly smaller than phase 2.
                    scale = 0.42f,
                    driftAbsMax = 0.35f,
                    spinAbsMaxDeg = 45f,
                    burstMin = 1,
                    burstMax = 1,
                },
                EruptionPhase.Phase2 => new PhaseSettings
                {
                    fallSpeed = 4.2f,
                    // Phase 2 now uses Phase 1's spawn rate.
                    spawnIntervalMin = 0.85f,
                    spawnIntervalMax = 1.25f,
                    scale = 0.82f,
                    driftAbsMax = 0.55f,
                    spinAbsMaxDeg = 75f,
                    burstMin = 1,
                    burstMax = 1,
                },
                _ => new PhaseSettings
                {
                    fallSpeed = 6.6f,
                    // Phase 3 now uses Phase 2's spawn rate.
                    spawnIntervalMin = 0.45f,
                    spawnIntervalMax = 0.75f,
                    scale = 1.05f,
                    driftAbsMax = 0.85f,
                    spinAbsMaxDeg = 120f,
                    burstMin = 1,
                    burstMax = 1,
                },
            };
        }

        private void TryAutoLoadHazardFrames()
        {
            if (_hazardFrames != null && _hazardFrames.Length > 0) return;

#if UNITY_EDITOR
            _hazardFrames = EditorOnly_LoadSootFrames();
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
        private static Sprite[] EditorOnly_LoadSootFrames()
        {
            // We pick one "main" sprite from each soot*.png (best-fit) to form an 8-frame loop.
            // This avoids manual inspector wiring while still using the provided assets.
            var paths = new List<string>
            {
                "Assets/Sprites/soot/soot1.png",
                "Assets/Sprites/soot/soot2.png",
                "Assets/Sprites/soot/soot3.png",
                "Assets/Sprites/soot/soot4.png",
                "Assets/Sprites/soot/soot5.png",
                "Assets/Sprites/soot/soot6.png",
                "Assets/Sprites/soot/soot7.png",
                "Assets/Sprites/soot/soot8.png",
            };

            var frames = new List<Sprite>(capacity: paths.Count);
            foreach (var path in paths)
            {
                var all = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path);
                Sprite best = null;
                var bestScore = float.NegativeInfinity;

                foreach (var a in all)
                {
                    if (a is not Sprite s) continue;
                    var r = s.rect;
                    if (r.width < 24 || r.height < 24) continue;
                    if (r.width > 1400 || r.height > 1400) continue; // ignore huge background-ish slices

                    // Prefer roughly "hazard-ish" sizes.
                    var area = r.width * r.height;
                    var sizeBias =
                        (r.width >= 40 && r.width <= 90 ? 1.2f : 1f) *
                        (r.height >= 55 && r.height <= 120 ? 1.2f : 1f);

                    var score = area * sizeBias;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = s;
                    }
                }

                if (best != null)
                    frames.Add(best);
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
            public float driftAbsMax;
            public float spinAbsMaxDeg;
            public int burstMin;
            public int burstMax;
        }
    }
}

