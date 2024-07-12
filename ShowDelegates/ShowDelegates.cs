using HarmonyLib;
using ResoniteModLoader;
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using FrooxEngine;
using FrooxEngine.UIX;
using Elements.Core;
using FrooxEngine.ProtoFlux;
using System.Reflection.Emit;

namespace ShowDelegates
{
    public class ShowDelegates : ResoniteMod
    {
        public override string Name => "ShowDelegates";
        public override string Author => "art0007i";
        public override string Version => "2.2.8";
        public override string Link => "https://github.com/art0007i/ShowDelegates/";

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> KEY_DEFAULT_OPEN = new ModConfigurationKey<bool>("default_open", "If true delegates will be expanded by default", () => false);
        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> KEY_SHOW_DELEGATES = new ModConfigurationKey<bool>("show_deleages", "If false delegates will not be shown", () => true);
        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> KEY_SHORT_NAMES = new ModConfigurationKey<bool>("short_names", "Show short delegate names.", () => true);
        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> KEY_SHOW_NON_DEFAULT = new ModConfigurationKey<bool>("show_non_default", "If false only delegates that appear in vanilla will be shown.", () => true);
        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> KEY_SHOW_HIDDEN = new ModConfigurationKey<bool>("show_hidden", "If true items hidden with the HideInInspector attribute will be shown", () => true);

        private static ModConfiguration config;

        public override void OnEngineInit()
        {
            config = GetConfiguration();
            Harmony harmony = new Harmony("me.art0007i.ShowDelegates");
            harmony.PatchAll();

        }
        private static void GenerateDelegateProxy<T>(UIBuilder ui, string name, T target) where T : class
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
            if (config.GetValue(KEY_SHORT_NAMES))
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
                        codes[i].operand = typeof(InitializeAllDelegatesPatch).GetMethod(nameof(GetAllMethodsForRealThisTime));
                    }
                }
                return codes.AsEnumerable();
            }

            // If there is a better way to do this please tell me
            public static MethodInfo[] GetAllMethodsForRealThisTime(Type t, BindingFlags _flags)
            {
                var set = Pool.BorrowHashSet<MethodInfo>();

                var type = t;
                while (type != null)
                {
                    foreach (var m in type.GetMethods(AccessTools.all))
                    {
                        // i hate this but it works?
                        if (set.Any(v => v.MethodHandle == m.MethodHandle)) continue;
                        set.Add(m);
                    }
                    type = type.BaseType;
                }

                var arr = set.ToArray();

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
                }
            }
        }

        [HarmonyPatch(typeof(WorkerInspector))]
        [HarmonyPatch("BuildInspectorUI")]
        class WorkerInspector_BuildInspectorUI_Patch
        {
            public static MethodInfo delegateFunc = typeof(ShowDelegates).GetMethod(nameof(ShowDelegates.GenerateDelegateProxy), BindingFlags.NonPublic | BindingFlags.Static);

            public static bool ProcessHidden(bool orig)
            {
                if (config.GetValue(KEY_SHOW_HIDDEN)) return false;
                return orig;
            }

            public static void GenerateDelegates(Worker worker, UIBuilder ui, Predicate<ISyncMember> memberFilter)
            {
                if (config.GetValue(KEY_SHOW_HIDDEN))
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

                if (!config.GetValue(KEY_SHOW_DELEGATES)) return;

                if (worker.SyncMethodCount > 0)
                {
                    var initInfo = Traverse.Create(worker).Field<WorkerInitInfo>("InitInfo").Value;
                    var syncFuncs = config.GetValue(KEY_SHOW_NON_DEFAULT) ? initInfo.syncMethods.AsEnumerable() : initInfo.syncMethods.Where((m) => m.methodType != typeof(Delegate) && m.method.IsPublic);

                    if (!syncFuncs.Any()) return;

                    var myTxt = ui.Text("---- SYNC METHODS HERE ----", true, new Alignment?(Alignment.MiddleCenter), true, null);
                    var delegates = ui.VerticalLayout();
                    delegates.Slot.ActiveSelf = false;
                    delegates.Slot.RemoveComponent(delegates.Slot.GetComponent<LayoutElement>());
                    var expander = myTxt.Slot.AttachComponent<Expander>();
                    expander.SectionRoot.Target = delegates.Slot;
                    expander.IsExpanded = config.GetValue(KEY_DEFAULT_OPEN);
                    var colorDriver = myTxt.Slot.AttachComponent<Button>().ColorDrivers.Add();
                    colorDriver.ColorDrive.Target = myTxt.Color;
                    RadiantUI_Constants.SetupLabelDriverColors(colorDriver);

                    foreach (var info in syncFuncs)
                    {
                        var delegateType = info.methodType;

                        if (!typeof(MulticastDelegate).IsAssignableFrom(delegateType))
                        {
                            try
                            {
                                // this could throw in many ways....
                                delegateType = Helper.ClassifyDelegate(info.method);
                            }
                            catch (Exception e)
                            {
                                Error("Error while classifying function " + info.method + "\n" + e.ToString());
                                delegateType = null;
                            }
                            if (delegateType == null)
                            {
                                Error("Unmapped type. Please report this message to the mod author: Could not identify " + info.method + " on type " + info.method.DeclaringType);
                                ui.Text("<color=orange>" + funName("<i>unknown</i>", info.method), true, new Alignment?(Alignment.MiddleLeft));
                                continue;
                            }
                        }

                        var method = info.method.IsStatic ? info.method.CreateDelegate(delegateType) : info.method.CreateDelegate(delegateType, worker);

                        delegateFunc.MakeGenericMethod(delegateType).Invoke(null, new object[]
                        {
                            ui,
                            funName(delegateType.ToString(), info.method),
                            method
                        });
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
}