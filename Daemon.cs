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
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace NetSync
{

    public class ClientInfo
    {
        public Options Options = null;
        public IoStream IoStream = null;
        public Thread ClientThread = null;
    }

    public class TcpSocketListener
    {
        private Socket _client;
        private Thread _clientThread;
        private ClientInfo _clientInfo;
        private List<TcpSocketListener> _clientSockets;

        public TcpSocketListener(Socket client, ref List<TcpSocketListener> clientSockets)
        {
            _client = client;
            _clientSockets = clientSockets;
            _clientInfo = new ClientInfo();
            _clientInfo.Options = new Options();
        }
        public void StartSocketListener()
        {
            if (_client != null)
            {
                _clientThread = new Thread(new ThreadStart(StartDaemon));
                _clientInfo.IoStream = new IoStream(new NetworkStream(_client));
                _clientInfo.IoStream.ClientThread = _clientThread;
                _clientThread.Start();
            }
        }

        public void StartDaemon()
        {
            var remoteAddr = _client.RemoteEndPoint.ToString();
            remoteAddr = remoteAddr.Substring(0, remoteAddr.IndexOf(':'));
            //string remoteHost = Dns.GetHostByAddress(IPAddress.Parse(remoteAddr)).HostName;
            var remoteHost = Dns.GetHostEntry(IPAddress.Parse(remoteAddr)).HostName;
            _clientInfo.Options.RemoteAddr = remoteAddr;
            _clientInfo.Options.RemoteHost = remoteHost;

            Daemon.StartDaemon(_clientInfo);
            _client.Close();
            _clientSockets.Remove(this);
        }
    }


    public class Daemon
    {
        private static TcpListener _server = null;
        private static List<TcpSocketListener> _clientSockets = null;
        private static bool _stopServer = true;
        public static Options ServerOptions = null;

        public static Configuration Config;

        public static int DaemonMain(Options options)
        {
            ServerOptions = options;
            Config = new Configuration(ServerOptions.ConfigFile);
            if (Config.LoadParm(options))
            {
                StartAcceptLoop(options.RsyncPort);
            }
            return -1;
        }

        public static void StartAcceptLoop(int port)
        {
            var localAddr = IPAddress.Parse(ServerOptions.BindAddress);
            _server = new TcpListener(localAddr, port); //Switched to this one because TcpListener(port) is obsolete
            //Server = new TcpListener(port);
            
            try
            {
                _server.Start();
            }
            catch (Exception)
            {
                WinRsync.Exit("Can't listening address " + ServerOptions.BindAddress + " on port " + port, null);
                Environment.Exit(0);
            }
            Log.WriteLine("WinRSyncd starting, listening on port " + port);
            _stopServer = false;
            _clientSockets = new List<TcpSocketListener>();
            while (!_stopServer)
            {
                try
                {
                    var soc = _server.AcceptSocket();
                    if (!Config.LoadParm(ServerOptions))
                    {
                        continue;
                    }
                    var socketListener = new TcpSocketListener(soc, ref _clientSockets);
                    lock (_clientSockets)
                    {
                        _clientSockets.Add(socketListener);
                    }
                    socketListener.StartSocketListener();
                    for (var i = 0; i < _clientSockets.Count; i++)
                    {
                        if (_clientSockets[i] == null)
                        {
                            _clientSockets.RemoveAt(i);
                        }
                    }
                }
                catch (SocketException)
                {
                    _stopServer = true;
                }
            }
            if (ServerOptions.LogFile != null)
            {
                ServerOptions.LogFile.Close();
            }
        }

        public static int StartDaemon(ClientInfo clientInfo)
        {
            var stream = clientInfo.IoStream;
            var options = clientInfo.Options;
            options.AmDaemon = true;

            stream.IoPrintf("@RSYNCD: " + options.ProtocolVersion + "\n");
            var line = stream.ReadLine();
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
                stream.IoPrintf("@ERROR: protocol startup error\n");
                return -1;
            }
            if (options.ProtocolVersion > options.RemoteProtocol)
            {
                options.ProtocolVersion = options.RemoteProtocol;
            }
            line = stream.ReadLine();
            if (String.Compare(line, "#list\n", StringComparison.Ordinal) == 0)
            {
                ClientServer.SendListing(stream);
                return -1;
            }

            if (line[0] == '#')
            {
                stream.IoPrintf("@ERROR: Unknown command '" + line.Replace("\n", String.Empty) + "'\n");
                return -1;
            }

            var i = Config.GetNumberModule(line.Replace("\n", String.Empty));
            if (i < 0)
            {
                stream.IoPrintf("@ERROR: Unknown module " + line);
                WinRsync.Exit("@ERROR: Unknown module " + line, clientInfo);
            }
            options.DoStats = true;
            options.ModuleId = i;
            ClientServer.RsyncModule(clientInfo, i);
            clientInfo.IoStream.Close();
            return 1;
        }

        public static void StartServer(ClientInfo cInfo, string[] args)
        {
            var f = cInfo.IoStream;
            var options = cInfo.Options;

            if (options.ProtocolVersion >= 23)
            {
                f.IoStartMultiplexOut();
            }
            if (options.AmSender)
            {
                options.KeepDirLinks = false; /* Must be disabled on the sender. */
                var excl = new Exclude(options);
                excl.ReceiveExcludeList(f);
                DoServerSender(cInfo, args);
            }
            else
            {
                DoServerReceive(cInfo, args);
            }
        }

        static void DoServerSender(ClientInfo clientInfo, string[] args)
        {
            var dir = args[0];
            var ioStream = clientInfo.IoStream;
            var options = clientInfo.Options;

            if (options.Verbose > 2)
            {
                Log.Write("Server sender starting");
            }
            if (options.AmDaemon && Config.ModuleIsWriteOnly(options.ModuleId))
            {
                WinRsync.Exit("ERROR: module " + Config.GetModuleName(options.ModuleId) + " is write only", clientInfo);
                return;
            }

            if (!options.RelativePaths && !Util.PushDir(dir))
            {
                WinRsync.Exit("Push_dir#3 " + dir + "failed", clientInfo);
                return;
            }

            var fList = new FileList(options);
            var fileList = fList.SendFileList(clientInfo, args);
            if (options.Verbose > 3)
            {
                Log.WriteLine("File list sent");
            }
            if (fileList.Count == 0)
            {
                WinRsync.Exit("File list is empty", clientInfo);
                return;
            }
            ioStream.IoStartBufferingIn();
            ioStream.IoStartBufferingOut();

            var sender = new Sender(options);
            sender.SendFiles(fileList, clientInfo);
            ioStream.Flush();
            WinRsync.Report(clientInfo);
            if (options.ProtocolVersion >= 24)
            {
                ioStream.ReadInt();
            }
            ioStream.Flush();
        }

        public static void DoServerReceive(ClientInfo cInfo, string[] args)
        {
            var options = cInfo.Options;
            var f = cInfo.IoStream;
            if (options.Verbose > 2)
            {
                Log.Write("Server receive starting");
            }
            if (options.AmDaemon && Config.ModuleIsReadOnly(options.ModuleId))
            {
                WinRsync.Exit("ERROR: module " + Config.GetModuleName(options.ModuleId) + " is read only", cInfo);
                return;
            }

            f.IoStartBufferingIn();
            if (options.DeleteMode && !options.DeleteExcluded)
            {
                var excl = new Exclude(options);
                excl.ReceiveExcludeList(f);
            }

            var fList = new FileList(cInfo.Options);
            var fileList = fList.ReceiveFileList(cInfo);
            DoReceive(cInfo, fileList, null);
        }

        public static int DoReceive(ClientInfo cInfo, List<FileStruct> fileList, string localName)
        {
            var f = cInfo.IoStream;
            var options = cInfo.Options;
            var receiver = new Receiver(options);

            options.CopyLinks = false;
            f.Flush();
            if (!options.DeleteAfter)
            {
                if (options.Recurse && options.DeleteMode && localName == null && fileList.Count > 0)
                {
                    receiver.DeleteFiles(fileList);
                }
            }
            f.IoStartBufferingOut();
            var gen = new Generator(options);
            gen.GenerateFiles(f, fileList, localName);
            f.Flush();
            if (fileList != null && fileList.Count != 0)
            {
                receiver.ReceiveFiles(cInfo, fileList, localName);
            }
            WinRsync.Report(cInfo);
            if (options.ProtocolVersion >= 24)
            {
                /* send a final goodbye message */
                f.WriteInt(-1);
            }
            f.Flush();
            return 0;
        }
    }
}
