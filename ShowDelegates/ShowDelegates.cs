using HarmonyLib;
using NeosModLoader;
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using FrooxEngine;
using FrooxEngine.UIX;
using BaseX;

namespace ShowDelegates
{
    public class ShowDelegates : NeosMod
    {
        public override string Name => "ShowDelegates";
        public override string Author => "art0007i";
        public override string Version => "1.1.1";
        public override string Link => "https://github.com/art0007i/ShowDelegates/";

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> KEY_DEFAULT_OPEN = new ModConfigurationKey<bool>("default_open", "If true delegates will be expanded by default", () => false);
        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> KEY_SHOW_DELEGATES = new ModConfigurationKey<bool>("show_deleages", "If false delegates will not be shown", () => true);
        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> KEY_SHOW_HIDDEN = new ModConfigurationKey<bool>("show_hidden", "If false items with the hidden HideInInspector attribute will not be shown", () => true);
        private static ModConfiguration config;
        private static MethodInfo DelegateProxyMethod = typeof(ShowDelegates).GetMethod(nameof(GenerateDelegateProxy), BindingFlags.NonPublic | BindingFlags.Static);
        public override void OnEngineInit()
        {
            Harmony harmony = new Harmony("me.art0007i.ShowDelegates");
            harmony.PatchAll();
            config = GetConfiguration();
        }
        private static void GenerateDelegateProxy<T>(UIBuilder ui, string name, T target) where T : class
        {
            LocaleString localeString = name + ":";
            Text text = ui.Text(localeString, true, new Alignment?(Alignment.MiddleLeft), true, null);
            text.Slot.GetComponent<RectTransform>(null, false).AnchorMax.Value = new float2(0.25f, 1f);
            InteractionElement.ColorDriver colorDriver = text.Slot.AttachComponent<Button>(true, null).ColorDrivers.Add();
            colorDriver.ColorDrive.Target = text.Color;
            colorDriver.NormalColor.Value = color.Black;
            colorDriver.HighlightColor.Value = color.Blue;
            colorDriver.PressColor.Value = color.Blue;
            text.Slot.AttachComponent<DelegateProxySource<T>>(true, null).Delegate.Target = target;
        }
        private static void GenerateGenericDelegateProxy(UIBuilder ui, string name, Delegate target, Type type) => DelegateProxyMethod.MakeGenericMethod(type).Invoke(null, new object[] { ui, name, target });
        private static void GenerateReferenceProxy(UIBuilder ui, string name, IWorldElement target)
        {
            LocaleString localeString = name + ":";
            Text text = ui.Text(localeString, true, new Alignment?(Alignment.MiddleLeft), true, null);
            text.Slot.GetComponent<RectTransform>(null, false).AnchorMax.Value = new float2(0.25f, 1f);
            InteractionElement.ColorDriver colorDriver = text.Slot.AttachComponent<Button>(true, null).ColorDrivers.Add();
            colorDriver.ColorDrive.Target = text.Color;
            colorDriver.NormalColor.Value = color.Black;
            colorDriver.HighlightColor.Value = color.Blue;
            colorDriver.PressColor.Value = color.Blue;
            text.Slot.AttachComponent<ReferenceProxySource>(true, null).Reference.Target = target;
        }

        private static string funName(Type delegateType, MethodInfo info) =>
            string.Concat(new string[]
            {
            info.IsStatic ? "Static " : "",
            delegateType.Name,
            " ",
            info.ToString().Substring(info.ToString().IndexOf(" ")).Replace("FrooxEngine.", ""),
            " -> ",
            info.ReturnType.Name
            });

        [HarmonyPatch(typeof(WorkerInspector))]
        [HarmonyPatch("BuildInspectorUI")]
        class WorkerInspector_BuildInspectorUI_Patch
        {
            private static void Postfix(WorkerInspector __instance, Worker worker, UIBuilder ui, Predicate<ISyncMember> memberFilter = null)
            {
                if (config.GetValue(KEY_SHOW_HIDDEN))
                    for (int i = 0; i < worker.SyncMemberCount; i++)
                    {
                        ISyncMember syncMember = worker.GetSyncMember(i);
                        if (worker.GetSyncMemberFieldInfo(i).GetCustomAttribute<HideInInspectorAttribute>() != null)
                        {
                            GenerateReferenceProxy(ui, worker.GetSyncMemberName(i), syncMember);
                        }
                    }
                if (!config.GetValue(KEY_SHOW_DELEGATES)) return;
                BindingFlags bindingAttr = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
                List<MethodInfo> list = worker.GetType().GetMethods(bindingAttr).ToList<MethodInfo>();
                list.AddRange(worker.GetType().BaseType.GetMethods(bindingAttr).ToArray<MethodInfo>());
                list = (from m in list
                        where m.GetParameters().Length <= 3 && m.GetCustomAttributes(typeof(SyncMethod), false).Any<object>()
                        select m).ToList<MethodInfo>();
                if (list.Count != 0)
                {
                    var myTxt = ui.Text("---- SYNC METHODS HERE ----", true, new Alignment?(Alignment.MiddleCenter), true, null);
                    var delegates = ui.VerticalLayout();
                    delegates.Slot.ActiveSelf = false;
                    delegates.Slot.RemoveComponent(delegates.Slot.GetComponent<LayoutElement>());
                    var expander = myTxt.Slot.AttachComponent<Expander>();
                    expander.SectionRoot.Target = delegates.Slot;
                    expander.IsExpanded = config.GetValue(KEY_DEFAULT_OPEN);
                    var colorDriver = myTxt.Slot.AttachComponent<Button>().ColorDrivers.Add();
                    colorDriver.ColorDrive.Target = myTxt.Color;
                    colorDriver.NormalColor.Value = color.Black;
                    colorDriver.HighlightColor.Value = new color(0.4f);
                    colorDriver.PressColor.Value = new color(0.7f);
                    foreach (MethodInfo methodInfo in list)
                    {
                        Type delegteType = null;
                        var param = methodInfo.GetParameters();
                        if (param.Length == 0)
                        {
                            if (methodInfo.ReturnType == typeof(void)) delegteType = typeof(Action);
                            else if (methodInfo.ReturnType == typeof(bool)) delegteType = typeof(Func<bool>);
                        }
                        else if (param.Length == 1)
                        {
                            Type parameterType = param[0].ParameterType;
                            if (methodInfo.ReturnType == typeof(ModalOverlay) && parameterType == typeof(Slot)) delegteType = typeof(ModalOverlayConstructor);
                            else if (methodInfo.ReturnType == typeof(bool) && parameterType == typeof(Worker)) delegteType = typeof(Predicate<Worker>);
                            else if (methodInfo.ReturnType == typeof(bool) && parameterType == typeof(ICollider)) delegteType = typeof(Func<ICollider, bool>);
                            else if (methodInfo.ReturnType == typeof(float3) && parameterType == typeof(RelayTouchSource)) delegteType = typeof(Func<RelayTouchSource, float3>);
                            else if (methodInfo.ReturnType == typeof(TouchType) && parameterType == typeof(RelayTouchSource)) delegteType = typeof(Func<RelayTouchSource, TouchType>);
                            else if (methodInfo.ReturnType == typeof(void)) delegteType = typeof(Action<>).MakeGenericType(param[0].ParameterType);
                        }
                        else if (param.Length == 2)
                        {
                            if (methodInfo.ReturnType == typeof(void))
                            {
                                if (param[0].ParameterType == typeof(ITouchable) && param[1].ParameterType == typeof(TouchEventInfo)) delegteType = typeof(TouchEvent);
                                else if (param[0].ParameterType == typeof(DevCreateNewForm) && param[1].ParameterType == typeof(Slot)) delegteType = typeof(ItemCreated);
                                else if (param[0].ParameterType == typeof(Display) && param[1].ParameterType == typeof(Slot)) delegteType = typeof(DesktopDisplayLayout.DisplayItemHandler);
                                else if (param[0].ParameterType == typeof(SlotGizmo) && param[1].ParameterType == typeof(SlotGizmo)) delegteType = typeof(SlotGizmo.SlotGizmoReplacement);
                                else if (param[0].ParameterType == typeof(IButton) && param[1].ParameterType == typeof(ButtonEventData)) delegteType = typeof(ButtonEventHandler);
                                else if (param[0].ParameterType == typeof(WorldOrb) && param[1].ParameterType == typeof(TouchEventInfo)) delegteType = typeof(Action<WorldOrb, TouchEventInfo>);
                                else if (param[0].ParameterType == typeof(ValueStream<float3>) && param[1].ParameterType == typeof(int)) delegteType = typeof(Action<ValueStream<float3>, int>);
                            }
                            else if (methodInfo.ReturnType == typeof(bool))
                            {
                                if (param[0].ParameterType == typeof(ICollider) && param[1].ParameterType == typeof(int)) delegteType = typeof(Func<ICollider, int, bool>);
                                else if (param[0].ParameterType == typeof(Snapper) && param[1].ParameterType == typeof(SnapTarget)) delegteType = typeof(SnapperFilter);
                                else if (param[0].ParameterType == typeof(string) && param[1].ParameterType == typeof(string)) delegteType = typeof(BrowserCreateDirectoryDialog.CreateHandler);
                                else if (param[0].ParameterType == typeof(IGrabbable) && param[1].ParameterType == typeof(Grabber)) delegteType = typeof(GrabCheck);
                            }
                        }
                        else if (param.Length == 3 && methodInfo.ReturnType == typeof(void) && param[0].ParameterType == typeof(IButton) && param[1].ParameterType == typeof(ButtonEventData)) delegteType = typeof(ButtonEventHandler<>).MakeGenericType(param[2].ParameterType);

                        if (delegteType != null) GenerateGenericDelegateProxy(ui, funName(delegteType, methodInfo), methodInfo.CreateDelegate(delegteType, methodInfo.IsStatic ? null : worker), delegteType);
                    }
                    ui.NestOut();
                }
            }
        }
    }
}