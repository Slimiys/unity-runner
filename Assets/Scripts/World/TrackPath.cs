using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Путь трассы, заданный набором точек (waypoints) по центральной линии.
    /// </summary>
    /// <remarks>
    /// Это простой вариант без сплайнов:
    /// - трасса — это ломаная линия из отрезков между точками
    /// - мы можем "идти" по ней, задавая расстояние вдоль пути
    /// - в каждой точке пути можем получить позицию и направление (tangent)
    /// </remarks>
    public partial class TrackPath : MonoBehaviour
    {
        /// <summary>
        /// Точки центральной линии трассы, по порядку.
        /// </summary>
        [SerializeField]
        private Transform[] _points;

        /// <summary>
        /// Если включено, путь генерируется динамически во время игры (как "бесконечная" трасса).
        /// </summary>
        [SerializeField]
        private bool _dynamicGeneration = false;

        /// <summary>
        /// Игрок, относительно которого поддерживаем длину пути (в динамическом режиме).
        /// </summary>
        [SerializeField]
        private Transform _player;

        /// <summary>
        /// Длина одного сегмента пути (расстояние между точками).
        /// </summary>
        [SerializeField]
        [Min(0.5f)]
        private float _segmentLength = 8f;

        /// <summary>
        /// Какую длину пути держать впереди игрока.
        /// </summary>
        [SerializeField]
        [Min(5f)]
        private float _generateAheadDistance = 120f;

        /// <summary>
        /// Какую длину пути оставлять позади игрока перед тем как удалять старые точки.
        /// </summary>
        [SerializeField]
        [Min(0f)]
        private float _trimBehindDistance = 40f;

        /// <summary>
        /// Вероятность поворота на каждом новом сегменте (0..1).
        /// </summary>
        [SerializeField]
        [Range(0f, 1f)]
        private float _turnChance = 0.2f;

        /// <summary>
        /// Максимальный угол поворота (в градусах) при генерации нового сегмента.
        /// </summary>
        [SerializeField]
        [Range(0f, 90f)]
        private float _maxTurnAngleDegrees = 25f;

        // Runtime-точки (используются и для статического, и для динамического режима).
        private readonly List<Vector3> _runtimePoints = new();
        private readonly List<float> _cumulativeLengths = new();

        // Глобальная дистанция, соответствующая первой runtime-точке. Нужна, если мы "отрезаем" хвост позади игрока.
        private float _startDistance;

        // Текущая суммарная длина runtime-пути (от 0 до конца списка).
        private float _localTotalLength;

        // Текущее направление генерации (касательная). Используется в динамическом режиме.
        private Vector3 _currentForward = Vector3.forward;

        // Последняя успешно вычисленная глобальная дистанция игрока вдоль пути.
        // Нужна, чтобы динамическая генерация не "останавливалась", если на конкретном кадре
        // TryGetClosestDistance вернул false.
        private float _lastKnownPlayerDistanceAlongPath;
        private bool _hasLastKnownPlayerDistanceAlongPath;

        // Версия пути: увеличивается, когда меняются точки (нужна для editor‑превью у других систем).
        [SerializeField]
        private int _pathVersion;

        /// <summary>
        /// Версия динамического пути (runtime): увеличивается при runtime‑генерации и обрезке.
        /// Нужна для систем, которым важно обновляться, когда трасса растёт во время игры.
        /// </summary>
        private int _runtimeVersion;

        /// <summary>
        /// Версия динамического пути (runtime).
        /// </summary>
        public int RuntimeVersion => _runtimeVersion;

#if UNITY_EDITOR
        /// <summary>
        /// Включает отладочные сообщения в Console о динамической генерации трассы.
        /// </summary>
        [SerializeField]
        private bool _debugDynamicGeneration = true;

        /// <summary>
        /// Как часто (в секундах) печатать отладочные сообщения в Console.
        /// </summary>
        [SerializeField]
        [Min(0.1f)]
        private float _debugLogIntervalSeconds = 1f;

        // Внутренние счётчики для ограничения частоты логов.
        private float _debugLastLogTime;
#endif

        /// <summary>
        /// Общая длина пути.
        /// </summary>
        /// <remarks>
        /// В динамическом режиме это "глобальная" длина: <c>StartDistance + LocalTotalLength</c>.
        /// </remarks>
        public float TotalLength => _startDistance + _localTotalLength;

        /// <summary>
        /// Признак того, что путь корректно задан и по нему можно вычислять позиции.
        /// </summary>
        public bool IsValidPath =>
            _runtimePoints.Count >= 2 &&
            _cumulativeLengths.Count == _runtimePoints.Count &&
            _localTotalLength > 0.001f;

        /// <summary>
        /// Версия пути: увеличивается, когда набор точек/кэш пути пересобирается.
        /// </summary>
        /// <remarks>
        /// Полезно для систем редакторского превью (например, генератора земли), чтобы понимать,
        /// что путь изменился и нужно пересобрать свои объекты.
        /// </remarks>
        public int PathVersion => _pathVersion;

        private void Awake()
        {
            BuildFromSerializedPoints();
            if (_dynamicGeneration)
            {
                if (_player == null)
                {
                    Debug.LogError(
                        $"[{nameof(TrackPath)}] Включена {nameof(_dynamicGeneration)}, но не задано поле {nameof(_player)}. " +
                        $"Динамическая генерация невозможна.",
                        this);
                    return;
                }

                EnsureInitializedForDynamicMode();
            }
        }

        /// <summary>
        /// Пересчитывает кэш длин отрезков по сериализованным точкам.
        /// </summary>
        public void BuildFromSerializedPoints()
        {
            _runtimePoints.Clear();
            _cumulativeLengths.Clear();
            _startDistance = 0f;
            _localTotalLength = 0f;

            if (_points == null || _points.Length < 2)
            {
                return;
            }

            for (var i = 0; i < _points.Length; i++)
            {
                if (_points[i] == null)
                {
                    continue;
                }

                AddPointInternal(_points[i].position);
            }

            if (_runtimePoints.Count >= 2)
            {
                _currentForward = (_runtimePoints[^1] - _runtimePoints[^2]).normalized;
            }

            BumpPathVersion();
            _runtimeVersion = 0;
        }

        private void BumpPathVersion()
        {
            // Защита от переполнения нам не критична: это текущий прототип.
            _pathVersion++;
        }

        /// <summary>
        /// Получает позицию и направление на пути по расстоянию вдоль центральной линии.
        /// </summary>
        /// <param name="distance">Глобальная дистанция вдоль пути, в диапазоне 0..TotalLength.</param>
        /// <param name="position">Позиция на пути.</param>
        /// <param name="forward">Направление движения (касательная к пути).</param>
        /// <returns>True, если путь задан корректно.</returns>
        public bool TryEvaluate(float distance, out Vector3 position, out Vector3 forward)
        {
            position = transform.position;
            forward = transform.forward;

            if (_runtimePoints.Count < 2 || _cumulativeLengths.Count != _runtimePoints.Count)
            {
                return false;
            }

            if (_localTotalLength <= 0.001f)
            {
                return false;
            }

            // Переводим глобальную дистанцию в локальную (относительно начала runtime-буфера).
            var localDistance = Mathf.Clamp(distance - _startDistance, 0f, _localTotalLength);

            // Находим сегмент, в который попадает distance.
            var segIndex = FindSegmentIndex(localDistance);
            var segStart = _runtimePoints[segIndex];
            var segEnd = _runtimePoints[segIndex + 1];

            var segLen = Mathf.Max(0.001f, Vector3.Distance(segStart, segEnd));
            var segStartDist = _cumulativeLengths[segIndex];
            var t = Mathf.Clamp01((localDistance - segStartDist) / segLen);

            position = Vector3.Lerp(segStart, segEnd, t);
            forward = (segEnd - segStart).normalized;
            return true;
        }

        /// <summary>
        /// Получает позицию и "кадр трассы" (forward/right) по расстоянию вдоль пути.
        /// </summary>
        /// <param name="distance">Глобальная дистанция вдоль пути.</param>
        /// <param name="position">Позиция на центральной линии.</param>
        /// <param name="forward">Направление движения по трассе.</param>
        /// <param name="right">Направление "вправо" (для полос), перпендикулярное forward в плоскости XZ.</param>
        /// <returns>True, если путь задан корректно.</returns>
        public bool TryEvaluateFrame(float distance, out Vector3 position, out Vector3 forward, out Vector3 right)
        {
            right = Vector3.right;
            if (!TryEvaluate(distance, out position, out forward))
            {
                return false;
            }

            // "Вправо" строим в горизонтальной плоскости.
            right = Vector3.Cross(Vector3.up, forward).normalized;
            if (right.sqrMagnitude < 0.001f)
            {
                right = Vector3.right;
            }

            return true;
        }

        /// <summary>
        /// Находит ближайшую точку на пути к заданной позиции и возвращает кадр трассы в этой точке.
        /// </summary>
        public bool TryGetClosestFrame(Vector3 worldPoint, out float distanceAlongPath, out Vector3 position, out Vector3 forward, out Vector3 right)
        {
            position = transform.position;
            forward = transform.forward;
            right = transform.right;

            if (!TryGetClosestDistance(worldPoint, out distanceAlongPath))
            {
                return false;
            }

            return TryEvaluateFrame(distanceAlongPath, out position, out forward, out right);
        }

        /// <summary>
        /// Ищет ближайшее расстояние вдоль пути до заданной точки.
        /// </summary>
        /// <remarks>
        /// Используется, чтобы корректно "встать" на трассу при старте/респауне.
        /// </remarks>
        public bool TryGetClosestDistance(Vector3 worldPoint, out float distanceAlongPath)
        {
            distanceAlongPath = 0f;

            if (_runtimePoints.Count < 2 || _cumulativeLengths.Count != _runtimePoints.Count)
            {
                return false;
            }

            var bestSqr = float.PositiveInfinity;
            var bestDistance = 0f;

            for (var i = 0; i < _runtimePoints.Count - 1; i++)
            {
                var a = _runtimePoints[i];
                var b = _runtimePoints[i + 1];
                var ab = b - a;
                var abLenSqr = Mathf.Max(0.001f, ab.sqrMagnitude);
                var t = Mathf.Clamp01(Vector3.Dot(worldPoint - a, ab) / abLenSqr);
                var p = a + ab * t;
                var dSqr = (worldPoint - p).sqrMagnitude;

                if (dSqr < bestSqr)
                {
                    bestSqr = dSqr;
                    var segLen = Vector3.Distance(a, b);
                    bestDistance = _cumulativeLengths[i] + segLen * t;
                }
            }

            distanceAlongPath = _startDistance + bestDistance;
            return true;
        }

        private int FindSegmentIndex(float distance)
        {
            // Линейный поиск достаточно быстрый для текущего проекта (обычно точек немного).
            // Если точек станет очень много — можно заменить на бинарный поиск.
            for (var i = 0; i < _cumulativeLengths.Count - 1; i++)
            {
                if (distance >= _cumulativeLengths[i] && distance <= _cumulativeLengths[i + 1])
                {
                    return i;
                }
            }

            return _cumulativeLengths.Count - 2;
        }

        private void Update()
        {
            if (!_dynamicGeneration || _player == null)
            {
                return;
            }

            // Поддерживаем достаточную длину пути впереди игрока.
            // В идеале каждый кадр вычисляем ближайшую дистанцию.
            // Но если TryGetClosestDistance временно вернул false (например, из-за тримминга),
            // используем последнюю известную дистанцию, чтобы генерация не останавливалась.
            var shouldUseFallbackDistance = false;
            var playerDistance = 0f;

            if (TryGetClosestDistance(_player.position, out var computedDistance))
            {
                playerDistance = computedDistance;
                _lastKnownPlayerDistanceAlongPath = computedDistance;
                _hasLastKnownPlayerDistanceAlongPath = true;
            }
            else
            {
                shouldUseFallbackDistance = true;
            }

            if (shouldUseFallbackDistance)
            {
                if (!_hasLastKnownPlayerDistanceAlongPath)
                {
                    return;
                }

                playerDistance = _lastKnownPlayerDistanceAlongPath;
            }

            // Для понимания "почему трасса не продолжается" полезно видеть:
            // - вычислялась ли текущая дистанция (TryGetClosestDistance)
            // - увеличилась ли TotalLength / Count после EnsureLength
            var beforeTotalLength = TotalLength;
            var beforeRuntimePointsCount = _runtimePoints.Count;

            EnsureLength(playerDistance + _generateAheadDistance);
            TrimBehind(playerDistance - _trimBehindDistance);

#if UNITY_EDITOR
            if (_debugDynamicGeneration && Time.time - _debugLastLogTime >= _debugLogIntervalSeconds)
            {
                _debugLastLogTime = Time.time;

                Debug.Log(
                    $"[{nameof(TrackPath)}] " +
                    $"dynGen={(true)} " +
                    $"playerDistance={playerDistance:F2} " +
                    $"ahead={_generateAheadDistance:F2} " +
                    $"requiredEnd={(playerDistance + _generateAheadDistance):F2} " +
                    $"TotalLength: {beforeTotalLength:F2} -> {TotalLength:F2} " +
                    $"runtimePoints: {beforeRuntimePointsCount} -> {_runtimePoints.Count} " +
                    $"startDistance={_startDistance:F2} localTotal={_localTotalLength:F2} " +
                    $"closestOk={(shouldUseFallbackDistance ? "fallback" : "computed")}"
                    , this);
            }
#endif
        }

        /// <summary>
        /// Гарантирует, что путь содержит точки как минимум до заданной глобальной дистанции.
        /// </summary>
        public void EnsureLength(float requiredGlobalDistance)
        {
            if (_runtimePoints.Count < 2)
            {
                EnsureInitializedForDynamicMode();
            }

            while (TotalLength < requiredGlobalDistance)
            {
                AddGeneratedPoint();
            }
        }

        /// <summary>
        /// Поддерживает динамическую трассу: гарантирует генерацию впереди игрока и удаление позади.
        /// </summary>
        /// <param name="playerDistanceAlongPath">
        /// Глобальная дистанция вдоль пути (s), соответствующая позиции игрока на центральной линии.
        /// </param>
        /// <remarks>
        /// Этот метод полезен, если другие системы (например PlayerController) двигаются по TrackPath
        /// и важно, чтобы длина трассы поддерживалась синхронно с движением.
        /// </remarks>
        public void MaintainDynamicLength(float playerDistanceAlongPath)
        {
            // Поддерживаем буфер впереди и режем хвост позади.
            EnsureLength(playerDistanceAlongPath + _generateAheadDistance);
            TrimBehind(playerDistanceAlongPath - _trimBehindDistance);
        }

        /// <summary>
        /// Пытается автоматически сделать путь валидным: создаёт стартовые точки и при необходимости догенерирует длину.
        /// </summary>
        /// <param name="requiredGlobalDistance">
        /// Минимальная требуемая длина пути (глобальная дистанция), до которой нужно обеспечить точки.
        /// </param>
        /// <returns>True, если после вызова путь валиден и по нему можно вычислять позиции.</returns>
        /// <remarks>
        /// Это удобный метод для систем, которые хотят работать в "строгом режиме" без ручной подготовки точек.
        /// Например, генератор земли может вызвать его, чтобы гарантировать наличие хотя бы двух точек.
        /// </remarks>
        public bool TryEnsureValidPath(float requiredGlobalDistance)
        {
            // Даже если путь "валиден", нам может не хватать длины именно под запрошенную дистанцию.
            // Если не дотянуть длину, TryEvaluateFrame будет зажимать distance к последней точке,
            // и разные sSpawn дадут одинаковый frame/позицию (что визуально выглядит как "монеты друг в друге").

            // Поддерживаем результат в рамках этого компонента (без создания editor‑Transform точек).
            // Это работает и в Play Mode, и в Edit Mode, потому что опирается на runtime‑буфер.
            EnsureLength(Mathf.Max(0f, requiredGlobalDistance));
            return IsValidPath;
        }

        private void EnsureInitializedForDynamicMode()
        {
            // Если пользователь не задал точки вручную, создаём две стартовые точки из transform объекта TrackPath.
            if (_runtimePoints.Count >= 2)
            {
                return;
            }

            var origin = transform.position;
            var forward = transform.forward;

            _currentForward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;

            _runtimePoints.Clear();
            _cumulativeLengths.Clear();
            _startDistance = 0f;
            _localTotalLength = 0f;
            _runtimeVersion = 0;

            AddPointInternal(origin);
            AddPointInternal(origin + _currentForward * _segmentLength);
        }

        private void AddGeneratedPoint()
        {
            // С вероятностью turnChance слегка поворачиваем направление генерации по Y.
            if (UnityEngine.Random.value < _turnChance)
            {
                var angle = UnityEngine.Random.Range(-_maxTurnAngleDegrees, _maxTurnAngleDegrees);
                _currentForward = Quaternion.AngleAxis(angle, Vector3.up) * _currentForward;
                _currentForward = _currentForward.normalized;
            }

            var last = _runtimePoints[^1];
            var next = last + _currentForward * _segmentLength;
            AddPointInternal(next);
            _runtimeVersion++;
        }

        private void TrimBehind(float keepFromGlobalDistance)
        {
            if (_runtimePoints.Count < 3)
            {
                return;
            }

            // Если keepFromGlobalDistance меньше StartDistance — нечего резать.
            if (keepFromGlobalDistance <= _startDistance)
            {
                return;
            }

            // Нужно удалить точки, которые полностью лежат "до" keepFromGlobalDistance.
            // Для простоты удаляем целыми сегментами, оставляя минимум 2 точки.
            var localKeep = keepFromGlobalDistance - _startDistance;

            var removeCount = 0;
            while (removeCount < _cumulativeLengths.Count - 2 && _cumulativeLengths[removeCount + 1] < localKeep)
            {
                removeCount++;
            }

            if (removeCount <= 0)
            {
                return;
            }

            // Сдвигаем startDistance и удаляем первые точки.
            _startDistance += _cumulativeLengths[removeCount];
            _runtimePoints.RemoveRange(0, removeCount);

            // Пересчитываем кумулятивные длины (список обычно небольшой).
            RebuildRuntimeCumulative();
            _runtimeVersion++;
        }

        private void RebuildRuntimeCumulative()
        {
            _cumulativeLengths.Clear();
            _cumulativeLengths.Add(0f);

            var sum = 0f;
            for (var i = 1; i < _runtimePoints.Count; i++)
            {
                sum += Vector3.Distance(_runtimePoints[i - 1], _runtimePoints[i]);
                _cumulativeLengths.Add(sum);
            }

            _localTotalLength = sum;
        }

        private void AddPointInternal(Vector3 point)
        {
            if (_runtimePoints.Count > 0)
            {
                _localTotalLength += Vector3.Distance(_runtimePoints[^1], point);
            }

            _runtimePoints.Add(point);
            _cumulativeLengths.Add(_localTotalLength);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;

            // 1) Рисуем runtime-точки (динамический режим).
            // 2) Если runtime-точек нет — рисуем сериализованные точки _points (редакторный/статический режим).
            if (_runtimePoints.Count >= 2 && _cumulativeLengths.Count == _runtimePoints.Count)
            {
                for (var i = 0; i < _runtimePoints.Count - 1; i++)
                {
                    Gizmos.DrawLine(_runtimePoints[i], _runtimePoints[i + 1]);
                }

                return;
            }

            if (_points == null || _points.Length < 2)
            {
                return;
            }

            for (var i = 0; i < _points.Length - 1; i++)
            {
                if (_points[i] == null || _points[i + 1] == null)
                {
                    continue;
                }

                Gizmos.DrawLine(_points[i].position, _points[i + 1].position);
            }
        }
    }
}

