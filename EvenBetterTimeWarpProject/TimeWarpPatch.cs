using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using KSP.Sim.impl;
using UnityEngine;

namespace EvenBetterTimeWarp;

[HarmonyPatch(typeof(TimeWarp))]
[SuppressMessage("ReSharper", "InconsistentNaming")]
[SuppressMessage("ReSharper", "RedundantAssignment")]
public class TimeWarpPatch
{
    private static readonly FieldInfo _currentRateIndexField =
        AccessTools.Field(typeof(TimeWarp), nameof(TimeWarp._currentRateIndex));
    
    [HarmonyPatch("_maxWarpRateIdx", MethodType.Getter)]
    [HarmonyPrefix]
    private static bool GetMaxWarpRateIdxPrefix(ref int __result)
    {
        __result = PhysicsSettings.TimeWarpLevels.Length - 1;
        return false;
    }
    
    [HarmonyPatch(nameof(TimeWarp.GetMaxRateIndex))]
    [HarmonyPrefix]
    private static void GetMaxRateIndexPrefix(ref bool includeInterstellar) => includeInterstellar = true;

    [HarmonyPatch(nameof(TimeWarp.GetMaxRateIndex))]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> GetMaxRateIndexTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        bool found = false;
        foreach (var instruction in instructions)
        {
            if (!found && instruction.opcode == OpCodes.Ldc_I4_0)
            {
                found = true;
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return Transpilers.EmitDelegate<Func<TimeWarp, bool>>(
                    timeWarp => timeWarp.IsPhysicsTimeWarp && 
                                (EvenBetterTimeWarpPlugin.Instance.Settings.PhysicsWarpLevels[timeWarp.CurrentRateIndex] || _wasHoldingAlt)
                    );
            }
            else yield return instruction;
        }
    }

    private static bool _wasHoldingAlt;
    
    [HarmonyPatch(nameof(TimeWarp.SetRateIndexNoBoundsCheck))]
    [HarmonyPrefix]
    private static void SetRateIndexNoBoundsCheckPrefix(int rateIndex, ref bool isPhysicsWarp)
    {
        _wasHoldingAlt = EvenBetterTimeWarpPlugin.Instance.IsPhysicsWarpShortcutPressed;
        isPhysicsWarp = isPhysicsWarp || _wasHoldingAlt ||
                        EvenBetterTimeWarpPlugin.Instance.Settings.PhysicsWarpLevels[rateIndex];
    }

    [HarmonyPatch(nameof(TimeWarp.SetRateIndexNoBoundsCheck))]
    [HarmonyPatch(nameof(TimeWarp.AutoWarpTo))]
    [HarmonyPatch("IFixedUpdate.OnFixedUpdate")]
    [HarmonyPostfix]
    private static void SetIsWarpingPostfix(TimeWarp __instance)
    {
        bool overridePhysicsWarp =
            EvenBetterTimeWarpPlugin.Instance.Settings.PhysicsWarpLevels[__instance.CurrentRateIndex] ||
            _wasHoldingAlt;
        __instance._isWarping = !Mathf.Approximately(__instance.CurrentRate, 1.0f) 
                                || !Mathf.Approximately(__instance._targetRate, 1.0f) 
                                || overridePhysicsWarp;
        __instance._isPhysicsWarp = (__instance._isPhysicsWarp || overridePhysicsWarp) && __instance._isWarping;
    }

    [HarmonyPatch("IFixedUpdate.OnFixedUpdate")]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> OnFixedUpdateTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.LoadsField(_currentRateIndexField))
                yield return Transpilers.EmitDelegate<Func<TimeWarp, bool>>(timeWarp =>
                    !Mathf.Approximately(timeWarp.CurrentRate, 1.0f) 
                    || !Mathf.Approximately(timeWarp._targetRate, 1.0f)
                    || EvenBetterTimeWarpPlugin.Instance.Settings.PhysicsWarpLevels[timeWarp.CurrentRateIndex] 
                    || _wasHoldingAlt);
            else yield return instruction;
        }
    }
}