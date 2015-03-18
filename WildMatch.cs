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

namespace NetSync
{
    public class WildMatch
    {

        private const int AbortAll = -1; //@fixed to const
        private const int AbortToStarstar = -2; //@fixed to const
        private const Char NegateClass = '!'; //@fixed to const?

        /// <summary>
        /// Find the pattern (pattern) in the text string (text).
        /// </summary>
        /// <param name="pattern"></param>
        /// <param name="text"></param>
        /// <returns></returns>
        public static bool CheckWildMatch(string pattern, string text)
        {
            return DoMatch(pattern, text) == 1;
        }

        //public static bool CC_EQ(string cclass, string litmatch) //@fixed remove it and use Equals
        //{
        //    return cclass.CompareTo(litmatch) == 0;
        //}

        /// <summary>
        /// Perfoms match of text and pattern
        /// </summary>
        /// <param name="pattern"></param>
        /// <param name="text"></param>
        /// <returns></returns>
        private static int DoMatch(string pattern, string text)
        {
            int matched, special; // They have both to be int just because DoMatch can return -1 and -2
            Char ch, prev;
            for (var k = 0; k < pattern.Length; k++)
            {
                ch = pattern[k];
                if (k > 0)
                {
                    if (text.Length > 1)
                    {
                        text = text.Substring(1);
                    }
                    else
                    {
                        return 0;
                    }
                }
                switch (ch)
                {
                    case '\\':
                        /* Literal match with following character.  Note that the test
                        * in "default" handles the p[1] == '\0' failure case. */
                        ch = pattern[++k];
                        /* FALLTHROUGH */
                        goto default;
                    default:
                        if (text[0] != ch)
                        {
                            return 0;
                        }
                        continue;
                    case '?':
                        /* Match anything but '/'. */
                        if (text[0] == '/')
                        {
                            return 0;
                        }
                        continue;
                    case '*':
                        if (k + 1 < pattern.Length)
                        {
                            if (pattern[++k] == '*')
                            {
                                while (pattern[++k] == '*')
                                {
                                }
                                special = 1;
                            }
                            else
                            {
                                special = 0;
                            }
                        }
                        else
                        {
                            special = 0;
                        }
                        if (pattern[k] == '\0')
                        {
                            return (special == 1) ? 1 : (text.IndexOf('/') == -1 ? 0 : 1);
                        }
                        if (pattern.Equals("*"))
                        {
                            text = text.Substring(1);
                        }
                        var r = pattern.Substring(k);
                        for (var t = 0; t < text.Length; )
                        {
                            if ((matched = DoMatch(r, text)) != 0)
                            {
                                if (special == 0 || matched != AbortToStarstar)
                                {
                                    return 1;
                                }
                            }
                            else if (special == 0 && text[0] == '/')
                            {
                                return AbortToStarstar;
                            }
                            text = text.Substring(1);
                        }
                        return AbortAll;
                    case '[':
                        k++;
                        /* Assign literal true/false because of "matched" comparison. */
                        special = ch == NegateClass ? 1 : 0;
                        if (special == 1)
                        {
                            /* Inverted character class. */
                            ch = pattern[++k];
                        }
                        prev = Char.MinValue;
                        matched = 0;
                        do
                        {
                            if (k >= pattern.Length)
                            {
                                return AbortAll;
                            }
                            if (ch == '\\')
                            {
                                ch = pattern[++k];
                                if (k > pattern.Length)
                                {
                                    return AbortAll;
                                }
                                if (text[0] == ch)
                                {
                                    matched = 1;
                                }
                            }
                            else if (ch == '-' && prev != Char.MinValue && pattern.Length - k > 1 && pattern[1] != ']')
                            {
                                ch = pattern[++k];
                                if (ch == '\\')
                                {
                                    ch = pattern[++k];
                                    if (k >= pattern.Length)
                                    {
                                        return AbortAll;
                                    }
                                }
                                if (text[0].CompareTo(ch) <= 0 && text[0].CompareTo(prev) >= 0)
                                {
                                    matched = 1;
                                }
                                ch = Char.MinValue; /* This makes "prev" get set to 0. */
                            }
                            else if (ch == '[' && pattern[k] == ':')
                            {
                                var j = 0;
                                ch = pattern[k + j + 1];
                                while (pattern.Length > k + 1 + j && ch != ']')
                                {
                                    j++;
                                    ch = pattern[k + 1 + j];
                                }
                                if (k + 1 >= pattern.Length)
                                {
                                    return AbortAll;
                                }
                                if (j == 0 || pattern[k + j] != ':')
                                {
                                    /* Didn't find ":]", so treat like a normal set. */
                                    ch = '[';
                                    if (text[0] == ch)
                                    {
                                        matched = 1;
                                    }
                                    continue;
                                }
                                k += j;
                                var s = pattern.Substring(k - j + 1, j - 1);
                                if (s.Equals("alnum"))
                                {
                                    if (Char.IsLetterOrDigit(text[0]))
                                    {
                                        matched = 1;
                                    }
                                }
                                else if (s.Equals("alpha"))
                                {
                                    if (Char.IsLetter(text[0]))
                                    {
                                        matched = 1;
                                    }
                                }
                                else if (s.Equals("blank"))
                                {
                                    if (Char.IsWhiteSpace(text[0]))
                                    {
                                        matched = 1;
                                    }
                                }
                                else if (s.Equals("digit"))
                                {
                                    if (Char.IsDigit(text[0]))
                                    {
                                        matched = 1;
                                    }
                                }
                                else if (s.Equals("lower"))
                                {
                                    if (Char.IsLower(text[0]))
                                    {
                                        matched = 1;
                                    }
                                }
                                else if (s.Equals("punct"))
                                {
                                    if (Char.IsPunctuation(text[0]))
                                    {
                                        matched = 1;
                                    }
                                }
                                else if (s.Equals("space"))
                                {
                                    if (Char.IsWhiteSpace(text[0]))
                                    {
                                        matched = 1;
                                    }
                                }
                                else if (s.Equals("upper"))
                                {
                                    if (Char.IsUpper(text[0]))
                                    {
                                        matched = 1;
                                    }
                                }
                                else if (s.Equals("xdigit"))
                                {
                                    if (Char.IsSurrogate(text[0]))
                                    {
                                        matched = 1;
                                    }
                                }
                                else
                                {
                                    return AbortAll;
                                }
                                ch = Char.MinValue;
                            }
                            else if (text[0] == ch)
                            {
                                matched = 1;
                            }
                            prev = ch;
                        } while ((ch = pattern[++k]) != ']');
                        if (matched == special || text[0] == '/')
                        {
                            return 0;
                        }
                        continue;
                }
            }
            text = text.Substring(1);
            return Convert.ToInt32(!text.IsBlank());
        }
    }
}
