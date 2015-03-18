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
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace NetSync
{

    public class Target
    {
        public UInt32 T;
        public int I;
    }

    public class TargetComparer : IComparer<Target>, IComparer
    {
        int IComparer.Compare(Object x, Object y)
        {
            return Match.CompareTargets((Target)x, (Target)y);
        }

        public int Compare(Target x, Target y)
        {
            return Match.CompareTargets(x, y);
        }
    }

    public class Match
    {
        /// <summary>
        /// 32 * 1024
        /// </summary>
        public const int ChunkSize = (32 * 1024);
        /// <summary>
        /// 1 &lt;&lt; 16
        /// </summary>
        public const int Tablesize = (1 << 16);
        /// <summary>
        /// -1
        /// </summary>
        public const int NullTag = -1;
        /// <summary>
        /// 1 &lt;&lt; 0
        /// </summary>
        public const byte SumflgSameOffset = (1 << 0);
        private int _falseAlarms;
        private int _tagHits;
        private int _matches;
        private Int64 _dataTransfer;

        private int _totalFalseAlarms;
        private int _totalTagHits;
        private int _totalMatches;
        private int _lastMatch;
        private List<Target> _targets = new List<Target>();
        private int[] _tagTable = new int[Tablesize];

        private Options _options;

        public Match(Options opt)
        {
            _options = opt;
        }

        public static int CompareTargets(Target t1, Target t2)
        {
            return (int)t1.T - (int)t2.T;
        }

        public static UInt32 GetTag2(UInt32 s1, UInt32 s2)
        {
            return (((s1) + (s2)) & 0xFFFF);
        }

        public static UInt32 GetTag(UInt32 sum)
        {
            return GetTag2(sum & 0xFFFF, sum >> 16);
        }

        public void BuildHashTable(SumStruct s)
        {
            for (int i = 0; i < s.Count; i++)
            {
                _targets.Add(new Target());
            }

            for (int i = 0; i < s.Count; i++)
            {
                _targets[i].I = i;
                _targets[i].T = GetTag(s.Sums[i].Sum1);
            }

            _targets.Sort(0, s.Count, new TargetComparer());

            for (int i = 0; i < Tablesize; i++)
            {
                _tagTable[i] = NullTag;
            }


            for (int i = s.Count; i-- > 0; )
            {
                _tagTable[((Target)_targets[i]).T] = i;
            }
        }

        public void MatchSums(IoStream f, SumStruct s, MapFile buf, int len)
        {
            byte[] fileSum = new byte[CheckSum.Md4SumLength];

            _lastMatch = 0;
            _falseAlarms = 0;
            _tagHits = 0;
            _matches = 0;
            _dataTransfer = 0;

            Sum sum = new Sum(_options);
            sum.Init(_options.ChecksumSeed);

            if (len > 0 && s.Count > 0)
            {
                BuildHashTable(s);

                if (_options.Verbose > 2)
                {
                    Log.WriteLine("built hash table");
                }

                HashSearch(f, s, buf, len, sum);

                if (_options.Verbose > 2)
                {
                    Log.WriteLine("done hash search");
                }
            }
            else
            {
                for (int j = 0; j < len - ChunkSize; j += ChunkSize)
                {
                    int n1 = Math.Min(ChunkSize, (len - ChunkSize) - j);
                    Matched(f, s, buf, j + n1, -2, sum);
                }
                Matched(f, s, buf, len, -1, sum);
            }

            fileSum = sum.End();
            if (buf != null && buf.Status)
            {
                fileSum[0]++;
            }

            if (_options.Verbose > 2)
            {
                Log.WriteLine("sending fileSum");
            }
            f.Write(fileSum, 0, CheckSum.Md4SumLength);

            _targets.Clear();

            if (_options.Verbose > 2)
            {
                Log.WriteLine("falseAlarms=" + _falseAlarms + " tagHits=" + _tagHits + " matches=" + _matches);
            }

            _totalTagHits += _tagHits;
            _totalFalseAlarms += _falseAlarms;
            _totalMatches += _matches;
            Options.Stats.LiteralData += _dataTransfer;
        }

        public void Matched(IoStream f, SumStruct s, MapFile buf, int offset, int i, Sum sum)
        {
            int n = offset - _lastMatch;
            int j;

            if (_options.Verbose > 2 && i >= 0)
            {
                Log.WriteLine("match at " + offset + " last_match=" + _lastMatch + " j=" + i + " len=" + s.Sums[i].Len + " n=" + n);
            }

            Token token = new Token(_options);
            token.SendToken(f, i, buf, _lastMatch, n, (int)(i < 0 ? 0 : s.Sums[i].Len));
            _dataTransfer += n;

            if (i >= 0)
            {
                Options.Stats.MatchedData += s.Sums[i].Len;
                n += (int)s.Sums[i].Len;
            }

            for (j = 0; j < n; j += ChunkSize)
            {
                int n1 = Math.Min(ChunkSize, n - j);
                int off = buf.MapPtr(_lastMatch + j, n1);
                sum.Update(buf.P, off, n1);
            }

            if (i >= 0)
            {
                _lastMatch = (int)(offset + s.Sums[i].Len);
            }
            else
            {
                _lastMatch = offset;
            }

            if (buf != null && _options.DoProgress)
            {
                Progress.ShowProgress(_lastMatch, buf.FileSize);
                if (i == -1)
                {
                    Progress.EndProgress(buf.FileSize);
                }
            }
        }

        public void HashSearch(IoStream f, SumStruct s, MapFile buf, int len, Sum _sum)
        {
            int offset, end, backup;
            UInt32 k;
            int wantI;
            byte[] sum2 = new byte[CheckSum.SumLength];
            UInt32 s1, s2, sum;
            int more;
            byte[] map;

            wantI = 0;
            if (_options.Verbose > 2)
            {
                Log.WriteLine("hash search ob=" + s.BLength + " len=" + len);
            }

            k = (UInt32)Math.Min(len, s.BLength);
            int off = buf.MapPtr(0, (int)k);
            map = buf.P;

            UInt32 g = s.Sums[0].Sum1;
            sum = CheckSum.GetChecksum1(map, off, (int)k);
            s1 = sum & 0xFFFF;
            s2 = sum >> 16;
            if (_options.Verbose > 3)
            {
                Log.WriteLine("sum=" + sum + " k=" + k);
            }

            offset = 0;
            end = (int)(len + 1 - s.Sums[s.Count - 1].Len);
            if (_options.Verbose > 3)
            {
                Log.WriteLine("hash search s.bLength=" + s.BLength + " len=" + len + " count=" + s.Count);
            }

            do
            {
                UInt32 t = GetTag2(s1, s2);
                bool doneCsum2 = false;
                int j = _tagTable[t];

                if (_options.Verbose > 4)
                {
                    Log.WriteLine("offset=" + offset + " sum=" + sum);
                }

                if (j == NullTag)
                {
                    goto null_tag;
                }

                sum = (s1 & 0xffff) | (s2 << 16);
                _tagHits++;
                do
                {
                    UInt32 l;
                    int i = ((Target)_targets[j]).I;

                    if (sum != s.Sums[i].Sum1)
                    {
                        continue;
                    }

                    l = (UInt32)Math.Min(s.BLength, len - offset);
                    if (l != s.Sums[i].Len)
                    {
                        continue;
                    }

                    if (_options.Verbose > 3)
                    {
                        Log.WriteLine("potential match at " + offset + " target=" + j + " " + i + " sum=" + sum);
                    }

                    if (!doneCsum2)
                    {
                        off = buf.MapPtr(offset, (int)l);
                        map = buf.P;
                        CheckSum cs = new CheckSum(_options);
                        sum2 = cs.GetChecksum2(map, off, (int)l);
                        doneCsum2 = true;
                    }

                    if (Util.MemoryCompare(sum2, 0, s.Sums[i].Sum2, 0, s.S2Length) != 0)
                    {
                        _falseAlarms++;
                        continue;
                    }

                    if (i != wantI && wantI < s.Count
                        && (!_options.Inplace || _options.MakeBackups || s.Sums[wantI].Offset >= offset
                        || (s.Sums[wantI].Flags & SumflgSameOffset) != 0)
                        && sum == s.Sums[wantI].Sum1
                        && Util.MemoryCompare(sum2, 0, s.Sums[wantI].Sum2, 0, s.S2Length) == 0)
                    {
                        i = wantI;
                    }
                //set_want_i:
                    wantI = i + 1;

                    Matched(f, s, buf, offset, i, _sum);
                    offset += (int)(s.Sums[i].Len - 1);
                    k = (UInt32)Math.Min(s.BLength, len - offset);
                    off = buf.MapPtr(offset, (int)k);
                    sum = CheckSum.GetChecksum1(map, off, (int)k);
                    s1 = sum & 0xFFFF;
                    s2 = sum >> 16;
                    _matches++;
                    break;
                } while (++j < s.Count && ((Target)_targets[j]).T == t);
            null_tag:
                backup = offset - _lastMatch;
                if (backup < 0)
                {
                    backup = 0;
                }

                more = (offset + k) < len ? 1 : 0;
                off = buf.MapPtr(offset - backup, (int)(k + more + backup)) + backup;
                s1 -= (UInt32)(CheckSum.ToInt(map[off]) + CheckSum.CharOffset);
                s2 -= (UInt32)(k * CheckSum.ToInt(map[off]) + CheckSum.CharOffset);
                off = (k + off >= map.Length) ? (int)(map.Length - k - 1) : off;
                if (more != 0)
                {
                    s1 += (UInt32)(CheckSum.ToInt(map[k + off]) + CheckSum.CharOffset);
                    s2 += s1;
                }
                else
                {
                    --k;
                }

                if (backup >= ChunkSize + s.BLength && end - offset > ChunkSize)
                {
                    Matched(f, s, buf, (int)(offset - s.BLength), -2, _sum);
                }
            } while (++offset < end);

            Matched(f, s, buf, len, -1, _sum);
            buf.MapPtr(len - 1, 1);
        }

        public void MatchReport(IoStream f)
        {
            if (_options.Verbose <= 1)
            {
                return;
            }

            string report = "total: matches=" + _totalMatches + "  tagHits=" + _totalTagHits + "  falseAlarms=" +
                _totalFalseAlarms + " data=" + Options.Stats.LiteralData;

            Log.WriteLine(report);
            if (_options.AmServer)
            {
                f.MultiplexWrite(MsgCode.MsgInfo, Encoding.ASCII.GetBytes(report), report.Length);
            }
        }
    }
}
