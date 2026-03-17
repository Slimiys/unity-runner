using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Камера, следующая за целью с фиксированным смещением.
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        /// <summary>
        /// Цель слежения камеры.
        /// </summary>
        [SerializeField]
        private Transform _target;

        /// <summary>
        /// Смещение камеры относительно цели.
        /// </summary>
        [SerializeField]
        private Vector3 _offset = new(0f, 5f, -7f);

        private void LateUpdate()
        {
            if (_target == null)
            {
                return;
            }

            transform.position = _target.position + _offset;
            transform.LookAt(_target);
        }
    }
}