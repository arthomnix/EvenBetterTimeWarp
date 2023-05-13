using KSP.Sim.impl;
using Newtonsoft.Json;

namespace EvenBetterTimeWarp;

[JsonObject(MemberSerialization.Fields)]
public class EvenBetterTimeWarpSettings
{
    public EvenBetterTimeWarpSettings()
    {
        TimeWarpLevels = new string[11];
        PhysicsWarpLevels = new bool[11];
        Presets = new Dictionary<string, (TimeWarp.TimeWarpLevel[], bool[])>();
    }

    public string[] TimeWarpLevels;
    public bool[] PhysicsWarpLevels;
    public Dictionary<string, (TimeWarp.TimeWarpLevel[], bool[])> Presets;
}