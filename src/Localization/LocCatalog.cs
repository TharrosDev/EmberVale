using System.Collections.Generic;
using System.Text;

namespace Embervale.Localization;

/// <summary>
/// Pure parser for the localization CSV (Phase 24G). Kept Godot-free so the catalogue format —
/// quoted fields, embedded commas/quotes, comment + blank lines, multiple locale columns — is
/// unit-testable without an engine. <see cref="Loc"/> feeds the result into Godot's
/// <c>TranslationServer</c>.
///
/// Format: the first row is a header whose first column is ignored (the key column) and whose
/// remaining columns name locales (<c>keys,en,fr,…</c>). Each later row is <c>key,value[,value…]</c>.
/// Lines that are blank or start with <c>#</c> are skipped.
/// </summary>
public static class LocCatalog
{
    /// <summary>Parses the CSV into <c>locale → (key → text)</c>. A locale column with an empty cell
    /// for a row contributes no entry for that key (so it falls back to the key at runtime).</summary>
    public static Dictionary<string, Dictionary<string, string>> Parse(string csv)
    {
        var byLocale = new Dictionary<string, Dictionary<string, string>>();
        if (string.IsNullOrEmpty(csv))
        {
            return byLocale;
        }

        string[]? locales = null;
        foreach (List<string> fields in ReadRows(csv))
        {
            if (locales == null)
            {
                // Header: columns after the key column are locale names.
                locales = new string[fields.Count - 1];
                for (int i = 1; i < fields.Count; i++)
                {
                    string locale = fields[i].Trim();
                    locales[i - 1] = locale;
                    byLocale[locale] = new Dictionary<string, string>();
                }

                continue;
            }

            string key = fields[0].Trim();
            if (key.Length == 0)
            {
                continue;
            }

            for (int i = 1; i < fields.Count && i - 1 < locales.Length; i++)
            {
                string value = fields[i];
                if (value.Length == 0)
                {
                    continue;
                }

                byLocale[locales[i - 1]][key] = value;
            }
        }

        return byLocale;
    }

    /// <summary>Splits the CSV into rows of fields, honouring RFC-4180 quoting (a quoted field may
    /// contain commas, newlines, and <c>""</c>-escaped quotes). Blank and <c>#</c>-comment rows are
    /// dropped (a comment is detected only on the unquoted first field).</summary>
    private static IEnumerable<List<string>> ReadRows(string csv)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;
        bool rowHasContent = false;

        for (int i = 0; i < csv.Length; i++)
        {
            char c = csv[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    rowHasContent = true;
                    break;
                case '\r':
                    break; // swallow; the \n ends the row
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    if (EmitRow(fields, rowHasContent) is { } row)
                    {
                        yield return row;
                    }

                    fields = new List<string>();
                    rowHasContent = false;
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        // Trailing row with no final newline.
        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            if (EmitRow(fields, rowHasContent || field.Length > 0) is { } row)
            {
                yield return row;
            }
        }
    }

    /// <summary>Returns the row unless it is blank or a <c>#</c> comment.</summary>
    private static List<string>? EmitRow(List<string> fields, bool hadContent)
    {
        if (fields.Count == 0)
        {
            return null;
        }

        string first = fields[0];
        bool blank = !hadContent && first.Trim().Length == 0;
        if (blank || first.TrimStart().StartsWith('#'))
        {
            return null;
        }

        return fields;
    }
}
