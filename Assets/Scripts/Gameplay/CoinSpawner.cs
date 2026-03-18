using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Спавнит монетки на уровне.
    /// </summary>
    /// <remarks>
    /// Динамически спавнит монеты во время забега:
    /// - строго по полосам (настройки берутся из <see cref="TrackSettings"/>)
    /// - привязывает высоту к поверхности трассы через Raycast (опционально)
    /// - учитывает повороты трассы через <see cref="_trackPath"/>
    /// - умеет сбрасываться после респауна (когда игрок резко перемещается назад)
    /// </remarks>
    public class CoinSpawner : MonoBehaviour
    {
        /// <summary>
        /// Префаб монетки для спавна.
        /// </summary>
        [SerializeField]
        private GameObject _coinPrefab;

        /// <summary>
        /// Игрок, относительно которого спавним монеты.
        /// </summary>
        [SerializeField]
        private Transform _player;

        /// <summary>
        /// Путь трассы (центральная линия), задающий позицию и направление на поворотах.
        /// </summary>
        /// <remarks>
        /// Спавнер ориентируется на <see cref="TrackPath"/>, поэтому отдельный TrackFrame не нужен.
        /// </remarks>
        [SerializeField]
        private TrackPath _trackPath;

        /// <summary>
        /// Настройки трассы, из которых берём параметры полос.
        /// </summary>
        /// <remarks>
        /// Обычно этот компонент находится на том же объекте, что и <see cref="_trackPath"/>.
        /// Мы не ищем его по всей сцене, чтобы избежать скрытых зависимостей.
        /// </remarks>
        [SerializeField]
        private TrackSettings _trackSettings;

        /// <summary>
        /// Насколько далеко впереди игрока поддерживать спавн монет (по оси Z).
        /// </summary>
        [SerializeField]
        [Min(1f)]
        private float _spawnAheadDistance = 40f;

        /// <summary>
        /// Если включено, спавнер "прилипает" к поверхности трассы: вычисляет высоту монеты через Raycast вниз.
        /// Полезно, если форма/высота дороги меняется (рампочки, холмы, лестницы и т.п.).
        /// </summary>
        [SerializeField]
        private bool _snapToGround = true;

        /// <summary>
        /// Маска слоёв поверхности, к которой можно "прилипать" (обычно слой Ground).
        /// </summary>
        [SerializeField]
        private LayerMask _groundMask;

        /// <summary>
        /// Насколько высоко над предполагаемой точкой спавна начинать луч вниз.
        /// </summary>
        [SerializeField]
        [Min(0.1f)]
        private float _groundRaycastStartHeight = 10f;

        /// <summary>
        /// Максимальная длина луча вниз для поиска поверхности.
        /// </summary>
        [SerializeField]
        [Min(0.1f)]
        private float _groundRaycastDistance = 30f;

        /// <summary>
        /// На каком расстоянии позади игрока монеты удаляются (по оси Z).
        /// </summary>
        [SerializeField]
        [Min(1f)]
        private float _despawnBehindDistance = 15f;

        /// <summary>
        /// Максимальное число активных монет (защита от бесконечного накопления).
        /// 0 означает «без лимита».
        /// </summary>
        [SerializeField]
        [Min(0)]
        private int _maxActiveCoins = 80;

        /// <summary>
        /// Если включено, спавнер сбрасывается, когда игрок резко перемещается назад по Z (например, после респауна).
        /// </summary>
        [SerializeField]
        private bool _resetOnPlayerMovedBackwards = true;

        /// <summary>
        /// На сколько единиц по Z игрок должен «прыгнуть назад», чтобы мы считали это респауном.
        /// </summary>
        [SerializeField]
        [Min(0.1f)]
        private float _backwardsResetThreshold = 3f;

        /// <summary>
        /// Если включено, при сбросе спавнера удаляем все уже созданные монеты.
        /// </summary>
        [SerializeField]
        private bool _clearCoinsOnReset = true;

        /// <summary>
        /// Минимальное расстояние между монетками по направлению движения.
        /// </summary>
        [SerializeField]
        private float _minZSpacing = 2f;

        /// <summary>
        /// Шаг спавна по направлению движения.
        /// Чем меньше значение, тем чаще будут появляться монеты.
        /// </summary>
        [SerializeField]
        [Min(0.1f)]
        private float _spawnStepZ = 4f;

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

        /// <summary>
        /// Включает отладочное логирование спавна монет (для поиска причин наложения).
        /// </summary>
        [SerializeField]
        private bool _debugSpawnLogging = false;

        /// <summary>
        /// Необязательная стратегия выбора полосы для спавна монет.
        /// Если не задана — используется случайная полоса.
        /// </summary>
        [SerializeField]
        private MonoBehaviour _laneSelectorSource;

        /// <summary>
        /// Необязательная стратегия snap монеты по высоте к земле.
        /// Если не задана — используется Raycast вниз (как раньше).
        /// </summary>
        [SerializeField]
        private MonoBehaviour _groundSnapperSource;

        /// <summary>
        /// Минимальный интервал между логами (чтобы консоль не "засорялась").
        /// </summary>
        [SerializeField]
        [Min(0.05f)]
        private float _debugLogIntervalSeconds = 0.5f;

        private float _lastDebugLogTime = float.NegativeInfinity;

        private bool ShouldDebugLog()
        {
            if (!_debugSpawnLogging)
            {
                return false;
            }

            var now = Time.unscaledTime;
            if (now - _lastDebugLogTime < _debugLogIntervalSeconds)
            {
                return false;
            }

            _lastDebugLogTime = now;
            return true;
        }

        private ICoinLaneSelector _laneSelector;
        private ICoinGroundSnapper _groundSnapper;

        /// <summary>
        /// Ограничение на число логов спавна, чтобы консоль не переполнилась.
        /// </summary>
        [SerializeField]
        [Min(0)]
        private int _debugMaxSpawnLogs = 30;

        private int _debugSpawnLogsCount;

        private Vector3 _lastSpawnWorldPosition;
        private bool _hasLastSpawnWorldPosition;

        // Защита от дублей: каждая логическая позиция спавна (по sSpawn и полосе) создаётся один раз за прогон.
        // Монеты тогда "только вперёд": если игрок позже оказался ближе, монеты на уже пройденных позициях не появятся снова.
        private readonly System.Collections.Generic.HashSet<long> _spawnedCoinKeys = new();

        /// <summary>
        /// Допуск, чтобы не спавнить монеты за пределами доступной длины пути.
        /// </summary>
        [SerializeField]
        [Min(0f)]
        private float _spawnSLengthEpsilon = 0.01f;

        /// <summary>
        /// Необязательная ссылка на источник очков (реализатор <see cref="IScoreService"/>).
        /// В Unity интерфейсы сериализовать нельзя, поэтому здесь хранится MonoBehaviour,
        /// который реализует <see cref="IScoreService"/>.
        /// </summary>
        [SerializeField]
        private MonoBehaviour _scoreServiceSource;

        private IScoreService _scoreService;

        // Храним ссылки на созданные монеты, чтобы:
        // - удалять их позади игрока (despawn)
        // - при необходимости быстро очистить всё при респауне (reset)
        private readonly System.Collections.Generic.List<GameObject> _spawnedCoins = new();

        // Следующая дистанция для спавна "вперёд" (в метрах/юнитах).
        //
        // Важно: это не мировая Z и не абсолютная координата.
        // Это расстояние "вперёд по направлению трассы" относительно текущей позиции игрока.
        // Мы уменьшаем его, когда игрок продвигается вперёд, и спавним новые монеты, чтобы
        // постоянно поддерживать заполненную область на расстоянии SpawnAheadDistance впереди.
        private float _nextSpawnForwardOffset;

        // Последняя позиция игрока нужна для вычисления пройденной дистанции за кадр.
        private Vector3 _lastPlayerPosition;

        // Последняя известная дистанция игрока вдоль TrackPath (в единицах s).
        // Используем, чтобы прогресс и удаление работали корректно на поворотах.
        private float _lastPlayerDistanceAlongTrack;
        private bool _hasLastPlayerDistanceAlongTrack;

        private void Start()
        {
            if (_coinPrefab == null)
            {
                return;
            }

            if (!ValidateConfiguration(isRuntime: true))
            {
                enabled = false;
                return;
            }

            _scoreService = _scoreServiceSource as IScoreService;
            if (_scoreService == null && ScoreManager.Instance != null)
            {
                _scoreService = ScoreManager.Instance;
            }

            _laneSelector = _laneSelectorSource as ICoinLaneSelector;
            _groundSnapper = _groundSnapperSource as ICoinGroundSnapper;
            InitializeDynamicSpawn();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _ = ValidateConfiguration(isRuntime: false);
        }
#endif

        private bool ValidateConfiguration(bool isRuntime)
        {
            if (_trackSettings == null && _trackPath != null)
            {
                _trackSettings = _trackPath.GetComponent<TrackSettings>();
            }

            if (_trackSettings == null)
            {
                var message =
                    $"[{nameof(CoinSpawner)}] Не задан {nameof(TrackSettings)}. Добавь компонент на объект трассы и укажи его (например, на тот же объект, что и {nameof(TrackPath)}).";

                if (isRuntime)
                {
                    Debug.LogError(message, this);
                }
                else
                {
                    Debug.LogWarning(message, this);
                }

                return false;
            }

            if (_trackPath == null)
            {
                var message = $"[{nameof(CoinSpawner)}] Не задан {nameof(TrackPath)}. В строгом режиме спавн невозможен.";
                if (isRuntime)
                {
                    Debug.LogError(message, this);
                }
                else
                {
                    Debug.LogWarning(message, this);
                }

                return false;
            }

            if (_player == null)
            {
                var message = $"[{nameof(CoinSpawner)}] Не задан игрок (поле Player). В строгом режиме спавн невозможен.";
                if (isRuntime)
                {
                    Debug.LogError(message, this);
                }
                else
                {
                    Debug.LogWarning(message, this);
                }

                return false;
            }

            return true;
        }

        private void Update()
        {
            if (_coinPrefab == null || _player == null)
            {
                return;
            }

            // В строгом режиме без обязательных зависимостей мы не работаем.
            if (_trackSettings == null || _trackPath == null)
            {
                return;
            }

            // На поворотах правильнее считать прогресс по кривой: movedForward = sNow - sLast.
            // Тогда шаги спавна и удаление монет работают "по пути", а не по эвристике dot-product в мировых координатах.
            var movedForward = 0f;
            var hasCurrentPlayerDistanceAlongTrack = false;
            var currentPlayerDistanceAlongTrack = 0f;

            if (_trackPath != null && _trackPath.TryGetClosestDistance(_player.position, out var playerDistance))
            {
                hasCurrentPlayerDistanceAlongTrack = true;
                currentPlayerDistanceAlongTrack = playerDistance;

                if (_hasLastPlayerDistanceAlongTrack)
                {
                    movedForward = currentPlayerDistanceAlongTrack - _lastPlayerDistanceAlongTrack;
                }
            }
            else
            {
                // Fallback: если TrackPath недоступен — используем dot-product.
                var forward = GetForward();
                var delta = _player.position - _lastPlayerPosition;
                movedForward = Vector3.Dot(delta, forward);
            }

            // Сброс спавнера при резком откате игрока назад.
            // На динамической трассе sNow-sLast иногда может давать отрицательные скачки
            // из-за тримминга/поиска ближайшей точки. Поэтому reset делаем по dot-product
            // вдоль направления трассы в предыдущей позиции, а курсор — по "положительному" прогрессу.
            if (_resetOnPlayerMovedBackwards)
            {
                var delta = _player.position - _lastPlayerPosition;

                // Базовое направление: беру forward из TrackPath в точке, соответствующей lastKnownDistance.
                // Если не получилось — используем эвристику GetForward().
                var forwardPrev = GetForward();
                if (_trackPath != null && _hasLastPlayerDistanceAlongTrack)
                {
                    if (_trackPath.TryEvaluateFrame(_lastPlayerDistanceAlongTrack, out _, out var fwd, out _))
                    {
                        forwardPrev = fwd;
                    }
                }

                var movedBackByDot = Vector3.Dot(delta, forwardPrev) < -_backwardsResetThreshold;
                if (movedBackByDot)
                {
                    if (ShouldDebugLog())
                    {
                        var trackIsValid = _trackPath != null && _trackPath.IsValidPath;
                        var trackTotalLen = _trackPath != null ? _trackPath.TotalLength : -1f;
                        var trackRuntimeVer = _trackPath != null ? _trackPath.RuntimeVersion : -1;

                        Debug.LogWarning(
                            $"[{nameof(CoinSpawner)}] Reset: movedBackByDot=true. " +
                            $"dot={Vector3.Dot(delta, forwardPrev):F3}, threshold={_backwardsResetThreshold:F3}, " +
                            $"delta={delta}, " +
                            $"hasLastS={_hasLastPlayerDistanceAlongTrack}, lastS={_lastPlayerDistanceAlongTrack:F3}, " +
                            $"trackIsValid={trackIsValid}, trackTotalLen={trackTotalLen:F3}, " +
                            $"trackRuntimeVer={trackRuntimeVer}");
                    }
                    ResetDynamicSpawn();
                    movedForward = 0f;
                }
            }

            // Когда игрок движется вперёд, все "точки впереди" становятся ближе.
            // Уменьшаем курсор спавна только на положительном прогрессе по пути.
            var movedForwardForCursor = Mathf.Max(movedForward, 0f);
            if (movedForwardForCursor > 0f)
            {
                _nextSpawnForwardOffset -= movedForwardForCursor;
            }

            // Не допускаем спавн "позади" игрока: курсор мог стать отрицательным из-за быстрого движения.
            _nextSpawnForwardOffset = Mathf.Max(_nextSpawnForwardOffset, _spawnStepZ);

            if (ShouldDebugLog())
            {
                var trackIsValid = _trackPath != null && _trackPath.IsValidPath;
                var trackTotalLen = _trackPath != null ? _trackPath.TotalLength : -1f;
                var trackRuntimeVer = _trackPath != null ? _trackPath.RuntimeVersion : -1;

                var playerS = hasCurrentPlayerDistanceAlongTrack ? currentPlayerDistanceAlongTrack : 0f;

                Debug.Log(
                    $"[{nameof(CoinSpawner)}] State: movedForward={movedForward:F3}, movedForwardForCursor={movedForwardForCursor:F3}, " +
                    $"nextSpawnOffset={_nextSpawnForwardOffset:F3}, spawnedCoins={_spawnedCoins.Count}, " +
                    $"hasPlayerS={hasCurrentPlayerDistanceAlongTrack}, playerS={playerS:F3}, " +
                    $"trackIsValid={trackIsValid}, trackTotalLen={trackTotalLen:F3}, trackRuntimeVer={trackRuntimeVer}");
            }

            SpawnAheadOfPlayer();
            DespawnBehindPlayer();

            _lastPlayerPosition = _player.position;

            if (hasCurrentPlayerDistanceAlongTrack)
            {
                _lastPlayerDistanceAlongTrack = currentPlayerDistanceAlongTrack;
                _hasLastPlayerDistanceAlongTrack = true;
            }
        }

        private void InitializeDynamicSpawn()
        {
            if (_player == null)
            {
                return;
            }

            // Стартуем спавн чуть впереди игрока, чтобы монеты не появлялись "под ногами".
            _nextSpawnForwardOffset = _spawnStepZ;
            _lastPlayerPosition = _player.position;

            if (_trackPath != null && _trackPath.TryGetClosestDistance(_player.position, out var playerDistance))
            {
                _lastPlayerDistanceAlongTrack = playerDistance;
                _hasLastPlayerDistanceAlongTrack = true;
            }
            else
            {
                _hasLastPlayerDistanceAlongTrack = false;
                if (ShouldDebugLog())
                {
                    var trackIsValid = _trackPath != null && _trackPath.IsValidPath;
                    var trackTotalLen = _trackPath != null ? _trackPath.TotalLength : -1f;
                    var trackRuntimeVer = _trackPath != null ? _trackPath.RuntimeVersion : -1;

                    Debug.LogWarning(
                        $"[{nameof(CoinSpawner)}] Initialize: TryGetClosestDistance=false. " +
                        $"trackIsValid={trackIsValid}, " +
                        $"trackTotalLen={trackTotalLen:F3}, " +
                        $"trackRuntimeVer={trackRuntimeVer}");
                }
            }
        }

        private bool HasPlayerMovedBackwardsSignificantly()
        {
            // Если игрок телепортировался назад (респаун), то он сдвинется "против" текущего направления forward.
            var forward = GetForward();
            var delta = _player.position - _lastPlayerPosition;
            var movedForward = Vector3.Dot(delta, forward);
            return movedForward < -_backwardsResetThreshold;
        }

        private void ResetDynamicSpawn()
        {
            if (_clearCoinsOnReset)
            {
                for (var i = _spawnedCoins.Count - 1; i >= 0; i--)
                {
                    var coin = _spawnedCoins[i];
                    if (coin != null)
                    {
                        Destroy(coin);
                    }
                }

                _spawnedCoins.Clear();
            }

            // Сбрасываем "курсор" спавна, чтобы снова создавать монеты впереди игрока.
            // Мы не используем _minZ: спавним шагами начиная с SpawnStepZ.
            _nextSpawnForwardOffset = _spawnStepZ;
            _lastPlayerPosition = _player.position;

            if (_trackPath != null && _trackPath.TryGetClosestDistance(_player.position, out var playerDistance))
            {
                _lastPlayerDistanceAlongTrack = playerDistance;
                _hasLastPlayerDistanceAlongTrack = true;
            }
            else
            {
                _hasLastPlayerDistanceAlongTrack = false;
            }
        }

        private void SpawnAheadOfPlayer()
        {
            // Спавним монеты ступеньками по дистанции "вперёд относительно игрока".
            // Это даёт корректное поведение на поворотах, потому что направление forward берётся от трассы.
            // Чтобы монетки точно попадали на кривую, мы считаем позицию в точке:
            // sSpawn = sPlayer + forwardOffset, и берём forward/right именно из TrackPath в этой точке.
            float playerDistanceAlongTrack = 0f;
            var hasPlayerDistanceAlongTrack = false;
            if (_trackPath != null && _trackPath.TryGetClosestDistance(_player.position, out var playerDistance))
            {
                playerDistanceAlongTrack = playerDistance;
                hasPlayerDistanceAlongTrack = true;
            }
            else
            {
                if (ShouldDebugLog())
                {
                    var trackIsValid = _trackPath != null && _trackPath.IsValidPath;
                    var trackTotalLen = _trackPath != null ? _trackPath.TotalLength : -1f;

                    Debug.LogWarning(
                        $"[{nameof(CoinSpawner)}] Spawn skipped: TryGetClosestDistance=false. " +
                        $"trackIsValid={trackIsValid}, trackTotalLen={trackTotalLen:F3}. " +
                        "Монеты не спавним, пока не можем вычислить sPlayer.");
                }
                return;
            }

            // Монеты должны появляться только вперёд относительно игрока.
            _nextSpawnForwardOffset = Mathf.Max(_nextSpawnForwardOffset, _spawnStepZ);

            while (_nextSpawnForwardOffset <= _spawnAheadDistance && (_maxActiveCoins == 0 || _spawnedCoins.Count < _maxActiveCoins))
            {
                // 1) Выбираем полосу (lane) через стратегию.
                // Если стратегия не задана — используем случайную полосу (дефолтное поведение).
                var laneIndex = _laneSelector != null ? _laneSelector.GetLaneIndex(_trackSettings.LaneCount) : GetRandomLaneIndex();
                var laneOffsetX = GetLaneCenterOffsetX(laneIndex);

                // 2) Рассчитываем позицию и направление трассы в точке спавна,
                //    чтобы монетка "смотрела" вдоль дороги (как игрок).
                var success = false;
                var worldPosition = GetSpawnWorldPosition(
                    laneOffsetX,
                    _nextSpawnForwardOffset,
                    hasPlayerDistanceAlongTrack ? playerDistanceAlongTrack : 0f,
                    out var forwardAtSpawn,
                    out success);

                if (!success)
                {
                    if (ShouldDebugLog())
                    {
                        var passedPlayerS = hasPlayerDistanceAlongTrack ? playerDistanceAlongTrack : 0f;
                        var trackIsValid = _trackPath != null && _trackPath.IsValidPath;
                        var trackTotalLen = _trackPath != null ? _trackPath.TotalLength : -1f;
                        var trackRuntimeVer = _trackPath != null ? _trackPath.RuntimeVersion : -1;

                        Debug.LogWarning(
                            $"[{nameof(CoinSpawner)}] Spawn loop: GetSpawnWorldPosition success=false. " +
                            $"hasPlayerS={hasPlayerDistanceAlongTrack}, passedPlayerS={passedPlayerS:F3}, " +
                            $"forwardOffset={_nextSpawnForwardOffset:F3}, trackTotalLen={trackTotalLen:F3}, " +
                            $"trackRuntimeVer={trackRuntimeVer}, trackIsValid={trackIsValid}. " +
                            "Спавн прекращаем, чтобы не получить накладки.");
                    }
                    // TrackPath не может вычислить кадр в этой точке — прекращаем спавн,
                    // чтобы не получить монеты "на месте".
                    break;
                }

                if (_debugSpawnLogsCount < _debugMaxSpawnLogs && _debugSpawnLogging)
                {
                    var passedS = hasPlayerDistanceAlongTrack ? playerDistanceAlongTrack : 0f;
                    var sSpawn = passedS + _nextSpawnForwardOffset;
                    var dxz = new Vector2(worldPosition.x - _lastSpawnWorldPosition.x, worldPosition.z - _lastSpawnWorldPosition.z);
                    var sqrDistToLast = _hasLastSpawnWorldPosition ? dxz.sqrMagnitude : -1f;

                    var distXZToLast = "n/a";
                    if (_hasLastSpawnWorldPosition && sqrDistToLast >= 0f)
                    {
                        distXZToLast = sqrDistToLast.ToString("F4");
                    }

                    var trackTotalLen = _trackPath != null ? _trackPath.TotalLength : -1f;
                    var trackRuntimeVer = _trackPath != null ? _trackPath.RuntimeVersion : -1;

                    Debug.LogWarning(
                        $"[{nameof(CoinSpawner)}] SpawnCoin: idx={_debugSpawnLogsCount + 1}, " +
                        $"laneOffsetX={laneOffsetX:F3}, forwardOffset={_nextSpawnForwardOffset:F3}, sSpawn={sSpawn:F3}, " +
                        $"worldPos=({worldPosition.x:F3},{worldPosition.y:F3},{worldPosition.z:F3}), " +
                        $"distXZToLast={distXZToLast}, " +
                        $"trackTotalLen={trackTotalLen:F3}, trackRuntimeVer={trackRuntimeVer}");
                    _debugSpawnLogsCount++;
                }

                // 3) Не спавним дубликаты на уже созданных позициях.
                // Коллизия по sSpawn может случаться, если курсор/путь временно не совпадают,
                // поэтому ключ хранит дискретизированный sSpawn и индекс полосы.
                var sKey = Mathf.RoundToInt((playerDistanceAlongTrack + _nextSpawnForwardOffset) / Mathf.Max(0.001f, _minZSpacing));
                var spawnKey = (long)sKey * 1000L + laneIndex;
                if (_spawnedCoinKeys.Contains(spawnKey))
                {
                    _nextSpawnForwardOffset += Mathf.Max(_spawnStepZ, _minZSpacing);
                    continue;
                }

                _spawnedCoinKeys.Add(spawnKey);

                var rotation = GetRandomRotation(forwardAtSpawn);

                var coin = Instantiate(_coinPrefab, worldPosition, rotation, transform);
                var coinComponent = coin.GetComponent<Coin>();
                if (coinComponent != null)
                {
                    coinComponent.SetScoreService(_scoreService);
                }
                _spawnedCoins.Add(coin);
                _lastSpawnWorldPosition = worldPosition;
                _hasLastSpawnWorldPosition = true;

                _nextSpawnForwardOffset += Mathf.Max(_spawnStepZ, _minZSpacing);
            }
        }

        private void DespawnBehindPlayer()
        {
            // Определяем "позади" относительно направления движения игрока.
            // Если монета ушла назад по forward больше чем на despawnBehindDistance — удаляем.
            float playerDistanceAlongTrack = 0f;
            var hasPlayerDistanceAlongTrack = false;
            if (_trackPath != null && _trackPath.TryGetClosestDistance(_player.position, out var playerDistance))
            {
                playerDistanceAlongTrack = playerDistance;
                hasPlayerDistanceAlongTrack = true;
            }

            var despawnThreshold = hasPlayerDistanceAlongTrack ? playerDistanceAlongTrack - _despawnBehindDistance : 0f;

            // Fallback: эвристика по dot product (когда TrackPath недоступен).
            var forward = GetForward();

            for (var i = _spawnedCoins.Count - 1; i >= 0; i--)
            {
                var coin = _spawnedCoins[i];
                if (coin == null)
                {
                    _spawnedCoins.RemoveAt(i);
                    continue;
                }

                if (hasPlayerDistanceAlongTrack && _trackPath != null && _trackPath.TryGetClosestDistance(coin.transform.position, out var coinDistance))
                {
                    // Строго по кривой: удаляем, если монета ушла "позади" на нужную дистанцию.
                    if (coinDistance < despawnThreshold)
                    {
                        Destroy(coin);
                        _spawnedCoins.RemoveAt(i);
                    }

                    continue;
                }

                // Fallback: когда нельзя вычислить s по кривой.
                var toCoin = coin.transform.position - _player.position;
                var forwardDistance = Vector3.Dot(toCoin, forward);
                if (forwardDistance < -_despawnBehindDistance)
                {
                    Destroy(coin);
                    _spawnedCoins.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Вычисляет мировую позицию монетки и направление трассы в точке спавна.
        /// </summary>
        /// <param name="laneOffsetX">Смещение по полосе (вдоль "right" трассы) относительно центра.</param>
        /// <param name="forwardOffset">Смещение вперёд (вдоль трассы) от текущей позиции игрока.</param>
        /// <param name="playerDistanceAlongTrack">Текущая глобальная дистанция игрока вдоль TrackPath (sPlayer).</param>
        /// <param name="forwardAtSpawn">Нормализованный вектор направления трассы в точке спавна.</param>
        private Vector3 GetSpawnWorldPosition(
            float laneOffsetX,
            float forwardOffset,
            float playerDistanceAlongTrack,
            out Vector3 forwardAtSpawn,
            out bool success)
        {
            success = false;
            forwardAtSpawn = Vector3.forward;

            // Корректное размещение по кривой:
            // sSpawn = sPlayer + forwardOffset
            // frame  = TrackPath на расстоянии sSpawn (centerPos/forward/right)
            // world  = centerPos + right * (laneOffsetX + LaneCenterX)
            //
            // Важно: мы НЕ добавляем forward * forwardOffset вручную,
            // потому что forward/right должны соответствовать именно точке sSpawn.
            var worldPosition = _player.position;
            var forward = Vector3.forward;
            var right = Vector3.right;

            if (!TryGetSpawnFrameOnTrack(laneOffsetX, forwardOffset, playerDistanceAlongTrack, out var centerPos, out var fwd, out var rgt))
            {
                return worldPosition;
            }

            worldPosition = centerPos + rgt * (laneOffsetX + _trackSettings.LaneCenterX);
            forward = fwd;
            right = rgt;
            worldPosition.y += _spawnHeight;

            if (!_snapToGround)
            {
                forwardAtSpawn = forward.normalized;
                success = true;
                return worldPosition;
            }

            SnapToGround(ref worldPosition);
            forwardAtSpawn = forward.normalized;
            success = true;
            return worldPosition;
        }

        /// <summary>
        /// Пытается вычислить frame трассы в точке спавна и убедиться, что место доступно.
        /// </summary>
        private bool TryGetSpawnFrameOnTrack(
            float laneOffsetX,
            float forwardOffset,
            float playerDistanceAlongTrack,
            out Vector3 centerPos,
            out Vector3 forwardAtSpawn,
            out Vector3 rightAtSpawn)
        {
            centerPos = Vector3.zero;
            forwardAtSpawn = Vector3.forward;
            rightAtSpawn = Vector3.right;

            if (_trackPath == null || !_trackPath.IsValidPath)
            {
                return false;
            }

            var sSpawn = playerDistanceAlongTrack + forwardOffset;
            var trackTotalLen = _trackPath.TotalLength;

            // TrackPath.TryEvaluateFrame внутри себя "зажимает" distance к концу пути,
            // поэтому по факту места мы не узнаем. Явно проверяем границы.
            if (sSpawn < 0f || sSpawn > trackTotalLen - _spawnSLengthEpsilon)
            {
                return false;
            }

            if (_trackPath.TryEvaluateFrame(sSpawn, out centerPos, out var fwd, out var rgt))
            {
                forwardAtSpawn = fwd;
                rightAtSpawn = rgt;
                return true;
            }

            if (ShouldDebugLog())
            {
                var trackRuntimeVer = _trackPath.RuntimeVersion;
                Debug.LogWarning(
                    $"[{nameof(CoinSpawner)}] TryGetSpawnFrameOnTrack: TryEvaluateFrame=false. " +
                    $"sSpawn={sSpawn:F3}, playerS={playerDistanceAlongTrack:F3}, forwardOffset={forwardOffset:F3}, " +
                    $"trackTotalLen={trackTotalLen:F3}, trackRuntimeVer={trackRuntimeVer}, laneOffsetX={laneOffsetX:F3}");
            }

            return false;
        }

        /// <summary>
        /// Привязывает Y позиции монеты к поверхности трассы (Raycast вниз).
        /// </summary>
        private void SnapToGround(ref Vector3 worldPosition)
        {
            if (_groundSnapper != null)
            {
                _groundSnapper.SnapToGround(
                    ref worldPosition,
                    _groundMask,
                    _groundRaycastStartHeight,
                    _groundRaycastDistance,
                    _spawnHeight);
                return;
            }

            // Начинаем луч выше и стреляем вниз, чтобы найти поверхность трассы даже на неровностях.
            var rayOrigin = worldPosition + Vector3.up * _groundRaycastStartHeight;
            var ray = new Ray(rayOrigin, Vector3.down);

            // Если маска не задана (value == 0), Raycast по умолчанию будет попадать во всё,
            // но это может "цеплять" лишние объекты. Поэтому в учебном проекте лучше явно задавать слой земли.
            var mask = _groundMask.value != 0 ? _groundMask.value : Physics.DefaultRaycastLayers;

            if (Physics.Raycast(ray, out var hit, _groundRaycastDistance, mask, QueryTriggerInteraction.Ignore))
            {
                // Ставим монету над поверхностью.
                worldPosition.y = hit.point.y + _spawnHeight;
            }
        }

        private Vector3 GetForward()
        {
            if (_trackPath != null && _trackPath.TryGetClosestFrame(_player.position, out _, out _, out var forward, out _))
            {
                return forward;
            }

            return _player != null ? _player.forward : Vector3.forward;
        }

        private int GetRandomLaneIndex()
        {
            // Индекс полосы: 0..LaneCount-1.
            return Random.Range(0, Mathf.Max(1, _trackSettings.LaneCount));
        }

        private float GetLaneCenterOffsetX(int laneIndex)
        {
            var centerIndex = (_trackSettings.LaneCount - 1) * 0.5f;
            return (laneIndex - centerIndex) * _trackSettings.LaneWidth;
        }

        // GetRandomPositionWithSpacing больше не нужен: в динамическом режиме шаг задаётся через _spawnStepZ/_minZSpacing.

        /// <summary>
        /// Возвращает случайный поворот монетки с учётом направления трассы.
        /// </summary>
        /// <param name="forwardAtSpawn">Направление трассы в точке спавна (из <see cref="GetSpawnWorldPosition"/>).</param>
        private Quaternion GetRandomRotation(Vector3 forwardAtSpawn)
        {
            // Базовую ориентацию монетки выравниваем вдоль направления трассы в точке спавна,
            // а затем добавляем небольшой случайный разброс, чтобы сцена не выглядела слишком "идеальной".
            var randomOffset = new Vector3(
                Random.Range(-_rotationSpreadEuler.x, _rotationSpreadEuler.x),
                Random.Range(-_rotationSpreadEuler.y, _rotationSpreadEuler.y),
                Random.Range(-_rotationSpreadEuler.z, _rotationSpreadEuler.z));

            var finalEuler = _baseRotationEuler + randomOffset;
            var baseRotation = Quaternion.LookRotation(forwardAtSpawn != Vector3.zero ? forwardAtSpawn : Vector3.forward, Vector3.up);
            return baseRotation * Quaternion.Euler(finalEuler);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = _gizmoColor;

            if (_trackSettings != null && _trackSettings.LaneCount > 1)
            {
                // Рисуем линии центров полос, чтобы в редакторе было видно, где появятся монеты.
                Gizmos.color = new Color(_gizmoColor.r, _gizmoColor.g, _gizmoColor.b, Mathf.Clamp01(_gizmoColor.a + 0.3f));

                for (var i = 0; i < _trackSettings.LaneCount; i++)
                {
                    var laneOffsetX = GetLaneCenterOffsetX(i);
                    // Рисуем линию вдоль forward TrackPath (приблизительно), если он задан.
                    // Для простоты используем transform TrackPath как "ориентир", а не вычисляем реальную кривую.
                    var frame = _trackPath != null ? _trackPath.transform : transform;
                    var start = frame.position + frame.right * laneOffsetX + Vector3.up * _spawnHeight;
                    var end = frame.position + frame.forward * _spawnAheadDistance + frame.right * laneOffsetX + Vector3.up * _spawnHeight;
                    Gizmos.DrawLine(start, end);
                }
            }
        }
    }
}

