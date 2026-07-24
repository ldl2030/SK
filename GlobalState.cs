using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestPlatform
{
    public static class GlobalState
    {
        // ==========================================
        // 调试专用：一键跳过登录权限验证
        // 将这里设为 true 即可在调试模式下默认拥有所有修改权限
        // ==========================================
#if DEBUG
        public static bool DebugBypassLogin = true;
#else
        public static bool DebugBypassLogin = false;
#endif

        private static bool _isLoggedIn = false;

        public static bool IsLoggedIn
        {
            get => DebugBypassLogin || _isLoggedIn;
            set
            {
                if (_isLoggedIn != value)
                {
                    _isLoggedIn = value;
                    // 可选：触发全局事件，方便其他界面刷新
                    OnLoginStatusChanged?.Invoke(null, EventArgs.Empty);
                }
            }
        }

        // 可选：登录状态改变事件
        public static event EventHandler OnLoginStatusChanged;
    }
}
