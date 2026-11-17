using System.Linq;
using Multiplayer.API;
using Multiplayer.Client.Patches;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client.Persistent;

public class GravshipTravelSession : Session
{
    public override Map Map => map;
    public PlanetTile InitialTile;

    // Set by the 1.7 UI ownership layer: the session covers a pending landing confirmation on
    // this map, for the gravship engine with this thingIDNumber.
    public bool LandingConfirmation;
    public int EngineId;

    private Map map;

    public GravshipTravelSession(Map map) : base(map)
    {
        this.map = map;
        InitialTile = map.Tile;
    }

    public override bool IsCurrentlyPausing(Map map) => Map != null && map == Map;
    public override FloatMenuOption GetBlockingWindowOptions(ColonistBar.Entry entry) => null;

    public void StopPausing()
    {
        map = null;
    }

    public override void PostRemoveSession()
    {
        PatchTickmanagerPlayerCanControlGetter.ResetLandingMessageFlag();
    }
}

public static class GravshipTravelUtils
{
    public static void OpenSessionAt(PlanetTile tile, bool landingConfirmation = false, Building_GravEngine engine = null)
    {
        // E.g. ArriveExistingMap without a map at the tile (vanilla logs an error and returns)
        Map map = Find.WorldObjects.MapParentAt(tile)?.Map;
        if (map == null)
            return;

        if (TryGetSessionAt(map.Tile, out GravshipTravelSession session))
        {
            session.LandingConfirmation |= landingConfirmation;
            if (engine != null)
                session.EngineId = engine.thingIDNumber;

            return;
        }

        GravshipTravelSession newSession = new GravshipTravelSession(map)
        {
            LandingConfirmation = landingConfirmation,
            EngineId = engine?.thingIDNumber ?? 0
        };
        map.MpComp()?.sessionManager?.AddSession(newSession);
    }

    public static Map LandingMap(WorldComponent_GravshipController controller)
    {
        GravshipLandingMarker marker = controller?.landingMarker;
        if (marker == null || marker.Destroyed || marker.gravship == null)
            return null;

        return marker.Map ?? controller.landingMap;
    }

    public static void CloseSessionAt(PlanetTile tile)
    {
        if(TryGetSessionAt(tile, out GravshipTravelSession session))
        {
            session.Map.MpComp()?.sessionManager?.RemoveSession(session);
            session.StopPausing();
        }
    }

    public static bool TryGetSessionAt(PlanetTile takeoffTile, out GravshipTravelSession session)
    {
        session = null;

        foreach (var sessionManager in Multiplayer.game.mapComps.Select(mp => mp.sessionManager))
        {
            foreach (var iSession in sessionManager.AllSessions.OfType<GravshipTravelSession>())
            {
                if (iSession.InitialTile == takeoffTile)
                {
                    session = iSession;
                    return true;
                }
            }
        }

        return false;
    }

    public static bool HasSessionAt(PlanetTile takeoffTile)
    {
        return TryGetSessionAt(takeoffTile, out _);
    }

    public static void StartFreeze()
    {
        SetFreeze(true);
    }

    public static void StopFreeze()
    {
        SetFreeze(false);
    }

    private static void SetFreeze(bool value) => Multiplayer.Client.Send(new ClientFreezePacket(value));
    private static string GravshipDialogPrefix => "ConfirmGravEngineLaunch".Translate().RawText;

    // TODO: Try to find a better solution for that
    public static void CloseGravshipPrelaunchDialog()
    {
        Dialog_MessageBox dialog = Find.WindowStack.Windows
            .OfType<Dialog_MessageBox>()
            .FirstOrDefault(w => w.text.RawText.StartsWith(GravshipDialogPrefix));
        dialog?.Close();
    }
}
