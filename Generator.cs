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
    public class Generator
    {
        private const int BlocksumBias = 10;
        private Options _options;
        private CheckSum _checkSum;

        public Generator(Options opt)
        {
            _options = opt;
            _checkSum = new CheckSum(_options);
        }

        public void WriteSumHead(IoStream f, SumStruct sum)
        {
            if (sum == null)
            {
                sum = new SumStruct();
            }
            f.WriteInt(sum.Count);
            f.WriteInt((int)sum.BLength);
            if (_options.ProtocolVersion >= 27)
            {
                f.WriteInt(sum.S2Length);
            }
            f.WriteInt((int)sum.Remainder);
        }

        public void GenerateFiles(IoStream f, List<FileStruct> fileList, string localName)
        {
            int i;
            int phase = 0;


            if (_options.Verbose > 2)
            {
                Log.WriteLine("generator starting count=" + fileList.Count);
            }

            for (i = 0; i < fileList.Count; i++)
            {
                FileStruct file = (fileList[i]);
                if (file.BaseName == null)
                {
                    continue;
                }
                if (Util.S_ISDIR(file.Mode))
                {
                    continue;
                }
                ReceiveGenerator(localName != null ? localName : file.GetFullName(), file, i, f);
            }

            phase++;
            _checkSum.Length = CheckSum.SumLength;
            if (_options.Verbose > 2)
            {
                Log.WriteLine("GenerateFiles phase=" + phase);
            }
            f.WriteInt(-1);

            phase++;
            if (_options.Verbose > 2)
            {
                Log.WriteLine("GenerateFiles phase=" + phase);
            }

            f.WriteInt(-1);

            if (_options.ProtocolVersion >= 29 && !_options.DelayUpdates)
            {
                f.WriteInt(-1);
            }

            /* now we need to fix any directory permissions that were
            * modified during the transfer 
            * */
            for (i = 0; i < fileList.Count; i++)
            {
                FileStruct file = (fileList[i]);
                if (file.BaseName != null || Util.S_ISDIR(file.Mode))
                {
                    continue;
                }
                ReceiveGenerator(localName != null ? localName : file.GetFullName(), file, i, null);
            }

            if (_options.Verbose > 2)
            {
                Log.WriteLine("GenerateFiles finished");
            }
        }

        public void ReceiveGenerator(string fileName, FileStruct file, int i, IoStream f)
        {
            fileName = Path.Combine(_options.Dir, fileName);

            if (UnchangedFile(fileName, file))
            {
                return;
            }
            if (_options.Verbose > 2)
            {
                Log.WriteLine("Receive Generator(" + fileName + "," + i + ")\n");
            }
            int statRet;
            FStat st = new FStat();
            if (_options.DryRun)
            {
                statRet = -1;
            }
            else
            {
                statRet = 0;
                try
                {
                    FileInfo fi = new FileInfo(fileName);
                    // TODO: path length
                    st.Size = fi.Length;
                    // TODO: path length
                    st.MTime = fi.LastWriteTime;
                }
                catch
                {
                    statRet = -1;
                }
            }

            if (_options.OnlyExisting && statRet == -1)
            {
                /* we only want to update existing files */
                if (_options.Verbose > 1)
                {
                    Log.WriteLine("not creating new file \"" + fileName + "\"");
                }
                return;
            }
            string fNameCmp = fileName;
            if (_options.WholeFile > 0)
            {
                f.WriteInt(i);
                WriteSumHead(f, null);
                return;
            }
            FileStream fd;
            try
            {
                fd = new FileStream(fNameCmp, FileMode.Open, FileAccess.Read);
            }
            catch
            {
                if (_options.Verbose > 3)
                {
                    Log.WriteLine("failed to open " + Util.FullFileName(fNameCmp) + ", continuing");
                }
                f.WriteInt(i);
                WriteSumHead(f, null);
                return;
            }

            if (_options.Verbose > 3)
            {
                Log.WriteLine("gen mapped " + fNameCmp + " of size " + st.Size);
            }

            if (_options.Verbose > 2)
            {
                Log.WriteLine("generating and sending sums for " + i);
            }

            f.WriteInt(i);
            Stream fCopy = null;
            GenerateAndSendSums(fd, st.Size, f, fCopy);

            if (fCopy != null)
            {
                fCopy.Close();
            }
            fd.Close();
        }

        public void GenerateAndSendSums(Stream fd, long len, IoStream f, Stream fCopy)
        {
            long i;
            MapFile mapBuf;
            SumStruct sum = new SumStruct();
            long offset = 0;

            SumSizesSqroot(sum, (UInt64)len);

            if (len > 0)
            {
                mapBuf = new MapFile(fd, (int)len, Options.MaxMapSize, (int)sum.BLength);
            }
            else
            {
                mapBuf = null;
            }

            WriteSumHead(f, sum);

            for (i = 0; i < sum.Count; i++)
            {
                UInt32 n1 = (UInt32)Math.Min(len, sum.BLength);
                int off = mapBuf.MapPtr((int)offset, (int)n1);
                byte[] map = mapBuf.P;
                UInt32 sum1 = CheckSum.GetChecksum1(map, off, (int)n1);
                byte[] sum2 = new byte[CheckSum.SumLength];

                sum2 = _checkSum.GetChecksum2(map, off, (int)n1);
                if (_options.Verbose > 3)
                {
                    Log.WriteLine("chunk[" + i + "] offset=" + offset + " len=" + n1 + " sum1=" + sum1);
                }
                f.WriteInt((int)sum1);
                f.Write(sum2, 0, sum.S2Length);
                len -= n1;
                offset += n1;
            }
            if (mapBuf != null)
            {
                mapBuf = null;
            }
        }

        public void SumSizesSqroot(SumStruct sum, UInt64 len)
        {
            UInt32 bLength;
            int s2Length;
            UInt32 c;
            UInt64 l;

            if (_options.BlockSize != 0)
            {
                bLength = (UInt32)_options.BlockSize;
            }
            else
                if (len <= Options.MaxBlockSize * Options.MaxBlockSize)
                {
                    bLength = Options.MaxBlockSize;
                }
                else
                {
                    l = len;
                    c = 1;
                    while ((l = (l >> 1)) != 0)
                    {
                        c <<= 1;
                    }
                    bLength = 0;
                    do
                    {
                        bLength |= c;
                        if (len < bLength * bLength)
                        {
                            bLength &= ~c;
                        }
                        c >>= 1;
                    } while (c >= 8);	/* round to multiple of 8 */
                    bLength = Math.Max(bLength, Options.MaxBlockSize);
                }

            if (_options.ProtocolVersion < 27)
            {
                s2Length = _checkSum.Length;
            }
            else
            {
                if (_checkSum.Length == CheckSum.SumLength)
                {
                    s2Length = CheckSum.SumLength;
                }
                else
                {
                    int b = BlocksumBias;
                    l = len;
                    while ((l = (l >> 1)) != 0)
                    {
                        b += 2;
                    }
                    c = bLength;
                    while ((c = (c >> 1)) != 0 && b != 0)
                    {
                        b--;
                    }
                    s2Length = (b + 1 - 32 + 7) / 8;
                    s2Length = Math.Max(s2Length, _checkSum.Length);
                    s2Length = Math.Min(s2Length, CheckSum.SumLength);
                }
            }

            sum.FLength = (int)len;
            sum.BLength = bLength;
            sum.S2Length = s2Length;
            sum.Count = (int)((len + (bLength - 1)) / bLength);
            sum.Remainder = (UInt32)(len % bLength);

            if (sum.Count != 0 && _options.Verbose > 2)
            {
                Log.WriteLine("count=" + sum.Count + " rem=" + sum.Remainder + " blength=" + sum.BLength +
                    " s2length=" + sum.S2Length + " flength=" + sum.FLength);
            }
        }

        /* Perform our quick-check heuristic for determining if a file is unchanged. */
        public bool UnchangedFile(string fileName, FileStruct file)
        {
            // TODO: path length
            if (!File.Exists(fileName))
            {
                return false;
            }

            FileInfo fi = new FileInfo(fileName);
            // TODO: path length
            if (fi.Length != file.Length)
            {
                return false;
            }

            /* if always checksum is set then we use the checksum instead
            of the file time to determine whether to sync */
            if (_options.AlwaysChecksum)
            {
                byte[] sum = new byte[CheckSum.Md4SumLength];
                // TODO: path length
                _checkSum.FileCheckSum(fileName, ref sum, (int)fi.Length);
                return Util.MemoryCompare(sum, 0, file.Sum, 0, _options.ProtocolVersion < 21 ? 2 : CheckSum.Md4SumLength) == 0;
            }

            if (_options.SizeOnly)
            {
                return true;
            }

            if (_options.IgnoreTimes)
            {
                return false;
            }

            // TODO: path length
            return Util.CompareModificationTime(fi.LastWriteTime.Second, file.ModTime.Second, _options) == 0;
        }
    }
}
