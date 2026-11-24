using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Multiplayer.Client.Desyncs;

// One row per applied patch. Plain strings so this type, and the formatter below, carry no Harmony or
// Verse dependency and can be unit-tested without a game.
public readonly record struct PatchRow(
    string Target,
    string TargetAssembly,
    int TargetToken,
    string Kind,
    string Owner,
    int Priority,
    string PatchMethod,
    string PatchAssembly,
    int PatchToken,
    string Package);

/// <summary>
/// Bounded JSON for the patch inventory. A desync report says a divergence happened; this says which
/// patches each peer had applied and who supplied them, which is usually the missing piece.
/// </summary>
public static class PatchInventoryFormat
{
    public const int MaxRows = 4000;
    public const int MaxBytes = 2 * 1024 * 1024;

    public static string Json(
        IReadOnlyList<PatchRow> rows,
        int omittedBefore,
        string harmonyVersion,
        int patchedMethodCount,
        int patchCount,
        IEnumerable<string> owners)
    {
        var kept = new List<string>(Math.Min(rows.Count, MaxRows));
        var omitted = omittedBefore;
        var size = 0;

        foreach (var row in rows)
        {
            var json = RowJson(row);
            if (kept.Count >= MaxRows || size + json.Length > MaxBytes)
            {
                // Everything from here on is dropped; say how much rather than truncating silently.
                omitted += rows.Count - kept.Count;
                break;
            }

            kept.Add(json);
            size += json.Length;
        }

        var sb = new StringBuilder(size + 512);
        sb.Append('{');
        Field(sb, "harmonyVersion", harmonyVersion);
        sb.Append(',');
        sb.Append("\"patchedMethods\":").Append(patchedMethodCount);
        sb.Append(',');
        sb.Append("\"patches\":").Append(patchCount);
        sb.Append(',');
        sb.Append("\"written\":").Append(kept.Count);
        sb.Append(',');
        sb.Append("\"omitted\":").Append(omitted);
        sb.Append(',');
        sb.Append("\"owners\":[");
        sb.Append(string.Join(",", owners.Select(Quote)));
        sb.Append("],\"rows\":[");
        sb.Append(string.Join(",", kept));
        sb.Append("]}");
        return sb.ToString();
    }

    private static string RowJson(PatchRow row)
    {
        var sb = new StringBuilder(256);
        sb.Append('{');
        Field(sb, "target", row.Target);
        sb.Append(',');
        Field(sb, "assembly", row.TargetAssembly);
        sb.Append(',');
        sb.Append("\"token\":").Append(row.TargetToken);
        sb.Append(',');
        Field(sb, "kind", row.Kind);
        sb.Append(',');
        Field(sb, "owner", row.Owner);
        sb.Append(',');
        sb.Append("\"priority\":").Append(row.Priority);
        sb.Append(',');
        Field(sb, "patch", row.PatchMethod);
        sb.Append(',');
        Field(sb, "patchAssembly", row.PatchAssembly);
        sb.Append(',');
        sb.Append("\"patchToken\":").Append(row.PatchToken);
        sb.Append(',');
        Field(sb, "package", row.Package);
        sb.Append('}');
        return sb.ToString();
    }

    private static void Field(StringBuilder sb, string name, string value)
    {
        sb.Append('"').Append(name).Append("\":").Append(Quote(value ?? ""));
    }

    internal static string Quote(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');

        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ')
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else
                        sb.Append(c);
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }
}
