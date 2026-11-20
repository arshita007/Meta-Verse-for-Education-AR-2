using UnityEngine;

namespace VipAr.UI
{
    /// <summary>
    /// Keeps a panel near where you're looking without jittering with every head movement: it
    /// stays put until you look well away, then glides back into view.
    /// </summary>
    public sealed class HeadFollower : MonoBehaviour
    {
        [Tooltip("Position relative to the head: x = right, y = up, z = forward, in meters. Uses heading only, so looking down at your hands doesn't drag the panel down.")]
        public Vector3 Offset = new(0f, -0.05f, 0.65f);
        [Tooltip("How far you can look away, in degrees, before the panel follows.")]
        public float FollowAngle = 32f;
        public float FollowSpeed = 3.5f;

        private bool m_following;
        private bool m_placed;

        /// <summary>Jump straight to the target, e.g. when the panel is first shown.</summary>
        public void SnapNow() => m_placed = false;

        private void OnEnable() => m_placed = false;

        private void LateUpdate()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            var head = camera.transform;
            var heading = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (heading.sqrMagnitude < 0.0001f)
            {
                heading = Vector3.ProjectOnPlane(head.up, Vector3.up);
            }
            heading.Normalize();
            var target = head.position + Quaternion.LookRotation(heading, Vector3.up) * Offset;

            if (!m_placed)
            {
                transform.position = target;
                m_placed = true;
            }
            else
            {
                var expected = Vector3.ProjectOnPlane(target - head.position, Vector3.up);
                var actual = Vector3.ProjectOnPlane(transform.position - head.position, Vector3.up);
                bool tooFarAround = Vector3.Angle(expected, actual) > FollowAngle;
                bool tooFarAway = Mathf.Abs((transform.position - head.position).magnitude - Offset.magnitude) > 0.35f;
                if (tooFarAround || tooFarAway)
                {
                    m_following = true;
                }

                if (m_following)
                {
                    transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-FollowSpeed * Time.deltaTime));
                    if (Vector3.Distance(transform.position, target) < 0.02f)
                    {
                        m_following = false;
                    }
                }
            }

            // Face the viewer, staying upright.
            var away = transform.position - head.position;
            if (away.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(away, Vector3.up);
            }
        }
    }
}
