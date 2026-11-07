using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Overlayer.Compat.Interface;
using Overlayer.Core;
using Overlayer.Overlay;

namespace Overlayer;

/// <summary>
/// UnityModManager 的启动引导：
/// 延迟初始化 Overlayer，逐帧驱动 Tick，场景切换时补做初始化与布局刷新，
/// 并按「是否处于关卡内」开关画布（主界面不渲染 Overlayer 内容）。
/// </summary>
internal sealed class OverlayerUmmBoot : MonoBehaviour {
    private const float BootDelay = 3f;
    private const float GateInterval = 0.25f;

    private static FieldInfo controllerInstanceField;
    private static bool controllerFieldProbed;

    private IOverlayerHost host;
    private float elapsed;
    private float gateTimer;
    private bool initialized;
    private int lastGate = -1;

    public void Init(IOverlayerHost host) {
        this.host = host;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Update() {
        if (!initialized) {
            elapsed += Time.unscaledDeltaTime;
            if (elapsed < BootDelay) {
                return;
            }

            initialized = true;
            try {
                MainCore.Initialize(host);
                host.OverlayerLogger.OverlayerMsg("[Overlayer] Delayed boot done (UMM)");
            } catch (Exception e) {
                host.OverlayerLogger.OverlayerErr("[Overlayer] Initialize failed: " + e);
            }
            return;
        }

        try {
            MainCore.EnsureInitialized();
            MainCore.EnsureRootAlive();
        } catch {
            // 逐帧兜底，异常不能打断游戏
        }

        gateTimer += Time.unscaledDeltaTime;
        if (gateTimer >= GateInterval) {
            gateTimer = 0f;
            try {
                ApplySceneGate();
            } catch {
            }
        }
    }

    /// <summary>
    /// 关卡内才显示画布。判定方式：反射读 Assembly-CSharp 的 scrController.instance，
    /// 非空即视为在关卡/编辑器场景里。主界面为 null。
    /// </summary>
    private void ApplySceneGate() {
        if (MainCore.Runtime == null) {
            return;
        }

        bool inGame = IsInGameplay();
        int state = inGame ? 1 : 0;
        if (state == lastGate) {
            return;
        }
        lastGate = state;

        var canvases = OverlayCore.Canvases;
        if (canvases == null) {
            return;
        }

        for (int i = 0; i < canvases.Count; i++) {
            var canvas = canvases[i];
            if (canvas == null || canvas.GameObject == null) {
                continue;
            }

            if (inGame) {
                // 回到关卡：按配置恢复
                try {
                    canvas.ApplyConfig();
                } catch {
                    canvas.GameObject.SetActive(true);
                }
            } else {
                canvas.GameObject.SetActive(false);
            }
        }

        host.OverlayerLogger.OverlayerMsg(
            "[Overlayer] Scene gate: " + (inGame ? "gameplay (canvases on)" : "menu (canvases off)"));
    }

    private static bool IsInGameplay() {
        try {
            if (!controllerFieldProbed) {
                controllerFieldProbed = true;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
                    var type = asm.GetType("scrController", false);
                    if (type == null) {
                        continue;
                    }
                    controllerInstanceField = type.GetField("instance",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (controllerInstanceField != null) {
                        break;
                    }
                }
            }

            if (controllerInstanceField == null) {
                return true; // 探测失败就不干预，保持原行为
            }

            return controllerInstanceField.GetValue(null) != null;
        } catch {
            return true;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
        if (!initialized) {
            return;
        }

        lastGate = -1; // 强制下次重新判定

        try {
            MainCore.EnsureInitialized();
            MainCore.EnsureRootAlive();
            OverlayCore.RequestLayoutRefresh();
        } catch (Exception e) {
            host.OverlayerLogger.OverlayerWrn("[Overlayer] Scene hook failed: " + e.Message);
        }
    }

    private void OnDestroy() {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
}
