using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace Multiplayer.Client.Persistent;

[HarmonyPatch(typeof(WorldComponent_GravshipController), "WorldComponentOnGUI")]
internal static class OwnedGravshipLandingControls
{
	private static bool Prefix(WorldComponent_GravshipController __instance)
	{
		return GravshipUiOwnership.LandingControls(__instance);
	}

	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> list = instructions.ToList();
		FieldInfo marker = AccessTools.Field(typeof(WorldComponent_GravshipController), "landingMarker");
		CodeInstruction[] array = list.Where((CodeInstruction i) => i.opcode == OpCodes.Stfld && object.Equals(i.operand, marker)).ToArray();
		MethodInfo vgeBeginLanding = AccessTools.Method("VanillaGravshipExpanded.WorldComponent_GravshipController_WorldComponentOnGUI_Patch:TryBeginLanding", (Type[])null, (Type[])null);
		if (array.Length == 0 && vgeBeginLanding != null && list.Count((Predicate<CodeInstruction>)((CodeInstruction i) => CodeInstructionExtensions.Calls(i, vgeBeginLanding))) == 1)
		{
			return list;
		}
		if (array.Length != 1)
		{
			throw new InvalidOperationException($"Expected one gravship landing UI marker store, found {array.Length}");
		}
		array[0].opcode = OpCodes.Call;
		array[0].operand = AccessTools.Method(typeof(GravshipUiOwnership), "SetLandingMarkerFromUi", (Type[])null, (Type[])null);
		return list;
	}
}
