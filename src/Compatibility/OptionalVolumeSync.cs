using System;
using System.Collections.Generic;
using System.Reflection;

namespace SinmaiAlpha.Compatibility;

/// <summary>
/// Grows a host mod's VolumeSync per-player volume array so Sinmai-Alpha's extra mine players (IDs past
/// InitParam.PlayerNum) never index past it. Handles AquaMai and MaiMagic builds whose
/// <c>VolumeSync._playerVolumes</c> is still a <c>float[]</c>; MaiMagic's current <c>Dictionary&lt;int, float&gt;</c>
/// store takes any player ID and is never touched (only float[] fields are bound).
/// The bound fields (hits and misses) are kept until a MaiMagic.* / AquaMai.* assembly loads
/// (<see cref="OptionalDisplay.HostGeneration"/>), so a host without a float[] store costs one scan per load.
/// </summary>
internal static class OptionalVolumeSync
{
    private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly string[] TypeNames = new string[2] { "MaiMagic.Mods.GameSystem.VolumeSync", "AquaMai.Mods.GameSystem.VolumeSync" };

    private static FieldInfo[] playerVolumes = new FieldInfo[0];

    private static int boundGeneration = -1;

    private static bool failureLogged;

    internal static void EnsureCapacity(int playerCount)
    {
        if (playerCount <= 0)
        {
            return;
        }
        try
        {
            int current = OptionalDisplay.HostGeneration;
            if (boundGeneration != current)
            {
                playerVolumes = Resolve();
                boundGeneration = current;
            }
            foreach (FieldInfo field in playerVolumes)
            {
                // Read the live value every call and grow it exactly as 0.1.4 does: copy, then 1 for the new players.
                if (field.GetValue(null) is float[] array && array.Length < playerCount)
                {
                    float[] grown = new float[playerCount];
                    Array.Copy(array, grown, array.Length);
                    for (int j = array.Length; j < grown.Length; j++)
                    {
                        grown[j] = 1f;
                    }
                    field.SetValue(null, grown);
                }
            }
        }
        catch (Exception ex)
        {
            // Runs from GameCtrl.CreateNotePool / SoundCtrl.Initialize postfixes: never let a host quirk escape.
            if (!failureLogged)
            {
                failureLogged = true;
                SafeLog.Warning("[Sinmai-Alpha] Cannot grow the host's VolumeSync player volumes (further failures are not logged): " + ex);
            }
        }
    }

    // Per type name, the first loaded assembly whose type has a float[] _playerVolumes (0.1.4's rule for AquaMai).
    private static FieldInfo[] Resolve()
    {
        List<FieldInfo> found = new List<FieldInfo>(2);
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (string typeName in TypeNames)
        {
            foreach (Assembly assembly in assemblies)
            {
                FieldInfo field;
                try
                {
                    field = assembly.GetType(typeName, throwOnError: false)?.GetField("_playerVolumes", StaticMembers);
                }
                catch (Exception)
                {
                    continue; // a dynamic or half-loaded assembly must not stop the scan
                }
                if (field != null && field.FieldType == typeof(float[]))
                {
                    found.Add(field);
                    break;
                }
            }
        }
        return found.ToArray();
    }
}
