using HarmonyLib;
using ResoniteModLoader;
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using FrooxEngine;
using FrooxEngine.UIX;
using Elements.Core;

namespace ShowDelegates
{
    public class ShowDelegates : ResoniteMod
	{
		public override string Name => "ShowDelegates";
		public override string Author => "art0007i";
		public override string Version => "2.1.0";
		public override string Link => "https://github.com/art0007i/ShowDelegates/";
        
		[AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> KEY_SHOW_HIDDEN = new ModConfigurationKey<bool>("show_hidden", "If false items with the hidden HideInInspector attribute will not be shown", () => true);
        [AutoRegisterConfigKey]
		private static readonly ModConfigurationKey<bool> KEY_DEFAULT_OPEN = new ModConfigurationKey<bool>("default_open", "If true delegates will be expanded by default", () => false);
		[AutoRegisterConfigKey]
		private static readonly ModConfigurationKey<bool> KEY_SHOW_DELEGATES = new ModConfigurationKey<bool>("show_deleages", "If false delegates will not be shown", () => true);
		[AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> KEY_SHORT_NAMES = new ModConfigurationKey<bool>("short_names", "Show short delegate names.", () => true);

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
						codes[i-1].operand = (sbyte)AccessTools.all;
                    }
                }
				return codes.AsEnumerable();
			}
		}

		[HarmonyPatch(typeof(WorkerInspector))]
		[HarmonyPatch("BuildInspectorUI")]
		class WorkerInspector_BuildInspectorUI_Patch
		{
			public static MethodInfo delegateFunc = typeof(ShowDelegates).GetMethod(nameof(ShowDelegates.GenerateDelegateProxy), BindingFlags.NonPublic | BindingFlags.Static);

            private static bool Prefix(WorkerInspector __instance, Worker worker, UIBuilder ui, Predicate<ISyncMember> memberFilter = null)
			{
				var hidden = Pool.BorrowList<ISyncMember>();
                for (int i = 0; i < worker.SyncMemberCount; i++)
                {
                    ISyncMember syncMember = worker.GetSyncMember(i);
                    if (memberFilter != null && !memberFilter(syncMember)) continue;
                    var shown = worker.GetSyncMemberFieldInfo(i).GetCustomAttribute<HideInInspectorAttribute>() == null;
                    if (shown)
                    {
                        SyncMemberEditorBuilder.Build(syncMember, worker.GetSyncMemberName(i), worker.GetSyncMemberFieldInfo(i), ui);
                    }
                    else
                    {
                        hidden.Add(syncMember);
                    }
                }
                if (config.GetValue(KEY_SHOW_HIDDEN)) {
					foreach (var item in hidden)
					{
                        GenerateReferenceProxy(ui, worker.GetSyncMemberName(item), item);
                    }
                }
                if (!config.GetValue(KEY_SHOW_DELEGATES)) return false;

				if (worker.SyncMethodCount > 0)
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
                    RadiantUI_Constants.SetupLabelDriverColors(colorDriver);


					var initInfo = Traverse.Create(worker).Field<WorkerInitInfo>("InitInfo").Value;

                    for (int j = 0; j < worker.SyncMethodCount; j++)
                    {
						var info = initInfo.syncMethods[j];

                        var delegateType = info.methodType;
						if (!typeof(MulticastDelegate).IsAssignableFrom(delegateType))
						{
							try
							{
								// this could throw in many ways....
								delegateType = Helper.ClassifyDelegate(info.method);
							}
							catch(Exception e)
							{
								Error("Error while classifying function " + info.method + "\n" + e.ToString());
								delegateType = null;
							}
                            if (delegateType == null)
							{
								//Error("Unmapped type. Please report this message to the mod author: Could not identify " + info.method + " on type " + info.method.DeclaringType);
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
                return false;
            }
		}
	}
}