using HarmonyLib;
using Overlayer.Patch.Safe;
using Overlayer.UI;
using System.Reflection;

namespace Overlayer.Module.ADOFAI.Patch;

public class SP_BlockAsyncInput() : SafeConditionalPatch(nameof(SP_BlockAsyncInput)) {
    protected override bool ShouldApply() => Core.Config.BlockInputWhenOpened;

    protected override MethodBase GetTargetMethod()
        => SafePatch.GetMethodSafe("scrPlayer", "ValidInputWasTriggered");

    protected override HarmonyMethod Prefix() => new HarmonyMethod(typeof(SP_BlockAsyncInput)
        .GetMethod(nameof(PrefixImpl), BindingFlags.Static | BindingFlags.NonPublic));

    private static bool PrefixImpl(ref bool __result) {
        if(UICore.CanvasObj == null || !UICore.CanvasObj.activeInHierarchy) {
            return true;
        }

        __result = false;
        GameAccess.ClearKeysFn.TryInvoke(null, out _);
        ClearMask(GameAccess.FrameKeyMask.Get(null));
        ClearMask(GameAccess.FrameKeyDownMask.Get(null));
        ClearMask(GameAccess.FrameKeyUpMask.Get(null));
        return false;
    }

    private static void ClearMask(object mask) {
        if(mask == null) return;
        Overlayer.Utility.Access.SafeAccess.TryCall(mask, "Clear", out _);
    }
}
