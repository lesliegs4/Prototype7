using UnityEngine;

namespace Prototype7
{
    public sealed class Hazard : MonoBehaviour
    {
        private const float OffscreenSafetyPaddingY = 0.05f;
        private const float TravelAngleMinDeg = -30f;
        private const float TravelAngleMaxDeg = 30f;

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

        public void Init(GameManager gm, Camera cam, SpriteRenderer spriteRenderer, Sprite[] frames, float fallSpeed, float uniformScale)
        {
            _gm = gm;
            _cam = cam;
            _sr = spriteRenderer;
            _frames = frames;

            if (_sr == null) _sr = GetComponentInChildren<SpriteRenderer>();
            if (_rb == null) _rb = GetComponent<Rigidbody2D>();

            _baseUniformScale = uniformScale;
            transform.localScale = new Vector3(uniformScale, uniformScale, 1f);

            if (_frames != null && _frames.Length > 0)
                SetSprite(_frames[0]);

            // Keep the whole sprite off-screen while we lock trajectory/rotation.
            EnsureFullyOffscreenBeforeShowing();

            // Pick a travel angle in [-30, +30] degrees (relative to straight down),
            // then fall along that direction.
            var travelAngleDeg = UnityEngine.Random.Range(TravelAngleMinDeg, TravelAngleMaxDeg);
            var dir = (Vector2)(Quaternion.Euler(0f, 0f, travelAngleDeg) * Vector2.down);
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.down;

            _velocity = dir * fallSpeed;

            // Rotate sprite to match direction of travel (keeps the trail behind).
            transform.rotation = Quaternion.FromToRotation(Vector3.down, new Vector3(dir.x, dir.y, 0f));
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
            while (_frameT >= 1f)
            {
                _frameT -= 1f;
                _frameIndex = (_frameIndex + 1) % _frames.Length;
                SetSprite(_frames[_frameIndex]);
            }
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
            transform.localScale = new Vector3(_baseUniformScale, _baseUniformScale, 1f);

            // Fit the "hit area" to the circular bottom portion using the soot1 collider template.
            var circle = GetComponent<CircleCollider2D>();
            if (circle != null)
            {
                circle.isTrigger = true;
                HazardColliderTemplate.ApplyTo(circle, sprite);
            }
        }

        private void SetSprite(Sprite sprite)
        {
            if (_sr == null || sprite == null) return;
            _sr.sprite = sprite;
            ApplySpriteSizingAndCollider(sprite);

            // sootNew frames are sliced with varying rects and pivots, which can cause visible "jitter".
            // If the renderer is on a child, offset it so the sprite's bounds center stays anchored.
            if (_sr.transform != transform)
            {
                _sr.transform.localPosition = -sprite.bounds.center;
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

