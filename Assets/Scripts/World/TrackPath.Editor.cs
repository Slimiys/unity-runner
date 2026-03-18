using System.Collections.Generic;
using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Editor-инструменты для <see cref="TrackPath"/> (генерация/очистка точек и обработка изменений в инспекторе).
    /// </summary>
    public partial class TrackPath
    {
#if UNITY_EDITOR
        /// <summary>
        /// Количество точек, которые будут сгенерированы в редакторе (включая стартовую).
        /// </summary>
        [SerializeField]
        [Min(2)]
        private int _editorPointCount = 10;

        /// <summary>
        /// Seed генерации для воспроизводимости результата в редакторе.
        /// </summary>
        [SerializeField]
        private int _editorSeed = 12345;

        /// <summary>
        /// Если включено, при генерации сначала удаляются старые точки‑дети.
        /// </summary>
        [SerializeField]
        private bool _editorReplaceExistingPoints = true;

        // Защита от рекурсии: генерация создаёт/удаляет объекты и может повторно триггерить OnValidate.
        private bool _isEditorAutoGenerating;

        // Храним "снимок" editor-настроек, чтобы при изменении параметров пересоздавать дочерние точки.
        private int _lastEditorPointsConfigHash = int.MinValue;

        // Чтобы не выполнять DestroyImmediate внутри OnValidate (Unity это запрещает),
        // планируем пересоздание точек на следующий редакторский тики через delayCall.
        private bool _editorPointsRebuildScheduled;

        private void OnValidate()
        {
            if (_isEditorAutoGenerating)
            {
                BuildFromSerializedPoints();
                return;
            }

            // Если включена динамическая генерация — в редакторе хотим, чтобы путь/точки
            // соответствовали текущим editor-настройкам (особенно когда меняется Editor Point Count).
            if (_dynamicGeneration)
            {
                var desiredCount = Mathf.Max(2, _editorPointCount);
                var currentHash = ComputeEditorPointsConfigHash(desiredCount);

                // Обновляем иерархию, если:
                // - менялись editor-настройки (seed/count/параметры генерации),
                // - или текущее число сериализованных точек не соответствует desiredCount.
                var currentPointsCount = _points != null ? _points.Length : 0;
                if (_lastEditorPointsConfigHash != currentHash || currentPointsCount != desiredCount)
                {
                    ScheduleEditorPointsRebuild(currentHash);
                    return;
                }
            }

            BuildFromSerializedPoints();
        }

        private void ScheduleEditorPointsRebuild(int desiredConfigHash)
        {
            if (_editorPointsRebuildScheduled)
            {
                return;
            }

            _editorPointsRebuildScheduled = true;
            _lastEditorPointsConfigHash = desiredConfigHash;

            UnityEditor.EditorApplication.delayCall += () =>
            {
                _editorPointsRebuildScheduled = false;

                // Компонент мог быть удалён/деактивирован.
                if (this == null)
                {
                    return;
                }

                _isEditorAutoGenerating = true;
                try
                {
                    GeneratePointsInEditor();
                }
                finally
                {
                    _isEditorAutoGenerating = false;
                }
            };
        }

        /// <summary>
        /// Генерирует набор точек пути в редакторе и записывает их в массив <see cref="_points"/>.
        /// </summary>
        /// <remarks>
        /// Генерация создаёт дочерние объекты‑waypoints под объектом с <see cref="TrackPath"/>,
        /// чтобы их можно было визуально редактировать руками (перемещать/удалять/добавлять).
        /// </remarks>
        public void GeneratePointsInEditor()
        {
            var count = Mathf.Max(2, _editorPointCount);

            // Если пользователь меняет количество точек, нужно гарантировать,
            // что иерархия будет "поджата/расправлена" до нового значения.
            // Поэтому очищаем, если:
            // - явно включён ReplaceExistingPoints, либо
            // - текущее число точек отличается от желаемого.
            var currentCount = _points != null ? _points.Length : 0;
            if (_editorReplaceExistingPoints || currentCount != count)
            {
                ClearPointsInEditor();
            }

            var created = new List<Transform>(count);

            // Делаем результат воспроизводимым.
            var previousState = UnityEngine.Random.state;
            UnityEngine.Random.InitState(_editorSeed);

            try
            {
                var origin = transform.position;
                var forward = transform.forward.sqrMagnitude > 0.001f ? transform.forward.normalized : Vector3.forward;

                var current = origin;
                created.Add(CreatePointTransform(0, current));

                for (var i = 1; i < count; i++)
                {
                    if (UnityEngine.Random.value < _turnChance)
                    {
                        var angle = UnityEngine.Random.Range(-_maxTurnAngleDegrees, _maxTurnAngleDegrees);
                        forward = (Quaternion.AngleAxis(angle, Vector3.up) * forward).normalized;
                    }

                    current += forward * _segmentLength;
                    created.Add(CreatePointTransform(i, current));
                }
            }
            finally
            {
                UnityEngine.Random.state = previousState;
            }

            _points = created.ToArray();
            BuildFromSerializedPoints();
            BumpPathVersion();
            _runtimeVersion = 0;
        }

        private int ComputeEditorPointsConfigHash(int desiredCount)
        {
            unchecked
            {
                var hash = 17;
                hash = (hash * 31) + desiredCount;
                hash = (hash * 31) + _editorSeed;
                hash = (hash * 31) + (_editorReplaceExistingPoints ? 1 : 0);

                hash = (hash * 31) + Mathf.RoundToInt(_segmentLength * 1000f);
                hash = (hash * 31) + Mathf.RoundToInt(_turnChance * 100000f);
                hash = (hash * 31) + Mathf.RoundToInt(_maxTurnAngleDegrees * 1000f);

                // Для воспроизводимости: учитываем положение/ориентацию объекта TrackPath.
                hash = (hash * 31) + Mathf.RoundToInt(transform.position.x * 1000f);
                hash = (hash * 31) + Mathf.RoundToInt(transform.position.z * 1000f);
                hash = (hash * 31) + Mathf.RoundToInt(transform.forward.x * 10000f);
                hash = (hash * 31) + Mathf.RoundToInt(transform.forward.z * 10000f);

                return hash;
            }
        }

        /// <summary>
        /// Удаляет все дочерние точки пути, созданные для редактора, и очищает массив <see cref="_points"/>.
        /// </summary>
        public void ClearPointsInEditor()
        {
            // Удаляем всех детей объекта (предполагаем, что TrackPath используется только для точек).
            // Если захочешь хранить под TrackPath другие объекты — лучше сделать отдельный контейнер.
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child != null)
                {
                    // Важно: DestroyImmediate выполняется НЕ из OnValidate, а из delayCall,
                    // поэтому это допустимо и позволяет гарантированно обновить иерархию.
                    DestroyImmediate(child.gameObject);
                }
            }

            _points = System.Array.Empty<Transform>();
            BuildFromSerializedPoints();
            BumpPathVersion();
            _runtimeVersion = 0;
        }

        private Transform CreatePointTransform(int index, Vector3 position)
        {
            var go = new GameObject($"Point_{index:00}");
            go.transform.SetParent(transform, worldPositionStays: true);
            go.transform.position = position;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go.transform;
        }
#endif
    }
}

