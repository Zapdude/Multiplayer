using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("Multiplayer")]
[assembly: AssemblyProduct("Multiplayer")]
[assembly: ComVisible(false)]
[assembly: Guid("562cc73b-d52d-4615-b8aa-bce875838d24")]

// Version.cs carries the mod version, folded from version.json by
// build/apply-version.sh; the file version tracks the MultiplayerAPI package the
// client links against.
[assembly: AssemblyVersion(Multiplayer.Common.MpVersion.AssemblyVersion)]
[assembly: AssemblyFileVersion(Multiplayer.API.MP.API)]
