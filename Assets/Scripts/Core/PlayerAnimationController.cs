using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Управляет параметрами Animator игрока на основе данных из <see cref="PlayerController"/>.
    /// Предназначен для работы с humanoid‑моделью и базовыми анимациями (Idle/Run/Jump/Fall).
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class PlayerAnimationController : MonoBehaviour
    {
        /// <summary>
        /// Контроллер игрока, из которого берутся данные для анимации.
        /// </summary>
        [SerializeField]
        private PlayerController _playerController;

        /// <summary>
        /// Минимальная скорость вперёд, с которой включается анимация бега.
        /// </summary>
        [SerializeField]
        private float _minRunSpeed = 0.1f;

        /// <summary>
        /// Нужно ли автоматически подстраивать скорость проигрывания анимации под скорость бега.
        /// </summary>
        [SerializeField]
        private bool _scaleAnimatorSpeedByForwardSpeed = true;

        /// <summary>
        /// Ограничение скорости Animator (чтобы анимация не «разъезжалась» при ускорении).
        /// </summary>
        [SerializeField]
        private Vector2 _animatorSpeedRange = new(0.8f, 1.6f);

        private Animator _animator;
        private float _baseAnimatorSpeed = 1f;

        // Чтобы не искать параметры по строке каждый кадр (это медленнее и создаёт риск опечатки),
        // мы заранее преобразуем имена параметров в int-хэши.
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");
        private static readonly int VerticalVelocityHash = Animator.StringToHash("VerticalVelocity");

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            // Запоминаем исходную скорость Animator, чтобы при выключенной автоподстройке вернуть значение назад.
            _baseAnimatorSpeed = _animator.speed;
        }

        private void Reset()
        {
            // Reset вызывается Unity при добавлении компонента/сбросе в инспекторе.
            // Здесь удобно автоматически связать компонент с PlayerController на родителе.
            _playerController = GetComponentInParent<PlayerController>();
        }

        private void Update()
        {
            if (_playerController == null)
            {
                return;
            }

            // Берём «сырые» данные от контроллера:
            // - скорость Rigidbody нужна для VerticalVelocity (jump/fall)
            // - скорость бега берём из ActualForwardSpeed, чтобы не было «бега на месте» при ошибке конфигурации
            var velocity = _playerController.Velocity;
            var forwardSpeed = Mathf.Abs(_playerController.ActualForwardSpeed);
            var isGrounded = _playerController.IsGrounded;

            // Эти параметры должны существовать в Animator Controller с теми же именами:
            // Speed (float), IsGrounded (bool), VerticalVelocity (float).
            _animator.SetFloat(SpeedHash, forwardSpeed);
            _animator.SetBool(IsGroundedHash, isGrounded);
            _animator.SetFloat(VerticalVelocityHash, velocity.y);

            if (_scaleAnimatorSpeedByForwardSpeed)
            {
                // Если мы ускоряем/замедляем бег (W/S), полезно подгонять скорость анимации,
                // чтобы ноги не «скользили» по земле.
                ApplyAnimatorSpeed(forwardSpeed);
            }
            else
            {
                _animator.speed = _baseAnimatorSpeed;
            }
        }

        private void ApplyAnimatorSpeed(float forwardSpeed)
        {
            if (forwardSpeed <= _minRunSpeed)
            {
                // Если скорость почти нулевая — не меняем скорость Animator.
                _animator.speed = _baseAnimatorSpeed;
                return;
            }

            // Нормализуем текущую скорость в диапазон 0..1, чтобы удобно интерполировать скорость Animator.
            // Верхняя граница берётся из CurrentForwardSpeed (целевой скорости), чтобы масштабирование было предсказуемым.
            var normalized = Mathf.InverseLerp(_minRunSpeed, Mathf.Max(_minRunSpeed, _playerController.CurrentForwardSpeed), forwardSpeed);
            var targetSpeed = Mathf.Lerp(_animatorSpeedRange.x, _animatorSpeedRange.y, normalized);
            _animator.speed = _baseAnimatorSpeed * targetSpeed;
        }
    }
}

