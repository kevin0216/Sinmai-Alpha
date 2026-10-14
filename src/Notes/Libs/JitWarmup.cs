using System.Collections.Generic;
using System.Reflection;
using System;
namespace SinmaiAlpha.Notes.Libs;

internal static class JitWarmup
{
    private const BindingFlags AllDeclared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly HashSet<Type> Warmed = new HashSet<Type>();

    internal static void Compile(params Type[] types)
    {
        foreach (Type type in types)
        {
            CompileType(type);
        }
    }

    private static void CompileType(Type type)
    {
        if (type == null || type.ContainsGenericParameters || !Warmed.Add(type))
        {
            return;
        }
        // Reflection over the type cannot fail for types whose code already runs, but a warm-up must never throw into
        // its caller (a ViewUpdate postfix, the noise state constructor). A type that fails is not retried.
        try
        {
            foreach (MethodInfo method in type.GetMethods(AllDeclared))
            {
                CompileMethod(method);
            }
            foreach (ConstructorInfo constructor in type.GetConstructors(AllDeclared))
            {
                CompileMethod(constructor);
            }
            // Lambdas, closures and the other compiler-generated helpers of the type.
            foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                CompileType(nested);
            }
        }
        catch (Exception)
        {
        }
    }

    private static void CompileMethod(MethodBase method)
    {
        try
        {
            if (method.IsAbstract || method.ContainsGenericParameters || method.IsConstructor && method.IsStatic || (method.MethodImplementationFlags & (MethodImplAttributes.InternalCall | MethodImplAttributes.Runtime)) != 0 || (method.Attributes & MethodAttributes.PinvokeImpl) != 0)
            {
                return;
            }
            method.MethodHandle.GetFunctionPointer();
        }
        catch (Exception)
        {
        }
    }
}
