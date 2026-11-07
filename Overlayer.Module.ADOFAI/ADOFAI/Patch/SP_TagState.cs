using HarmonyLib;
using Overlayer.Module.ADOFAI.Tag.Gameplay;
using Overlayer.Patch.Safe;
using Overlayer.Utility.Access;
using System;
using System.Reflection;

namespace Overlayer.Module.ADOFAI.Patch;

public sealed class SP_ResetTagState() : SafeConditionalPatch(nameof(SP_ResetTagState)) {
    protected override bool ShouldApply() => true;
    protected override MethodBase GetTargetMethod() => SafePatch.GetMethodSafe("scrMarginTracker", "Reset");
    protected override HarmonyMethod Postfix() => new(typeof(SP_ResetTagState).GetMethod(nameof(PostfixImpl), BindingFlags.Static | BindingFlags.NonPublic));
    private static void PostfixImpl() => Status.GameplayState.Reset();
}

public sealed class SP_RecordTiming() : SafeConditionalPatch(nameof(SP_RecordTiming)) {
    protected override bool ShouldApply() => true;
    protected override MethodBase GetTargetMethod() => SafePatch.GetMethodSafe("scrPlanet", "SwitchChosen");
    protected override HarmonyMethod Prefix() => new(typeof(SP_RecordTiming).GetMethod(nameof(PrefixImpl), BindingFlags.Static | BindingFlags.NonPublic));

    private static void PrefixImpl(object __instance) {
        if(__instance == null) return;
        var controller = GameAccess.Controller.Get(null);
        if(controller == null) return;
        if(!SafeAccess.TryRead(__instance, "conductor", out object conductor) || conductor == null) return;
        var floor = GameAccess.CurrFloor.Get(controller);
        if(floor == null) return;
        if(!SafeAccess.TryRead(__instance, "angle", out object angleObj) || angleObj is not IConvertible) return;
        if(!SafeAccess.TryRead(__instance, "targetExitAngle", out object exitObj) || exitObj is not IConvertible) return;
        double angle = Convert.ToDouble(angleObj);
        double exitAngle = Convert.ToDouble(exitObj);
        double denominator = Math.PI * GameAccess.ConductorBpm.Get(conductor)
            * GameAccess.FloorSpeed.Get(floor) * GameAccess.SongPitch.Get(GameAccess.Song.Get(conductor));
        if(denominator == 0) return;
        bool isCCW = GameAccess.FloorIsCCW.Get(floor);
        double timing = (angle - exitAngle) * (isCCW ? -1d : 1d) * 60000d / denominator;
        Status.GameplayState.RecordTiming(timing);
    }
}
