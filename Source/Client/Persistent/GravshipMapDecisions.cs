using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client.Persistent;

public static class GravshipMapDecisions
{
	private sealed class LocalState
	{
		internal readonly HashSet<int> presented = new HashSet<int>();

		internal readonly Dictionary<int, (int generation, int owner, Window window)> windows = new Dictionary<int, (int, int, Window)>();
	}

	private static readonly ConditionalWeakTable<Game, LocalState> local = new ConditionalWeakTable<Game, LocalState>();

	private static List<GravshipMapDecision> Records => Multiplayer.GameComp.gravshipMapDecisions;

	public static void RegisterSync()
	{
		SyncMethod.Register(typeof(GravshipMapDecisions), "Resolve").SetVersion(9);
		SyncMethod.Register(typeof(GravshipMapDecisions), "Claim").SetVersion(9);
	}

	private static int NextGeneration()
	{
		return UniqueIDsManager.GetNextID(ref Multiplayer.GameComp.nextSessionId);
	}

	private static Map MapFor(GravshipMapDecision record)
	{
		return Find.Maps.FirstOrDefault((Predicate<Map>)((Map map) => map.uniqueID == record.mapId && map.Parent?.ID == record.parentId));
	}

	private static GravshipMapDecision Pending(int mapId)
	{
		return Records.FirstOrDefault((Predicate<GravshipMapDecision>)((GravshipMapDecision record) => record.mapId == mapId && !record.resolved));
	}

	public static bool HasPending(MapParent parent)
	{
		if (Multiplayer.Client != null && parent != null)
		{
			return Records.Any((Predicate<GravshipMapDecision>)((GravshipMapDecision record) =>
			{
				if (record.parentId == ((WorldObject)parent).ID && !record.resolved)
				{
					Map val = MapFor(record);
					return ((val != null) ? val.Parent : null) == parent;
				}
				return false;
			}));
		}
		return false;
	}

	public static bool WasManaged(MapParent parent)
	{
		if (Multiplayer.Client != null && parent != null)
		{
			return Records.Any((Predicate<GravshipMapDecision>)((GravshipMapDecision record) => record.parentId == ((WorldObject)parent).ID));
		}
		return false;
	}

	public static void HandleDialog(Map map, Building_GravEngine engine, Dialog_MessageBox dialog)
	{
		if (Multiplayer.Client == null)
		{
			Find.WindowStack.Add((Window)(object)dialog);
		}
		else
		{
			if (!Multiplayer.ExecutingCmds && !Multiplayer.Ticking)
			{
				return;
			}
			Map val = map;
			if (((val != null) ? val.Parent : null) == null || ((engine != null) ? ((Thing)engine).Faction : null) == null || !Find.Maps.Contains(map) || dialog == null)
			{
				return;
			}
			GravshipMapDecision gravshipMapDecision = Pending(map.uniqueID);
			if (gravshipMapDecision != null && MapFor(gravshipMapDecision) != map)
			{
				gravshipMapDecision.resolved = true;
				gravshipMapDecision = null;
			}
			if (gravshipMapDecision == null)
			{
				gravshipMapDecision = new GravshipMapDecision
				{
					mapId = map.uniqueID,
					parentId = ((WorldObject)map.Parent).ID,
					engineId = ((Thing)engine).thingIDNumber,
					generation = NextGeneration(),
					owner = GravshipUiOwnership.Owner(engine),
					factionId = ((Thing)engine).Faction.loadID,
					allowKeep = (dialog.buttonAAction == null),
					allowAbandon = (dialog.buttonAAction != null),
					allowSettle = (dialog.buttonBAction != null)
				};
				Records.RemoveAll((GravshipMapDecision old) => old.mapId == map.uniqueID && old.parentId == ((WorldObject)map.Parent).ID);
				Records.Add(gravshipMapDecision);
			}
		}
	}

	private static bool CanIssue(GravshipMapDecision record)
	{
		int currentExecutingCmdPlayerId = TickPatch.currentExecutingCmdPlayerId;
		if (Multiplayer.Client != null && Multiplayer.ExecutingCmds && currentExecutingCmdPlayerId >= 0 && Multiplayer.GameComp.playerData.ContainsKey(currentExecutingCmdPlayerId))
		{
			return Faction.OfPlayer?.loadID == record.factionId;
		}
		return false;
	}

	public static void Claim(int mapId, int generation, int expectedOwner)
	{
		GravshipMapDecision gravshipMapDecision = ((Multiplayer.Client == null) ? null : Pending(mapId));
		if (gravshipMapDecision != null && gravshipMapDecision.generation == generation && gravshipMapDecision.owner == expectedOwner && MapFor(gravshipMapDecision) != null && CanIssue(gravshipMapDecision))
		{
			gravshipMapDecision.owner = TickPatch.currentExecutingCmdPlayerId;
			gravshipMapDecision.generation = NextGeneration();
			CloseLocal(mapId);
		}
	}

	public static void Resolve(int mapId, int generation, int expectedOwner, int choice)
	{
		GravshipMapDecision gravshipMapDecision = ((Multiplayer.Client == null) ? null : Pending(mapId));
		Map val = default;
		int num;
		if (gravshipMapDecision != null && gravshipMapDecision.generation == generation && gravshipMapDecision.owner == expectedOwner && expectedOwner >= 0 && TickPatch.currentExecutingCmdPlayerId == expectedOwner && CanIssue(gravshipMapDecision))
		{
			val = MapFor(gravshipMapDecision);
			num = ((val == null) ? 1 : 0);
		}
		else
		{
			num = 1;
		}
		bool flag = (byte)num != 0;
		if (!flag)
		{
			bool flag2 = ((choice < 0 || choice > 2) ? true : false);
			flag = flag2;
		}
		if (flag || (choice == 0 && !gravshipMapDecision.allowKeep) || (choice == 1 && !gravshipMapDecision.allowSettle) || (choice == 2 && !gravshipMapDecision.allowAbandon))
		{
			return;
		}
		gravshipMapDecision.resolved = true;
		CloseLocal(mapId);
		switch (choice)
		{
		case 1:
			if (((bool)val.Parent.CanBeSettled))
			{
				SettleInExistingMapUtility.Settle(val);
			}
			break;
		case 2:
		{
			MapParent parent = val.Parent;
			if (parent is Settlement && ((WorldObject)parent).Faction == Faction.OfPlayer)
			{
				parent.Abandon(false);
				break;
			}
			bool flag3 = default;
			parent.ShouldRemoveMapNow(out flag3);
			Current.Game.DeinitAndRemoveMap(val, false);
			if (!((WorldObject)parent).Destroyed && (flag3 || parent.forceRemoveWorldObjectWhenMapRemoved))
			{
				((WorldObject)parent).Destroy();
			}
			break;
		}
		}
	}

	public static void ForgetPlayer(int playerId)
	{
		if (Multiplayer.Client == null || (!Multiplayer.ExecutingCmds && !Multiplayer.Ticking))
		{
			return;
		}
		foreach (GravshipMapDecision item in from record in Records
			where !record.resolved && record.owner == playerId
			orderby record.mapId
			select record)
		{
			item.owner = -1;
			item.generation = NextGeneration();
		}
	}

	private static bool MayPresent(GravshipMapDecision record)
	{
		if (!Multiplayer.IsReplay && !TickPatch.Simulating && !Multiplayer.arbiterInstance && record.owner >= 0 && record.owner == Multiplayer.session.playerId && Multiplayer.RealPlayerFaction?.loadID == record.factionId)
		{
			return MapFor(record) != null;
		}
		return false;
	}

	private static void CloseLocal(int mapId)
	{
		if (Current.Game == null || !local.TryGetValue(Current.Game, out var value) || !value.windows.Remove(mapId, out (int, int, Window) value2))
		{
			return;
		}
		using (LocalOnlyScope.Enter())
		{
			try
			{
				if (Find.WindowStack.Windows.Contains(value2.Item3))
				{
					value2.Item3.Close(true);
				}
			}
			catch (Exception value3)
			{
				Log.Error($"Multiplayer: gravship map decision presentation failed: {value3}");
			}
		}
	}

	internal static void Pump()
	{
		if (Multiplayer.Client == null || !Multiplayer.InInterface || Multiplayer.ExecutingCmds || Multiplayer.Ticking || Current.Game == null)
		{
			return;
		}
		LocalState orCreateValue = local.GetOrCreateValue(Current.Game);
		KeyValuePair<int, (int, int, Window)>[] array = orCreateValue.windows.ToArray();
		for (int i = 0; i < array.Length; i++)
		{
			KeyValuePair<int, (int, int, Window)> keyValuePair = array[i];
			GravshipMapDecision gravshipMapDecision = Pending(keyValuePair.Key);
			if (gravshipMapDecision == null || gravshipMapDecision.generation != keyValuePair.Value.Item1 || gravshipMapDecision.owner != keyValuePair.Value.Item2 || !MayPresent(gravshipMapDecision))
			{
				CloseLocal(keyValuePair.Key);
			}
		}
		foreach (GravshipMapDecision item in from record in Records
			where !record.resolved
			orderby record.mapId
			select record)
		{
			if (!MayPresent(item) || !orCreateValue.presented.Add(item.generation))
			{
				continue;
			}
			using (LocalOnlyScope.Enter())
			{
				try
				{
					int mapId = item.mapId;
					int generation = item.generation;
					int owner = item.owner;
					Dialog_MessageBox val = (item.allowAbandon ? new Dialog_MessageBox(Translator.Translate("VGE_MapDecisionText"), ((string)Translator.Translate("VGE_DiscardMap")), (Action)(() =>
					{
						Resolve(mapId, generation, owner, 2);
					}), ((string)Translator.Translate("VGE_KeepMap")), (Action)(() =>
					{
						Resolve(mapId, generation, owner, 1);
					}), (string)null, true, (Action)null, (Action)null, (WindowLayer)1) : new Dialog_MessageBox(Translator.Translate("VGE_MapDecisionSettleText"), ((string)Translator.Translate("VGE_DontSettle")), (Action)(() =>
					{
						Resolve(mapId, generation, owner, 0);
					}), ((string)Translator.Translate("VGE_SettleMap")), (Action)(() =>
					{
						Resolve(mapId, generation, owner, 1);
					}), (string)null, false, (Action)null, (Action)null, (WindowLayer)1));
					orCreateValue.windows[mapId] = (generation, owner, (Window)(object)val);
					Find.WindowStack.Add((Window)(object)val);
				}
				catch (Exception value)
				{
					Log.Error($"Multiplayer: gravship map decision presentation failed: {value}");
				}
			}
		}
	}

	internal static IEnumerable<Gizmo> Gizmos(MapParent parent, IEnumerable<Gizmo> original)
	{
		foreach (Gizmo item in original ?? Enumerable.Empty<Gizmo>())
		{
			yield return item;
		}
		if (Multiplayer.Client == null || !Multiplayer.InInterface || parent == null)
		{
			yield break;
		}
		GravshipMapDecision gravshipMapDecision = Records.FirstOrDefault((Predicate<GravshipMapDecision>)((GravshipMapDecision record) =>
		{
			if (record.parentId == ((WorldObject)parent).ID && !record.resolved)
			{
				Map val = MapFor(record);
				return ((val != null) ? val.Parent : null) == parent;
			}
			return false;
		}));
		if (gravshipMapDecision != null && gravshipMapDecision.factionId == Multiplayer.RealPlayerFaction?.loadID)
		{
			int mapId = gravshipMapDecision.mapId;
			int generation = gravshipMapDecision.generation;
			int expectedOwner = gravshipMapDecision.owner;
			yield return (Gizmo)new Command_Action
			{
				defaultLabel = ((string)Translator.Translate("MpGravshipMapDecision")),
				defaultDesc = ((string)Translator.Translate("MpGravshipMapDecisionDesc")),
				action = () =>
				{
					Claim(mapId, generation, expectedOwner);
				}
			};
		}
	}
}
