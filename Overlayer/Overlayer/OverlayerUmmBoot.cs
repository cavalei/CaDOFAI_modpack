using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Overlayer.Compat.Interface;
using Overlayer.Core;
using Overlayer.Overlay;

namespace Overlayer;

/// <summary>
/// UnityModManager 的启动引导：延迟 3 秒初始化 Overlayer，逐帧驱动 Tick，
/// 并在场景加载后补做初始化与布局刷新。
/// </summary>
internal sealed class OverlayerUmmBoot : MonoBehaviour {
    private const float BootDelay = 3f;

    private IOverlayerHost host;
    private float elapsed;
    private bool initialized;

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
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
        if (!initialized) {
            return;
        }

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
