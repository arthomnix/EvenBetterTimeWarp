using HarmonyLib;
using KSP.Sim.impl;

namespace EvenBetterTimeWarp;

[HarmonyPatch(typeof(UniverseTime))]
public class UniverseTimePatch
{
    [HarmonyPatch("KSP.Sim.IUniverseTimeCommandEntry.SetTimeScale")]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> SetTimeScaleTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.LoadsConstant(1E+12f) || instruction.LoadsConstant(4f))
                instruction.operand = float.MaxValue;

            yield return instruction;
        }
    }
}