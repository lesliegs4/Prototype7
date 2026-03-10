using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Prototype7
{
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class PlayerMovement : MonoBehaviour
    {
        [SerializeField] private float moveSpeedUnitsPerSecond = 8.5f;
        [SerializeField] private float horizontalPadding = 0.2f;
        [SerializeField] private float targetScreenPixels = 80f;

        [Header("Sprites (Inspector wiring)")]
        [SerializeField] private Sprite dinoLeftSprite;
        [SerializeField] private Sprite dinoRightSprite;

        [Header("Collision (tuning)")]
        [SerializeField, Range(0.1f, 1f)] private float colliderWidthFraction = 0.45f;
        [SerializeField, Range(0.1f, 1f)] private float colliderHeightFraction = 0.70f;
        [SerializeField, Range(-0.5f, 0.5f)] private float colliderCenterYOffsetFraction = -0.08f;
        [SerializeField, Range(0f, 40f)] private float colliderExtraLeftPixels = 12f;
        [SerializeField, Range(0f, 40f)] private float colliderFacingShiftPixels = 12f;

        private GameManager _gm;
        private Camera _cam;
        private Rigidbody2D _rb;
        private Collider2D _col;
        private SpriteRenderer _sr;

        private Sprite _defaultSprite;
        private int _facing = 1; // -1 left, +1 right
        private Vector3 _baseScale = Vector3.one;

        private float _winWobbleT;

        public void Bind(GameManager gm, Camera cam, Rigidbody2D rb, Collider2D col)
        {
            _gm = gm;
            _cam = cam;
            _rb = rb;
            _col = col;

            if (_sr == null) _sr = GetComponent<SpriteRenderer>();
            if (_sr != null) _sr.sortingOrder = 10;
            if (_sr != null && _defaultSprite == null) _defaultSprite = _sr.sprite;
            _baseScale = transform.localScale;

            // Ensure consistent "only horizontal movement" behavior regardless of scene setup.
            // The auto-bootstrapper used a kinematic body with no gravity; keep the same here.
            if (_rb != null)
            {
                _rb.bodyType = RigidbodyType2D.Kinematic;
                _rb.gravityScale = 0f;
                _rb.freezeRotation = true;
            }

            TryAutoLoadDinoSprites();

            // If nothing is assigned on the SpriteRenderer, prefer real art if provided.
            if (_sr != null && _sr.sprite == null)
                _defaultSprite = dinoRightSprite != null ? dinoRightSprite : dinoLeftSprite;

            ApplyFacingSprite(force: true);

            // Restore the original placement behavior (bootstrapper put the player near bottom-center).
            if (_cam != null)
            {
                var bottomY = _cam.transform.position.y - _cam.orthographicSize;
                var safeY = bottomY + 0.9f;
                var p = transform.position;
                transform.position = new Vector3(_cam.transform.position.x, safeY, p.z);
            }

            UpdatePlayerCollider();
        }

        private void Update()
        {
            if (IsEscapePressed())
                QuitGame();
        }

        private void Reset()
        {
            _rb = GetComponent<Rigidbody2D>();
            _col = GetComponent<Collider2D>();
            _sr = GetComponent<SpriteRenderer>();
        }

        private void FixedUpdate()
        {
            if (_gm == null || _cam == null || _rb == null) return;

            if (_gm.State == RunState.Won)
            {
                _winWobbleT += Time.fixedDeltaTime * 10f;
                var s = 1f + Mathf.Sin(_winWobbleT) * 0.05f;
                transform.localScale = new Vector3(_baseScale.x * s, _baseScale.y * (1f / s), _baseScale.z);
                _rb.linearVelocity = Vector2.zero;
                return;
            }

            if (_gm.State != RunState.Playing)
            {
                _rb.linearVelocity = Vector2.zero;
                return;
            }

            var left = IsLeftPressed();
            var right = IsRightPressed();
            float axis = 0f;
            if (left) axis -= 1f;
            if (right) axis += 1f;

            if (axis < 0f) _facing = -1;
            else if (axis > 0f) _facing = 1;
            ApplyFacingSprite(force: false);

            var vel = new Vector2(axis * moveSpeedUnitsPerSecond, 0f);
            _rb.linearVelocity = vel;

            // Constrain within the camera view.
            var pos = _rb.position;
            var halfWidth = GetHalfWidthWorld();
            var camHalfHeight = _cam.orthographicSize;
            var camHalfWidth = camHalfHeight * _cam.aspect;
            var minX = _cam.transform.position.x - camHalfWidth + halfWidth + horizontalPadding;
            var maxX = _cam.transform.position.x + camHalfWidth - halfWidth - horizontalPadding;
            pos.x = Mathf.Clamp(pos.x, minX, maxX);
            _rb.position = pos;
        }

        private float GetHalfWidthWorld()
        {
            if (_col == null) return 0.25f;
            return _col.bounds.extents.x;
        }

        private static bool IsLeftPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb.leftArrowKey.isPressed;
#else
            return Input.GetKey(KeyCode.LeftArrow);
#endif
        }

        private static bool IsRightPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb.rightArrowKey.isPressed;
#else
            return Input.GetKey(KeyCode.RightArrow);
#endif
        }

        private static bool IsEscapePressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        private static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void ApplyFacingSprite(bool force)
        {
            if (_sr == null) return;

            var target =
                _facing < 0 ? (dinoLeftSprite != null ? dinoLeftSprite : _defaultSprite) :
                (dinoRightSprite != null ? dinoRightSprite : _defaultSprite);

            if (!force && _sr.sprite == target) return;
            if (target == null) return;

            _sr.sprite = target;

            // If we have real art, don't tint it with the prototype cyan.
            if (dinoLeftSprite != null || dinoRightSprite != null)
                _sr.color = Color.white;

            FitSpriteToScreenPixels(target);

            // Keep collider aligned to current sprite + facing direction.
            UpdatePlayerCollider();
        }

        private void UpdatePlayerCollider()
        {
            // The BoxCollider2D's "center" is determined by its offset (local space).
            // We recompute it when the sprite or facing changes.
            if (_sr == null || _sr.sprite == null) return;

            var bodyBox = _col as BoxCollider2D;
            if (bodyBox == null) bodyBox = GetComponent<BoxCollider2D>();
            if (bodyBox == null) bodyBox = gameObject.AddComponent<BoxCollider2D>();

            // Disable any other collider type so only this hitbox is used.
            if (_col != null && _col != bodyBox) _col.enabled = false;
            _col = bodyBox;

            bodyBox.isTrigger = false;

            var s = _sr.sprite.bounds.size;
            var w = Mathf.Max(0.01f, s.x * colliderWidthFraction) + 2f;
            var h = Mathf.Max(0.01f, s.y * colliderHeightFraction);

            var ppu = Mathf.Max(0.001f, _sr.sprite.pixelsPerUnit);
            var extraLeftLocal = colliderExtraLeftPixels / ppu;
            bodyBox.size = new Vector2(w + extraLeftLocal, h);

            var facingShiftLocal = colliderFacingShiftPixels / ppu;
            var facingDir = _facing < 0 ? -1f : 1f;

            var offsetX = (-extraLeftLocal * 0.5f) + (facingShiftLocal * facingDir);
            var offsetY = s.y * colliderCenterYOffsetFraction;
            // Keep the current left-facing placement, but nudge the hitbox right when facing right.
            var rightFacingExtra = _facing > 0 ? 2.1f : 0f;
            bodyBox.offset = new Vector2((offsetX - 1.1f) + rightFacingExtra, offsetY);
        }

        private void FitSpriteToScreenPixels(Sprite sprite)
        {
            if (_cam == null || sprite == null) return;
            if (targetScreenPixels <= 0f) return;
            if (Screen.height <= 0) return;

            var size = sprite.bounds.size;
            var side = Mathf.Max(size.x, size.y);
            if (side <= 0f) return;

            // Convert desired on-screen pixels to world units for an orthographic camera:
            // worldUnitsPerPixel = (2 * orthoSize) / screenHeightPixels
            var worldUnitsPerPixel = (2f * _cam.orthographicSize) / Screen.height;
            var desiredWorldSide = targetScreenPixels * worldUnitsPerPixel;

            var factor = desiredWorldSide / side;
            if (factor <= 0f) return;

            _baseScale = new Vector3(factor, factor, 1f);
            transform.localScale = _baseScale;
        }

        private void TryAutoLoadDinoSprites()
        {
            if (dinoLeftSprite != null && dinoRightSprite != null) return;

#if UNITY_EDITOR
            dinoLeftSprite ??= EditorOnly_LoadBestSpriteAtPath("Assets/Sprites/dinoLeft.png");
            dinoRightSprite ??= EditorOnly_LoadBestSpriteAtPath("Assets/Sprites/dinoRight.png");
#endif
        }

#if UNITY_EDITOR
        private static Sprite EditorOnly_LoadBestSpriteAtPath(string path)
        {
            var all = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path);
            Sprite best = null;
            var bestArea = -1f;

            foreach (var a in all)
            {
                if (a is not Sprite s) continue;
                var r = s.rect;
                var area = r.width * r.height;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = s;
                }
            }

            return best;
        }
#endif
    }
}

