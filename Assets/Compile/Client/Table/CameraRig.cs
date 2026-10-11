using System;
using UnityEngine;

namespace Compile.Client.Table
{
    // A framing of the table: show this rectangle (in table units), seen from a seat tilted by pitch.
    public struct CameraShot
    {
        public Rect area;
        public float pitch;     // degrees from straight down; tilts the view up the table, away from the local player
        public float margin;    // extra space around the area, as a fraction of its size

        public CameraShot(Rect area, float pitch, float margin = 0.08f)
        {
            this.area = area;
            this.pitch = pitch;
            this.margin = margin;
        }
    }

    // Moves the camera between shots. The shot is re-fitted every frame, so resizing or rotating the
    // window (wide vs. tall) keeps the area in view.
    public sealed class CameraRig : MonoBehaviour
    {
        const float TransitionSeconds = 0.75f;

        Camera _camera;
        Func<CameraShot> _shot;
        Vector3 _fromPosition;
        Quaternion _fromRotation;
        float _t = 1f;
        readonly Vector3[] _corners = new Vector3[4];

        public Camera Camera => _camera;

        public void Init(Camera camera) => _camera = camera;

        public void Show(CameraShot shot, bool instant = false) => Show(() => shot, instant);

        // Use a provider when the framed area changes over time, such as a panel that resizes.
        public void Show(Func<CameraShot> shot, bool instant = false)
        {
            _shot = shot;
            _fromPosition = _camera.transform.position;
            _fromRotation = _camera.transform.rotation;
            _t = instant ? 1f : 0f;
            LateUpdate();
        }

        void LateUpdate()
        {
            if (_shot == null || _camera == null)
                return;

            var (position, rotation) = Pose(_shot());
            if (_t < 1f)
            {
                _t = Mathf.Min(1f, _t + Time.unscaledDeltaTime / TransitionSeconds);
                var k = Ease.InOutCubic(_t);
                position = Vector3.Lerp(_fromPosition, position, k);
                rotation = Quaternion.Slerp(_fromRotation, rotation, k);
            }

            _camera.transform.SetPositionAndRotation(position, rotation);
        }

        // Aims at the area's center and backs off until all four corners are on screen (inside the margin).
        // In a tilted view the near edge looks bigger than the far edge, so this is solved, not estimated.
        (Vector3, Quaternion) Pose(CameraShot shot)
        {
            var rotation = Quaternion.Euler(-shot.pitch, 0f, 0f);
            var inverse = Quaternion.Inverse(rotation);
            var forward = rotation * Vector3.forward;
            var center = (Vector3)shot.area.center;

            var tanV = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var tanH = tanV * _camera.aspect;
            var fill = 1f / (1f + shot.margin);
            var corners = _corners;
            corners[0] = new Vector3(shot.area.xMin, shot.area.yMin);
            corners[1] = new Vector3(shot.area.xMax, shot.area.yMin);
            corners[2] = new Vector3(shot.area.xMin, shot.area.yMax);
            corners[3] = new Vector3(shot.area.xMax, shot.area.yMax);

            bool Fits(float distance)
            {
                var position = center - forward * distance;
                foreach (var corner in corners)
                {
                    var v = inverse * (corner - position);
                    if (v.z <= _camera.nearClipPlane || Mathf.Abs(v.x / v.z) > tanH * fill || Mathf.Abs(v.y / v.z) > tanV * fill)
                        return false;
                }
                return true;
            }

            float near = 0.5f, far = 500f;
            for (var i = 0; i < 32; i++)
            {
                var mid = (near + far) * 0.5f;
                if (Fits(mid))
                    far = mid;
                else
                    near = mid;
            }

            return (center - forward * far, rotation);
        }
    }

    public static class Ease
    {
        public static float OutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
        public static float InOutCubic(float t) => t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
    }
}
