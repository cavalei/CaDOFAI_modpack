using HarmonyLib;
using Overlayer.Module.ADOFAI.IO.File;
using Overlayer.Patch.Safe;
using System.Reflection;

namespace Overlayer.Module.ADOFAI.Patch;

public class SP_SessionAttemptLoad() : SafeConditionalPatch(nameof(SP_SessionAttemptLoad)) {
    protected override bool ShouldApply() => true;

    protected override MethodBase GetTargetMethod()
        => SafePatch.GetMethodSafe("scnGame", "LoadLevel");

    protected override HarmonyMethod Postfix() => new HarmonyMethod(typeof(SP_SessionAttemptLoad)
        .GetMethod(nameof(PostfixImpl), BindingFlags.Static | BindingFlags.NonPublic));

    private static void PostfixImpl() => SessionAttemptState.Reset();
}

public class SP_SessionAttemptPlay() : SafeConditionalPatch(nameof(SP_SessionAttemptPlay)) {
    protected override bool ShouldApply() => true;

    protected override MethodBase GetTargetMethod()
        => SafePatch.GetMethodSafe("scnGame", "Play");

    protected override HarmonyMethod Postfix() => new HarmonyMethod(typeof(SP_SessionAttemptPlay)
        .GetMethod(nameof(PostfixImpl), BindingFlags.Static | BindingFlags.NonPublic));

    private static void PostfixImpl() => SessionAttemptState.Increase();
}
