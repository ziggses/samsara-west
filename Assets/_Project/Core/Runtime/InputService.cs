using UnityEngine;
using UnityEngine.InputSystem;

namespace SamsaraWest.Core
{
    /// <summary>
    /// 输入服务。骨架期只提供接线位与服务边界：真正的八方向方格移动在探索阶段实现，
    /// 此处保证业务代码不直接依赖 <c>Keyboard.current</c> 这类全局静态入口。
    /// </summary>
    public interface IInputService : IService
    {
        bool IsEnabled { get; }

        bool IsConfigured { get; }

        void Enable();

        void Disable();

        /// <summary>读取移动轴，未配置时返回 <see cref="Vector2.zero"/>。</summary>
        Vector2 ReadMove();
    }

    public sealed class InputService : IInputService
    {
        private readonly InputActionAsset _asset;
        private readonly InputAction _moveAction;

        public InputService(InputActionAsset asset = null, string moveActionPath = "Gameplay/Move")
        {
            _asset = asset;

            if (_asset != null && !string.IsNullOrEmpty(moveActionPath))
            {
                _moveAction = _asset.FindAction(moveActionPath, throwIfNotFound: false);
                if (_moveAction == null)
                {
                    GameLog.Warn(LogChannel.Core, $"输入资源中找不到动作 '{moveActionPath}'，移动输入将被忽略。");
                }
            }
        }

        public bool IsEnabled => _asset != null && _asset.enabled;

        public bool IsConfigured => _moveAction != null;

        public void OnRegistered(IServiceRegistry registry)
        {
            if (!IsConfigured)
            {
                GameLog.Debug(LogChannel.Core, "输入服务已注册但尚未绑定 InputActionAsset（探索阶段接入）。");
            }
        }

        public void OnUnregistered() => Disable();

        public void Enable() => _asset?.Enable();

        public void Disable() => _asset?.Disable();

        public Vector2 ReadMove()
        {
            if (_moveAction == null || !IsEnabled)
            {
                return Vector2.zero;
            }

            return _moveAction.ReadValue<Vector2>();
        }
    }
}
