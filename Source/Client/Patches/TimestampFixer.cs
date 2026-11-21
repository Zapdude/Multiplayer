using System;
using System.Collections.Generic;
using HarmonyLib;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace Multiplayer.Client.Patches;

public static class TimestampFixer
{
    delegate ref int FieldGetter<in T>(T obj) where T : IExposable;

    // Shifts a timestamp field by the given offset
    delegate void FieldFixer(IExposable obj, int offset);

    private static Dictionary<Type, List<FieldFixer>> timestampFields = new();

    // timestampFields including the fields of base types, by the exact type
    private static Dictionary<Type, List<FieldFixer>> resolvedFields = new();

    // Values below minValid are "never" markers: kept as they are, and shifted values are clamped to stay valid
    private static void Add<T>(FieldGetter<T> t, int minValid = int.MinValue) where T : IExposable
    {
        timestampFields.GetOrAddNew(typeof(T)).Add((obj, offset) =>
        {
            ref int field = ref t((T)obj);
            if (field >= minValid)
                field = Math.Max(minValid, field + offset);
        });
    }

    /// <summary>
    /// Registers a timestamp fixer owned by a compat mod. The delegate receives the exposable and the
    /// offset and must add it to its own fields. Multiplayer.Compat's RegisterTimestampFixer probes for
    /// exactly this signature; without it the fallback builds a FieldGetter list that does not match
    /// this fork's List&lt;FieldFixer&gt; and every VEF ability fixer fails at startup.
    /// </summary>
    public static void RegisterTimestampFields(Type type, Action<object, int> convert)
    {
        if (type == null || convert == null)
            return;

        timestampFields.GetOrAddNew(type).Add((obj, offset) => convert(obj, offset));

        // FieldsFor caches the base-type-expanded list per exact type.
        resolvedFields.Clear();
    }

    static TimestampFixer()
    {
        // Pawn
        Add((Pawn pawn) => ref pawn.lastSleepDisturbedTick);

        // Pawn_MindState. Fields with a -99999 "never" value keep it; 0 is a real tick
        Add((Pawn_MindState mind) => ref mind.canSleepTick, minValid: 0);
        Add((Pawn_MindState mind) => ref mind.canLovinTick, minValid: 0);
        Add((Pawn_MindState mind) => ref mind.lastIngestTick, minValid: 0);
        Add((Pawn_MindState mind) => ref mind.nextApparelOptimizeTick, minValid: 0);
        Add((Pawn_MindState mind) => ref mind.exitMapAfterTick, minValid: 0);
        Add((Pawn_MindState mind) => ref mind.lastDisturbanceTick, minValid: 0);
        Add((Pawn_MindState mind) => ref mind.hibernationEndedTick, minValid: 0);
        Add((Pawn_MindState mind) => ref mind.lastMeleeThreatHarmTick, minValid: 0);
        Add((Pawn_MindState mind) => ref mind.lastEngageTargetTick, minValid: 0);
        Add((Pawn_MindState mind) => ref mind.lastAttackTargetTick, minValid: 0);
        Add((Pawn_MindState mind) => ref mind.lastAssignedInteractTime, minValid: 0);
        Add((Pawn_MindState mind) => ref mind.lastInventoryRawFoodUseTick);
        Add((Pawn_MindState mind) => ref mind.lastTakeCombatEnhancingDrugTick, minValid: 0);
        Add((Pawn_MindState mind) => ref mind.lastTakeRecreationalDrugTick, minValid: 0);
        Add((Pawn_MindState mind) => ref mind.lastHarmTick, minValid: 0);
        Add((Pawn_MindState mind) => ref mind.noAidRelationsGainUntilTick, minValid: 0);
        Add((Pawn_MindState mind) => ref mind.applyBedThoughtsTick, minValid: 1); // 0 = not scheduled
        Add((Pawn_MindState mind) => ref mind.applyThroneThoughtsTick, minValid: 1); // 0 = not scheduled
        Add((Pawn_MindState mind) => ref mind.lastSelfTendTick, minValid: 0);
        Add((Pawn_MindState mind) => ref mind.lastPredatorHuntingPlayerNotificationTick, minValid: 0);
        Add((Pawn_MindState mind) => ref mind.lastSlaveSuppressedTick, minValid: 0);
        Add((Pawn_MindState mind) => ref mind.lastHumanMeatIngestedTick, minValid: 0);
        Add((Pawn_MindState mind) => ref mind.nextInventoryStockTick, minValid: 0);
        Add((Pawn_MindState mind) => ref mind.nextSleepingBreastfeedStrictTick, minValid: 0);
        Add((Pawn_MindState mind) => ref mind.lastRangedHarmTick, minValid: 0);
        Add((Pawn_MindState mind) => ref mind.lastRotStinkTick, minValid: 0);
        Add((Pawn_MindState mind) => ref mind.lastBroughtToSafeTemperatureTick, minValid: 0);
        Add((Pawn_MindState mind) => ref mind.lastBecameVisibleTick, minValid: 0);
        Add((Pawn_MindState mind) => ref mind.lastBecameInvisibleTick, minValid: 0);
        Add((Pawn_MindState mind) => ref mind.lastForcedVisibleTick, minValid: 0);
        Add((Pawn_MindState mind) => ref mind.lastCombatantTick, minValid: 0);
        Add((Pawn_MindState mind) => ref mind.lastSwamTick, minValid: 0);

        // lastDayInteractionTick is a time of day, not an absolute tick. The roam cooldown is nullable.
        timestampFields.GetOrAddNew(typeof(Pawn_MindState)).Add((obj, offset) =>
        {
            var mind = (Pawn_MindState)obj;
            if (mind.lastStartRoamCooldownTick is int tick && tick >= 0)
                mind.lastStartRoamCooldownTick = Math.Max(0, tick + offset);
        });

        // Pawn_PathFollower
        Add((Pawn_PathFollower pather) => ref pather.lastMovedTick, minValid: 0);
        Add((Pawn_PathFollower pather) => ref pather.lastEnteredCellTick, minValid: 0);
        Add((Pawn_PathFollower pather) => ref pather.failedToFindCloseUnoccupiedCellTicks, minValid: 0);
        Add((Pawn_PathFollower pather) => ref pather.foundPathWhichCollidesWithPawns, minValid: 0);
        Add((Pawn_PathFollower pather) => ref pather.foundPathWithDanger, minValid: 0);

        // Other pawn trackers
        Add((Pawn_GuestTracker guest) => ref guest.ticksWhenAllowedToEscapeAgain);
        Add((Pawn_GuestTracker guest) => ref guest.lastPrisonBreakTicks);
        Add((Pawn_ApparelTracker apparel) => ref apparel.lastApparelWearoutTick, minValid: 0);
        Add((Pawn_HealthTracker health) => ref health.lastReceivedNeuralSuperchargeTick, minValid: 0);
        Add((Pawn_IdeoTracker ideo) => ref ideo.joinTick);
        Add((Pawn_PlayerSettings settings) => ref settings.joinTick);
        Add((Pawn_TrainingTracker training) => ref training.countDecayFrom);
        Add((Pawn_PsychicEntropyTracker entropy) => ref entropy.lastMeditationTick, minValid: 0);
        Add((Pawn_RelationsTracker relations) => ref relations.romanceEnableTick, minValid: 0);
        Add((Pawn_InteractionsTracker interactions) => ref interactions.lastInteractionTime, minValid: 0);
        Add((Pawn_MeleeVerbs melee) => ref melee.lastTerrainBasedVerbUseTick, minValid: 0);

        // Needs, abilities, skills
        Add((Need_Food food) => ref food.lastNonStarvingTick, minValid: 0);
        Add((Ability ability) => ref ability.cooldownEndTick);
        Add((Ability ability) => ref ability.lastCastTick, minValid: 0);
        Add((Pawn_SkillTracker skills) => ref skills.lastXpSinceMidnightResetTimestamp, minValid: 0);

        // Royalty
        Add((Pawn_RoyaltyTracker royalty) => ref royalty.lastDecreeTicks, minValid: 0);
        Add((RoyalTitle title) => ref title.receivedTick, minValid: 0);
        Add((FactionPermit permit) => ref permit.lastUsedTick, minValid: 1);
    }

    public static int? currentOffset;

    public static void FixPawn(Pawn p, Map oldMap, Map newMap)
    {
        var oldTime = oldMap?.AsyncTime().mapTicks ?? Multiplayer.AsyncWorldTime.worldTicks;
        var newTime = newMap?.AsyncTime().mapTicks ?? Multiplayer.AsyncWorldTime.worldTicks;
        if (newTime == oldTime) return;

        currentOffset = newTime - oldTime;

        MpLog.Debug($"Fixing pawn timestamps for {p} moving from {oldMap?.ToString() ?? "World"}:{oldTime} to {newMap?.ToString() ?? "World"}:{newTime}");

        try
        {
            // Saving a destroyed pawn logs a warning, and every pawn killed on a map is passed to the world pawns. Its
            // timestamps still have to move to the world's time: they're shifted back if it's resurrected.
            if (p.Destroyed)
                FixDirectly(p);
            else
                // Auxiliary save which is used to visit the pawn's data
                Scribe.saver.DebugOutputFor(p);
        }
        finally
        {
            currentOffset = null;
        }
    }

    // The holders of the fields above, without the save pass's walk through the pawn's data
    private static void FixDirectly(Pawn p)
    {
        ProcessExposable(p.mindState);
        ProcessExposable(p.guest);
        ProcessExposable(p.needs?.food);
        ProcessExposable(p.skills);
        ProcessExposable(p.royalty);
        ProcessExposable(p.pather);
        ProcessExposable(p.apparel);
        ProcessExposable(p.health);
        ProcessExposable(p.ideo);
        ProcessExposable(p.playerSettings);
        ProcessExposable(p.training);
        ProcessExposable(p.psychicEntropy);
        ProcessExposable(p.relations);
        ProcessExposable(p.interactions);
        ProcessExposable(p.meleeVerbs);

        // Also the abilities of titles, equipment, apparel and hediffs, like the save pass. Not the ideo roles' ones,
        // the ideo holds those
        if (p.abilities != null)
            foreach (var ability in p.abilities.AllAbilitiesForReading)
                if (ability.sourcePrecept is not Precept_Role)
                    ProcessExposable(ability);

        if (p.royalty != null)
        {
            foreach (var title in p.royalty.AllTitlesForReading)
                ProcessExposable(title);

            foreach (var permit in p.royalty.AllFactionPermits)
                ProcessExposable(permit);
        }
    }

    public static void ProcessExposable(IExposable exposable)
    {
        if (exposable == null) return;

        foreach (var fixer in FieldsFor(exposable.GetType()))
            fixer(exposable, currentOffset!.Value);
    }

    private static List<FieldFixer> FieldsFor(Type type)
    {
        if (resolvedFields.TryGetValue(type, out var fields))
            return fields;

        fields = new List<FieldFixer>();
        for (var t = type; t != null; t = t.BaseType)
            if (timestampFields.TryGetValue(t, out var own))
                fields.AddRange(own);

        return resolvedFields[type] = fields;
    }
}

[HarmonyPatch(typeof(DebugLoadIDsSavingErrorsChecker), nameof(DebugLoadIDsSavingErrorsChecker.RegisterDeepSaved))]
static class RegisterDeepSaved_ProcessExposable
{
    static void Prefix(object obj)
    {
        if (TimestampFixer.currentOffset != null && obj is IExposable exposable)
            TimestampFixer.ProcessExposable(exposable);
    }
}

[HarmonyPatch(typeof(Pawn), nameof(Pawn.DeSpawn))]
static class PawnDespawn_RememberMap
{
    static void Prefix(Pawn __instance)
    {
        if (Multiplayer.Client == null) return;
        __instance.GetComp<MultiplayerPawnComp>().lastMap = __instance.Map.uniqueID;
    }
}

[HarmonyPatch(typeof(WorldPawns), nameof(WorldPawns.RemovePawn))]
static class WorldPawnsRemovePawn_RememberTick
{
    static void Prefix(Pawn p)
    {
        if (Multiplayer.Client == null) return;
        p.GetComp<MultiplayerPawnComp>().worldPawnRemoveTick = Multiplayer.AsyncWorldTime.worldTicks;
    }
}

[HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
static class PawnSpawn_FixTimestamp
{
    static void Postfix(Pawn __instance)
    {
        if (Multiplayer.Client == null) return;
        if (__instance.Map == null) return;

        var comp = __instance.GetComp<MultiplayerPawnComp>();

        // World to map: WorldPawns.RemovePawn runs from here (or from DropPodUtility before the spawn) and records
        // the world clock. Drop pods and shuttles from the world take this path.
        if (comp.worldPawnRemoveTick != -1)
        {
            TimestampFixer.FixPawn(__instance, null, __instance.Map);
            comp.worldPawnRemoveTick = -1;
            comp.lastMap = -1;
            return;
        }

        // Map to map without a world pawn in between (teleports, gravships, other mods). PawnDespawn_RememberMap
        // recorded the map it left; the arrival never converted those timestamps before (#422).
        var lastMap = comp.lastMap;
        if (lastMap == -1) return;

        comp.lastMap = -1;
        if (lastMap == __instance.Map.uniqueID) return;

        var oldMap = Find.Maps.FirstOrDefault(m => m.uniqueID == lastMap);
        if (oldMap != null)
            TimestampFixer.FixPawn(__instance, oldMap, __instance.Map);
    }
}

[HarmonyPatch(typeof(WorldPawns), nameof(WorldPawns.AddPawn))]
static class WorldPawnsAddPawn_FixTimestamp
{
    static void Prefix(Pawn p)
    {
        if (Multiplayer.Client == null) return;

        var lastMap = p.GetComp<MultiplayerPawnComp>().lastMap;
        if (lastMap != -1)
        {
            TimestampFixer.FixPawn(p, Find.Maps.FirstOrDefault(m => m.uniqueID == lastMap), null);
            p.GetComp<MultiplayerPawnComp>().lastMap = -1;
        }
    }
}
