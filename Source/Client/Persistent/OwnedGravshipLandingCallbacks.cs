using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.Client.Util;
using RimWorld;
using Verse;

namespace Multiplayer.Client.Persistent;

[HarmonyPatch]
internal static class OwnedGravshipLandingCallbacks
{
	private static IEnumerable<MethodBase> TargetMethods()
	{
		yield return MpMethodUtil.GetLambda(typeof(GravshipUtility), "ArriveExistingMap", (MethodType)0, (Type[])null, 0);
		yield return MpMethodUtil.GetLambda(typeof(GravshipLandingMarker), "SpawnSetup", (MethodType)0, (Type[])null, 0);
		yield return MpMethodUtil.GetLambda(typeof(WorldComponent_GravshipController), "ExposeData", (MethodType)0, (Type[])null, 0);
	}

	private static bool Prefix(object __instance)
	{
		return GravshipUiOwnership.LandingPresentationAllowed(__instance);
	}
}
