using System.IO;

// 空命名空间：让 `using MelonLoader;` 在 UMM 构建下依然合法。
namespace MelonLoader { }

namespace MelonLoader.Utils {
    /// <summary>
    /// MelonLoader 的路径 API 垫片。UMM 下没有 MelonLoader，
    /// 这里用游戏根目录推导出等价的路径。
    /// </summary>
    public static class MelonEnvironment {
        public static string GameRootDirectory {
            get {
                // Unity 下当前工作目录就是游戏根目录
                string dir = Directory.GetCurrentDirectory();
                return string.IsNullOrEmpty(dir) ? "." : dir;
            }
        }

        public static string UserDataDirectory {
            get {
                string path = Path.Combine(GameRootDirectory, "UserData");
                Directory.CreateDirectory(path);
                return path;
            }
        }
    }
}
