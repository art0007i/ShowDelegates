using HarmonyLib;
using NeosModLoader;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using FrooxEngine;
using FrooxEngine.LogiX;
using FrooxEngine.UIX;
using BaseX;

namespace ShowDelegates
{
    public class ShowDelegates : NeosMod
    {
        public override string Name => "ShowDelegates";
        public override string Author => "art0007i";
        public override string Version => "1.1.0";
        public override string Link => "https://github.com/art0007i/ShowDelegates/";

		[AutoRegisterConfigKey]
		private static readonly ModConfigurationKey<bool> KEY_SHOW_DELEGATES = new ModConfigurationKey<bool>("show_deleages", "If false delegates will not be shown", () => true);
		[AutoRegisterConfigKey]
		private static readonly ModConfigurationKey<bool> KEY_SHOW_HIDDEN = new ModConfigurationKey<bool>("show_hidden", "If false items with the hidden HideInInspector attribute will not be shown", ()=>true);
		private static ModConfiguration config;

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

		private static string funName(string prefix, MethodInfo info)
		{
			return string.Concat(new string[]
			{
			info.IsStatic ? "Static " : "",
			prefix,
			" ",
			info.ToString().Substring(info.ToString().IndexOf(" ")).Replace("FrooxEngine.", ""),
			" -> ",
			info.ReturnType.Name
			});
		}

		[HarmonyPatch(typeof(WorkerInspector))]
		[HarmonyPatch("BuildInspectorUI")]
		class WorkerInspector_BuildInspectorUI_Patch
        {
			private static void Postfix(WorkerInspector __instance, Worker worker, UIBuilder ui, Predicate<ISyncMember> memberFilter = null)
			{
				if(config.GetValue(KEY_SHOW_HIDDEN))
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
                    myTxt.Slot.AttachComponent<Expander>().SectionRoot.Target = delegates.Slot;
					var colorDriver = myTxt.Slot.AttachComponent<Button>().ColorDrivers.Add();
					colorDriver.ColorDrive.Target = myTxt.Color;
					colorDriver.NormalColor.Value = color.Black;
					colorDriver.HighlightColor.Value = new color(0.4f);
					colorDriver.PressColor.Value = new color(0.7f);
					foreach (MethodInfo methodInfo in list)
					{
						if (methodInfo.GetParameters().Length == 0)
						{
							if (methodInfo.ReturnType == typeof(void))
							{
								GenerateDelegateProxy<Action>(ui, funName("Action", methodInfo), (Action)methodInfo.CreateDelegate(typeof(Action), worker));
							}
							else if (methodInfo.ReturnType == typeof(bool))
							{
								GenerateDelegateProxy<Func<bool>>(ui, funName("Func<bool>", methodInfo), (Func<bool>)methodInfo.CreateDelegate(typeof(Func<bool>), worker));
							}
						}
						else if (methodInfo.GetParameters().Length == 1)
						{
							Type parameterType = methodInfo.GetParameters()[0].ParameterType;
							if (methodInfo.ReturnType == typeof(ModalOverlay) && parameterType == typeof(Slot))
							{
								ModalOverlayConstructor target = (ModalOverlayConstructor)methodInfo.CreateDelegate(typeof(ModalOverlayConstructor), methodInfo.IsStatic ? null : worker);
								GenerateDelegateProxy<ModalOverlayConstructor>(ui, funName("ModalOverlayConstructor", methodInfo), target);
							}
							else if (methodInfo.ReturnType == typeof(bool) && parameterType == typeof(Worker))
							{
								Predicate<Worker> target2 = (Predicate<Worker>)methodInfo.CreateDelegate(typeof(Predicate<Worker>), methodInfo.IsStatic ? null : worker);
								GenerateDelegateProxy<Predicate<Worker>>(ui, funName("Predicate<Worker>", methodInfo), target2);
							}
							else if (methodInfo.ReturnType == typeof(bool) && parameterType == typeof(ICollider))
							{
								Func<ICollider, bool> target3 = (Func<ICollider, bool>)methodInfo.CreateDelegate(typeof(Func<ICollider, bool>), methodInfo.IsStatic ? null : worker);
								GenerateDelegateProxy<Func<ICollider, bool>>(ui, funName("Func<ICollider, bool>", methodInfo), target3);
							}
							else if (methodInfo.ReturnType == typeof(float3) && parameterType == typeof(RelayTouchSource))
							{
								Func<RelayTouchSource, float3> target4 = (Func<RelayTouchSource, float3>)methodInfo.CreateDelegate(typeof(Func<RelayTouchSource, float3>), methodInfo.IsStatic ? null : worker);
								GenerateDelegateProxy<Func<RelayTouchSource, float3>>(ui, funName("Func<RelayTouchSource, float3>", methodInfo), target4);
							}
							else if (methodInfo.ReturnType == typeof(TouchType) && parameterType == typeof(RelayTouchSource))
							{
								Func<RelayTouchSource, TouchType> target5 = (Func<RelayTouchSource, TouchType>)methodInfo.CreateDelegate(typeof(Func<RelayTouchSource, TouchType>), methodInfo.IsStatic ? null : worker);
								GenerateDelegateProxy<Func<RelayTouchSource, TouchType>>(ui, funName("Func<RelayTouchSource, TouchType>", methodInfo), target5);
							}
							else if (methodInfo.ReturnType == typeof(void))
							{
								typeof(ShowDelegates).GetMethod("GenerateDelegateProxy", BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(new Type[]
								{
							typeof(Action<>).MakeGenericType(new Type[]
							{
								parameterType
							})
								}).Invoke(null, new object[]
								{
							ui,
							funName("Action<T> ", methodInfo),
							methodInfo.CreateDelegate(typeof(Action<>).MakeGenericType(new Type[]
							{
								parameterType
							}), methodInfo.IsStatic ? null : worker)
								});
							}
						}
						else if (methodInfo.GetParameters().Length == 2)
						{
							if (methodInfo.ReturnType == typeof(void))
							{
								if (methodInfo.GetParameters()[0].ParameterType == typeof(ITouchable) && methodInfo.GetParameters()[1].ParameterType == typeof(TouchEventInfo))
								{
									GenerateDelegateProxy<TouchEvent>(ui, funName("TouchEvent", methodInfo), (TouchEvent)methodInfo.CreateDelegate(typeof(TouchEvent), methodInfo.IsStatic ? null : worker));
								}
								else if (methodInfo.GetParameters()[0].ParameterType == typeof(DevCreateNewForm) && methodInfo.GetParameters()[1].ParameterType == typeof(Slot))
								{
									GenerateDelegateProxy<ItemCreated>(ui, funName("ItemCreated", methodInfo), (ItemCreated)methodInfo.CreateDelegate(typeof(ItemCreated), methodInfo.IsStatic ? null : worker));
								}
								else if (methodInfo.GetParameters()[0].ParameterType == typeof(Display) && methodInfo.GetParameters()[1].ParameterType == typeof(Slot))
								{
									GenerateDelegateProxy<DesktopDisplayLayout.DisplayItemHandler>(ui, funName("DisplayItemHandler", methodInfo), (DesktopDisplayLayout.DisplayItemHandler)methodInfo.CreateDelegate(typeof(DesktopDisplayLayout.DisplayItemHandler), methodInfo.IsStatic ? null : worker));
								}
								else if (methodInfo.GetParameters()[0].ParameterType == typeof(SlotGizmo) && methodInfo.GetParameters()[1].ParameterType == typeof(SlotGizmo))
								{
									GenerateDelegateProxy<SlotGizmo.SlotGizmoReplacement>(ui, funName("SlotGizmoReplacement", methodInfo), (SlotGizmo.SlotGizmoReplacement)methodInfo.CreateDelegate(typeof(SlotGizmo.SlotGizmoReplacement), methodInfo.IsStatic ? null : worker));
								}
								else if (methodInfo.GetParameters()[0].ParameterType == typeof(IButton) && methodInfo.GetParameters()[1].ParameterType == typeof(ButtonEventData))
								{
									GenerateDelegateProxy<ButtonEventHandler>(ui, funName("ButtonEventHandler", methodInfo), (ButtonEventHandler)methodInfo.CreateDelegate(typeof(ButtonEventHandler), methodInfo.IsStatic ? null : worker));
								}
								else if (methodInfo.GetParameters()[0].ParameterType == typeof(WorldOrb) && methodInfo.GetParameters()[1].ParameterType == typeof(TouchEventInfo))
								{
									GenerateDelegateProxy<Action<WorldOrb, TouchEventInfo>>(ui, funName("Action<WorldOrb, TouchEventInfo>", methodInfo), (Action<WorldOrb, TouchEventInfo>)methodInfo.CreateDelegate(typeof(Action<WorldOrb, TouchEventInfo>), methodInfo.IsStatic ? null : worker));
								}
								else if (methodInfo.GetParameters()[0].ParameterType == typeof(ValueStream<float3>) && methodInfo.GetParameters()[1].ParameterType == typeof(int))
								{
									GenerateDelegateProxy<Action<ValueStream<float3>, int>>(ui, funName("Action<ValueStream<float3>, int>", methodInfo), (Action<ValueStream<float3>, int>)methodInfo.CreateDelegate(typeof(Action<ValueStream<float3>, int>), methodInfo.IsStatic ? null : worker));
								}
							}
							else if (methodInfo.ReturnType == typeof(bool))
							{
								if (methodInfo.GetParameters()[0].ParameterType == typeof(ICollider) && methodInfo.GetParameters()[1].ParameterType == typeof(int))
								{
									GenerateDelegateProxy<Func<ICollider, int, bool>>(ui, funName("Func<ICollider, int, bool>", methodInfo), (Func<ICollider, int, bool>)methodInfo.CreateDelegate(typeof(Func<ICollider, int, bool>), methodInfo.IsStatic ? null : worker));
								}
								else if (methodInfo.GetParameters()[0].ParameterType == typeof(Snapper) && methodInfo.GetParameters()[1].ParameterType == typeof(SnapTarget))
								{
									GenerateDelegateProxy<SnapperFilter>(ui, funName("SnapperFilter", methodInfo), (SnapperFilter)methodInfo.CreateDelegate(typeof(SnapperFilter), methodInfo.IsStatic ? null : worker));
								}
								else if (methodInfo.GetParameters()[0].ParameterType == typeof(string) && methodInfo.GetParameters()[1].ParameterType == typeof(string))
								{
									GenerateDelegateProxy<BrowserCreateDirectoryDialog.CreateHandler>(ui, funName("BrowserCreateDirectoryDialog.CreateHandler", methodInfo), (BrowserCreateDirectoryDialog.CreateHandler)methodInfo.CreateDelegate(typeof(BrowserCreateDirectoryDialog.CreateHandler), methodInfo.IsStatic ? null : worker));
								}
								else if (methodInfo.GetParameters()[0].ParameterType == typeof(IGrabbable) && methodInfo.GetParameters()[1].ParameterType == typeof(Grabber))
								{
									GenerateDelegateProxy<GrabCheck>(ui, funName("GrabCheck", methodInfo), (GrabCheck)methodInfo.CreateDelegate(typeof(GrabCheck), methodInfo.IsStatic ? null : worker));
								}
							}
						}
						else if (methodInfo.GetParameters().Length == 3 && methodInfo.ReturnType == typeof(void) && methodInfo.GetParameters()[0].ParameterType == typeof(IButton) && methodInfo.GetParameters()[1].ParameterType == typeof(ButtonEventData))
						{
							typeof(ShowDelegates).GetMethod("GenerateDelegateProxy", BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(new Type[]
							{
						typeof(ButtonEventHandler<>).MakeGenericType(new Type[]
						{
							methodInfo.GetParameters()[2].ParameterType
						})
							}).Invoke(null, new object[]
							{
						ui,
						funName("ButtonEventHandler<T>", methodInfo),
						methodInfo.CreateDelegate(typeof(ButtonEventHandler<>).MakeGenericType(new Type[]
						{
							methodInfo.GetParameters()[2].ParameterType
						}), methodInfo.IsStatic ? null : worker)
							});
						}
					}
					ui.NestOut();
				}
			}
		}
	}
}