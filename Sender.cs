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
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace NetSync
{
    class Sender
    {
        private Options _options;
        private CheckSum _checkSum;

        public Sender(Options opt)
        {
            _options = opt;
            _checkSum = new CheckSum(_options);
        }

        public void SendFiles(List<FileStruct> fileList, ClientInfo clientInfo)
        {
            ShowMessage("Processing...");
            try
            {
                IoStream ioStream = clientInfo.IoStream;
                string fileName = String.Empty, fileName2 = String.Empty;
                SumStruct s = null;
                int phase = 0;
                bool saveMakeBackups = _options.MakeBackups;
                Match match = new Match(_options);

                if (_options.Verbose > 2)
                {
                    Log.WriteLine("SendFiles starting");
                }
                while (true)
                {
                    fileName = String.Empty;
                    int i = ioStream.ReadInt();
                    if (i == -1)
                    {
                        if (phase == 0)
                        {
                            phase++;
                            _checkSum.Length = CheckSum.SumLength;
                            ioStream.WriteInt(-1);
                            if (_options.Verbose > 2)
                            {
                                Log.WriteLine("SendFiles phase=" + phase);
                            }
                            _options.MakeBackups = false;
                            continue;
                        }
                        break;
                    }

                    if (i < 0 || i >= fileList.Count)
                    {
                        WinRsync.Exit("Invalid file index " + i + " (count=" + fileList.Count + ")", clientInfo);
                    }

                    FileStruct file = fileList[i];

                    Options.Stats.CurrentFileIndex = i;
                    Options.Stats.NumTransferredFiles++;
                    Options.Stats.TotalTransferredSize += file.Length;

                    if (!string.IsNullOrEmpty(file.BaseDir))
                    {
                        fileName = file.BaseDir;
                        if (!fileName.EndsWith("/"))
                        {
                            fileName += "/";
                        }
                    }
                    fileName2 = file.GetFullName();
                    fileName += file.GetFullName();
                    ShowMessage("uploading " + fileName);

                    if (_options.Verbose > 2)
                    {
                        Log.WriteLine("sendFiles(" + i + ", " + fileName + ")");
                    }

                    if (_options.DryRun)
                    {
                        if (!_options.AmServer && _options.Verbose != 0)
                        {
                            Log.WriteLine(fileName2);
                        }
                        ioStream.WriteInt(i);
                        continue;
                    }

                    Stats initialStats = Options.Stats;
                    s = ReceiveSums(clientInfo);

                    Stream fd;
                    try
                    {
                        fd = new FileStream(fileName, FileMode.Open, FileAccess.Read);
                    }
                    catch (FileNotFoundException)
                    {
                        Log.WriteLine("file has vanished: " + Util.FullFileName(fileName));
                        s = null;
                        continue;
                    }
                    catch (Exception)
                    {
                        Log.WriteLine("SendFiles failed to open " + Util.FullFileName(fileName));
                        s = null;
                        continue;
                    }

                    FStat st = new FStat();
                    FileInfo fi = new FileInfo(fileName);
                    // TODO: path length
                    st.MTime = fi.LastWriteTime;
                    // TODO: path length
                    st.Size = fi.Length;

                    MapFile mbuf = null;
                    if (st.Size != 0)
                    {
                        int mapSize = (int)Math.Max(s.BLength * 3, Options.MaxMapSize);
                        mbuf = new MapFile(fd, (int)st.Size, mapSize, (int)s.BLength);
                    }

                    if (_options.Verbose > 2)
                    {
                        Log.WriteLine("SendFiles mapped " + fileName + " of size " + st.Size);
                    }

                    ioStream.WriteInt(i);
                    Generator gen = new Generator(_options);
                    gen.WriteSumHead(ioStream, s);

                    if (_options.Verbose > 2)
                    {
                        Log.WriteLine("calling MatchSums " + fileName);
                    }

                    if (!_options.AmServer && _options.Verbose != 0)
                    {
                        Log.WriteLine(fileName2);
                    }

                    Token token = new Token(_options);
                    token.SetCompression(fileName);

                    match.MatchSums(ioStream, s, mbuf, (int)st.Size);
                    Log.LogSend(file, initialStats);

                    if (mbuf != null)
                    {
                        bool j = mbuf.UnMapFile();
                        if (j)
                        {
                            Log.WriteLine("read errors mapping " + Util.FullFileName(fileName));
                        }
                    }
                    fd.Close();

                    s.Sums = null;

                    if (_options.Verbose > 2)
                    {
                        Log.WriteLine("sender finished " + fileName);
                    }
                }
                _options.MakeBackups = saveMakeBackups;

                if (_options.Verbose > 2)
                {
                    Log.WriteLine("send files finished");
                }

                match.MatchReport(ioStream);
                ioStream.WriteInt(-1);
            }
            finally
            {
                HideMessage();
            }
        }

        public SumStruct ReceiveSums(ClientInfo cInfo)
        {
            IoStream f = cInfo.IoStream;
            SumStruct s = new SumStruct();
            int i;
            int offset = 0;
            ReadSumHead(cInfo, ref s);
            s.Sums = null;

            if (_options.Verbose > 3)
            {
                Log.WriteLine("count=" + s.Count + " n=" + s.BLength + " rem=" + s.Remainder);
            }

            if (s.Count == 0)
            {
                return s;
            }

            s.Sums = new SumBuf[s.Count];

            for (i = 0; i < s.Count; i++)
            {
                s.Sums[i] = new SumBuf();
                s.Sums[i].Sum1 = (UInt32)f.ReadInt();
                s.Sums[i].Sum2 = f.ReadBuffer(s.S2Length);
                s.Sums[i].Offset = offset;
                s.Sums[i].Flags = 0;

                if (i == s.Count - 1 && s.Remainder != 0)
                {
                    s.Sums[i].Len = s.Remainder;
                }
                else
                {
                    s.Sums[i].Len = s.BLength;
                }
                offset += (int)s.Sums[i].Len;

                if (_options.Verbose > 3)
                {
                    Log.WriteLine("chunk[" + i + "] len=" + s.Sums[i].Len);
                }
            }

            s.FLength = offset;
            return s;
        }

        public void ReadSumHead(ClientInfo clientInfo, ref SumStruct sum)
        {
            IoStream ioStream = clientInfo.IoStream;
            sum.Count = ioStream.ReadInt();
            sum.BLength = (UInt32)ioStream.ReadInt();
            if (_options.ProtocolVersion < 27)
            {
                sum.S2Length = _checkSum.Length;
            }
            else
            {
                sum.S2Length = ioStream.ReadInt();
                if (sum.S2Length > CheckSum.Md4SumLength)
                {
                    WinRsync.Exit("Invalid checksum length " + sum.S2Length, clientInfo);
                }
            }
            sum.Remainder = (UInt32)ioStream.ReadInt();
        }

        NotifyIcon _icon = new NotifyIcon();
        public void ShowMessage(string msg)
        {
            // TODO: path length
            if (!File.Exists("logo.ico"))
            {
                return;
            }

            if (msg.Length > 64)
            {
                msg = msg.Substring(0, 60) + "...";
            }

            _icon.Icon = new Icon("logo.ico");
            _icon.Text = msg;
            _icon.Visible = true;
        }

        public void HideMessage()
        {
            _icon.Visible = false;
        }
    }
}
