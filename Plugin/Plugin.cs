using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.NET.Common;
using BepInExResoniteShim;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.ProtoFlux;
using FrooxEngine.UIX;
using HarmonyLib;

namespace ShowDelegates;

[ResonitePlugin(PluginMetadata.GUID, PluginMetadata.NAME, PluginMetadata.VERSION, PluginMetadata.AUTHORS, PluginMetadata.REPOSITORY_URL)]
[BepInDependency(BepInExResoniteShim.PluginMetadata.GUID, BepInDependency.DependencyFlags.HardDependency)]
public class Plugin : BasePlugin
{
#nullable disable
    internal static new ManualLogSource Log;
    internal static ConfigEntry<bool> DefaultOpen;
    internal static ConfigEntry<bool> ShowDelegates;
    internal static ConfigEntry<bool> ShortNames;
    internal static ConfigEntry<bool> ShowNonDefault;
    internal static ConfigEntry<bool> ShowHidden;
#nullable enable

    public override void Load()
    {
        Log = base.Log;

        DefaultOpen = Config.Bind("General", "DefaultOpen", false, "If true delegates will be expanded by default.");
        ShowDelegates = Config.Bind("General", "ShowDelegates", true, "If false delegates will not be shown.");
        ShortNames = Config.Bind("General", "ShortNames", true, "Show short delegate names.");
        ShowNonDefault = Config.Bind("General", "ShowNonDefault", true, "If false only delegates that appear in vanilla will be shown.");
        ShowHidden = Config.Bind("General", "ShowHidden", true, "If true items hidden with the HideInInspector attribute will be shown");
        
        HarmonyInstance.PatchAll();
    }
        private static void GenerateDelegateProxy<T>(UIBuilder ui, string name, T target) where T : Delegate
        {
            LocaleString localeString = name;
            Text text = ui.Text(localeString, true, new Alignment?(Alignment.MiddleLeft), true, null);
            text.Slot.GetComponent<RectTransform>(null, false).AnchorMax.Value = new float2(0.25f, 1f);
            InteractionElement.ColorDriver colorDriver = text.Slot.AttachComponent<Button>(true, null).ColorDrivers.Add();
            colorDriver.ColorDrive.Target = text.Color;
            RadiantUI_Constants.SetupLabelDriverColors(colorDriver);
            text.Slot.AttachComponent<DelegateProxySource<T>>(true, null).Delegate.Target = target;
        }
        private static void GenerateReferenceProxy(UIBuilder ui, string name, IWorldElement target)
        {
            LocaleString localeString = name + ":";
            Text text = ui.Text(localeString, true, new Alignment?(Alignment.MiddleLeft), true, null);
            text.Slot.GetComponent<RectTransform>(null, false).AnchorMax.Value = new float2(0.25f, 1f);
            InteractionElement.ColorDriver colorDriver = text.Slot.AttachComponent<Button>(true, null).ColorDrivers.Add();
            colorDriver.ColorDrive.Target = text.Color;
            RadiantUI_Constants.SetupLabelDriverColors(colorDriver);
            text.Slot.AttachComponent<ReferenceProxySource>(true, null).Reference.Target = target;
        }

        private static string funName(string prefix, MethodInfo info)
        {
            if (ShortNames.Value)
            {
                string text = string.Join(", ", from p in info.GetParameters()
                                                select p.ParameterType.GetNiceName() + " " + p.Name);
                return info.ReturnType.GetNiceName() + " " + info.Name + "(" + text + ")";
            }
            return string.Concat(new string[]
            {
            info.IsStatic ? "Static " : "",
            prefix,
            " ",
            info.ToString().Substring(info.ToString().IndexOf(" ")).Replace("FrooxEngine.", ""),
            " -> ",
            info.ReturnType.Name
            }
            );
        }

        [HarmonyPatch(typeof(WorkerInitializer), nameof(WorkerInitializer.Initialize), new Type[] { typeof(Type) })]
        public static class InitializeAllDelegatesPatch
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codes = instructions.ToList();
                for (int i = 0; i < codes.Count; i++)
                {
                    var code = codes[i];
                    if (code.operand is MethodInfo mf && mf.Name == nameof(Type.GetMethods))
                    {
                        var patchMethod = typeof(InitializeAllDelegatesPatch).GetMethod(nameof(GetAllMethodsForRealThisTime));
                        codes[i] = new(OpCodes.Call, patchMethod);
                    }
                }
                return codes.AsEnumerable();
            }

            // If there is a better way to do this please tell me
            public static MethodInfo[] GetAllMethodsForRealThisTime(Type t, BindingFlags _flags)
            {
                var set = Pool.BorrowDictionary<RuntimeMethodHandle, MethodInfo>();

                var type = t;
                while (type != null)
                {
                    foreach (var m in type.GetMethods(AccessTools.all))
                    {
                        if (set.ContainsKey(m.MethodHandle)) continue;
                        set.Add(m.MethodHandle, m);
                    }
                    type = type.BaseType;
                }

                var arr = set.Values.ToArray();

                Pool.Return(ref set);
                return arr;
            }
        }

        [HarmonyPatch(typeof(ProtoFluxTool), "OnCreateDelegateProxy")]
        class FixProtoFluxDeleagtes
        {
            public static void Prefix(IButton button, ButtonEventData eventData, ref Delegate target)
            {
                try
                {
                    // this could throw in many ways....
                    var delegateType = Helper.GetFuncOrAction(target.Method);
                    target = target.Method.CreateDelegate(delegateType, target.Target);
                }
                catch (Exception e)
                {
                    Log.LogDebug("Exception in FixProtoFluxDeleagtes.Prefix: " + e);
                }
            }

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes)
            {
                foreach (var code in codes)
                {
                    if (code.operand is MethodInfo mf && mf.Name == nameof(Type.GetType))
                    {
                        var patchMethod = typeof(FixProtoFluxDeleagtes).GetMethod(nameof(GetTypeFixed));
                        yield return new(OpCodes.Call, patchMethod);
                    }
                    else
                    {
                        yield return code;
                    }
                }
            }

            public static Type? GetTypeFixed(string typeName)
            {
                return typeof(ProtoFluxTool).Assembly.GetType(typeName);
            }
        }

        [HarmonyPatch(typeof(WorkerInspector))]
        [HarmonyPatch("BuildInspectorUI")]
        class WorkerInspector_BuildInspectorUI_Patch
        {
            public static MethodInfo delegateFunc = typeof(Plugin).GetMethod(nameof(Plugin.GenerateDelegateProxy), BindingFlags.NonPublic | BindingFlags.Static);

            public static bool ProcessHidden(bool orig)
            {
                if (ShowHidden.Value) return false;
                return orig;
            }

            public static void GenerateDelegates(Worker worker, UIBuilder ui, Predicate<ISyncMember> memberFilter)
            {
                if (ShowHidden.Value)
                {
                    for (int i = 0; i < worker.SyncMemberCount; i++)
                    {
                        ISyncMember syncMember = worker.GetSyncMember(i);
                        if (memberFilter != null && !memberFilter(syncMember)) continue;
                        var hidden = worker.GetSyncMemberFieldInfo(i).GetCustomAttribute<HideInInspectorAttribute>() != null;
                        if (hidden)
                        {
                            GenerateReferenceProxy(ui, worker.GetSyncMemberName(syncMember), syncMember);
                        }
                    }
                }

                if (!ShowDelegates.Value) return;

                if (worker.SyncMethodCount > 0)
                {
                    var initInfo = Traverse.Create(worker).Field<WorkerInitInfo>("InitInfo").Value;
                    var syncFuncs = ShowNonDefault.Value ? initInfo.syncMethods.AsEnumerable() : initInfo.syncMethods.Where((m) => m.methodType != typeof(Delegate) && m.method.IsPublic);

                    if (!syncFuncs.Any()) return;

                    var myTxt = ui.Text("---- SYNC METHODS HERE ----", true, new Alignment?(Alignment.MiddleCenter), true, null);
                    var delegates = ui.VerticalLayout();
                    delegates.Slot.ActiveSelf = false;
                    delegates.Slot.RemoveComponent(delegates.Slot.GetComponent<LayoutElement>());
                    var expander = myTxt.Slot.AttachComponent<Expander>();
                    expander.SectionRoot.Target = delegates.Slot;
                    expander.IsExpanded = DefaultOpen.Value;
                    var theButton = myTxt.Slot.AttachComponent<Button>();
                    var colorDriver = theButton.ColorDrivers.Add();
                    colorDriver.ColorDrive.Target = myTxt.Color;
                    RadiantUI_Constants.SetupLabelDriverColors(colorDriver);
                    // Prevent accidental presses when scrolling
                    theButton.RequireLockInToPress.Value = true;

                    foreach (var info in syncFuncs)
                    {
                        var delegateType = info.methodType;

                        Delegate method = null;
                        if (typeof(MulticastDelegate).IsAssignableFrom(delegateType))
                        {
                            try
                            {
                                method = info.method.IsStatic ? info.method.CreateDelegate(delegateType) : info.method.CreateDelegate(delegateType, worker);
                            }
                            catch (Exception e)
                            {
                                Log.LogError($"Error when trying to create a delegate for {info.method.FullDescription()} with sync method type {delegateType.GetNiceName()}. This is most likely a bug with Resonite, not the mod.\n{e}");
                            }
                        }
                        
                        if (method == null)
                        {
                            try
                            {
                                // this could throw in many ways....
                                delegateType = Helper.ClassifyDelegate(info.method);
                                method = info.method.IsStatic ? info.method.CreateDelegate(delegateType) : info.method.CreateDelegate(delegateType, worker);
                            }
                            catch (Exception e)
                            {
                                Log.LogError("Error while classifying function " + info.method + "\n" + e);
                            }
                        }
                        
                        if (method == null)
                        {
                            Log.LogError("Unmapped type. Please report this message to the mod author: Could not identify " + info.method + " on type " + info.method.DeclaringType);
                            ui.Text("<color=orange>" + funName("<i>unknown</i>", info.method), true, Alignment.MiddleLeft);
                            continue;
                        }
                        delegateFunc.MakeGenericMethod(delegateType).Invoke(null, [
                            ui,
                            funName(delegateType.ToString(), info.method),
                            method
                        ]);
                    }
                    ui.NestOut();
                }
            }

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes)
            {
                foreach (var code in codes)
                {
                    yield return code;
                    if (code.operand is MethodBase func)
                    {
                        if ((func.Name == "get_SyncMethodCount"))
                        {
                            yield return new(OpCodes.Pop);
                            yield return new(OpCodes.Ldc_I4_0);

                            yield return new(OpCodes.Ldarg_0);
                            yield return new(OpCodes.Ldarg_1);
                            yield return new(OpCodes.Ldarg_2);
                            yield return new(OpCodes.Call, typeof(WorkerInspector_BuildInspectorUI_Patch).GetMethod(nameof(GenerateDelegates)));
                        }
                    }
                }

            }
        }
}
