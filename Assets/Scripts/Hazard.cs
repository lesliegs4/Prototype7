using UnityEngine;

namespace Prototype7
{
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class Hazard : MonoBehaviour
    {
        private const float PixelShrink = 5f;
        private const float OffscreenSafetyPaddingY = 0.05f;
        private const float PreVisibleCounterClockwiseDeg = 45f;
        private const float TravelAngleMinDeg = -60f;
        private const float TravelAngleMaxDeg = 60f;

        [SerializeField] private float animationFps = 12f;

        private GameManager _gm;
        private Camera _cam;
        private SpriteRenderer _sr;
        private Rigidbody2D _rb;

        private Sprite[] _frames;
        private int _frameIndex;
        private float _frameT;

        private Vector2 _velocity;

        private float _baseUniformScale = 1f;

        public void Init(GameManager gm, Camera cam, Sprite[] frames, float fallSpeed, float driftSpeed, float spinDegPerSecond, float uniformScale)
        {
            _gm = gm;
            _cam = cam;
            _frames = frames;

            if (_sr == null) _sr = GetComponent<SpriteRenderer>();
            if (_rb == null) _rb = GetComponent<Rigidbody2D>();

            _baseUniformScale = uniformScale;
            transform.localScale = new Vector3(uniformScale, uniformScale, 1f);
            _sr.sortingOrder = 20;

            if (_frames != null && _frames.Length > 0)
                _sr.sprite = _frames[0];

            ApplySpriteSizingAndCollider(_sr != null ? _sr.sprite : null);

            // Rotate a bit off-screen first (never visible), then choose the final travel angle
            // and keep falling along that direction.
            transform.rotation = Quaternion.Euler(0f, 0f, PreVisibleCounterClockwiseDeg);

            var travelAngleDeg = UnityEngine.Random.Range(TravelAngleMinDeg, TravelAngleMaxDeg);
            var dir = (Vector2)(Quaternion.Euler(0f, 0f, travelAngleDeg) * Vector2.down);
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.down;

            // Keep overall speed consistent across angles.
            _velocity = dir * fallSpeed;

            // Align sprite so its "down" points along the movement direction (trail stays behind).
            transform.rotation = Quaternion.FromToRotation(Vector3.down, new Vector3(dir.x, dir.y, 0f));

            // After final rotation is applied, ensure the whole sprite is still off-screen (prevents visible snap).
            EnsureFullyOffscreenBeforeShowing();
        }

        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            _rb = GetComponent<Rigidbody2D>();
        }

        private void Update()
        {
            if (_gm != null && _gm.State != RunState.Playing)
            {
                // Freeze motion/animation on win/lose for readability.
                if (_rb != null) _rb.linearVelocity = Vector2.zero;
                return;
            }

            Animate();
            Move();
            DespawnIfOffscreen();
        }

        private void Animate()
        {
            if (_frames == null || _frames.Length <= 1) return;

            _frameT += Time.deltaTime * animationFps;
            if (_frameT < 1f) return;
            _frameT -= 1f;

            _frameIndex = (_frameIndex + 1) % _frames.Length;
            _sr.sprite = _frames[_frameIndex];
            ApplySpriteSizingAndCollider(_sr.sprite);
        }

        private void Move()
        {
            var dt = Time.deltaTime;
            var pos = transform.position;
            pos.x += _velocity.x * dt;
            pos.y += _velocity.y * dt;
            transform.position = pos;
        }

        private void ApplySpriteSizingAndCollider(Sprite sprite)
        {
            // Make soot sprites 5 pixels smaller (visual), without modifying source textures.
            var shrinkFactor = GetPixelShrinkFactor(sprite, PixelShrink);
            transform.localScale = new Vector3(_baseUniformScale * shrinkFactor, _baseUniformScale * shrinkFactor, 1f);

            // Fit the "hit area" to the circular bottom portion using the soot1 collider template.
            var circle = GetComponent<CircleCollider2D>();
            if (circle != null)
            {
                circle.isTrigger = true;
                HazardColliderTemplate.ApplyTo(circle, sprite);
            }
        }

        private void EnsureFullyOffscreenBeforeShowing()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;
            if (_sr == null) return;

            // Prevent visible "snap" when we lock rotation: keep the whole sprite above the camera top.
            var camTopY = _cam.transform.position.y + _cam.orthographicSize;
            var extY = Mathf.Max(0.001f, _sr.bounds.extents.y);
            var desiredY = camTopY + extY + OffscreenSafetyPaddingY;

            var p = transform.position;
            if (p.y < desiredY)
            {
                p.y = desiredY;
                transform.position = p;
            }
        }

        private static float GetPixelShrinkFactor(Sprite sprite, float pixels)
        {
            if (sprite == null) return 1f;

            var rect = sprite.rect;
            if (rect.width <= pixels || rect.height <= pixels) return 1f;

            var fx = (rect.width - pixels) / rect.width;
            var fy = (rect.height - pixels) / rect.height;
            return Mathf.Clamp01(Mathf.Min(fx, fy));
        }

        private void DespawnIfOffscreen()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            var bottom = _cam.transform.position.y - _cam.orthographicSize;
            if (transform.position.y < bottom - 2f)
                Destroy(gameObject);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_gm == null || _gm.State != RunState.Playing) return;
            if (!other || other.gameObject == null) return;

            if (other.gameObject.name == "Player" || other.GetComponent<PlayerMovement>() != null)
            {
                _gm.Lose(transform.position);
            }
        }
    }
}

