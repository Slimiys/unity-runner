using System.Collections.Generic;
using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Генерирует непрерывную геометрию трассы (mesh‑ленту) вдоль <see cref="TrackPath"/>.
    /// </summary>
    /// <remarks>
    /// Зачем это нужно:
    /// - вместо набора префабов (квадратов/плиток) получаем цельную "дорогу" без разрывов
    /// - повороты становятся визуально непрерывными (ленту можно дополнительно сгладить увеличением частоты семплов)
    ///
    /// Ограничения учебной версии:
    /// - строим простую ленту (две кромки: левую и правую)
    /// - уклон по Y берём из точки на пути (если путь по Y ровный — дорога ровная)
    /// - кривизна зависит от того, насколько часто мы семплируем путь (SampleStep)
    /// </remarks>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class TrackMeshGenerator : MonoBehaviour
    {
        [SerializeField]
        private TrackPath _trackPath;

        [SerializeField]
        private TrackSettings _trackSettings;

        /// <summary>
        /// Игрок, относительно которого строим меш трассы (чтобы меш всегда был под ним).
        /// </summary>
        [SerializeField]
        private Transform _player;

        /// <summary>
        /// Дополнительная ширина к трассе (слева+справа) в мировых единицах.
        /// </summary>
        [SerializeField]
        [Min(0f)]
        private float _widthPadding = 0.5f;

        /// <summary>
        /// Смещение меша по Y относительно точки на пути.
        /// </summary>
        [SerializeField]
        private float _yOffset = 0f;

        /// <summary>
        /// Шаг семплирования пути (в единицах вдоль пути).
        /// </summary>
        /// <remarks>
        /// Чем меньше шаг — тем плавнее повороты, но тем больше вершин в меше.
        /// </remarks>
        [SerializeField]
        [Min(0.1f)]
        private float _sampleStep = 1f;

        /// <summary>
        /// Насколько далеко вперёд от игрока строить меш (в единицах вдоль пути).
        /// </summary>
        [SerializeField]
        [Min(1f)]
        private float _buildAheadDistance = 200f;

        /// <summary>
        /// Насколько далеко позади игрока строить меш (в единицах вдоль пути).
        /// </summary>
        [SerializeField]
        [Min(0f)]
        private float _buildBehindDistance = 40f;

        /// <summary>
        /// Масштаб UV по длине (как часто повторяется текстура вдоль дороги).
        /// </summary>
        [SerializeField]
        [Min(0.001f)]
        private float _uvTilingAlong = 0.1f;

        /// <summary>
        /// Если включено, создаём/обновляем MeshCollider по сгенерированному мешу.
        /// </summary>
        [SerializeField]
        private bool _generateCollider = true;

        private MeshFilter _meshFilter;
        private MeshCollider _meshCollider;

        private int _lastBuiltPathVersionRuntime = -1;
        private int _lastBuiltConfigHashRuntime;

#if UNITY_EDITOR
        [SerializeField]
        private bool _previewInEditor = true;

        private int _lastSeenPathVersion = -1;
        private int _lastSeenConfigHash;
        private bool _editorRebuildRequested;
#endif

        private void Awake()
        {
            EnsureComponents();
        }

        private void OnEnable()
        {
            EnsureComponents();

#if UNITY_EDITOR
            if (!Application.isPlaying && _previewInEditor)
            {
                _editorRebuildRequested = true;
            }
#endif
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying && _previewInEditor)
            {
                _editorRebuildRequested = true;
            }
        }
#endif

        private void Update()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (!_previewInEditor)
                {
                    return;
                }

                if (_editorRebuildRequested)
                {
                    _editorRebuildRequested = false;
                    RebuildMeshSafe();
                    return;
                }

                var configHash = ComputeConfigHash();
                if (_lastSeenConfigHash != configHash)
                {
                    _lastSeenConfigHash = configHash;
                    _editorRebuildRequested = true;
                    return;
                }

                if (_trackPath != null && _lastSeenPathVersion != _trackPath.PathVersion)
                {
                    _lastSeenPathVersion = _trackPath.PathVersion;
                    _editorRebuildRequested = true;
                    return;
                }
            }
#endif

            if (!Application.isPlaying)
            {
                return;
            }

            // В Play Mode пересобираем только когда реально что-то изменилось.
            var runtimeHash = ComputeRuntimeConfigHash();
            var runtimeVersion = _trackPath != null ? _trackPath.RuntimeVersion : -1;
            if (_lastBuiltPathVersionRuntime == runtimeVersion && _lastBuiltConfigHashRuntime == runtimeHash && _meshFilter != null && _meshFilter.sharedMesh != null)
            {
                return;
            }

            RebuildMeshSafe();
        }

        private void RebuildMeshSafe()
        {
            EnsureComponents();

            if (!ValidateConfiguration(isRuntime: Application.isPlaying))
            {
                ClearMesh();
                return;
            }

            BuildMesh();

#if UNITY_EDITOR
            if (_trackPath != null)
            {
                _lastSeenPathVersion = _trackPath.PathVersion;
            }

            _lastSeenConfigHash = ComputeConfigHash();
#endif
        }

        private bool ValidateConfiguration(bool isRuntime)
        {
            if (_trackSettings == null && _trackPath != null)
            {
                _trackSettings = _trackPath.GetComponent<TrackSettings>();
            }

            if (_trackPath == null)
            {
                Log(isRuntime, $"[{nameof(TrackMeshGenerator)}] Не задан {nameof(TrackPath)}. Генерация меша невозможна.");
                return false;
            }

            if (_trackSettings == null)
            {
                Log(isRuntime, $"[{nameof(TrackMeshGenerator)}] Не задан {nameof(TrackSettings)}. Генерация меша невозможна.");
                return false;
            }

            if (_player == null)
            {
                Log(isRuntime, $"[{nameof(TrackMeshGenerator)}] Не задан игрок (поле Player). Генерация меша невозможна.");
                return false;
            }

            if (!_trackPath.IsValidPath)
            {
                // Попробуем автоинициализировать (как в генераторе земли).
                var required = Mathf.Max(_buildAheadDistance + _buildBehindDistance, _sampleStep * 2f);
                if (!_trackPath.TryEnsureValidPath(required))
                {
                    Log(isRuntime, $"[{nameof(TrackMeshGenerator)}] {nameof(TrackPath)} задан, но путь невалиден.");
                    return false;
                }
            }

            _sampleStep = Mathf.Max(0.1f, _sampleStep);
            _buildAheadDistance = Mathf.Max(1f, _buildAheadDistance);
            _buildBehindDistance = Mathf.Max(0f, _buildBehindDistance);
            _uvTilingAlong = Mathf.Max(0.001f, _uvTilingAlong);
            return true;
        }

        private void BuildMesh()
        {
            var width = GetTrackWidth();

            if (!_trackPath.TryGetClosestDistance(_player.position, out var playerDistance))
            {
                ClearMesh();
                return;
            }

            var startDistance = Mathf.Max(0f, playerDistance - _buildBehindDistance);
            var endDistance = Mathf.Min(_trackPath.TotalLength, playerDistance + _buildAheadDistance);
            var length = Mathf.Max(_sampleStep, endDistance - startDistance);
            var sampleCount = Mathf.Max(2, Mathf.FloorToInt(length / _sampleStep) + 1);

            // Две вершины на срез (левая/правая кромка).
            var vertices = new Vector3[sampleCount * 2];
            var normals = new Vector3[sampleCount * 2];
            var uvs = new Vector2[sampleCount * 2];

            // По два треугольника на "квад" между соседними срезами.
            var triangles = new int[(sampleCount - 1) * 6];

            var halfWidth = width * 0.5f;
            var localUp = transform.InverseTransformDirection(Vector3.up).normalized;
            if (localUp.sqrMagnitude < 0.001f)
            {
                localUp = Vector3.up;
            }

            for (var i = 0; i < sampleCount; i++)
            {
                var s = Mathf.Min(endDistance, startDistance + i * _sampleStep);
                if (!_trackPath.TryEvaluateFrame(s, out var centerPos, out _, out var right))
                {
                    // Фоллбек: если не смогли — используем предыдущие значения.
                    if (i > 0)
                    {
                        vertices[i * 2] = vertices[(i - 1) * 2];
                        vertices[i * 2 + 1] = vertices[(i - 1) * 2 + 1];
                        normals[i * 2] = Vector3.up;
                        normals[i * 2 + 1] = Vector3.up;
                        uvs[i * 2] = uvs[(i - 1) * 2];
                        uvs[i * 2 + 1] = uvs[(i - 1) * 2 + 1];
                    }

                    continue;
                }

                var pos = centerPos + Vector3.up * _yOffset + right * _trackSettings.LaneCenterX;
                var left = pos - right * halfWidth;
                var rightPos = pos + right * halfWidth;

                // Левый/правый край.
                // Mesh хранит вершины в локальных координатах объекта с MeshFilter.
                vertices[i * 2] = transform.InverseTransformPoint(left);
                vertices[i * 2 + 1] = transform.InverseTransformPoint(rightPos);

                normals[i * 2] = localUp;
                normals[i * 2 + 1] = localUp;

                // UV: U по ширине (0..1), V по длине.
                var v = (s - startDistance) * _uvTilingAlong;
                uvs[i * 2] = new Vector2(0f, v);
                uvs[i * 2 + 1] = new Vector2(1f, v);
            }

            var triIndex = 0;
            for (var i = 0; i < sampleCount - 1; i++)
            {
                var v0 = i * 2;
                var v1 = i * 2 + 1;
                var v2 = (i + 1) * 2;
                var v3 = (i + 1) * 2 + 1;

                // Треугольники: (v0, v2, v1) и (v1, v2, v3)
                triangles[triIndex++] = v0;
                triangles[triIndex++] = v2;
                triangles[triIndex++] = v1;

                triangles[triIndex++] = v1;
                triangles[triIndex++] = v2;
                triangles[triIndex++] = v3;
            }

            var mesh = _meshFilter.sharedMesh;
            if (mesh == null)
            {
                mesh = new Mesh { name = "TrackMesh" };
                _meshFilter.sharedMesh = mesh;
            }
            else
            {
                mesh.Clear();
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.RecalculateBounds();

            if (_generateCollider)
            {
                if (_meshCollider == null)
                {
                    _meshCollider = GetComponent<MeshCollider>();
                    if (_meshCollider == null)
                    {
                        _meshCollider = gameObject.AddComponent<MeshCollider>();
                    }
                }

                // Для статической трассы MeshCollider должен быть НЕ convex.
                // Trigger тоже выключаем, иначе игрок провалится.
                _meshCollider.convex = false;
                _meshCollider.isTrigger = false;

                _meshCollider.sharedMesh = null;
                _meshCollider.sharedMesh = mesh;
            }
            else
            {
                if (_meshCollider != null)
                {
                    _meshCollider.sharedMesh = null;
                }
            }

            if (Application.isPlaying)
            {
                _lastBuiltPathVersionRuntime = _trackPath != null ? _trackPath.RuntimeVersion : -1;
                _lastBuiltConfigHashRuntime = ComputeRuntimeConfigHash();
            }
        }

        private void ClearMesh()
        {
            if (_meshFilter == null)
            {
                return;
            }

            if (_meshFilter.sharedMesh != null)
            {
                _meshFilter.sharedMesh.Clear();
            }

            if (_meshCollider != null)
            {
                _meshCollider.sharedMesh = null;
            }
        }

        private float GetTrackWidth()
        {
            var baseWidth = Mathf.Max(0.1f, _trackSettings.LaneCount * _trackSettings.LaneWidth);
            return baseWidth + _widthPadding * 2f;
        }

        private void EnsureComponents()
        {
            if (_meshFilter == null)
            {
                _meshFilter = GetComponent<MeshFilter>();
            }

            if (_meshCollider == null)
            {
                _meshCollider = GetComponent<MeshCollider>();
            }
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
        private int ComputeConfigHash()
        {
            unchecked
            {
                var hash = 17;
                hash = (hash * 31) + (_trackPath != null ? _trackPath.GetInstanceID() : 0);
                hash = (hash * 31) + (_trackSettings != null ? _trackSettings.GetInstanceID() : 0);
                hash = (hash * 31) + (_player != null ? _player.GetInstanceID() : 0);
                hash = (hash * 31) + _widthPadding.GetHashCode();
                hash = (hash * 31) + _yOffset.GetHashCode();
                hash = (hash * 31) + _sampleStep.GetHashCode();
                hash = (hash * 31) + _buildAheadDistance.GetHashCode();
                hash = (hash * 31) + _buildBehindDistance.GetHashCode();
                hash = (hash * 31) + _uvTilingAlong.GetHashCode();
                hash = (hash * 31) + _generateCollider.GetHashCode();
                return hash;
            }
        }
#endif

        private int ComputeRuntimeConfigHash()
        {
            unchecked
            {
                var hash = 17;
                hash = (hash * 31) + (_trackPath != null ? _trackPath.GetInstanceID() : 0);
                hash = (hash * 31) + (_trackSettings != null ? _trackSettings.GetInstanceID() : 0);
                hash = (hash * 31) + (_player != null ? _player.GetInstanceID() : 0);
                hash = (hash * 31) + _widthPadding.GetHashCode();
                hash = (hash * 31) + _yOffset.GetHashCode();
                hash = (hash * 31) + _sampleStep.GetHashCode();
                hash = (hash * 31) + _buildAheadDistance.GetHashCode();
                hash = (hash * 31) + _buildBehindDistance.GetHashCode();
                hash = (hash * 31) + _uvTilingAlong.GetHashCode();
                hash = (hash * 31) + _generateCollider.GetHashCode();
                return hash;
            }
        }
    }
}

