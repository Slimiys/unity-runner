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
        /// Смещение камеры относительно цели в её локальных координатах.
        /// </summary>
        /// <remarks>
        /// X — вбок от цели, Y — вверх, Z — назад/вперёд вдоль направления цели.
        /// Например, (0, 5, -7) означает "на 5 единиц выше и на 7 позади".
        /// </remarks>
        [SerializeField]
        private Vector3 _offset = new(0f, 5f, -7f);

        /// <summary>
        /// Дополнительный поворот камеры (в градусах по осям X/Y/Z) после наведения на цель.
        /// </summary>
        [SerializeField]
        private Vector3 _rotationOffsetDegrees;

        private void LateUpdate()
        {
            if (_target == null)
            {
                return;
            }

            // Позиционируем камеру в локальной системе координат цели,
            // чтобы она "держалась" за спиной игрока даже на поворотах трассы.
            var desiredPosition = _target.TransformPoint(_offset);
            transform.position = desiredPosition;

            // Смотрим на цель и даём возможность слегка скорректировать угол через RotationOffsetDegrees.
            transform.LookAt(_target.position);
            transform.rotation = Quaternion.Euler(transform.eulerAngles + _rotationOffsetDegrees);
        }
    }
}