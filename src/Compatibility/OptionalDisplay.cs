using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SinmaiAlpha.Compatibility;

/// <summary>
/// Accessors for the optional members Sinmai-Alpha reads from its host mod: MaiMagic, or AquaMai under its
/// original names (MaiMagic is tried first, with the same relative names).
/// <para>
/// Each member is bound once into a typed delegate (property getter, <see cref="AccessTools.FieldRef{F}"/> or
/// method) that reads the member's <em>current</em> value on every call. Values are never latched: MaiMagic
/// loads its assemblies inside its own OnInitializeMelon, after ours, and some values change at runtime
/// (PracticeMode.speed, config reloads). The bindings are redone only after an assembly named
/// <c>MaiMagic.*</c> or <c>AquaMai.*</c> loads; <see cref="AppDomain.AssemblyLoad"/> may fire on any thread, so
/// it only bumps a generation counter and the next read rebinds.
/// </para>
/// A missing type or member reads as the default (false / false / 1f / null / null) and never throws.
/// </summary>
internal static class OptionalDisplay
{
    private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly string[] Roots = new string[2] { "MaiMagic", "AquaMai" };

    private static readonly object Gate = new object();

    // Bumped by OnAssemblyLoad when a host assembly loads.
    private static int generation;

    // The generation the delegates below were bound against (-1: never bound).
    private static int boundGeneration = -1;

    private static Func<bool> singlePlayer;

    private static Func<bool> hideSubMonitor;

    private static Func<float> practiceSpeed;

    private static Func<Camera> gameRenderCamera;

    private static Func<int, bool, RawImage> displayImage;

    private static bool readFailureLogged;

    /// <summary><c>&lt;root&gt;.Core.Helpers.GuiSizes.SinglePlayer</c> (a static property in MaiMagic).</summary>
    internal static bool SinglePlayer
    {
        get
        {
            Bind();
            try
            {
                return singlePlayer();
            }
            catch (Exception ex)
            {
                ReadFailed("GuiSizes.SinglePlayer", ex);
                return false;
            }
        }
    }

    /// <summary><c>&lt;root&gt;.Mods.GameSystem.SinglePlayer.HideSubMonitor</c> (a static field in MaiMagic).</summary>
    internal static bool HideSubMonitor
    {
        get
        {
            Bind();
            try
            {
                return hideSubMonitor();
            }
            catch (Exception ex)
            {
                ReadFailed("SinglePlayer.HideSubMonitor", ex);
                return false;
            }
        }
    }

    /// <summary><c>&lt;root&gt;.Mods.UX.PracticeMode.PracticeMode.speed</c> (a static field in MaiMagic), 1 when absent.</summary>
    internal static float PracticeSpeed
    {
        get
        {
            Bind();
            try
            {
                return practiceSpeed();
            }
            catch (Exception ex)
            {
                ReadFailed("PracticeMode.speed", ex);
                return 1f;
            }
        }
    }

    /// <summary><c>&lt;root&gt;.Mods.Utils.ScreenPositionAdjust.GameRenderCamera</c>; null when absent or when the host section is off.</summary>
    internal static Camera GameRenderCamera
    {
        get
        {
            Bind();
            try
            {
                return gameRenderCamera();
            }
            catch (Exception ex)
            {
                ReadFailed("ScreenPositionAdjust.GameRenderCamera", ex);
                return null;
            }
        }
    }

    /// <summary>Bumped whenever a MaiMagic.* or AquaMai.* assembly loads; other host lookups re-resolve when it changes.</summary>
    internal static int HostGeneration => Volatile.Read(ref generation);

    static OptionalDisplay()
    {
        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
    }

    /// <summary><c>&lt;root&gt;.Mods.Utils.ScreenPositionAdjust.DisplayImage(int, bool)</c>; null when absent.</summary>
    internal static RawImage DisplayImage(int monitor, bool sub)
    {
        Bind();
        try
        {
            return displayImage(monitor, sub);
        }
        catch (Exception ex)
        {
            ReadFailed("ScreenPositionAdjust.DisplayImage", ex);
            return null;
        }
    }

    private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
    {
        // Runs inside whoever loads the assembly (MaiMagic's AppDomain.Load among others): must never throw.
        bool host;
        try
        {
            string name = args.LoadedAssembly.GetName().Name;
            host = name.StartsWith("MaiMagic.", StringComparison.Ordinal) || name.StartsWith("AquaMai.", StringComparison.Ordinal);
        }
        catch (Exception)
        {
            // Cannot tell: a spurious rebind is cheap, a missed host is not.
            host = true;
        }
        if (host)
        {
            Interlocked.Increment(ref generation);
        }
    }

    private static void Bind()
    {
        // Read the generation before resolving: an assembly that loads while we resolve bumps it again,
        // so the next read rebinds instead of keeping a stale miss.
        int current = Volatile.Read(ref generation);
        if (Volatile.Read(ref boundGeneration) == current)
        {
            return;
        }
        lock (Gate)
        {
            if (boundGeneration == current)
            {
                return;
            }
            List<string> bound = new List<string>();
            singlePlayer = StaticGetter<bool>("Core.Helpers.GuiSizes", "SinglePlayer", bound) ?? new Func<bool>(False);
            hideSubMonitor = StaticGetter<bool>("Mods.GameSystem.SinglePlayer", "HideSubMonitor", bound) ?? new Func<bool>(False);
            practiceSpeed = StaticGetter("Mods.UX.PracticeMode.PracticeMode", "speed", bound, 1f) ?? new Func<float>(NormalSpeed);
            gameRenderCamera = StaticGetter<Camera>("Mods.Utils.ScreenPositionAdjust", "GameRenderCamera", bound) ?? new Func<Camera>(NoCamera);
            displayImage = DisplayImageMethod(bound) ?? new Func<int, bool, RawImage>(NoImage);
            Volatile.Write(ref boundGeneration, current);
            SafeLog.Msg("[Sinmai-Alpha] Host mod members: " + ((bound.Count == 0) ? "none found" : string.Join(", ", bound.ToArray())));
        }
    }

    // fallback: what the old reflective read returned when the host value is not a T (1f for the practice speed).
    private static Func<T> StaticGetter<T>(string relativeType, string member, List<string> bound, T fallback = default(T))
    {
        foreach (string root in Roots)
        {
            string typeName = root + "." + relativeType;
            try
            {
                Type type = FindType(typeName);
                if (!(type == null))
                {
                    Func<T> getter = PropertyGetter(type, member, fallback) ?? FieldGetter(type, member, fallback);
                    if (getter != null)
                    {
                        bound.Add(typeName + "." + member);
                        return getter;
                    }
                }
            }
            catch (Exception ex)
            {
                SafeLog.Warning("[Sinmai-Alpha] Cannot bind " + typeName + "." + member + ": " + ex.Message);
            }
        }
        return null;
    }

    private static Func<T> PropertyGetter<T>(Type type, string name, T fallback)
    {
        PropertyInfo property = type.GetProperty(name, StaticMembers);
        MethodInfo getter = property?.GetGetMethod(nonPublic: true);
        if (getter == null || getter.GetParameters().Length != 0)
        {
            return null;
        }
        if (getter.ReturnType == typeof(T))
        {
            return (Func<T>)Delegate.CreateDelegate(typeof(Func<T>), getter);
        }
        // Unexpected shape: keep the old reflective semantics (value is T, else the old fallback).
        return () => (property.GetValue(null, null) is T value) ? value : fallback;
    }

    private static Func<T> FieldGetter<T>(Type type, string name, T fallback)
    {
        FieldInfo field = type.GetField(name, StaticMembers);
        if (field == null)
        {
            return null;
        }
        if (field.FieldType == typeof(T) && !field.IsLiteral)
        {
            AccessTools.FieldRef<T> reference = null;
            try
            {
                reference = AccessTools.StaticFieldRefAccess<T>(field);
            }
            catch (Exception ex)
            {
                SafeLog.Warning("[Sinmai-Alpha] No field ref for " + type.FullName + "." + name + ", reading it by reflection: " + ex.Message);
            }
            if (reference != null)
            {
                return () => reference();
            }
        }
        // Unexpected shape (or no field ref): keep the old reflective semantics (value is T, else the old fallback).
        return () => (field.GetValue(null) is T value) ? value : fallback;
    }

    private static Func<int, bool, RawImage> DisplayImageMethod(List<string> bound)
    {
        foreach (string root in Roots)
        {
            string typeName = root + ".Mods.Utils.ScreenPositionAdjust";
            try
            {
                // Exact parameter types: a name-only lookup would throw AmbiguousMatchException on an overload.
                MethodInfo method = FindType(typeName)?.GetMethod("DisplayImage", StaticMembers, null, new Type[2]
                {
                    typeof(int),
                    typeof(bool)
                }, null);
                if (!(method == null) && !method.ReturnType.IsValueType)
                {
                    Func<int, bool, RawImage> result;
                    if (method.ReturnType == typeof(RawImage))
                    {
                        result = (Func<int, bool, RawImage>)Delegate.CreateDelegate(typeof(Func<int, bool, RawImage>), method);
                    }
                    else
                    {
                        // Not per frame (ChartWatermark calls it once per bind), so another return type may use Invoke.
                        result = (int monitor, bool sub) => method.Invoke(null, new object[2] { monitor, sub }) as RawImage;
                    }
                    bound.Add(typeName + ".DisplayImage");
                    return result;
                }
            }
            catch (Exception ex)
            {
                SafeLog.Warning("[Sinmai-Alpha] Cannot bind " + typeName + ".DisplayImage: " + ex.Message);
            }
        }
        return null;
    }

    private static Type FindType(string fullName)
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            try
            {
                Type type = assemblies[i].GetType(fullName, throwOnError: false);
                if (type != null)
                {
                    return type;
                }
            }
            catch (Exception)
            {
                // A dynamic or half-loaded assembly must not stop the scan.
            }
        }
        return null;
    }

    private static void ReadFailed(string member, Exception ex)
    {
        if (!readFailureLogged)
        {
            readFailureLogged = true;
            SafeLog.Warning("[Sinmai-Alpha] Reading host member " + member + " failed, using its default (further failures are not logged): " + ex);
        }
    }

    private static bool False()
    {
        return false;
    }

    private static float NormalSpeed()
    {
        return 1f;
    }

    private static Camera NoCamera()
    {
        return null;
    }

    private static RawImage NoImage(int monitor, bool sub)
    {
        return null;
    }
}
