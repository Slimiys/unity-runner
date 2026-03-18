using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Sandbox
{
    /// <summary>
    /// Читает ввод игрока через Input System и предоставляет его в удобном виде для игровых компонентов.
    /// </summary>
    /// <remarks>
    /// Этот компонент — альтернатива режиму <see cref="PlayerInput"/> «Send Messages».
    /// Вместо вызовов методов по имени мы явно подписываемся на события <see cref="InputAction"/>.
    /// </remarks>
    [RequireComponent(typeof(PlayerInput))]
    public class PlayerInputReader : MonoBehaviour
    {
        private const string GameplayMapName = "Gameplay";
        private const string MoveActionName = "Move";
        private const string ChangeLaneLeftActionName = "ChangeLaneLeft";
        private const string ChangeLaneRightActionName = "ChangeLaneRight";
        private const string JumpActionName = "Jump";

        /// <summary>
        /// Текущее значение движения из действия <c>Move</c>.
        /// </summary>
        /// <remarks>
        /// Ось X может использоваться для аналогового управления, ось Y — для ускорения/замедления и т.п.
        /// </remarks>
        public Vector2 Move { get; private set; }

        /// <summary>
        /// Событие вызывается при нажатии действия <c>ChangeLaneLeft</c>.
        /// </summary>
        public event Action LaneLeftPressed;

        /// <summary>
        /// Событие вызывается при нажатии действия <c>ChangeLaneRight</c>.
        /// </summary>
        public event Action LaneRightPressed;

        /// <summary>
        /// Событие вызывается при нажатии действия <c>Jump</c>.
        /// </summary>
        public event Action JumpPressed;

        private PlayerInput _playerInput;
        private InputAction _moveAction;
        private InputAction _changeLaneLeftAction;
        private InputAction _changeLaneRightAction;
        private InputAction _jumpAction;

        private Action<InputAction.CallbackContext> _onMovePerformed;
        private Action<InputAction.CallbackContext> _onMoveCanceled;
        private Action<InputAction.CallbackContext> _onLaneLeftPerformed;
        private Action<InputAction.CallbackContext> _onLaneRightPerformed;
        private Action<InputAction.CallbackContext> _onJumpPerformed;

        private void Awake()
        {
            _playerInput = GetComponent<PlayerInput>();
        }

        private void OnEnable()
        {
            BindActions();
            EnableActions();
        }

        private void OnDisable()
        {
            UnbindActions();
        }

        private void BindActions()
        {
            if (_playerInput == null || _playerInput.actions == null)
            {
                return;
            }

            var map = _playerInput.actions.FindActionMap(GameplayMapName, throwIfNotFound: false);
            if (map == null)
            {
                return;
            }

            _moveAction = map.FindAction(MoveActionName, throwIfNotFound: false);
            _changeLaneLeftAction = map.FindAction(ChangeLaneLeftActionName, throwIfNotFound: false);
            _changeLaneRightAction = map.FindAction(ChangeLaneRightActionName, throwIfNotFound: false);
            _jumpAction = map.FindAction(JumpActionName, throwIfNotFound: false);

            _onMovePerformed = ctx => Move = ctx.ReadValue<Vector2>();
            _onMoveCanceled = _ => Move = Vector2.zero;
            _onLaneLeftPerformed = _ => LaneLeftPressed?.Invoke();
            _onLaneRightPerformed = _ => LaneRightPressed?.Invoke();
            _onJumpPerformed = _ => JumpPressed?.Invoke();

            if (_moveAction != null)
            {
                _moveAction.performed += OnMovePerformed;
                _moveAction.canceled += OnMoveCanceled;
            }

            if (_changeLaneLeftAction != null)
            {
                _changeLaneLeftAction.performed += OnLaneLeftPerformed;
            }

            if (_changeLaneRightAction != null)
            {
                _changeLaneRightAction.performed += OnLaneRightPerformed;
            }

            if (_jumpAction != null)
            {
                _jumpAction.performed += OnJumpPerformed;
            }
        }

        private void EnableActions()
        {
            // PlayerInput обычно сам включает actions, но при ручных настройках/отключении компонента
            // безопаснее убедиться, что asset активен.
            _playerInput?.actions?.Enable();
        }

        private void UnbindActions()
        {
            if (_moveAction != null)
            {
                _moveAction.performed -= OnMovePerformed;
                _moveAction.canceled -= OnMoveCanceled;
            }

            if (_changeLaneLeftAction != null)
            {
                _changeLaneLeftAction.performed -= OnLaneLeftPerformed;
            }

            if (_changeLaneRightAction != null)
            {
                _changeLaneRightAction.performed -= OnLaneRightPerformed;
            }

            _moveAction = null;
            _changeLaneLeftAction = null;
            _changeLaneRightAction = null;
            _jumpAction = null;

            Move = Vector2.zero;
        }

        private void OnMovePerformed(InputAction.CallbackContext context)
        {
            _onMovePerformed?.Invoke(context);
        }

        private void OnMoveCanceled(InputAction.CallbackContext context)
        {
            _onMoveCanceled?.Invoke(context);
        }

        private void OnLaneLeftPerformed(InputAction.CallbackContext context)
        {
            _onLaneLeftPerformed?.Invoke(context);
        }

        private void OnLaneRightPerformed(InputAction.CallbackContext context)
        {
            _onLaneRightPerformed?.Invoke(context);
        }

        private void OnJumpPerformed(InputAction.CallbackContext context)
        {
            _onJumpPerformed?.Invoke(context);
        }
    }
}

