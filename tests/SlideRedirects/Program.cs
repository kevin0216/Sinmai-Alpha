using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using DB;
using HarmonyLib;
using Manager;
using Mono.Cecil;
using SinmaiAlpha.Notes;
using SinmaiAlpha.Notes.Libs;
using UnityEngine;

// Run from the repository root after building Sinmai-Alpha in Release.
// Pass the game's MelonLoader/net35 directory if Harmony dependencies are not in Libs.
// Exercise the real game IL and mod redirects without invoking Unity rendering.
internal static class Program
{
    private static int checks;
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }

    private static int Main(string[] args)
    {
        try { LoadAndRun(args); return 0; }
        catch (Exception ex) { System.Console.Error.WriteLine(ex); return 1; }
    }

    private static void LoadAndRun(string[] dependencyDirectories)
    {
        var directories = new[] { Path.GetFullPath("Libs") }.Concat(dependencyDirectories).ToArray();
        using var resolver = new DefaultAssemblyResolver();
        foreach (var directory in directories) resolver.AddSearchDirectory(Path.GetFullPath(directory));
        var reader = new ReaderParameters { AssemblyResolver = resolver };
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var name = new AssemblyName(args.Name).Name + ".dll";
            var path = directories.Select(directory => Path.Combine(directory, name)).FirstOrDefault(File.Exists);
            return path != null ? Assembly.LoadFrom(Path.GetFullPath(path)) : null;
        };
        // The game DLL has a native loader entry point for Unity. Its managed note
        // code can run independently when the test output copy is marked IL-only.
        using (var game = AssemblyDefinition.ReadAssembly("Libs/Assembly-CSharp.dll", reader))
        {
            game.MainModule.Attributes |= ModuleAttributes.ILOnly;
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assembly-CSharp.dll");
            game.Write(path);
            Assembly.LoadFrom(path);
        }
        // MelonLoader's log sinks require its native host. Replace only those sinks
        // in the test output; the source game, mod and Libs directory stay intact.
        using (var logger = AssemblyDefinition.ReadAssembly("Libs/MelonLoader.dll", reader))
        {
            foreach (var method in logger.MainModule.GetType("MelonLoader.MelonLogger").Methods.Where(m =>
                (m.Name == "Msg" || m.Name == "Warning" || m.Name == "Error") &&
                m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.String"))
            {
                method.Body = new Mono.Cecil.Cil.MethodBody(method);
                var il = method.Body.GetILProcessor();
                il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
                il.Emit(Mono.Cecil.Cil.OpCodes.Call, logger.MainModule.ImportReference(
                    typeof(System.Console).GetMethod(nameof(System.Console.WriteLine), new[] { typeof(string) })));
                il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
            }
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MelonLoader.dll");
            logger.Write(path);
            Assembly.LoadFrom(path);
        }
        Run();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run()
    {
        var manager = (SlideManager)FormatterServices.GetUninitializedObject(typeof(SlideManager));
        var nativePath = new List<Vector4> { new Vector4(0, 0, 1234, 0) };
        var nativeHits = new List<SlideManager.HitArea> { new SlideManager.HitArea() };
        Set(manager, "_slidePathList", Enumerable.Range(0, 14).Select(type => Enumerable.Range(0, 8)
            .Select(start => Enumerable.Range(0, 8).Select(offset => type == 1 && (offset < 2 || offset == 7)
                ? new List<Vector4>() : nativePath).ToList()).ToList()).ToList());
        Set(manager, "_hitAreaList", Enumerable.Range(0, 14).Select(type => Enumerable.Range(0, 8)
            .Select(start => Enumerable.Range(0, 8).Select(offset => nativeHits).ToList()).ToList()).ToList());

        var first = Custom(6, 5, "TG1:7-C>E5-5-E6-6");
        var second = Custom(4, 5, "TG1:5-B2>4d<B5-B6-6");
        foreach (var slide in new[] { first, second })
        {
            Check(slide.slideData.type == SlideType.Slide_Straight, "reported custom path has a straight ending");
            var threw = false;
            try { manager.GetSlideLength(slide.slideData.type, slide.startButtonPos, slide.slideData.targetNote); }
            catch (ArgumentOutOfRangeException) { threw = true; }
            Check(threw, "native adjacent straight path reproduces the reported exception");
        }

        var native = new NoteData { startButtonPos = 0 };
        native.slideData.type = SlideType.Slide_Straight;
        native.slideData.targetNote = 4;
        var head = new NoteData { type = new NotesTypeID(NotesTypeID.Def.Star), startButtonPos = 6 };
        head.child.Add(first);

        var totalLookups = 0;
        foreach (var target in CustomNoteTypes.SlideNoteDataHack.TargetMethods())
        {
            var original = PatchProcessor.GetOriginalInstructions(target);
            var patched = CustomNoteTypes.SlideNoteDataHack.Transpiler(original).ToList();
            var redirects = patched.Select((instruction, index) => new { instruction, index })
                .Where(x => x.instruction.operand is MethodInfo method && method.DeclaringType == typeof(CustomNoteTypes) &&
                    method.Name.StartsWith("GetSlide", StringComparison.Ordinal)).ToList();
            totalLookups += redirects.Count;
            foreach (var entry in redirects)
            {
                var method = (MethodInfo)entry.instruction.operand;
                var source = patched[entry.index - 1];
                var child = source.IsLdloc();
                if (target.DeclaringType.Name.Contains("Star")) Check(child, "star rotation reads the child slide");
                if (target.DeclaringType.Name == "SlideFan")
                { Check(source.IsLdarg(1), "fan lanes retain their source note"); continue; }

                var lookup = CompileLookup(patched, entry.index);
                if (child)
                {
                    // The same child slot must work across a chain, even if two
                    // custom paths share a native type or the parent is custom.
                    foreach (var parent in new NoteData[] { head, first })
                        foreach (var slide in new[] { first, second })
                            CheckLookup(lookup(manager, parent, slide), method.Name, slide);
                    CheckNative(lookup(manager, first, native), method.Name, nativePath, nativeHits);
                }
                else
                {
                    foreach (var slide in new[] { first, second })
                        CheckLookup(lookup(manager, slide, null), method.Name, slide);
                    CheckNative(lookup(manager, native, first), method.Name, nativePath, nativeHits);
                }
            }
        }
        Check(totalLookups == 11, "all root, connected, fan and star lookup sites were exercised");
        System.Console.WriteLine($"PASS: {checks} checks across {totalLookups} native lookup sites; both reported paths reproduced and corrected");
    }

    private static CustomSlideNoteData Custom(int start, int end, string code)
    {
        var slide = new CustomSlideNoteData { startButtonPos = start };
        slide.slideData.targetNote = end;
        Check(slide.ParseSlideCode(code, OptionMirrorID.Normal), "parse chart geometry " + code);
        return slide;
    }

    private static void Set(object owner, string name, object value) => owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);

    private static void CheckLookup(object actual, string method, CustomSlideNoteData slide)
    {
        if (method == nameof(CustomNoteTypes.GetSlideLengthRedirect))
            Check((float)actual == slide.SlidePathLength && (float)actual > 1, "custom length uses the current segment");
        else if (method == nameof(CustomNoteTypes.GetSlidePathRedirect))
            Check(ReferenceEquals(actual, slide.SlidePathList[0]), "custom arrows use the current segment");
        else Check(ReferenceEquals(actual, slide.SlideHitAreasList[0]), "custom hit areas use the current segment");
    }

    private static void CheckNative(object actual, string method, List<Vector4> path, List<SlideManager.HitArea> hits)
    {
        if (method == nameof(CustomNoteTypes.GetSlideLengthRedirect)) Check((float)actual == 1234, "native segment length");
        else if (method == nameof(CustomNoteTypes.GetSlidePathRedirect)) Check(ReferenceEquals(actual, path), "native segment path");
        else Check(ReferenceEquals(actual, hits), "native segment hit areas");
    }

    private static Func<SlideManager, NoteData, NoteData, object> CompileLookup(List<CodeInstruction> code, int callIndex)
    {
        // Compile the actual patched argument/call sequence. Supply the manager,
        // Initialize argument and current child local directly, and use button 0.
        var start = callIndex;
        while (start >= 0 && !(code[start].operand is MethodInfo m && m.Name == "get_Instance")) start--;
        Check(start >= 0, "lookup receiver found in native IL");
        var dynamicMethod = new DynamicMethod("SlideLookup", typeof(object),
            new[] { typeof(SlideManager), typeof(NoteData), typeof(NoteData) }, typeof(Program), true);
        var il = dynamicMethod.GetILGenerator();
        for (var i = start; i <= callIndex; i++)
        {
            var instruction = code[i];
            if (i == start) il.Emit(OpCodes.Ldarg_0);
            else if (instruction.IsLdarg(1)) il.Emit(OpCodes.Ldarg_1);
            else if (instruction.IsLdloc()) il.Emit(OpCodes.Ldarg_2);
            else if (instruction.IsLdarg(0))
            {
                Check(code[++i].operand is MethodInfo getter && getter.Name == "get_ButtonId", "only owner read is ButtonId");
                il.Emit(OpCodes.Ldc_I4_0);
            }
            else if (instruction.operand is FieldInfo field) il.Emit(instruction.opcode, field);
            else if (instruction.operand is MethodInfo method) il.Emit(instruction.opcode, method);
            else throw new Exception("Unexpected lookup instruction: " + instruction);
        }
        var resultType = ((MethodInfo)code[callIndex].operand).ReturnType;
        if (resultType.IsValueType) il.Emit(OpCodes.Box, resultType);
        il.Emit(OpCodes.Ret);
        return (Func<SlideManager, NoteData, NoteData, object>)dynamicMethod.CreateDelegate(typeof(Func<SlideManager, NoteData, NoteData, object>));
    }
}
