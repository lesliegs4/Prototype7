using UnityEngine;

namespace Prototype7
{
    /// <summary>
    /// Stores a collider "fit" as normalized ratios relative to a sprite's bounds,
    /// so we can apply the same hit shape across different soot frames/sizes.
    /// </summary>
    public static class HazardColliderTemplate
    {
        private static bool _hasTemplate;
        private static Vector2 _offsetNormalized;
        private static float _radiusNormalizedToMinBounds;
        private static float _templateMinBounds;

        public static bool HasTemplate => _hasTemplate;

        public static void SetFrom(CircleCollider2D collider, Sprite sprite)
        {
            if (collider == null || sprite == null) return;

            var size = sprite.bounds.size;
            if (size.x <= 0f || size.y <= 0f) return;

            _offsetNormalized = new Vector2(
                collider.offset.x / size.x,
                collider.offset.y / size.y
            );

            var min = Mathf.Min(size.x, size.y);
            if (min <= 0f) return;

            _radiusNormalizedToMinBounds = collider.radius / min;
            _templateMinBounds = min;
            _hasTemplate = true;
        }

        public static void ApplyTo(CircleCollider2D collider, Sprite sprite)
        {
            if (!_hasTemplate || collider == null || sprite == null) return;

            var size = sprite.bounds.size;
            if (size.x <= 0f || size.y <= 0f) return;

            collider.offset = new Vector2(
                _offsetNormalized.x * size.x,
                _offsetNormalized.y * size.y
            );

            var min = Mathf.Min(size.x, size.y);
            if (min <= 0f) return;

            // Base: same normalized "bottom circle" fit as soot1.
            var radius = _radiusNormalizedToMinBounds * min;

            // If this frame is larger than the template, expand the circle outward by:
            // Δr = r_newSprite - r_oldSprite = (min_new - min_old) / 2
            // (center/offset stays the same, so it grows equally in all directions).
            if (_templateMinBounds > 0f)
            {
                var deltaRadius = (min - _templateMinBounds) * 0.5f;
                if (deltaRadius > 0f) radius += deltaRadius;
            }

            collider.radius = radius;
        }
    }
}

