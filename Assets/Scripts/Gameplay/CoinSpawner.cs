using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Генерирует монетки на уровне в случайных позициях в заданном диапазоне.
    /// </summary>
    public class CoinSpawner : MonoBehaviour
    {
        /// <summary>
        /// Префаб монетки для спавна.
        /// </summary>
        [SerializeField]
        private GameObject _coinPrefab;

        /// <summary>
        /// Количество монеток, которые нужно сгенерировать.
        /// </summary>
        [SerializeField]
        private int _coinCount = 10;

        /// <summary>
        /// Минимальное и максимальное значение позиции X для спавна монеток.
        /// </summary>
        [SerializeField]
        private float _minX = -2f;

        [SerializeField]
        private float _maxX = 2f;

        /// <summary>
        /// Минимальное и максимальное значение позиции Z для спавна монеток.
        /// </summary>
        [SerializeField]
        private float _minZ = 5f;

        [SerializeField]
        private float _maxZ = 45f;

        /// <summary>
        /// Минимальное расстояние между монетками по оси Z.
        /// </summary>
        [SerializeField]
        private float _minZSpacing = 2f;

        /// <summary>
        /// Высота над уровнем земли, на которой появляются монетки.
        /// </summary>
        [SerializeField]
        private float _spawnHeight = 1f;

        /// <summary>
        /// Базовый угол поворота монетки при спавне (в градусах, по Эйлеру).
        /// </summary>
        [SerializeField]
        private Vector3 _baseRotationEuler = new(0f, 0f, 0f);

        /// <summary>
        /// Максимальное отклонение угла поворота монетки по каждой оси (в градусах).
        /// </summary>
        [SerializeField]
        private Vector3 _rotationSpreadEuler = new(0f, 180f, 0f);

        /// <summary>
        /// Цвет гизма, отображающего область спавна монеток.
        /// </summary>
        [SerializeField]
        private Color _gizmoColor = new(1f, 0.84f, 0f, 0.3f);

        private void Start()
        {
            if (_coinPrefab == null)
            {
                return;
            }

            var lastSpawnedZ = float.NegativeInfinity;

            for (var i = 0; i < _coinCount; i++)
            {
                var position = GetRandomPositionWithSpacing(ref lastSpawnedZ);
                var rotation = GetRandomRotation();
                Instantiate(_coinPrefab, position, rotation, transform);
            }
        }

        private Vector3 GetRandomPosition()
        {
            var x = Random.Range(_minX, _maxX);
            var z = Random.Range(_minZ, _maxZ);
            var y = _spawnHeight;

            var localPosition = new Vector3(x, y, z);
            return transform.TransformPoint(localPosition);
        }

        private Vector3 GetRandomPositionWithSpacing(ref float lastSpawnedZ)
        {
            // Пытаемся несколько раз найти позицию, которая соблюдает минимальный интервал по Z.
            const int maxAttempts = 10;
            var attempt = 0;

            var position = GetRandomPosition();
            while (attempt < maxAttempts && Mathf.Abs(position.z - lastSpawnedZ) < _minZSpacing)
            {
                position = GetRandomPosition();
                attempt++;
            }

            lastSpawnedZ = position.z;
            return position;
        }

        private Quaternion GetRandomRotation()
        {
            var randomOffset = new Vector3(
                Random.Range(-_rotationSpreadEuler.x, _rotationSpreadEuler.x),
                Random.Range(-_rotationSpreadEuler.y, _rotationSpreadEuler.y),
                Random.Range(-_rotationSpreadEuler.z, _rotationSpreadEuler.z));

            var finalEuler = _baseRotationEuler + randomOffset;
            return Quaternion.Euler(finalEuler);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = _gizmoColor;

            var localCenter = new Vector3(
                (_minX + _maxX) * 0.5f,
                _spawnHeight,
                (_minZ + _maxZ) * 0.5f);

            var center = transform.TransformPoint(localCenter);

            var size = new Vector3(
                Mathf.Abs(_maxX - _minX),
                0.2f,
                Mathf.Abs(_maxZ - _minZ));

            Gizmos.DrawWireCube(center, size);
        }
    }
}

