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
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace NetSync
{
    public class WinRsync
    {
        private const string BackupSuffix = "~";
        private const string RsyncName = "rsync";
        private const string RsyncVersion = "1.0";

        public WinRsync()
        {
            ;
        }

        public Options Opt { get; private set; }

        public void Run(string[] args)
        {
            Opt = new Options();

            //if (args.Length == 0)
            //{
            //    WinRsync.Exit(String.Empty, null);
            //}
            //int argsNotUsed = CommandLineParser.ParseArguments(args, Opt);
            //if (argsNotUsed == -1)
            //{
            //    WinRsync.Exit("Error parsing options", null);
            //}
            //string[] args2 = new string[argsNotUsed];
            //for (int i = 0; i < argsNotUsed; i++)
            //{
            //    args2[i] = args[args.Length - argsNotUsed + i];
            //}

            if (Opt.AmDaemon && !Opt.AmSender)
            {
                Daemon.DaemonMain(Opt);
                return;
            }
            var cInfo = new ClientInfo();
            cInfo.Options = Opt;
            StartClient(args2, cInfo);
            Opt.DoStats = true;
            cInfo.IoStream = null;
            Report(cInfo);
            Console.Write("Press 'Enter' to exit.");
            Console.Read();
        }

        public static void Report(ClientInfo cInfo)
        {
            var f = cInfo.IoStream;
            var options = cInfo.Options;

            var totalWritten = Options.Stats.TotalWritten;
            var totalRead = Options.Stats.TotalRead;
            if (options.AmServer && f != null)
            {
                if (options.AmSender)
                {
                    f.WriteLongInt(totalRead);
                    f.WriteLongInt(totalWritten);
                    f.WriteLongInt(Options.Stats.TotalSize);
                }
                return;
            }
            if (!options.AmSender && f != null)
            {
                /* Read the first two in opposite order because the meaning of
                 * read/write swaps when switching from sender to receiver. */
                totalWritten = f.ReadLongInt();
                totalRead = f.ReadLongInt();
                Options.Stats.TotalSize = f.ReadLongInt();
            }

            if (options.DoStats)
            {
                Log.WriteLine("Number of files: " + Options.Stats.NumFiles);
                Log.WriteLine("Number of files transferred: " + Options.Stats.NumTransferredFiles);
                Log.WriteLine("Total file size: " + Options.Stats.TotalSize);
                Log.WriteLine("Total transferred file size: " + Options.Stats.TotalTransferredSize);
                Log.WriteLine("Literal data: " + Options.Stats.LiteralData);
                Log.WriteLine("Matched data: " + Options.Stats.MatchedData);
                Log.WriteLine("File list size: " + Options.Stats.FileListSize);
                Log.WriteLine("Total bytes written: " + totalWritten);
                Log.WriteLine("Total bytes received: " + totalRead);
            }
        }

        public static int StartClient(string[] args, ClientInfo cInfo)
        {
            var options = cInfo.Options;
            if (args[0].StartsWith(Options.UrlPrefix) && !options.ReadBatch) //source is remote
            {
                string path, user = String.Empty;
                //string host = args[0].Substring(Options.URL_PREFIX.Length, args[0].Length - Options.URL_PREFIX.Length); //@fixed use 1-param version of Substring
                var host = args[0].Substring(Options.UrlPrefix.Length);
                if (host.LastIndexOf('@') != -1)
                {
                    user = host.Substring(0, host.LastIndexOf('@'));
                    host = host.Substring(host.LastIndexOf('@') + 1);
                }
                else
                {
                    Exit("Unknown host", null);
                }
                if (host.IndexOf("/", StringComparison.Ordinal) != -1)
                {
                    path = host.Substring(host.IndexOf("/", StringComparison.Ordinal) + 1);
                    host = host.Substring(0, host.IndexOf("/", StringComparison.Ordinal));
                }
                else
                {
                    path = String.Empty;
                }
                if (host[0] == '[' && host.IndexOf(']') != -1)
                {
                    host = host.Remove(host.IndexOf(']'), 1);
                    host = host.Remove(host.IndexOf('['), 1);
                }
                if (host.IndexOf(':') != -1)
                {
                    options.RsyncPort = Convert.ToInt32(host.Substring(host.IndexOf(':')));
                    host = host.Substring(0, host.IndexOf(':'));
                }
                var newArgs = Util.DeleteFirstElement(args);
                return StartSocketClient(host, path, user, newArgs, cInfo);
            }

            //source is local
            if (!options.ReadBatch)
            {
                var p = Util.FindColon(args[0]);
                var user = String.Empty;
                options.AmSender = true;
                if (args[args.Length - 1].StartsWith(Options.UrlPrefix) && !options.ReadBatch)
                {
                    string path;
                    var host = args[args.Length - 1].Substring(Options.UrlPrefix.Length);
                    if (host.LastIndexOf('@') != -1)
                    {
                        user = host.Substring(0, host.LastIndexOf('@'));
                        host = host.Substring(host.LastIndexOf('@') + 1);
                    }
                    else
                    {
                        Exit("Unknown host", null);
                    }
                    if (host.IndexOf("/") != -1)
                    {
                        path = host.Substring(host.IndexOf("/") + 1);
                        host = host.Substring(0, host.IndexOf("/"));
                    }
                    else
                    {
                        path = String.Empty;
                    }
                    if (host[0] == '[' && host.IndexOf(']') != -1)
                    {
                        host = host.Remove(host.IndexOf(']'), 1);
                        host = host.Remove(host.IndexOf('['), 1);
                    }
                    if (host.IndexOf(':') != -1)
                    {
                        options.RsyncPort = Convert.ToInt32(host.Substring(host.IndexOf(':')));
                        host = host.Substring(0, host.IndexOf(':'));
                    }
                    var newArgs = Util.DeleteLastElement(args);
                    return StartSocketClient(host, path, user, newArgs, cInfo);
                }
                p = Util.FindColon(args[args.Length - 1]);
                if (p == -1) //src & dest are local
                {
                    /* no realized*/
                }
                else if (args[args.Length - 1][p + 1] == ':')
                {
                    if (options.ShellCmd == null)
                    {
                        return StartSocketClient(args[args.Length - 1].Substring(0, p),
                            args[args.Length - 1].Substring(p + 2), user, args, cInfo);
                    }
                }
            }
            return 0;
        }

        public static int StartSocketClient(string host, string path, string user, string[] args, ClientInfo cInfo)
        {
            var options = cInfo.Options;
            if (path.CompareTo(String.Empty) != 0 && path[0] == '/')
            {
                Log.WriteLine("ERROR: The remote path must start with a module name not a /");
                return -1;
            }
            cInfo.IoStream = OpenSocketOutWrapped(host, options.RsyncPort, options.BindAddress);

            if (cInfo.IoStream != null)
            {
                StartInbandExchange(user, path, cInfo, args.Length);
            }

            var client = new Client();
            return client.ClientRun(cInfo, -1, args);
        }

        public static int StartInbandExchange(string user, string path, ClientInfo cInfo, int argc)
        {
            var options = cInfo.Options;
            var f = cInfo.IoStream;

            var sargs = new string[Options.MaxArgs];
            var sargc = options.ServerOptions(sargs);
            sargs[sargc++] = ".";
            //if(path != null && path.Length>0)
            //sargs[sargc++] = path;

            if (argc == 0 && !options.AmSender)
            {
                options.ListOnly = true;
            }
            if (path[0] == '/')
            {
                Log.WriteLine("ERROR: The remote path must start with a module name");
                return -1;
            }
            f.IoPrintf("@RSYNCD: " + options.ProtocolVersion + "\n");
            var line = f.ReadLine();
            try
            {
                options.RemoteProtocol = Int32.Parse(line.Substring(9, 2));
            }
            catch
            {
                options.RemoteProtocol = 0;
            }
            var isValidstring = line.StartsWith("@RSYNCD: ") && line.EndsWith("\n") && options.RemoteProtocol > 0;
            if (!isValidstring)
            {
                f.IoPrintf("@ERROR: protocol startup error\n");
                return -1;
            }
            if (options.ProtocolVersion > options.RemoteProtocol)
            {
                options.ProtocolVersion = options.RemoteProtocol;
            }
            f.IoPrintf(path + "\n");
            while (true)
            {
                line = f.ReadLine();
                if (line.CompareTo("@RSYNCD: OK\n") == 0)
                {
                    break;
                }
                if (line.Length > 18 && line.Substring(0, 18).CompareTo("@RSYNCD: AUTHREQD ") == 0)
                {
                    var pass = String.Empty;
                    if (user.IndexOf(':') != -1)
                    {
                        pass = user.Substring(user.IndexOf(':') + 1);
                        user = user.Substring(0, user.IndexOf(':'));
                    }
                    f.IoPrintf(user + " " +
                               Authentication.AuthorizeClient(user, pass, line.Substring(18).Replace("\n", String.Empty),
                                   options) + "\n");
                    continue;
                }

                if (line.CompareTo("@RSYNCD: EXIT\n") == 0)
                {
                    Exit("@RSYNCD: EXIT", null);
                }

                if (line.StartsWith("@ERROR: "))
                {
                    Exit("Server: " + line.Replace("\n", String.Empty), null);
                }
            }

            for (var i = 0; i < sargc; i++)
            {
                f.IoPrintf(sargs[i] + "\n");
            }
            f.IoPrintf("\n");
            return 0;
        }

        public static IoStream OpenSocketOutWrapped(string host, int port, string bindAddress)
        {
            return OpenSocketOut(host, port, bindAddress);
        }

        public static IoStream OpenSocketOut(string host, int port, string bindAddress)
        {
            TcpClient client = null;
            try
            {
                client = new TcpClient(host, port);
            }
            catch (Exception)
            {
                Exit("Can't connect to server", null);
            }
            var stream = new IoStream(client.GetStream());
            return stream;
        }

        /// <summary>
        /// </summary>
        /// <param name="cInfo"></param>
        public static void SetupProtocol(ClientInfo cInfo)
        {
            var f = cInfo.IoStream;
            var options = cInfo.Options;

            if (options.RemoteProtocol == 0)
            {
                if (!options.ReadBatch)
                {
                    f.WriteInt(options.ProtocolVersion);
                }
                options.RemoteProtocol = f.ReadInt();
                if (options.ProtocolVersion > options.RemoteProtocol)
                {
                    options.ProtocolVersion = options.RemoteProtocol;
                }
            }
            if (options.ReadBatch && options.RemoteProtocol > options.ProtocolVersion)
            {
                Exit("The protocol version in the batch file is too new", null);
            }
            if (options.Verbose > 3)
            {
                Log.WriteLine("(" + (options.AmServer ? "Server" : "Client") + ") Protocol versions: remote=" +
                              options.RemoteProtocol + ", negotiated=" + options.ProtocolVersion);
            }
            if (options.RemoteProtocol < Options.MinProtocolVersion ||
                options.RemoteProtocol > Options.MaxProtocolVersion)
            {
                Exit("Protocol version mistmatch", null);
            }
            if (options.AmServer)
            {
                if (options.ChecksumSeed == 0)
                {
                    options.ChecksumSeed = (int) DateTime.Now.Ticks;
                }
                f.WriteInt(options.ChecksumSeed);
            }
            else
            {
                options.ChecksumSeed = f.ReadInt();
            }
        }

        public static void PrintRsyncVersion()
        {
            Log.WriteLine(RsyncName + " version " + RsyncVersion);
            Log.WriteLine(@"
   This port is Copyright (C) 2006 Alex Pedenko, Michael Feingold and Ivan Semenov
  
   This program is free software; you can redistribute it and/or modify
   it under the terms of the GNU General Public License as published by
   the Free Software Foundation; either version 2 of the License, or
   (at your option) any later version.
 
   This program is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
   GNU General Public License for more details.
 
   You should have received a copy of the GNU General Public License
   along with this program; if not, write to the Free Software
   Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA");
        }

        public static void Exit(string message, ClientInfo clientInfo)
        {
            Log.Write(message);

            if (!Opt.AmDaemon)
            {
                Console.Read();
                Environment.Exit(0);
            }
            else
            {
                if (clientInfo != null && clientInfo.IoStream != null && clientInfo.IoStream.ClientThread != null)
                {
                    clientInfo.IoStream.ClientThread.Abort();
                }
            }
        }
    }

    internal class Log
    {
        /// <summary>
        ///     Writes string to log adding newLine character at the end
        /// </summary>
        /// <param name="str"></param>
        public static void WriteLine(string str)
        {
            LogWrite(str + Environment.NewLine);
        }

        /// <summary>
        ///     Writes string to log
        /// </summary>
        /// <param name="str"></param>
        public static void Write(string str)
        {
            LogWrite(str);
        }

        /// <summary>
        ///     Empty method at this moment
        /// </summary>
        /// <param name="file"></param>
        /// <param name="initialStats"></param>
        public static void LogSend(FileStruct file, Stats initialStats)
        {
        }

        /// <summary>
        ///     Writes string to logFile or to console if client
        /// </summary>
        /// <param name="str"></param>
        private static void LogWrite(string str)
        {
            if (Daemon.ServerOptions != null)
            {
                if (Daemon.ServerOptions.LogFile == null)
                {
                    try
                    {
                        Daemon.ServerOptions.LogFile =
                            new FileStream(Path.Combine(Environment.SystemDirectory, "rsyncd.log"),
                                FileMode.Append, FileAccess.Write); //FileMode.OpenOrCreate | FileMode.Append is redundant
                    }
                    catch (Exception)
                    {
                        return;
                    }
                }
                str = "[ " + DateTime.Now + " ] " + str;
                Daemon.ServerOptions.LogFile.Write(Encoding.ASCII.GetBytes(str), 0, str.Length); //@todo cyrillic
                Daemon.ServerOptions.LogFile.Flush();
            }
            else
            {
                if (!WinRsync.Opt.AmDaemon)
                {
                    Console.Write(str);
                }
            }
        }
    }

    public class SumBuf
    {
        public byte Flags;
        public UInt32 Len;
        public int Offset;
        public UInt32 Sum1;
        public byte[] Sum2 = new byte[CheckSum.SumLength];
    }

    public class SumStruct
    {
        public UInt32 BLength;
        public int Count;
        public int FLength;
        public UInt32 Remainder;
        public int S2Length;
        public SumBuf[] Sums;
    }

    public class FStat
    {
        public int Gid;
        public int Mode;
        public DateTime MTime;
        public int Rdev;
        public long Size;
        public int Uid;
    }

    public class Progress
    {
        public static void ShowProgress(long offset, long size)
        {
        }

        public static void EndProgress(long size)
        {
        }
    }
}