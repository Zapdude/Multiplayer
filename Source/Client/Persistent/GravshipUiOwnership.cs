using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client.Compatibility;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Persistent;

public static class GravshipUiOwnership
{
	private sealed class LocalState
	{
		internal readonly Dictionary<int, (Building_GravEngine engine, Action action)> Pending = new Dictionary<int, (Building_GravEngine, Action)>();

		internal readonly Dictionary<int, (Building_GravEngine engine, System.WeakReference<Window> window)> Windows = new Dictionary<int, (Building_GravEngine, System.WeakReference<Window>)>();

		internal readonly List<int> RemovedWindows = new List<int>();

		internal Building_GravEngine RouteEngine;

		internal TilePicker RoutePicker;

		internal Action<PlanetTile> RouteChosen;

		internal Designator_MoveGravship LandingDesignator;

		internal readonly Dictionary<GravshipLandingMarker, (Rot4 rotation, int pending)> Rotations = new Dictionary<GravshipLandingMarker, (Rot4, int)>();

		internal readonly List<GravshipLandingMarker> RemovedRotations = new List<GravshipLandingMarker>();
	}

	public const int NoOwner = -1;

	private static readonly ConditionalWeakTable<Game, LocalState> local = new ConditionalWeakTable<Game, LocalState>();

	[ThreadStatic]
	private static int presenting;

	[ThreadStatic]
	private static Building_GravEngine presentingEngine;

	internal static bool IsPresenting => presenting > 0;

	internal static Building_GravEngine LandingEngine
	{
		get
		{
			WorldComponent_GravshipController gravshipController = Find.GravshipController;
			if (gravshipController == null)
			{
				return null;
			}
			GravshipLandingMarker landingMarker = gravshipController.landingMarker;
			if (landingMarker == null)
			{
				return null;
			}
			Gravship gravship = landingMarker.gravship;
			if (gravship == null)
			{
				return null;
			}
			return gravship.Engine;
		}
	}

	public static int Owner(Building_GravEngine engine)
	{
		if (engine != null && Multiplayer.game != null)
		{
			return Multiplayer.GameComp.gravshipUiOwners.GetValueOrDefault(((Thing)engine).thingIDNumber, -1);
		}
		return -1;
	}

	public static bool IsLocalOwner(Building_GravEngine engine)
	{
		if (Multiplayer.Client != null)
		{
			if (!Multiplayer.IsReplay && !TickPatch.Simulating && !Multiplayer.arbiterInstance && engine != null && Owner(engine) == Multiplayer.session.playerId)
			{
				return ((Thing)engine).Faction == Multiplayer.RealPlayerFaction;
			}
			return false;
		}
		return true;
	}

	public static Building_GravEngine Engine(TargetInfo target)
	{
		Thing thing = target.Thing;
		if (thing == null)
		{
			return null;
		}
		return ThingCompUtility.TryGetComp<CompPilotConsole>(thing)?.engine;
	}

	public static void Record(Building_GravEngine engine, bool replace = false)
	{
		if (Multiplayer.Client != null && Multiplayer.ExecutingCmds && engine != null && TickPatch.currentExecutingCmdPlayerId >= 0 && (replace || Owner(engine) == -1))
		{
			Multiplayer.GameComp.gravshipUiOwners[((Thing)engine).thingIDNumber] = TickPatch.currentExecutingCmdPlayerId;
		}
	}

	public static void ForgetPlayer(int playerId)
	{
		int[] array = (from p in Multiplayer.GameComp.gravshipUiOwners
			where p.Value == playerId
			select p.Key).ToArray();
		foreach (int key in array)
		{
			Multiplayer.GameComp.gravshipUiOwners.Remove(key);
		}
	}

	public static void Forget(Building_GravEngine engine)
	{
		if (Multiplayer.Client != null && engine != null && (Multiplayer.ExecutingCmds || Multiplayer.Ticking))
		{
			Multiplayer.GameComp.gravshipUiOwners.Remove(((Thing)engine).thingIDNumber);
		}
	}

	public static void RegisterSync()
	{
		SyncMethod.Register(typeof(GravshipUiOwnership), "ClaimAndOpen").SetVersion(4);
		SyncMethod.Register(typeof(GravshipUiOwnership), "ConfirmPrelaunch").SetVersion(4);
		SyncMethod.Register(typeof(GravshipUiOwnership), "RotateLanding").SetVersion(4);
		SyncMethod.Register(typeof(GravshipUiOwnership), "AbortLanding").SetVersion(4);
		SyncMethod.Register(typeof(GravshipUiOwnership), "ClaimPendingLanding").SetVersion(5);
		SyncMethod.Register(typeof(GravshipUiOwnership), "RotatePendingLanding").SetVersion(5);
		SyncMethod.Register(typeof(GravshipUiOwnership), "AbortPendingLanding").SetVersion(5);
	}

	private static void Queue(Building_GravEngine engine, Action action)
	{
		if (IsLocalOwner(engine) && Current.Game != null)
		{
			local.GetOrCreateValue(Current.Game).Pending[((Thing)engine).thingIDNumber] = (engine, action);
		}
	}

	internal static void Pump()
	{
		if (!Multiplayer.InInterface || TickPatch.Simulating || Multiplayer.IsReplay || Current.Game == null || !local.TryGetValue(Current.Game, out var value))
		{
			return;
		}
		value.RemovedWindows.Clear();
		foreach (KeyValuePair<int, (Building_GravEngine, System.WeakReference<Window>)> window in value.Windows)
		{
			if (!window.Value.Item2.TryGetTarget(out var target) || !Find.WindowStack.Windows.Contains(target))
			{
				value.RemovedWindows.Add(window.Key);
			}
			else if (!IsLocalOwner(window.Value.Item1))
			{
				target.Close(true);
				value.RemovedWindows.Add(window.Key);
			}
		}
		foreach (int removedWindow in value.RemovedWindows)
		{
			value.Windows.Remove(removedWindow);
		}
		if (value.RouteEngine != null && !IsLocalOwner(value.RouteEngine))
		{
			if (value.RoutePicker != null && Find.TilePicker == value.RoutePicker && (object)value.RoutePicker.tileChosen == value.RouteChosen)
			{
				value.RoutePicker.StopTargetingInt();
			}
			value.RouteEngine = null;
			value.RoutePicker = null;
			value.RouteChosen = null;
		}
		Designator_MoveGravship landingDesignator = value.LandingDesignator;
		if (landingDesignator != null)
		{
			GravshipLandingMarker marker = landingDesignator.marker;
			object engine;
			if (marker == null)
			{
				engine = null;
			}
			else
			{
				Gravship gravship = marker.gravship;
				engine = ((gravship != null) ? gravship.Engine : null);
			}
			if (!IsLocalOwner((Building_GravEngine)engine) || Find.GravshipController?.landingMarker != landingDesignator.marker)
			{
				if ((object)Find.DesignatorManager.SelectedDesignator == landingDesignator)
				{
					Find.DesignatorManager.Deselect();
				}
				value.LandingDesignator = null;
			}
		}
		value.RemovedRotations.Clear();
		foreach (GravshipLandingMarker key in value.Rotations.Keys)
		{
			if (IsCurrentLanding(Find.GravshipController, key))
			{
				Gravship gravship2 = key.gravship;
				if (IsLocalOwner((gravship2 != null) ? gravship2.Engine : null))
				{
					continue;
				}
			}
			value.RemovedRotations.Add(key);
		}
		foreach (GravshipLandingMarker removedRotation in value.RemovedRotations)
		{
			value.Rotations.Remove(removedRotation);
		}
		if (value.Pending.Count == 0)
		{
			return;
		}
		(Building_GravEngine engine, Action action)[] array = value.Pending.Values.ToArray();
		value.Pending.Clear();
		(Building_GravEngine, Action)[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			(Building_GravEngine, Action) tuple = array2[i];
			if (IsLocalOwner(tuple.Item1) && !((Thing)tuple.Item1).Destroyed)
			{
				Rand.PushState();
				Building_GravEngine val = presentingEngine;
				(presentingEngine, _) = tuple;
				presenting++;
				try
				{
					tuple.Item2();
				}
				finally
				{
					presenting--;
					presentingEngine = val;
					Rand.PopState();
				}
			}
		}
	}

	internal static void TrackWindow(Window window)
	{
		if (Multiplayer.Client != null && Current.Game != null && Multiplayer.InInterface)
		{
			Dialog_MessageBox val = (Dialog_MessageBox)(object)((window is Dialog_MessageBox) ? window : null);
			if (val != null)
			{
				object? obj = val.buttonAAction?.Target;
				WorldComponent_GravshipController val2 = (WorldComponent_GravshipController)((obj is WorldComponent_GravshipController) ? obj : null);
				if (val2 != null && val.buttonAAction.Method == AccessTools.Method(typeof(WorldComponent_GravshipController), "AbortLanding", (Type[])null, (Type[])null) && val2 == Find.GravshipController)
				{
					GravshipLandingMarker landingMarker = val2.landingMarker;
					object engine;
					if (landingMarker == null)
					{
						engine = null;
					}
					else
					{
						Gravship gravship = landingMarker.gravship;
						engine = ((gravship != null) ? gravship.Engine : null);
					}
					if (IsLocalOwner((Building_GravEngine)engine))
					{
						GravshipLandingMarker landingMarker2 = val2.landingMarker;
						Building_GravEngine engine2 = landingMarker2.gravship.Engine;
						int owner = Owner(engine2);
						Map map = GravshipTravelUtils.LandingMap(val2);
						int markerId = ((Thing)landingMarker2).thingIDNumber;
						val.acceptAction = (val.buttonAAction = () =>
						{
							AbortPendingLanding(map, markerId, owner);
						});
						local.GetOrCreateValue(Current.Game).Windows[((Thing)engine2).thingIDNumber] = (engine2, new System.WeakReference<Window>(window));
					}
				}
			}
		}
		if (IsPresenting && presentingEngine != null && window is Dialog_MessageBox && Current.Game != null)
		{
			local.GetOrCreateValue(Current.Game).Windows[((Thing)presentingEngine).thingIDNumber] = (presentingEngine, new System.WeakReference<Window>(window));
		}
	}

	internal static void ClosePrelaunchWindow(Building_GravEngine engine)
	{
		if (engine != null && Current.Game != null && local.TryGetValue(Current.Game, out var value) && value.Windows.Remove(((Thing)engine).thingIDNumber, out (Building_GravEngine, System.WeakReference<Window>) value2) && value2.Item2.TryGetTarget(out var target) && Find.WindowStack.Windows.Contains(target))
		{
			target.Close(true);
		}
	}

	internal static void TrackRoute(CompPilotConsole console, bool launching)
	{
		if (IsPresenting && launching && Current.Game != null)
		{
			LocalState orCreateValue = local.GetOrCreateValue(Current.Game);
			orCreateValue.RouteEngine = ((CompGravshipFacility)console).engine;
			orCreateValue.RoutePicker = Find.TilePicker;
			orCreateValue.RouteChosen = orCreateValue.RoutePicker?.tileChosen;
		}
	}

	internal static void TrackLandingDesignator(Designator designator)
	{
		if (Multiplayer.Client == null || Current.Game == null)
		{
			return;
		}
		Designator_MoveGravship val = (Designator_MoveGravship)(object)((designator is Designator_MoveGravship) ? designator : null);
		if (val != null && Find.GravshipController?.landingMarker == val.marker)
		{
			GravshipLandingMarker marker = val.marker;
			object engine;
			if (marker == null)
			{
				engine = null;
			}
			else
			{
				Gravship gravship = marker.gravship;
				engine = ((gravship != null) ? gravship.Engine : null);
			}
			if (IsLocalOwner((Building_GravEngine)engine))
			{
				local.GetOrCreateValue(Current.Game).LandingDesignator = val;
			}
		}
	}

	internal static bool IsCurrentLanding(WorldComponent_GravshipController controller, GravshipLandingMarker marker)
	{
		if (controller != Find.GravshipController || marker == null || ((Thing)marker).Destroyed || marker.gravship == null || controller.landingMarker != marker)
		{
			return false;
		}
		Map val = controller.landingMap ?? ((Thing)marker).Map;
		if (val != null && Find.Maps.Contains(val))
		{
			if (((Thing)marker).Spawned)
			{
				return ((Thing)marker).Map == val;
			}
			return true;
		}
		return false;
	}

	private static GravshipLandingMarker PendingLanding(Map map, int markerId)
	{
		WorldComponent_GravshipController gravshipController = Find.GravshipController;
		GravshipLandingMarker val = gravshipController?.landingMarker;
		if (markerId <= 0 || val == null || ((Thing)val).thingIDNumber != markerId || !IsCurrentLanding(gravshipController, val) || GravshipTravelUtils.LandingMap(gravshipController) != map)
		{
			return null;
		}
		return val;
	}

	public static void ClaimPendingLanding(Map map, int markerId, int expectedOwner)
	{
		GravshipLandingMarker val = PendingLanding(map, markerId);
		if (val != null)
		{
			ClaimAndOpen(null, expectedOwner, val);
		}
	}

	public static void RotatePendingLanding(Map map, int markerId, int expectedOwner, Rot4 rotation)
	{
		GravshipLandingMarker val = PendingLanding(map, markerId);
		if (val != null && IssuedForLanding(Find.GravshipController, val, expectedOwner))
		{
			RotateLanding(val, rotation);
		}
	}

	public static void AbortPendingLanding(Map map, int markerId, int expectedOwner)
	{
		GravshipLandingMarker val = PendingLanding(map, markerId);
		if (val != null)
		{
			AbortLanding(Find.GravshipController, val, expectedOwner);
		}
	}

	private static bool IssuedForLanding(WorldComponent_GravshipController controller, GravshipLandingMarker marker, int expectedOwner)
	{
		if (Multiplayer.Client != null && Multiplayer.ExecutingCmds && IsCurrentLanding(controller, marker) && expectedOwner >= 0 && Owner(marker.gravship.Engine) == expectedOwner && TickPatch.currentExecutingCmdPlayerId == expectedOwner)
		{
			return CommandAuthority.CanActForFaction(((Thing)marker.gravship.Engine).Faction);
		}
		return false;
	}

	public static void RotateLanding(GravshipLandingMarker marker, Rot4 rotation)
	{
		try
		{
			if (rotation.IsValid)
			{
				WorldComponent_GravshipController gravshipController = Find.GravshipController;
				object engine;
				if (marker == null)
				{
					engine = null;
				}
				else
				{
					Gravship gravship = marker.gravship;
					engine = ((gravship != null) ? gravship.Engine : null);
				}
				if (IssuedForLanding(gravshipController, marker, Owner((Building_GravEngine)engine)))
				{
					marker.GravshipRotation = rotation;
				}
			}
		}
		finally
		{
			if (Multiplayer.ExecutingCmds && TickPatch.currentExecutingCmdIssuedBySelf && marker != null && Current.Game != null && local.TryGetValue(Current.Game, out var value) && value.Rotations.TryGetValue(marker, out (Rot4, int) value2))
			{
				if (value2.Item2 <= 1)
				{
					value.Rotations.Remove(marker);
				}
				else
				{
					value.Rotations[marker] = (value2.Item1, value2.Item2 - 1);
				}
			}
		}
	}

	internal static void RequestRotation(GravshipLandingMarker marker, Rot4 rotation)
	{
		if (rotation.IsValid && Current.Game != null)
		{
			Gravship gravship = marker.gravship;
			if (IsLocalOwner((gravship != null) ? gravship.Engine : null) && IsCurrentLanding(Find.GravshipController, marker))
			{
				LocalState orCreateValue = local.GetOrCreateValue(Current.Game);
				orCreateValue.Rotations.TryGetValue(marker, out (Rot4, int) value);
				orCreateValue.Rotations[marker] = (rotation, value.Item2 + 1);
				RotatePendingLanding(GravshipTravelUtils.LandingMap(Find.GravshipController), ((Thing)marker).thingIDNumber, Owner(marker.gravship.Engine), rotation);
			}
		}
	}

	internal static bool TryReadRotation(GravshipLandingMarker marker, out Rot4 rotation)
	{
		rotation = default;
		if (Multiplayer.InInterface)
		{
			Gravship gravship = marker.gravship;
			if (IsLocalOwner((gravship != null) ? gravship.Engine : null) && Current.Game != null && IsCurrentLanding(Find.GravshipController, marker) && local.TryGetValue(Current.Game, out var value) && value.Rotations.TryGetValue(marker, out (Rot4, int) value2))
			{
				(rotation, _) = value2;
				return true;
			}
		}
		return false;
	}

	public static void AbortLanding(WorldComponent_GravshipController controller, GravshipLandingMarker marker, int expectedOwner)
	{
		if (IssuedForLanding(controller, marker, expectedOwner) && controller.landingMap != null)
		{
			controller.AbortLanding();
		}
	}

	internal static bool Prelaunch(Building_GravEngine engine, Action launchAction)
	{
		if (Multiplayer.Client == null || IsPresenting)
		{
			return true;
		}
		if (Multiplayer.ExecutingCmds || Multiplayer.Ticking)
		{
			Record(engine);
			GravshipTravelUtils.OpenSessionAt(((Thing)engine).Map.Tile, landingConfirmation: false, engine);
		}
		Queue(engine, () =>
		{
			if (((Thing)engine).Spawned && GravshipTravelUtils.HasSessionAt(((Thing)engine).Map.Tile))
			{
				ClosePrelaunchWindow(engine);
				GravshipUtility.PreLaunchConfirmation(engine, launchAction);
			}
		});
		return false;
	}

	internal static bool Route(CompPilotConsole console, bool launching)
	{
		if (Multiplayer.Client == null || !launching)
		{
			return true;
		}
		if (IsPresenting)
		{
			return true;
		}
		Building_GravEngine engine = ((CompGravshipFacility)console).engine;
		if (engine == null)
		{
			return false;
		}
		if (Multiplayer.ExecutingCmds || Multiplayer.Ticking)
		{
			Record(engine);
			GravshipTravelUtils.OpenSessionAt(((Thing)engine).Map.Tile, landingConfirmation: false, engine);
		}
		if (DebugSettings.skipGravshipTileSelection)
		{
			return true;
		}
		Queue(engine, () =>
		{
			if (((Thing)engine).Spawned && GravshipTravelUtils.HasSessionAt(((Thing)engine).Map.Tile))
			{
				ClosePrelaunchWindow(engine);
				console.StartChoosingDestination_NewTemp(true);
			}
		});
		return false;
	}

	internal static Building_GravEngine SessionEngine(GravshipTravelSession session)
	{
		if (session.LandingConfirmation)
		{
			return LandingEngine;
		}
		Map map = ((Session)session).Map;
		if (map == null)
		{
			return null;
		}
		return (from e in map.listerThings.AllThings.OfType<Building_GravEngine>()
			where (session.EngineId != 0) ? (((Thing)e).thingIDNumber == session.EngineId) : (e.launchInfo != null)
			orderby ((Thing)e).thingIDNumber
			select e).FirstOrDefault();
	}

	public static void RequestOpen(Building_GravEngine engine, GravshipLandingMarker marker = null)
	{
		if (Multiplayer.Client != null && Multiplayer.InInterface && engine != null)
		{
			if (marker != null)
			{
				ClaimPendingLanding(GravshipTravelUtils.LandingMap(Find.GravshipController), ((Thing)marker).thingIDNumber, Owner(engine));
			}
			else
			{
				ClaimAndOpen(engine, Owner(engine), null);
			}
		}
	}

	public static void ClaimAndOpen(Building_GravEngine engine, int expectedOwner, GravshipLandingMarker marker)
	{
		if (marker != null)
		{
			if (!IsCurrentLanding(Find.GravshipController, marker))
			{
				return;
			}
			engine = marker.gravship.Engine;
		}
		if (Multiplayer.Client == null || !Multiplayer.ExecutingCmds || engine == null || ((Thing)engine).Destroyed || TickPatch.currentExecutingCmdPlayerId < 0 || Owner(engine) != expectedOwner || ((Thing)engine).Faction != Faction.OfPlayer)
		{
			return;
		}
		RitualSession ritual = ((Thing)engine).Map?.MpComp()?.sessionManager.GetFirstOfType<RitualSession>();
		RitualSession ritualSession = ritual;
		bool flag = ritualSession != null && ritualSession.data.isGravshipRitual && Engine(ritual.data.target) == engine;
		if (marker != null)
		{
			if (Find.GravshipController?.landingMarker != marker)
			{
				return;
			}
			Gravship gravship = marker.gravship;
			if (((gravship != null) ? gravship.Engine : null) != engine || GravshipTravelUtils.LandingMap(Find.GravshipController) == null)
			{
				return;
			}
		}
		else if (!((Thing)engine).Spawned || (!flag && !GravshipTravelUtils.HasSessionAt(((Thing)engine).Map.Tile)))
		{
			return;
		}
		Record(engine, replace: true);
		if (marker != null)
		{
			Queue(engine, () =>
			{
				if (Find.GravshipController?.landingMarker == marker)
				{
					Map val = GravshipTravelUtils.LandingMap(Find.GravshipController);
					if (val != null)
					{
						CameraJumper.TryJump(val.Center, val, CameraJumper.MovementMode.Cut);
						Find.DesignatorManager.Select((Designator)(object)Find.GravshipController.MoveDesignator());
					}
				}
			});
		}
		else if (flag)
		{
			Queue(engine, () =>
			{
				if (((Thing)engine).Map?.MpComp()?.sessionManager.GetFirstOfType<RitualSession>() == ritual && !Find.WindowStack.Windows.OfType<RitualBeginProxy>().Any((RitualBeginProxy d) => d.Session == ritual))
				{
					ritual.OpenWindow();
				}
			});
		}
		else
		{
			if (((Thing)engine).Map.listerThings.AllThings.Select((Thing t) => ThingCompUtility.TryGetComp<CompPilotConsole>(t)).FirstOrDefault((CompPilotConsole c) => c?.engine == engine) == null)
			{
				return;
			}
			Queue(engine, () =>
			{
				if (((Thing)engine).Spawned && GravshipTravelUtils.HasSessionAt(((Thing)engine).Map.Tile))
				{
					ClosePrelaunchWindow(engine);
					int owner = Owner(engine);
					GravshipUtility.PreLaunchConfirmation(engine, (Action)(() =>
					{
						ConfirmPrelaunch(engine, owner);
					}));
				}
			});
		}
	}

	public static void ConfirmPrelaunch(Building_GravEngine engine, int expectedOwner)
	{
		if (Multiplayer.Client != null && Multiplayer.ExecutingCmds && engine != null && !((Thing)engine).Destroyed && ((Thing)engine).Spawned && ((Thing)engine).Faction == Faction.OfPlayer && Owner(engine) == expectedOwner && TickPatch.currentExecutingCmdPlayerId == expectedOwner && GravshipTravelUtils.HasSessionAt(((Thing)engine).Map.Tile))
		{
			CompPilotConsole? val = ((Thing)engine).Map.listerThings.AllThings.Select((Thing t) => ThingCompUtility.TryGetComp<CompPilotConsole>(t)).FirstOrDefault((CompPilotConsole c) => c?.engine == engine);
			if (val != null)
			{
				val.StartChoosingDestination_NewTemp(true);
			}
		}
	}

	internal static bool LandingPresentationAllowed(object instance)
	{
		if (Multiplayer.Client == null)
		{
			return true;
		}
		WorldComponent_GravshipController gravshipController = Find.GravshipController;
		GravshipLandingMarker val = gravshipController?.landingMarker;
		if (val?.gravship == null)
		{
			return false;
		}
		GravshipLandingMarker val2 = (GravshipLandingMarker)((instance is GravshipLandingMarker) ? instance : null);
		if (val2 != null)
		{
			if (val2 != val)
			{
				return false;
			}
		}
		else
		{
			WorldComponent_GravshipController val3 = (WorldComponent_GravshipController)((instance is WorldComponent_GravshipController) ? instance : null);
			if (val3 != null)
			{
				if (val3 != gravshipController)
				{
					return false;
				}
			}
			else
			{
				object? obj2;
				if (instance != null)
				{
					object? obj = AccessTools.Field(instance.GetType(), "map")?.GetValue(instance);
					obj2 = ((obj is Map) ? obj : null);
				}
				else
				{
					obj2 = null;
				}
				Map val4 = (Map)obj2;
				if (val4 == null || val4 != GravshipTravelUtils.LandingMap(gravshipController))
				{
					return false;
				}
			}
		}
		return IsLocalOwner(val.gravship.Engine);
	}

	internal static bool LandingControls(WorldComponent_GravshipController controller)
	{
		if (Multiplayer.Client != null)
		{
			GravshipLandingMarker landingMarker = controller.landingMarker;
			object engine;
			if (landingMarker == null)
			{
				engine = null;
			}
			else
			{
				Gravship gravship = landingMarker.gravship;
				engine = ((gravship != null) ? gravship.Engine : null);
			}
			if (!IsLocalOwner((Building_GravEngine)engine))
			{
				if (!controller.LandingAreaConfirmationInProgress || Find.ScreenshotModeHandler.Active || !WorldRendererUtility.DrawingMap || Multiplayer.IsReplay || TickPatch.Simulating || Multiplayer.arbiterInstance)
				{
					return false;
				}
				Rect val = new Rect((float)UI.screenWidth / 2f - 150f, (float)UI.screenHeight - 150f - 70f, 300f, 55f);
				Widgets.DrawWindowBackground(val);
				if (Widgets.ButtonText(GenUI.ContractedBy(val, 8f), ((string)Translator.Translate("MpGravshipTakeControls")), true, true, true, (TextAnchor?)null))
				{
					GravshipLandingMarker landingMarker2 = controller.landingMarker;
					object engine2;
					if (landingMarker2 == null)
					{
						engine2 = null;
					}
					else
					{
						Gravship gravship2 = landingMarker2.gravship;
						engine2 = ((gravship2 != null) ? gravship2.Engine : null);
					}
					RequestOpen((Building_GravEngine)engine2, controller.landingMarker);
				}
				return false;
			}
		}
		return true;
	}

	internal static void SetLandingMarkerFromUi(WorldComponent_GravshipController controller, GravshipLandingMarker marker)
	{
		if (Multiplayer.Client == null || Multiplayer.ExecutingCmds)
		{
			controller.landingMarker = marker;
		}
	}
}
