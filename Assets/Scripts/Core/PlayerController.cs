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
        /// <summary>
        /// Горизонтальная скорость перемещения по оси X.
        /// </summary>
        [SerializeField]
        private float _horizontalSpeed = 5f;

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
        /// Текущее направление движения, полученное от системы ввода.
        /// Ось X используется для перемещения влево и вправо.
        /// </summary>
        private Vector2 _moveInput;

        /// <summary>
        /// Стартовая позиция игрока для восстановления после падения.
        /// </summary>
        private Vector3 _startPosition;

        private Rigidbody _playerRigidbody;

        /// <summary>
        /// Текущий множитель скорости движения вперёд.
        /// </summary>
        private float _currentSpeedMultiplier = 1f;

        /// <summary>
        /// Обработчик действия Move из новой системы ввода.
        /// Вызывается компонентом PlayerInput при Behavior = Send Messages.
        /// </summary>
        /// <param name="value">Значение вектора движения.</param>
        public void OnMove(InputValue value)
        {
            _moveInput = value.Get<Vector2>();
        }

        private void Awake()
        {
            _playerRigidbody = GetComponent<Rigidbody>();
            _startPosition = transform.position;
        }

        private void FixedUpdate()
        {
            var velocity = _playerRigidbody.linearVelocity;

            // Определяем целевой множитель скорости в зависимости от ввода по оси Y (W/S).
            var targetMultiplier = 1f;
            if (_moveInput.y > 0.1f)
            {
                targetMultiplier = _maxSpeedMultiplier;
            }
            else if (_moveInput.y < -0.1f)
            {
                targetMultiplier = _minSpeedMultiplier;
            }

            // Плавно изменяем текущий множитель к целевому.
            _currentSpeedMultiplier = Mathf.MoveTowards(
                _currentSpeedMultiplier,
                targetMultiplier,
                _speedChangeRate * Time.fixedDeltaTime);

            var currentForwardSpeed = _baseForwardSpeed * _currentSpeedMultiplier;

            // Постоянное движение вперёд
            velocity.z = currentForwardSpeed;

            // Управление по оси X
            velocity.x = _moveInput.x * _horizontalSpeed;

            _playerRigidbody.linearVelocity = velocity;
        }

        private void Update()
        {
            if (Keyboard.current != null &&
                Keyboard.current.spaceKey.wasPressedThisFrame &&
                IsGrounded())
            {
                Jump();
            }
        }

        private bool IsGrounded()
        {
            Vector3 checkPosition = transform.position + _groundCheckOffset;
            var isGrounded = Physics.CheckSphere(checkPosition, _groundCheckRadius, _groundLayer, QueryTriggerInteraction.Ignore);
            Debug.Log("IsGrounded: " + isGrounded);
            return isGrounded;
        }

        private void Jump()
        {
            Debug.Log("Jump");
            Vector3 velocity = _playerRigidbody.linearVelocity;
            velocity.y = 0f;
            _playerRigidbody.linearVelocity = velocity;

            _playerRigidbody.AddForce(Vector3.up * _jumpForce, ForceMode.Impulse);
        }

        /// <summary>
        /// Возвращает игрока в стартовую позицию и останавливает движение.
        /// </summary>
        public void Respawn()
        {
            transform.position = _startPosition;
            _playerRigidbody.linearVelocity = Vector3.zero;
            _playerRigidbody.angularVelocity = Vector3.zero;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Vector3 checkPosition = transform.position + _groundCheckOffset;
            Gizmos.DrawWireSphere(checkPosition, _groundCheckRadius);
        }
    }
}