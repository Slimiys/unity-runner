using UnityEngine;
using UnityEngine.InputSystem;

namespace Sandbox
{
    /// <summary>
    /// Управляет движением игрока в 3D‑раннере, используя новую систему ввода и физику.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PlayerController : MonoBehaviour, IRespawnable
    {
        private const string PlayerTag = "Player";
        private const float GroundLayerUnsetThreshold = 0f;

        /// <summary>
        /// Отвечает только за проверку контакта игрока с землёй.
        /// </summary>
        private sealed class GroundChecker
        {
            private readonly Vector3 _checkOffset;
            private readonly float _checkRadius;
            private readonly LayerMask _groundLayer;

            public GroundChecker(Vector3 checkOffset, float checkRadius, LayerMask groundLayer)
            {
                _checkOffset = checkOffset;
                _checkRadius = checkRadius;
                _groundLayer = groundLayer;
            }

            public bool IsGrounded(Transform playerRoot)
            {
                var checkPosition = playerRoot.position + _checkOffset;
                return Physics.CheckSphere(checkPosition, _checkRadius, _groundLayer, QueryTriggerInteraction.Ignore);
            }
        }

        /// <summary>
        /// Отвечает только за запуск прыжка (вертикальная скорость + импульс).
        /// </summary>
        private sealed class JumpExecutor
        {
            private readonly float _jumpForce;

            public JumpExecutor(float jumpForce)
            {
                _jumpForce = jumpForce;
            }

            public void Jump(Rigidbody playerRigidbody)
            {
                var velocity = playerRigidbody.linearVelocity;
                // Сбрасываем вертикальную скорость перед прыжком, чтобы прыжок всегда был одинаковым.
                velocity.y = 0f;
                playerRigidbody.linearVelocity = velocity;
                playerRigidbody.AddForce(Vector3.up * _jumpForce, ForceMode.Impulse);
            }
        }

        /// <summary>
        /// Текущая скорость игрока (в мировых координатах), взятая из Rigidbody.
        /// </summary>
        /// <remarks>
        /// В Unity корректнее считывать скорость именно из <see cref="Rigidbody"/>, потому что физика обновляет её сама.
        /// Это значение удобно использовать для анимаций (например, вертикальная скорость для Jump/Fall).
        /// </remarks>
        public Vector3 Velocity => _playerRigidbody != null ? _playerRigidbody.linearVelocity : Vector3.zero;

        /// <summary>
        /// Текущая скорость движения вперёд по оси Z (с учётом множителя скорости).
        /// </summary>
        /// <remarks>
        /// Мы задаём движение вперёд принудительно каждый физический кадр, поэтому эта скорость — «целевое» значение.
        /// </remarks>
        public float CurrentForwardSpeed => _baseForwardSpeed * _currentSpeedMultiplier;

        /// <summary>
        /// Фактическая скорость движения вперёд, которую мы применили в текущем физическом кадре.
        /// </summary>
        /// <remarks>
        /// Нужна, чтобы анимация не показывала «бег на месте», когда движение не выполняется из-за конфигурации.
        /// </remarks>
        public float ActualForwardSpeed => _actualForwardSpeed;

        /// <summary>
        /// Признак того, что игрок находится на земле (по проверке сферы).
        /// </summary>
        /// <remarks>
        /// Это свойство вычисляется «на лету». Для оптимизации можно кэшировать результат, но на старте важнее понятность.
        /// </remarks>
        public bool IsGrounded => CheckIsGrounded();

        /// <summary>
        /// Количество полос, по которым может перемещаться игрок.
        /// </summary>
        public int LaneCount => _laneCount;

        /// <summary>
        /// Ширина одной полосы (расстояние между центрами полос) в мировых единицах по оси X.
        /// </summary>
        public float LaneWidth => _laneWidth;

        /// <summary>
        /// Горизонтальная скорость перемещения по оси X.
        /// </summary>
        [SerializeField]
        private float _horizontalSpeed = 5f;

        /// <summary>
        /// Количество полос на трассе (обычно 3).
        /// </summary>
        [SerializeField]
        [Min(1)]
        private int _laneCount = 3;

        /// <summary>
        /// Ширина одной полосы по оси X (расстояние между центрами полос).
        /// </summary>
        [SerializeField]
        [Min(0.1f)]
        private float _laneWidth = 1.5f;

        /// <summary>
        /// Настройки трассы, из которых берём параметры полос.
        /// </summary>
        [SerializeField]
        private TrackSettings _trackSettings;

        /// <summary>
        /// Путь трассы (центральная линия), по которому бежит игрок.
        /// </summary>
        /// <remarks>
        /// В строгом режиме обязателен: движение выполняется вдоль центральной линии трассы.
        /// </remarks>
        [SerializeField]
        private TrackPath _trackPath;

        /// <summary>
        /// Скорость перестроения между полосами (единиц X в секунду).
        /// </summary>
        [SerializeField]
        [Min(0.1f)]
        private float _laneChangeSpeed = 8f;

        // _laneInputThreshold больше не нужен: перестроение обрабатывается отдельным действием Input System (ChangeLane).

        /// <summary>
        /// Базовая скорость движения вперёд по оси Z (100%).
        /// </summary>
        [SerializeField]
        private float _baseForwardSpeed = 5f;

        /// <summary>
        /// Максимальный множитель скорости при ускорении (клавиша W).
        /// </summary>
        [SerializeField]
        private float _maxSpeedMultiplier = 1.5f;

        /// <summary>
        /// Минимальный множитель скорости при замедлении (клавиша S).
        /// </summary>
        [SerializeField]
        private float _minSpeedMultiplier = 0.5f;

        /// <summary>
        /// Скорость изменения множителя скорости во времени.
        /// </summary>
        [SerializeField]
        private float _speedChangeRate = 2f;

        /// <summary>
        /// Сила прыжка.
        /// </summary>
        [SerializeField]
        private float _jumpForce = 5f;

        /// <summary>
        /// Слой, по которому определяется земля для проверки возможности прыжка.
        /// </summary>
        [SerializeField]
        private LayerMask _groundLayer;

        /// <summary>
        /// Радиус сферы проверки касания земли.
        /// </summary>
        [SerializeField]
        private float _groundCheckRadius = 0.2f;

        /// <summary>
        /// Смещение точки проверки касания земли относительно центра игрока.
        /// </summary>
        [SerializeField]
        private Vector3 _groundCheckOffset = new(0f, -0.5f, 0f);

        /// <summary>
        /// Компонент, который читает ввод через Input System и отдаёт его игровому коду.
        /// </summary>
        [SerializeField]
        private PlayerInputReader _inputReader;

        // Индекс текущей полосы. 0 — крайняя левая, (_laneCount - 1) — крайняя правая.
        // Важно: это чисто "абстрактный" номер полосы, он не зависит от поворотов трассы.
        private int _currentLaneIndex;

        // Текущее и целевое смещения полосы вдоль оси "right" трассы (в мировых единицах).
        // Положительное значение — вправо от центра трассы (относительно её локального "right"),
        // отрицательное — влево. Мы больше не работаем с мировой осью X — только с осью трассы.
        private float _currentLaneOffset;
        private float _targetLaneOffset;

        // Пройденная дистанция вдоль пути трассы (0..TrackPath.TotalLength).
        private float _distanceAlongTrack;

        // Перестроение обрабатывается дискретно через Input System (ChangeLane).

        /// <summary>
        /// Стартовая позиция игрока для восстановления после падения.
        /// </summary>
        private Vector3 _startPosition;

        private Rigidbody _playerRigidbody;

        /// <summary>
        /// Текущий множитель скорости движения вперёд.
        /// </summary>
        private float _currentSpeedMultiplier = 1f;

        // Фактическая скорость вперёд (для анимаций/диагностики).
        private float _actualForwardSpeed;

        private GroundChecker _groundChecker;
        private JumpExecutor _jumpExecutor;

        private void Awake()
        {
            _playerRigidbody = GetComponent<Rigidbody>();
            _startPosition = transform.position;

            // В строгом режиме допускаем автопривязку TrackSettings только от явно назначенного TrackPath.
            if (_trackSettings == null && _trackPath != null)
            {
                _trackSettings = _trackPath.GetComponent<TrackSettings>();
            }

            SyncLaneSettingsFromTrack();

            InitializeTrackDistance();

            if (_inputReader == null)
            {
                _inputReader = GetComponent<PlayerInputReader>();
            }

            if (_inputReader != null)
            {
                _inputReader.LaneLeftPressed += OnLaneLeftPressed;
                _inputReader.LaneRightPressed += OnLaneRightPressed;
                _inputReader.JumpPressed += OnJumpPressed;
            }

            InitializeLanePosition();
            if (!ValidateConfiguration(isEditorOnly: false))
            {
                enabled = false;
                return;
            }

            // Делаем из "параметров инспектора" маленькие неизменяемые помощники.
            _groundChecker = new GroundChecker(_groundCheckOffset, _groundCheckRadius, _groundLayer);
            _jumpExecutor = new JumpExecutor(_jumpForce);
        }

        private void OnDestroy()
        {
            if (_inputReader != null)
            {
                _inputReader.LaneLeftPressed -= OnLaneLeftPressed;
                _inputReader.LaneRightPressed -= OnLaneRightPressed;
                _inputReader.JumpPressed -= OnJumpPressed;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // OnValidate вызывается в редакторе при изменении значений в инспекторе,
            // при добавлении компонента и при загрузке сцены. Это удобное место для «самопроверок»,
            // чтобы ловить типичные ошибки настройки ещё до запуска Play Mode.
            SyncLaneSettingsFromTrack();
            _ = ValidateConfiguration(isEditorOnly: true);
        }
#endif

        private bool ValidateConfiguration(bool isEditorOnly)
        {
            // 1) Тег игрока нужен для взаимодействий (например, DeathZone и Coin).
            if (!CompareTag(PlayerTag))
            {
                Debug.LogWarning(
                    $"[{nameof(PlayerController)}] На объекте '{name}' рекомендуется поставить тег '{PlayerTag}', иначе триггеры (например DeathZone) могут не сработать.",
                    this);
            }

            // 2) GroundLayer должен быть задан, иначе IsGrounded почти всегда будет false.
            // LayerMask хранится как битовая маска: значение 0 означает «не выбрано ни одного слоя».
            if (_groundLayer.value == GroundLayerUnsetThreshold)
            {
                Debug.LogWarning(
                    $"[{nameof(PlayerController)}] Поле Ground Layer не настроено. Укажи слой(и), на котором находится земля, чтобы прыжок и анимации работали корректно.",
                    this);
            }

            // 3) Коллайдер на игроке обычно обязателен: иначе он проваливается сквозь пол и не взаимодействует с триггерами предсказуемо.
            // Важно: коллайдер может быть как на корне, так и на дочернем объекте. Здесь проверяем наличие в иерархии.
            var collider = GetComponentInChildren<Collider>();
            if (collider == null)
            {
                Debug.LogWarning(
                    $"[{nameof(PlayerController)}] На игроке не найден Collider. Добавь, например, CapsuleCollider на корневой объект '{name}', чтобы физика работала корректно.",
                    this);
            }

            // 4) Rigidbody обязателен (атрибут RequireComponent уже подсказывает Unity), но проверка помогает, если компонент удалили.
            if (_playerRigidbody == null)
            {
                _playerRigidbody = GetComponent<Rigidbody>();
            }

            if (_playerRigidbody == null)
            {
                Debug.LogError(
                    $"[{nameof(PlayerController)}] Не найден Rigidbody на объекте '{name}'. Без него движение и прыжок работать не будут.",
                    this);
                return false;
            }

            // 5) Для раннера ожидаем, что игрок не вращается от столкновений. Это не ошибка, но хороший дефолт.
            // В режиме проверки в редакторе мы не меняем настройки автоматически — только предупреждаем.
            if (_playerRigidbody.freezeRotation == false)
            {
                Debug.LogWarning(
                    $"[{nameof(PlayerController)}] Rigidbody.freezeRotation выключен. Рекомендуется включить, чтобы игрок не заваливался от физики (особенно на мобильных).",
                    this);
            }

            // 6) Для управления через Input System нужен PlayerInputReader (и PlayerInput рядом с ним).
            if (_inputReader == null)
            {
                Debug.LogWarning(
                    $"[{nameof(PlayerController)}] Не найден {nameof(PlayerInputReader)}. Добавь компонент на объект '{name}', чтобы управление работало через Input System.",
                    this);
            }

            if (_trackSettings == null)
            {
                var message = $"[{nameof(PlayerController)}] Не задан {nameof(TrackSettings)}. В строгом режиме игрок не может работать без настроек трассы.";
                if (isEditorOnly)
                {
                    Debug.LogWarning(message, this);
                }
                else
                {
                    Debug.LogError(message, this);
                }

                if (!isEditorOnly)
                {
                    return false;
                }
            }

            if (_trackPath == null)
            {
                var message = $"[{nameof(PlayerController)}] Не задан {nameof(TrackPath)}. В строгом режиме игрок не может двигаться без пути трассы.";
                if (isEditorOnly)
                {
                    Debug.LogWarning(message, this);
                }
                else
                {
                    Debug.LogError(message, this);
                }

                if (!isEditorOnly)
                {
                    return false;
                }
            }
            else
            {
                if (!_trackPath.IsValidPath)
                {
                    var message =
                        $"[{nameof(PlayerController)}] {nameof(TrackPath)} задан, но путь невалиден (нужно минимум 2 точки и ненулевая длина). " +
                        $"Проверь точки в компоненте {nameof(TrackPath)} или включи динамическую генерацию.";

                    if (isEditorOnly)
                    {
                        Debug.LogWarning(message, this);
                    }
                    else
                    {
                        Debug.LogError(message, this);
                        return false;
                    }
                }
            }

            return true;
        }

        private void SyncLaneSettingsFromTrack()
        {
            if (_trackSettings == null)
            {
                return;
            }

            _laneCount = Mathf.Max(1, _trackSettings.LaneCount);
            _laneWidth = Mathf.Max(0.1f, _trackSettings.LaneWidth);
        }

        private void InitializeTrackDistance()
        {
            if (_trackPath == null)
            {
                return;
            }

            if (_trackPath.TryGetClosestDistance(transform.position, out var s))
            {
                _distanceAlongTrack = s;
            }
        }

        private void InitializeLanePosition()
        {
            // Ставим игрока на центральную полосу (если полос несколько).
            // Например, при 3 полосах: индекс 1 (левая — 0, центр — 1, правая — 2).
            _currentLaneIndex = Mathf.Clamp(_laneCount / 2, 0, Mathf.Max(0, _laneCount - 1));

            // Начальное смещение берём строго из индекса полосы.
            // Это означает: "поставь игрока в центр текущей полосы относительно трассы",
            // а не относительно мировых координат.
            _targetLaneOffset = GetLaneOffsetByIndex(_currentLaneIndex);
            _currentLaneOffset = _targetLaneOffset;
        }

        private float GetLaneOffsetByIndex(int laneIndex)
        {
            // Смещение полосы относительно центра трассы в единицах TrackSettings.LaneWidth.
            // Например:
            // - при LaneCount = 3 и LaneWidth = 1.5 получаем примерно -1.5, 0, +1.5
            // - при LaneCount = 1 всегда получаем 0 (одна центральная полоса).
            //
            // Идея: "центр трассы" — это нулевая линия, от неё мы откладываем полосы влево/вправо.
            var centerIndex = (_laneCount - 1) * 0.5f;
            return (laneIndex - centerIndex) * _laneWidth;
        }

        private void RequestLaneChange(int direction)
        {
            // Игнорируем 0 и «лево+право».
            if (direction == 0)
            {
                return;
            }

            // В классическом раннере перестроение дискретное: на 1 полосу за нажатие.
            // Поэтому мы просто увеличиваем/уменьшаем индекс, а реальные координаты получаем из TrackSettings.
            _currentLaneIndex = Mathf.Clamp(_currentLaneIndex + direction, 0, _laneCount - 1);

            // Целевое смещение вычисляем один раз, а плавное движение делаем в FixedUpdate,
            // чтобы не привязывать логику к частоте Update и не зависеть от FPS.
            _targetLaneOffset = GetLaneOffsetByIndex(_currentLaneIndex);
        }

        private void OnLaneLeftPressed()
        {
            RequestLaneChange(-1);
        }

        private void OnLaneRightPressed()
        {
            RequestLaneChange(1);
        }

        private void OnJumpPressed()
        {
            if (CheckIsGrounded())
            {
                Jump();
            }
        }

        private void FixedUpdate()
        {
            // FixedUpdate вызывается с фиксированным шагом времени и синхронизирован с физикой,
            // поэтому любые изменения Rigidbody (скорости/сил) корректнее делать именно здесь.
            var velocity = _playerRigidbody.linearVelocity;

            // Определяем целевой множитель скорости в зависимости от ввода по оси Y (W/S).
            var moveInput = _inputReader != null ? _inputReader.Move : Vector2.zero;
            var targetMultiplier = 1f;
            if (moveInput.y > 0.1f)
            {
                targetMultiplier = _maxSpeedMultiplier;
            }
            else if (moveInput.y < -0.1f)
            {
                targetMultiplier = _minSpeedMultiplier;
            }

            // Плавно изменяем текущий множитель к целевому.
            _currentSpeedMultiplier = Mathf.MoveTowards(
                _currentSpeedMultiplier,
                targetMultiplier,
                _speedChangeRate * Time.fixedDeltaTime);

            var currentForwardSpeed = _baseForwardSpeed * _currentSpeedMultiplier;
            _actualForwardSpeed = 0f;

            // Строгий режим: движение только по TrackPath.
            if (_trackPath == null)
            {
                return;
            }

            // 1) Считаем, куда игрок сместится по пути в этом физическом кадре.
            var proposedDistanceAlongTrack = _distanceAlongTrack + currentForwardSpeed * Time.fixedDeltaTime;

            // 2) Гарантируем, что TrackPath успевает "дорисовать" хвост впереди и обрезать позади.
            _trackPath.MaintainDynamicLength(proposedDistanceAlongTrack);

            // 3) Не "запираем" дистанцию на текущей TotalLength: доверяем MaintainDynamicLength.
            // Это убирает ситуацию, когда игрок упирается в "конец" из-за одного кадра,
            // где TotalLength ещё не успел увеличиться.
            _distanceAlongTrack = proposedDistanceAlongTrack;

            if (_trackPath.TryEvaluateFrame(_distanceAlongTrack, out var centerPos, out var forward, out var right))
            {
                // Плавно меняем смещение полосы в системе координат трассы.
                // Здесь мы уже работаем не с мировой осью X, а с локальной осью "right" дорожки.
                _currentLaneOffset = Mathf.MoveTowards(
                    _currentLaneOffset,
                    _targetLaneOffset,
                    _laneChangeSpeed * Time.fixedDeltaTime);

                // Базовое смещение центра трассы (TrackSettings.LaneCenterX) нужно,
                // если сама дорога в сцене находится не на X = 0.
                var baseCenterOffset = _trackSettings != null ? _trackSettings.LaneCenterX : _startPosition.x;
                var targetXZ = centerPos + right * (baseCenterOffset + _currentLaneOffset);
                var position = _playerRigidbody.position;
                position.x = targetXZ.x;
                position.z = targetXZ.z;
                _playerRigidbody.MovePosition(position);

                // Поворачиваем корневой объект игрока вдоль направления трассы,
                // чтобы модель и все навешанные компоненты были согласованы с движением.
                var targetRotation = Quaternion.LookRotation(forward, Vector3.up);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    targetRotation,
                    10f * Time.fixedDeltaTime);

                // Скорость задаём вдоль forward трассы, без боковой компоненты относительно пути.
                // Это предотвращает "снос" на поворотах: Rigidbody больше не имеет поперечной скорости,
                // только движение вперёд + вертикальная компонента (прыжок/падение).
                var verticalVelocity = velocity.y;
                var planarForwardVelocity = forward.normalized * currentForwardSpeed;
                _playerRigidbody.linearVelocity = new Vector3(
                    planarForwardVelocity.x,
                    verticalVelocity,
                    planarForwardVelocity.z);

                _actualForwardSpeed = currentForwardSpeed;

                // Корректируем _distanceAlongTrack по фактической позиции игрока.
                // Причина: при смещении по lane и поворотах трассы возможен небольшой дрейф,
                // из-за которого наша "прогнозная" s может чуть уйти вперёд.
                // Перепривязка к ближайшей точке по TrackPath удерживает игрока и генерацию синхронно.
                if (_trackPath.TryGetClosestDistance(_playerRigidbody.position, out var actualDistanceAlongTrack))
                {
                    _distanceAlongTrack = actualDistanceAlongTrack;
                }
            }
            else
            {
                // Если по какой‑то причине не смогли получить кадр пути — обнуляем движение вперёд,
                // чтобы игрок не "улетал" по инерции.
                _playerRigidbody.linearVelocity = new Vector3(0f, velocity.y, 0f);
                _actualForwardSpeed = 0f;
            }
        }

        private void Update()
        {
            // Update оставлен пустым намеренно: ввод обрабатывается через Input System в PlayerInputReader,
            // а физика и движение — в FixedUpdate.
        }

        private bool CheckIsGrounded() => _groundChecker != null && _groundChecker.IsGrounded(transform);

        private void Jump()
        {
            if (_jumpExecutor == null || _playerRigidbody == null)
            {
                return;
            }

            _jumpExecutor.Jump(_playerRigidbody);
        }

        /// <summary>
        /// Возвращает игрока в стартовую позицию и останавливает движение.
        /// </summary>
        public void Respawn()
        {
            transform.position = _startPosition;
            // Обнуляем скорости, чтобы после респауна игрок не «улетал» по инерции.
            _playerRigidbody.linearVelocity = Vector3.zero;
            _playerRigidbody.angularVelocity = Vector3.zero;

            // Возвращаем на центральную полосу и сбрасываем «нажатие обработано».
            InitializeLanePosition();
            InitializeTrackDistance();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Vector3 checkPosition = transform.position + _groundCheckOffset;
            Gizmos.DrawWireSphere(checkPosition, _groundCheckRadius);
        }
    }
}