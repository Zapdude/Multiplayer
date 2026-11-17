using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client.Persistent;
using Multiplayer.Client.Saving;
using Multiplayer.Common;
using Verse;

namespace Multiplayer.Client.Comp
{
    public class MultiplayerGameComp : IExposable, IHasSessionData
    {
        public bool asyncTime;
        public bool multifaction;
        public bool debugMode;
        public bool logDesyncTraces;
        public PauseOnLetter pauseOnLetter;
        public TimeControl timeControl;
        public Dictionary<int, PlayerData> playerData = new(); // player id to player data
        public int nextSessionId;

        // Gravship UI ownership and pending map decisions. Ported from the 1.7 Multiplayer build
        // (workshop 3820003322) so Multiplayer Compatibility's VGE module can drive them.
        public Dictionary<int, int> gravshipUiOwners = new(); // gravship engine thingIDNumber -> player id
        public List<GravshipMapDecision> gravshipMapDecisions = new();

        public string idBlockBase64;

        public bool IsLowestWins => timeControl == TimeControl.LowestWins;

        public PlayerData LocalPlayerDataOrNull => playerData.GetValueOrDefault(Multiplayer.session.playerId);

        public void ExposeData()
        {
            Scribe_Values.Look(ref asyncTime, "asyncTime", true, true);
            Scribe_Values.Look(ref multifaction, "multifaction", false, true);
            Scribe_Values.Look(ref debugMode, "debugMode");
            Scribe_Values.Look(ref logDesyncTraces, "logDesyncTraces");
            Scribe_Values.Look(ref pauseOnLetter, "pauseOnLetter");
            Scribe_Values.Look(ref timeControl, "timeControl");
            Scribe_Values.Look(ref nextSessionId, "nextSessionId");
            Scribe_Collections.Look(ref gravshipUiOwners, "gravshipUiOwners", LookMode.Value, LookMode.Value);
            if (gravshipUiOwners == null) gravshipUiOwners = new Dictionary<int, int>();
            Scribe_Collections.Look(ref gravshipMapDecisions, "gravshipMapDecisions", LookMode.Deep, Array.Empty<object>());
            if (gravshipMapDecisions == null) gravshipMapDecisions = new List<GravshipMapDecision>();

            // Store for back-compat conversion in GameExposeComponentsPatch
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                Scribe_Values.Look(ref idBlockBase64, "globalIdBlock");
        }

        public void WriteSessionData(ByteWriter writer)
        {
            SyncSerialization.WriteSync(writer, playerData);
        }

        public void ReadSessionData(ByteReader reader)
        {
            playerData = SyncSerialization.ReadSync<Dictionary<int, PlayerData>>(reader);
            DebugSettings.godMode = LocalPlayerDataOrNull?.godMode ?? false;
        }

        [SyncMethod(debugOnly = true)]
        public void SetGodMode(int playerId, bool godMode)
        {
            playerData[playerId].godMode = godMode;
        }

        public TimeSpeed GetLowestTimeVote(int tickableId, bool excludePaused = false)
        {
            return (TimeSpeed)playerData.Values
                .SelectMany(p => p.AllTimeVotes.GetOrEmpty(tickableId))
                .Where(v => !excludePaused || v != TimeVote.Paused)
                .DefaultIfEmpty(TimeVote.Paused)
                .Min();
        }

        public void ResetAllTimeVotes(int tickableId)
        {
            playerData.Values.Do(p => p.SetTimeVote(tickableId, TimeVote.PlayerResetTickable));
        }
    }
}
