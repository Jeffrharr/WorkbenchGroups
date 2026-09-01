using System;
using System.Collections.Generic;

namespace WorkbenchGroups.Core
{
    /// <summary>
    /// Parses and matches the player's list of bench classes to leave alone.
    ///
    /// The escape hatch exists because the recipe-shaped gate in <see cref="RecipeGate"/> cannot
    /// see everything that matters. A modded bench class may hard-cast
    /// <c>bill.billStack.billGiver</c> to its own type inside its own code, exactly the way
    /// <c>Building_WorkTableAutonomous</c> does; no rule over defs can detect that, and the
    /// symptom is an exception every frame. Rather than make the player wait for a patch, they
    /// can name the class and be rid of it immediately.
    ///
    /// Kept as free text rather than a list widget because that is what a player can act on from
    /// a bug report: the class name is right there in the stack trace, and pasting it into a box
    /// is a two-second fix. A picker would have to enumerate every loaded bench class to offer the
    /// one entry they need.
    /// </summary>
    public static class ClassExclusionList
    {
        private static readonly string[] Empty = new string[0];

        // Commas are what people write, newlines are what they get when they paste a list, and
        // semicolons show up because some mod lists use them. All three cost nothing to accept.
        private static readonly char[] Separators = { ',', '\n', '\r', ';' };

        /// <summary>Splits the raw setting into trimmed, non-empty entries.</summary>
        public static string[] Parse(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return Empty;
            }

            string[] pieces = raw.Split(Separators);
            List<string> entries = new List<string>(pieces.Length);

            foreach (string piece in pieces)
            {
                string trimmed = piece.Trim();
                if (trimmed.Length > 0)
                {
                    entries.Add(trimmed);
                }
            }

            return entries.ToArray();
        }

        /// <summary>
        /// Whether a class is excluded, matching either its namespace-qualified name or its bare
        /// name, case-insensitively.
        ///
        /// Both forms are accepted because both are what people have in front of them: a stack
        /// trace prints <c>RimWorld.Building_WorkTable_HeatPush</c>, a mod's XML prints
        /// <c>Building_WorkTable_HeatPush</c>, and demanding the other one is a silent no-op that
        /// reads as the setting being broken.
        /// </summary>
        public static bool Excludes(string[] entries, string fullName, string shortName)
        {
            if (entries == null || entries.Length == 0)
            {
                return false;
            }

            foreach (string entry in entries)
            {
                if (Matches(entry, fullName) || Matches(entry, shortName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Matches(string entry, string name)
        {
            return name != null && string.Equals(entry, name, StringComparison.OrdinalIgnoreCase);
        }
    }
}
