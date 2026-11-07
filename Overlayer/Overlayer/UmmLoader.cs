using System;
using HarmonyLib;
using Overlayer.Compat.Interface;
using Overlayer.Core;
using UnityEngine;
using UnityModManagerNet;

namespace Overlayer;

/// <summary>
/// UMM 入口。UnityModManager 会反射查找签名匹配的静态 Load 方法。
/// </summary>
public static class UmmLoader {
    private static UnityModManager.ModEntry entry;
    private static Harmony harmony;
    private static GameObject bootObject;
    private static UmmHost host;

    public static bool Load(UnityModManager.ModEntry modEntry) {
        entry = modEntry;
        harmony = new Harmony(modEntry.Info.Id);
        host = new UmmHost(modEntry, harmony);

        bootObject = new GameObject("OverlayerUmmBoot");
        UnityEngine.Object.DontDestroyOnLoad(bootObject);
        bootObject.AddComponent<OverlayerUmmBoot>().Init(host);

        modEntry.OnUpdate = OnUpdate;
        modEntry.OnUnload = OnUnload;
        modEntry.Logger.Log("[Overlayer] UMM loader armed, booting in 3s");
        return true;
    }

    private static void OnUpdate(UnityModManager.ModEntry modEntry, float dt) {
        try {
            MainCore.Tick();
        } catch (Exception e) {
            entry?.Logger.Error("[Overlayer] Tick failed: " + e.Message);
        }
    }

    private static bool OnUnload(UnityModManager.ModEntry modEntry) {
        try {
            MainCore.Dispose();
        } catch {
        }

        if (bootObject != null) {
            UnityEngine.Object.Destroy(bootObject);
            bootObject = null;
        }

        harmony?.UnpatchAll(entry?.Info.Id);
        entry?.Logger.Log("[Overlayer] Unloaded");
        return true;
    }

    /// <summary>把 UMM 的 ModEntry / Logger / Harmony 适配成 Overlayer 需要的接口。</summary>
    private sealed class UmmHost : IOverlayerHost, IOverlayerLogger {
        private readonly UnityModManager.ModEntry modEntry;
        private readonly Harmony harmony;

        public UmmHost(UnityModManager.ModEntry modEntry, Harmony harmony) {
            this.modEntry = modEntry;
            this.harmony = harmony;
        }

        public IOverlayerLogger OverlayerLogger => this;

        public string OverlayerFilePath => modEntry.Path;

        public Harmony OverlayerHarmony => harmony;

        public void OverlayerMsg(string msg) => modEntry.Logger.Log(msg);

        public void OverlayerWrn(string msg) => modEntry.Logger.Warning(msg);

        public void OverlayerErr(string msg) => modEntry.Logger.Error(msg);
    }
}
