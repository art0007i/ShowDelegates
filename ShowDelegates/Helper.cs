using Elements.Core;
using FrooxEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace ShowDelegates;

public struct MethodArgs
{
    public Type returnType;
    public Type[] argumentTypes;

    public MethodArgs(Type returnType, Type[] argumentTypes)
    {
        this.returnType = returnType;
        this.argumentTypes = argumentTypes;
    }
    public MethodArgs(params Type[] argumentTypes)
    {
        this.returnType = typeof(void);
        this.argumentTypes = argumentTypes;
    }
    public MethodArgs(MethodInfo source)
    {
        this.returnType = source.ReturnType;
        this.argumentTypes = source.GetParameters().Select((f) => f.ParameterType).ToArray();
    }

    public override string ToString()
    {
        var rett = returnType == null ? "void" : returnType.Name;
        var args = string.Join(", ", argumentTypes.Select(t => t.Name));
        return $"{rett} ({args})";
    }

    public override bool Equals(object obj)
    {
        if(obj is MethodArgs y)
        {
            var x = this;
            if (x.returnType != y.returnType) return false;
            if (x.argumentTypes.Length != y.argumentTypes.Length) return false;
            for (int i = 0; i < x.argumentTypes.Length; i++)
            {
                if (x.argumentTypes[i] != y.argumentTypes[i]) return false;
            }
            return true;
        }
        return base.Equals(obj);
    }
    public static bool operator ==(MethodArgs lhs, MethodArgs rhs)
    {
        return lhs.Equals(rhs);
    }
    public static bool operator !=(MethodArgs lhs, MethodArgs rhs)
    {
        return !lhs.Equals(rhs);
    }

    public override int GetHashCode()
    {
        unchecked // Overflow is fine, just wrap
        {
            int hash = 17; // Start with a prime number

            // Combine the hash code of the returnType
            hash = hash * 23 + (returnType != null ? returnType.GetHashCode() : 0);

            // Combine the hash codes of each argument type in the array
            if (argumentTypes != null)
            {
                foreach (var argType in argumentTypes)
                {
                    hash = hash * 23 + (argType != null ? argType.GetHashCode() : 0);
                }
            }

            return hash;
        }
    }
}


internal class Helper
{
    public static Dictionary<MethodArgs, Type> argumentLookup = new()
    {
        { new(typeof(IButton), typeof(ButtonEventData)), typeof(ButtonEventHandler) }, // used literally everywhere lol
        { new(typeof(bool), new Type[] {typeof(IGrabbable), typeof(Grabber) }), typeof(GrabCheck) }, // Used in Grabbable.UserRootGrabCheck
        //{ new(typeof(bool), new Type[] {} ), typeof(Func<bool>) }, // Used in SlotInspector.IsTargetEmpty
        //{ new(typeof(TextEditor)), typeof(Action<TextEditor>) }, // Used in FieldEditor.EditingFinished, FieldEditor.EditingChanged, FieldEditor.EditingStarted
        { new(typeof(ITouchable), typeof(TouchEventInfo).MakeByRefType()), typeof(TouchEvent) },
        { new(typeof(ITouchable), new Type[] {typeof(RelayTouchSource), typeof(float3).MakeByRefType(), typeof(float3).MakeByRefType(), typeof(float3).MakeByRefType(), typeof(bool).MakeByRefType()}), typeof(TouchableGetter) },
        { new(typeof(SlotGizmo), typeof(SlotGizmo)), typeof(SlotGizmo.SlotGizmoReplacement) },
        { new(typeof(LegacyWorldItem)), typeof(LegacyWorldItemAction) },
        //{ new(typeof(void), new Type[] {}), typeof(Action) }, // Used in WorldCloseDialog.Close 
        // Action<LocomotionController> // used in somewhere?

    };

    public static Type ClassifyDelegate(MethodInfo m)
    {
        if (argumentLookup.TryGetValue(new(m), out var t))
        {
            return t;
        }
        var p = m.GetParameters().Select(para => para.ParameterType).ToArray();
        if (p.Length == 3 && p[0] == typeof(IButton) && p[1] == typeof(ButtonEventData))
        {
            return typeof(ButtonEventHandler<>).MakeGenericType(p[2]);
        }
        return GetFuncOrAction(m, p);

    }

    public static Type GetFuncOrAction(MethodInfo m)
    {
        var p = m.GetParameters().Select(para => para.ParameterType).ToArray();
        return GetFuncOrAction(m, p);
    }

    public static Type GetFuncOrAction(MethodInfo m, Type[] p)
    {
        if (m.ReturnType == typeof(void))
        {
            return Expression.GetActionType(p);
        }
        else
        {
            p = p.Concat(new[] { m.ReturnType }).ToArray();
            return Expression.GetFuncType(p);
        }
    }
}
