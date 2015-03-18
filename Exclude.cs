/**
 *  Copyright (C) 2006 Alex Pedenko
 * 
 *  This program is free software; you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation; either version 2 of the License, or
 *  (at your option) any later version.
 *
 *  This program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with this program; if not, write to the Free Software
 *  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
 */
using System;
using System.Collections.Generic;
using System.IO;

namespace NetSync
{
    public class ExcludeStruct
    {
        public string Pattern;
        public UInt32 MatchFlags;
        public int SlashCnt;

        public ExcludeStruct(string pattern, UInt32 matchFlags, int slashCnt)
        {
            Pattern = pattern;
            MatchFlags = matchFlags;
            SlashCnt = slashCnt;
        }

        public ExcludeStruct() { }
    }

    public class Exclude
    {
        private Options _options;

        public Exclude(Options opt)
        {
            _options = opt;
        }

        public static void AddCvsExcludes() { }
        public void AddExcludeFile(ref List<ExcludeStruct> exclList, string fileName, int xFlags)
        {
            var wordSplit = (xFlags & Options.XflgWordSplit) != 0;
            TextReader f;
            // TODO: path length
            if (fileName == null || fileName.CompareTo(String.Empty) == 0 || !File.Exists(fileName))
            {
                return;
            }
            if (fileName.CompareTo("-") == 0)
            {
                f = Console.In;
            }
            else
            {
                try
                {
                    f = new StreamReader(fileName);
                }
                catch
                {
                    if ((xFlags & Options.XflgFatalErrors) != 0)
                    {
                        Log.Write("failed to open " + (((xFlags & Options.XflgDefInclude) != 0) ? "include" : "exclude") + " file " + fileName);
                    }
                    return;
                }
            }
            while (true)
            {
                var line = f.ReadLine();
                if (line == null)
                {
                    break;
                }
                if (line.CompareTo(String.Empty) != 0 && (wordSplit || (line[0] != ';' && line[0] != '#')))
                {
                    AddExclude(ref exclList, line, xFlags);
                }
            }
            f.Close();

        }
        public void AddExclude(ref List<ExcludeStruct> exclList, string pattern, int xFlags)
        {
            UInt32 mFlags;
            if (pattern == null)
            {
                return;
            }
            var cp = pattern;
            var len = 0;
            while (true)
            {
                if (len >= cp.Length)
                {
                    break;
                }
                cp = GetExcludeToken(cp.Substring(len), out len, out mFlags, xFlags);
                if (len == 0)
                {
                    break;
                }
                if ((mFlags & Options.MatchflgClearList) != 0)
                {
                    if (_options.Verbose > 2)
                    {
                        Log.WriteLine("[" + _options.WhoAmI() + "] clearing exclude list");
                    }
                    exclList.Clear();
                    continue;
                }

                MakeExlude(ref exclList, cp, mFlags);
                if (_options.Verbose > 2)
                {
                    Log.WriteLine("[" + _options.WhoAmI() + "] AddExclude(" + cp + ")");
                }
            }
        }

        public void MakeExlude(ref List<ExcludeStruct> exclList, string pat, UInt32 mFlags)
        {
            var exLen = 0;
            var patLen = pat.Length;
            var ret = new ExcludeStruct();
            if (_options.ExcludePathPrefix != null)
            {
                mFlags |= Options.MatchflgAbsPath;
            }
            if (_options.ExcludePathPrefix != null && pat[0] == '/')
            {
                exLen = _options.ExcludePathPrefix.Length;
            }
            else
            {
                exLen = 0;
            }
            ret.Pattern = String.Empty;
            if (exLen != 0)
            {
                ret.Pattern += _options.ExcludePathPrefix;
            }
            ret.Pattern += pat.Replace('\\', '/');
            patLen += exLen;

            if (ret.Pattern.IndexOfAny(new char[] { '*', '[', '?' }) != -1)
            {
                mFlags |= Options.MatchflgWild;
                if (ret.Pattern.IndexOf("**") != -1)
                {
                    mFlags |= Options.MatchflgWild2;
                    if (ret.Pattern.IndexOf("**") == 0)
                    {
                        mFlags |= Options.MatchflgWild2Prefix;
                    }
                }
            }

            if (patLen > 1 && ret.Pattern[ret.Pattern.Length - 1] == '/')
            {
                ret.Pattern = ret.Pattern.Remove(ret.Pattern.Length - 1, 1);
                mFlags |= Options.MatchflgDirectory;
            }

            for (var i = 0; i < ret.Pattern.Length; i++)
            {
                if (ret.Pattern[i] == '/')
                {
                    ret.SlashCnt++;
                }
            }
            ret.MatchFlags = mFlags;
            exclList.Add(ret);
        }

        static string GetExcludeToken(string p, out int len, out uint mFlags, int xFlags)
        {
            len = 0;
            var s = p;
            mFlags = 0;
            if (p.CompareTo(String.Empty) == 0)
            {
                return String.Empty;
            }

            if ((xFlags & Options.XflgWordSplit) != 0)
            {
                p = s = p.Trim(' ');
            }
            if ((xFlags & Options.XflgWordsOnly) == 0 && (s[0] == '-' || s[0] == '+') && s[1] == ' ')
            {
                if (s[0] == '+')
                {
                    mFlags |= Options.MatchflgInclude;
                }
                s = s.Substring(2);
            }
            else if ((xFlags & Options.XflgDefInclude) != 0)
            {
                mFlags |= Options.MatchflgInclude;
            }
            if ((xFlags & Options.XflgDirectory) != 0)
            {
                mFlags |= Options.MatchflgDirectory;
            }
            if ((xFlags & Options.XflgWordSplit) != 0)
            {
                var i = 0;
                while (i < s.Length && s[i] == ' ')
                {
                    i++;
                }
                len = s.Length - i;
            }
            else
            {
                len = s.Length;
            }
            if (p[0] == '!' && len == 1)
            {
                mFlags |= Options.MatchflgClearList;
            }
            return s;
        }

        /*
        * Return -1 if file "name" is defined to be excluded by the specified
        * exclude list, 1 if it is included, and 0 if it was not matched.
        */
        public int CheckExclude(List<ExcludeStruct> listp, string name, int nameIsDir)
        {
            foreach (var ex in listp)
            {
                if (CheckOneExclude(name, ex, nameIsDir))
                {
                    ReportExcludeResult(name, ex, nameIsDir);
                    return (ex.MatchFlags & Options.MatchflgInclude) != 0 ? 1 : -1;
                }
            }
            return 0;
        }

        static bool CheckOneExclude(string name, ExcludeStruct ex, int nameIsDir)
        {
            var matchStart = 0;
            var pattern = ex.Pattern;

            if (name.CompareTo(String.Empty) == 0)
            {
                return false;
            }
            if (pattern.CompareTo(String.Empty) == 0)
            {
                return false;
            }

            if (0 != (ex.MatchFlags & Options.MatchflgDirectory) && nameIsDir == 0)
            {
                return false;
            }

            if (pattern[0] == '/')
            {
                matchStart = 1;
                pattern = pattern.TrimStart('/');
                if (name[0] == '/')
                {
                    name = name.TrimStart('/');
                }
            }

            if ((ex.MatchFlags & Options.MatchflgWild) != 0)
            {
                /* A non-anchored match with an infix slash and no "**"
                 * needs to match the last slash_cnt+1 name elements. */
                if (matchStart != 0 && ex.SlashCnt != 0 && 0 == (ex.MatchFlags & Options.MatchflgWild2))
                {
                    name = name.Substring(name.IndexOf('/') + 1);
                }
                if (WildMatch.CheckWildMatch(pattern, name))
                {
                    return true;
                }
                if ((ex.MatchFlags & Options.MatchflgWild2Prefix) != 0)
                {
                    /* If the **-prefixed pattern has a '/' as the next
                    * character, then try to match the rest of the
                    * pattern at the root. */
                    if (pattern[2] == '/' && WildMatch.CheckWildMatch(pattern.Substring(3), name))
                    {
                        return true;
                    }
                }
                else if (0 == matchStart && (ex.MatchFlags & Options.MatchflgWild2) != 0)
                {
                    /* A non-anchored match with an infix or trailing "**"
                    * (but not a prefixed "**") needs to try matching
                    * after every slash. */
                    int posSlash;
                    while ((posSlash = name.IndexOf('/')) != -1)
                    {
                        name = name.Substring(posSlash + 1);
                        if (WildMatch.CheckWildMatch(pattern, name))
                        {
                            return true;
                        }
                    }
                }
            }
            else if (matchStart != 0)
            {
                if (name.CompareTo(pattern) == 0)
                {
                    return true;
                }
            }
            else
            {
                var l1 = name.Length;
                var l2 = pattern.Length;
                if (l2 <= l1 &&
                    name.Substring(l1 - l2).CompareTo(pattern) == 0 &&
                    (l1 == l2 || name[l1 - (l2 + 1)] == '/'))
                {
                    return true;
                }
            }

            return false;
        }

        public void ReportExcludeResult(string name, ExcludeStruct ent, int nameIsDir)
        {
            /* If a trailing slash is present to match only directories,
            * then it is stripped out by make_exclude.  So as a special
            * case we add it back in here. */

            if (_options.Verbose >= 2)
            {
                Log.Write(_options.WhoAmI() + " " + ((ent.MatchFlags & Options.MatchflgInclude) != 0 ? "in" : "ex") +
                    "cluding " + (nameIsDir != 0 ? "directory" : "file") + " " +
                    name + " because of " + ent.Pattern + " pattern " +
                    ((ent.MatchFlags & Options.MatchflgDirectory) != 0 ? "/" : String.Empty) + "\n");
            }
        }

        public void SendExcludeList(IoStream f)
        {
            if (_options.ListOnly && !_options.Recurse)
            {
                AddExclude(ref _options.ExcludeList, "/*/*", 0);
            }

            foreach (var ent in _options.ExcludeList)
            {
                int l;
                string p;

                if (ent.Pattern.Length == 0 || ent.Pattern.Length > Options.Maxpathlen)
                {
                    continue;
                }
                l = ent.Pattern.Length;
                p = ent.Pattern;
                if ((ent.MatchFlags & Options.MatchflgDirectory) != 0)
                {
                    p += "/\0";
                }

                if ((ent.MatchFlags & Options.MatchflgInclude) != 0)
                {
                    f.WriteInt(l + 2);
                    f.IoPrintf("+ ");
                }
                else if ((p[0] == '-' || p[0] == '+') && p[1] == ' ')
                {
                    f.WriteInt(l + 2);
                    f.IoPrintf("- ");
                }
                else
                {
                    f.WriteInt(l);
                }
                f.IoPrintf(p);

            }
            f.WriteInt(0);
        }

        /// <summary>
        /// Receives exclude list from stream
        /// </summary>
        /// <param name="ioStream"></param>
        public void ReceiveExcludeList(IoStream ioStream)
        {
            var line = String.Empty;
            int length;
            while ((length = ioStream.ReadInt()) != 0)
            {
                if (length >= Options.Maxpathlen + 3)
                {
                    Log.Write("Overflow: recv_exclude_list");
                    continue;
                }

                line = ioStream.ReadStringFromBuffer(length);
                AddExclude(ref _options.ExcludeList, line, 0);
            }
        }
    }
}
