using System.Collections.Generic;
using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Динамически создаёт сегменты земли (Ground) вдоль <see cref="TrackPath"/>.
    /// </summary>
    /// <remarks>
    /// Идея: <see cref="TrackPath"/> определяет центральную линию и направление,
    /// а этот компонент отвечает только за визуально‑физическое покрытие (плоскости/сегменты),
    /// чтобы трасса могла быть "бесконечной".
    ///
    /// Ограничения текущей версии:
    /// - сегменты создаются как отдельные префабы и удаляются позади игрока
    /// - под кривизну/уклоны (по Y) мы подстраиваемся только позиционированием по кадру пути
    /// - предполагается, что префаб ориентирован вдоль локальной оси Z (forward)
    /// </remarks>
    [ExecuteAlways]
    public class TrackGroundGenerator : MonoBehaviour
    {
        [SerializeField]
        private TrackPath _trackPath;

        [SerializeField]
        private TrackSettings _trackSettings;

        [SerializeField]
        private Transform _player;

        /// <summary>
        /// Префаб сегмента земли. Должен содержать коллайдер (если нужна физика).
        /// </summary>
        [SerializeField]
        private GameObject _groundSegmentPrefab;

#if UNITY_EDITOR
        /// <summary>
        /// Если включено, сегменты земли будут генерироваться прямо в редакторе (без Play Mode).
        /// </summary>
        [SerializeField]
        private bool _previewInEditor = true;
#endif

        /// <summary>
        /// Длина одного сегмента земли вдоль движения (в мировых единицах).
        /// </summary>
        [SerializeField]
        [Min(0.5f)]
        private float _segmentLength = 8f;

        /// <summary>
        /// Насколько далеко вперёд держать землю от игрока (в единицах вдоль пути).
        /// </summary>
        [SerializeField]
        [Min(5f)]
        private float _generateAheadDistance = 120f;

        /// <summary>
        /// Насколько далеко позади игрока оставлять землю перед удалением.
        /// </summary>
        [SerializeField]
        [Min(0f)]
        private float _trimBehindDistance = 40f;

        /// <summary>
        /// Дополнительная ширина к трассе (слева+справа) в мировых единицах.
        /// </summary>
        [SerializeField]
        [Min(0f)]
        private float _widthPadding = 0.5f;

        /// <summary>
        /// Смещение сегмента по Y относительно точки на пути (например, если путь проходит по центру модели дороги).
        /// </summary>
        [SerializeField]
        private float _yOffset = 0f;

        /// <summary>
        /// Предполагаемый базовый размер префаба по X/Z (в локальных единицах), чтобы корректно масштабировать.
        /// </summary>
        /// <remarks>
        /// Например, стандартный Plane в Unity имеет размер 10x10, а Quad — 1x1.
        /// Укажи здесь "сколько единиц" префаб покрывает без скейла.
        /// </remarks>
        [SerializeField]
        private Vector2 _prefabSizeXZ = new(1f, 1f);

        private readonly List<GroundSegment> _segments = new();
        private float _nextSegmentStartDistance;

#if UNITY_EDITOR
        private const string PreviewRootName = "__GroundPreviewRoot";
        private Transform _previewRoot;
        private int _lastSeenPathVersion = -1;
        private int _lastSeenConfigHash;
        private bool _editorRebuildRequested;
#endif

        private void OnEnable()
        {
            // В редакторе Start не вызывается. Если включён preview — инициализируемся сразу.
            if (!Application.isPlaying)
            {
#if UNITY_EDITOR
                if (_previewInEditor)
                {
                    // Не пересобираем прямо здесь: безопаснее попросить пересборку и выполнить её в editor-Update.
                    _editorRebuildRequested = true;
                }
#endif
                return;
            }
        }

        private void Start()
        {
            if (!ValidateConfiguration(isRuntime: true))
            {
                enabled = false;
                return;
            }

            InitializeFromPlayerPosition();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _ = ValidateConfiguration(isRuntime: false);

            if (!Application.isPlaying && _previewInEditor)
            {
                // В OnValidate не всегда безопасно удалять/создавать объекты в сцене.
                // Поэтому мы только помечаем, что превью надо пересобрать, а реальную работу делаем в Update (ExecuteAlways).
                _editorRebuildRequested = true;
            }
        }
#endif

        private void Update()
        {
#if UNITY_EDITOR
            // В редакторе Update будет вызываться только при ExecuteAlways.
            if (!Application.isPlaying)
            {
                if (!_previewInEditor)
                {
                    return;
                }

                if (!ValidateConfiguration(isRuntime: false))
                {
                    return;
                }

                if (_editorRebuildRequested)
                {
                    _editorRebuildRequested = false;
                    TryInitializeEditorPreview();
                    return;
                }

                // Если менялись параметры GroundGenerator — пересобираем превью земли.
                var configHash = ComputeEditorConfigHash();
                if (_lastSeenConfigHash != configHash)
                {
                    _lastSeenConfigHash = configHash;
                    _editorRebuildRequested = true;
                    return;
                }

                // Если путь пересобрали (нажали "Generate Points" или изменили точки руками) — пересобираем превью земли.
                if (_trackPath != null && _lastSeenPathVersion != _trackPath.PathVersion)
                {
                    _lastSeenPathVersion = _trackPath.PathVersion;
                    _editorRebuildRequested = true;
                }
            }
#endif

            if (_trackPath == null || _trackSettings == null || _player == null || _groundSegmentPrefab == null)
            {
                return;
            }

            if (!_trackPath.IsValidPath)
            {
                return;
            }

            if (!_trackPath.TryGetClosestDistance(_player.position, out var playerDistance))
            {
                return;
            }

            // Если TrackPath в динамическом режиме, он сам поддерживает длину.
            // Здесь мы просто гарантируем, что земля будет создана на нужной дистанции.
            EnsureSegments(playerDistance + _generateAheadDistance);
            TrimSegments(playerDistance - _trimBehindDistance);
        }

        private bool ValidateConfiguration(bool isRuntime)
        {
            if (_trackSettings == null && _trackPath != null)
            {
                _trackSettings = _trackPath.GetComponent<TrackSettings>();
            }

            if (_trackPath == null)
            {
                Log(isRuntime, $"[{nameof(TrackGroundGenerator)}] Не задан {nameof(TrackPath)}. Генерация земли невозможна.");
                return false;
            }

            if (_trackSettings == null)
            {
                Log(isRuntime, $"[{nameof(TrackGroundGenerator)}] Не задан {nameof(TrackSettings)}. Генерация земли невозможна.");
                return false;
            }

            if (_player == null)
            {
                Log(isRuntime, $"[{nameof(TrackGroundGenerator)}] Не задан игрок (поле Player). Генерация земли невозможна.");
                return false;
            }

            if (_groundSegmentPrefab == null)
            {
                Log(isRuntime, $"[{nameof(TrackGroundGenerator)}] Не задан префаб сегмента земли (Ground Segment Prefab).");
                return false;
            }

            _segmentLength = Mathf.Max(0.5f, _segmentLength);
            _generateAheadDistance = Mathf.Max(5f, _generateAheadDistance);
            _trimBehindDistance = Mathf.Max(0f, _trimBehindDistance);
            _prefabSizeXZ.x = Mathf.Max(0.001f, _prefabSizeXZ.x);
            _prefabSizeXZ.y = Mathf.Max(0.001f, _prefabSizeXZ.y);

            if (!_trackPath.IsValidPath)
            {
                // Автоинициализация: создаём первые точки и догенерируем путь до нужной длины.
                var requiredLength = Mathf.Max(_generateAheadDistance + _segmentLength, _segmentLength * 2f);
                if (_trackPath.TryEnsureValidPath(requiredLength))
                {
                    return true;
                }

                Log(isRuntime, $"[{nameof(TrackGroundGenerator)}] {nameof(TrackPath)} задан, но путь невалиден (нужно минимум 2 точки и ненулевая длина).");
                return false;
            }

            return true;
        }

        private void InitializeFromPlayerPosition()
        {
            if (!_trackPath.TryGetClosestDistance(_player.position, out var playerDistance))
            {
                _nextSegmentStartDistance = 0f;
                return;
            }

            // Стартуем чуть позади игрока, чтобы под ногами гарантированно был сегмент.
            _nextSegmentStartDistance = Mathf.Max(0f, playerDistance - _segmentLength);
        }

        private void EnsureSegments(float requiredEndDistance)
        {
            // Делаем сегменты "старт‑энд" длиной SegmentLength вдоль пути.
            // Позицию сегмента берём в середине, чтобы он покрывал участок [start..end].
            while (_nextSegmentStartDistance < requiredEndDistance)
            {
                var start = _nextSegmentStartDistance;
                var end = start + _segmentLength;
                var mid = start + _segmentLength * 0.5f;

                if (!_trackPath.TryEvaluateFrame(mid, out var centerPos, out var forward, out var right))
                {
                    // Если по какой-то причине оценка не прошла — не зависаем в цикле.
                    _nextSegmentStartDistance += _segmentLength;
                    continue;
                }

                var width = GetTrackWidth();
                var position = centerPos + Vector3.up * _yOffset;
                var rotation = Quaternion.LookRotation(forward, Vector3.up);

                var parent = GetSegmentParent();
                var segment = InstantiateSegment(_groundSegmentPrefab, position, rotation, parent);
                if (segment == null)
                {
                    _nextSegmentStartDistance += _segmentLength;
                    continue;
                }

                // Помечаем сегмент, чтобы в редакторе можно было надёжно чистить превью после перекомпиляции.
                var marker = segment.GetComponent<TrackGroundSegmentMarker>();
                if (marker == null)
                {
                    marker = segment.AddComponent<TrackGroundSegmentMarker>();
                }

                marker.StartDistance = start;
                marker.EndDistance = end;

                // Масштабируем по ширине (X) и длине (Z). По Y не трогаем (оставляем как в префабе).
                var scale = segment.transform.localScale;
                scale.x *= width / _prefabSizeXZ.x;
                scale.z *= _segmentLength / _prefabSizeXZ.y;
                segment.transform.localScale = scale;

                // Центр трассы (LaneCenterX) — это смещение по "right" от центральной линии.
                // Это полезно, если визуальная дорога в сцене не совпадает с X=0.
                segment.transform.position += right * _trackSettings.LaneCenterX;

                _segments.Add(new GroundSegment(start, end, segment));
                _nextSegmentStartDistance += _segmentLength;
            }
        }

        private void TrimSegments(float keepFromDistance)
        {
            for (var i = _segments.Count - 1; i >= 0; i--)
            {
                if (_segments[i].EndDistance >= keepFromDistance)
                {
                    continue;
                }

                if (_segments[i].Instance != null)
                {
                    DestroySegment(_segments[i].Instance);
                }

                _segments.RemoveAt(i);
            }
        }

        private float GetTrackWidth()
        {
            // Ширина трассы — это количество полос * ширина полосы.
            // Добавляем небольшой padding, чтобы края не "обрезались".
            var baseWidth = Mathf.Max(0.1f, _trackSettings.LaneCount * _trackSettings.LaneWidth);
            return baseWidth + _widthPadding * 2f;
        }

        private static void Log(bool isRuntime, string message)
        {
            if (isRuntime)
            {
                Debug.LogError(message);
            }
            else
            {
                Debug.LogWarning(message);
            }
        }

#if UNITY_EDITOR
        private void TryInitializeEditorPreview()
        {
            // В редакторе сначала всегда чистим предыдущее превью, чтобы не было накопления.
            EnsurePreviewRoot();
            ClearAllPreviewImmediate();

            // После очистки пробуем валидировать и сгенерировать актуальное состояние.
            if (!ValidateConfiguration(isRuntime: false))
            {
                _segments.Clear();
                _nextSegmentStartDistance = 0f;
                return;
            }

            // Полная пересборка превью: удаляем старые сегменты и генерируем заново от позиции игрока.
            InitializeFromPlayerPosition();

            if (_trackPath.TryGetClosestDistance(_player.position, out var playerDistance))
            {
                EnsureSegments(playerDistance + _generateAheadDistance);
                TrimSegments(playerDistance - _trimBehindDistance);
            }

            if (_trackPath != null)
            {
                _lastSeenPathVersion = _trackPath.PathVersion;
            }

            _lastSeenConfigHash = ComputeEditorConfigHash();
        }

        private int ComputeEditorConfigHash()
        {
            unchecked
            {
                var hash = 17;
                hash = (hash * 31) + (_trackPath != null ? _trackPath.GetInstanceID() : 0);
                hash = (hash * 31) + (_trackSettings != null ? _trackSettings.GetInstanceID() : 0);
                hash = (hash * 31) + (_player != null ? _player.GetInstanceID() : 0);
                hash = (hash * 31) + (_groundSegmentPrefab != null ? _groundSegmentPrefab.GetInstanceID() : 0);

                hash = (hash * 31) + _segmentLength.GetHashCode();
                hash = (hash * 31) + _generateAheadDistance.GetHashCode();
                hash = (hash * 31) + _trimBehindDistance.GetHashCode();
                hash = (hash * 31) + _widthPadding.GetHashCode();
                hash = (hash * 31) + _yOffset.GetHashCode();
                hash = (hash * 31) + _prefabSizeXZ.GetHashCode();

                return hash;
            }
        }

        private void EnsurePreviewRoot()
        {
            if (_previewRoot != null)
            {
                return;
            }

            var existing = transform.Find(PreviewRootName);
            if (existing != null)
            {
                _previewRoot = existing;
                return;
            }

            var go = new GameObject(PreviewRootName);
            go.hideFlags |= HideFlags.DontSaveInEditor;
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            _previewRoot = go.transform;
        }

        private void ClearAllPreviewChildrenImmediate()
        {
            if (_previewRoot == null)
            {
                return;
            }

            // NOTE: метод оставлен для совместимости; используется более жёсткая очистка в ClearAllPreviewImmediate().
            for (var i = _previewRoot.childCount - 1; i >= 0; i--)
            {
                var child = _previewRoot.GetChild(i);
                if (child != null)
                {
                    DestroySegment(child.gameObject);
                }
            }

            _segments.Clear();
            _nextSegmentStartDistance = 0f;
        }

        private void ClearAllPreviewImmediate()
        {
            // 1) Чистим все сегменты в контейнере превью.
            ClearAllPreviewChildrenImmediate();

            // 2) Чистим "наследие" от прошлых версий: сегменты могли создаваться прямо под GroundGenerator.
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                // Не трогаем сам контейнер превью.
                if (_previewRoot != null && child == _previewRoot)
                {
                    continue;
                }

                // Удаляем только то, что мы точно создавали: объекты с маркером.
                if (child.GetComponent<TrackGroundSegmentMarker>() == null)
                {
                    continue;
                }

                DestroySegment(child.gameObject);
            }
        }
#endif

        private Transform GetSegmentParent()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                EnsurePreviewRoot();
                return _previewRoot != null ? _previewRoot : transform;
            }
#endif

            return transform;
        }

        private static GameObject InstantiateSegment(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent)
        {
            if (prefab == null)
            {
                return null;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                // В редакторе у пользователя в поле может оказаться:
                // - prefab-asset (Project) — тогда корректнее InstantiatePrefab
                // - объект из сцены — тогда InstantiatePrefab вернёт null, и нужно делать обычный Instantiate
                GameObject instance = null;

                if (UnityEditor.PrefabUtility.IsPartOfPrefabAsset(prefab))
                {
                    instance = UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
                }

                if (instance == null)
                {
                    instance = Object.Instantiate(prefab, position, rotation, parent);
                    instance.hideFlags |= HideFlags.DontSaveInEditor;
                }

                instance.transform.SetPositionAndRotation(position, rotation);
                return instance;
            }
#endif

            return Object.Instantiate(prefab, position, rotation, parent);
        }

        private static void DestroySegment(GameObject instance)
        {
            if (instance == null)
            {
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                // Через Undo удаление в редакторе надёжнее и корректно обновляет сцену/иерархию.
                UnityEditor.Undo.DestroyObjectImmediate(instance);
                return;
            }
#endif

            Object.Destroy(instance);
        }

        private readonly struct GroundSegment
        {
            public GroundSegment(float startDistance, float endDistance, GameObject instance)
            {
                StartDistance = startDistance;
                EndDistance = endDistance;
                Instance = instance;
            }

            public float StartDistance { get; }
            public float EndDistance { get; }
            public GameObject Instance { get; }
        }
    }
}

