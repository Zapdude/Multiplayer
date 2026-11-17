using Verse;

namespace Multiplayer.Client.Persistent;

public sealed class GravshipMapDecision : IExposable
{
	public int mapId;

	public int parentId;

	public int engineId;

	public int generation;

	public int owner = -1;

	public int factionId;

	public bool allowKeep;

	public bool allowSettle;

	public bool allowAbandon;

	public bool resolved;

	public void ExposeData()
	{
		Scribe_Values.Look<int>(ref mapId, "mapId", 0, false);
		Scribe_Values.Look<int>(ref parentId, "parentId", 0, false);
		Scribe_Values.Look<int>(ref engineId, "engineId", 0, false);
		Scribe_Values.Look<int>(ref generation, "generation", 0, false);
		Scribe_Values.Look<int>(ref owner, "owner", -1, false);
		Scribe_Values.Look<int>(ref factionId, "factionId", 0, false);
		Scribe_Values.Look<bool>(ref allowKeep, "allowKeep", false, false);
		Scribe_Values.Look<bool>(ref allowSettle, "allowSettle", false, false);
		Scribe_Values.Look<bool>(ref allowAbandon, "allowAbandon", false, false);
		Scribe_Values.Look<bool>(ref resolved, "resolved", false, false);
	}
}
