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
    class CommandLineParser
    {
        public static int ParseArguments(string[] args, Options options)
        {
            int argsNotUsed = 0;
            int i = 0;
            Exclude excl = new Exclude(options);
            while (i < args.Length)
            {
                try
                {
                    switch (args[i])
                    {
                        case "--version":
                            WinRsync.PrintRsyncVersion();
                            WinRsync.Exit(String.Empty, null);
                            break;
                        case "--suffix":
                            options.BackupSuffix = args[++i];
                            break;
                        case "--rsync-path":
                            options.RsyncPath = args[++i];
                            break;
                        case "--password-file":
                            options.PasswordFile = args[++i];
                            break;
                        case "--ignore-times":
                        case "-I":
                            options.IgnoreTimes = true;
                            break;
                        case "--size-only":
                            options.SizeOnly = true;
                            break;
                        case "--modify-window":
                            options.UsingModifyWindow = true;
                            options.ModifyWindow = Convert.ToInt32(args[++i]);
                            break;
                        case "--one-file-system":
                        case "-x":
                            options.OneFileSystem = true;
                            break;
                        case "--delete":
                            options.DeleteMode = true;
                            break;
                        case "--existing":
                            options.OnlyExisting = true;
                            break;
                        case "--ignore-existing":
                            options.OptIgnoreExisting = true;
                            break;
                        case "--delete-after":
                            options.DeleteMode = true;
                            options.DeleteAfter = true;
                            break;
                        case "--delete-excluded":
                            options.DeleteMode = true;
                            options.DeleteExcluded = true;
                            break;
                        case "--force":
                            options.ForceDelete = true;
                            break;
                        case "--numeric-ids":
                            options.NumericIds = true;
                            break;
                        case "--exclude":
                            excl.AddExclude(ref options.ExcludeList, args[++i], 0);
                            break;
                        case "--include":
                            excl.AddExclude(ref options.ExcludeList, args[++i], (int)Options.XflgDefInclude);
                            options.ForceDelete = true;
                            break;
                        case "--exclude-from":
                        case "--include-from":
                            string arg = args[i];
                            excl.AddExcludeFile(ref options.ExcludeList, args[++i],
                                    (arg.CompareTo("--exclude-from") == 0) ? 0 : (int)Options.XflgDefInclude);
                            break;
                        case "--safe-links":
                            options.SafeSymlinks = true;
                            break;
                        case "--help":
                        case "-h":
                            WinRsync.Exit(String.Empty, null);
                            break;
                        case "--backup":
                        case "-b":
                            options.MakeBackups = true;
                            break;
                        case "--dry-run":
                        case "-n":
                            options.DryRun = true;
                            break;
                        case "--sparse":
                        case "-S":
                            options.SparseFiles = true;
                            break;
                        case "--cvs-exclude":
                        case "-C":
                            options.CvsExclude = true;
                            break;
                        case "--update":
                        case "-u":
                            options.UpdateOnly = true;
                            break;
                        case "--inplace":
                            options.Inplace = true;
                            break;
                        case "--keep-dirlinks":
                        case "-K":
                            options.KeepDirLinks = true;
                            break;
                        case "--links":
                        case "-l":
                            options.PreserveLinks = true;
                            break;
                        case "--copy-links":
                        case "-L":
                            options.CopyLinks = true;
                            break;
                        case "--whole-file":
                        case "-W":
                            options.WholeFile = 1;
                            break;
                        case "--no-whole-file":
                            options.WholeFile = 0;
                            break;
                        case "--copy-unsafe-links":
                            options.CopyUnsafeLinks = true;
                            break;
                        case "--perms":
                        case "-p":
                            options.PreservePerms = true;
                            break;
                        case "--owner":
                        case "-o":
                            options.PreserveUid = true;
                            break;
                        case "--group":
                        case "-g":
                            options.PreserveGid = true;
                            break;
                        case "--devices":
                        case "-D":
                            options.PreserveDevices = true;
                            break;
                        case "--times":
                        case "-t":
                            options.PreserveTimes = true;
                            break;
                        case "--checksum":
                        case "-c":
                            options.AlwaysChecksum = true;
                            break;
                        case "--verbose":
                        case "-v":
                            options.Verbose++;
                            break;
                        case "--quiet":
                        case "-q":
                            options.Quiet++;
                            break;
                        case "--archive":
                        case "-a":
                            options.ArchiveMode = true;
                            break;
                        case "--server":
                            options.AmServer = true;
                            break;
                        case "--sender":
                            options.AmSender = true;
                            break;
                        case "--recursive":
                        case "-r":
                            options.Recurse = true;
                            break;
                        case "--relative":
                        case "-R":
                            options.RelativePaths = true;
                            break;
                        case "--no-relative":
                            options.RelativePaths = false;
                            break;
                        case "--rsh":
                        case "-e":
                            options.ShellCmd = args[++i];
                            break;
                        case "--block-size":
                        case "-B":
                            options.BlockSize = Convert.ToInt32(args[++i]);
                            break;
                        case "--max-delete":
                            options.MaxDelete = Convert.ToInt32(args[++i]);
                            break;
                        case "--timeout":
                            options.IoTimeout = Convert.ToInt32(args[++i]);
                            break;
                        case "--temp-dir":
                        case "-T":
                            options.TmpDir = args[++i];
                            break;
                        case "--compare-dest":
                            options.CompareDest = args[++i];
                            break;
                        case "--link-dest":
                            options.CompareDest = args[++i];
                            break;
                        case "--compress":
                        case "-z":
                            options.DoCompression = true;
                            break;
                        case "--stats":
                            options.DoStats = true;
                            break;
                        case "--progress":
                            options.DoProgress = true;
                            break;
                        case "--partial":
                            options.KeepPartial = true;
                            break;
                        case "--partial-dir":
                            options.PartialDir = args[++i];
                            break;
                        case "--ignore-errors":
                            options.IgnoreErrors = true;
                            break;
                        case "--blocking-io":
                            options.BlockingIo = 1;
                            break;
                        case "--no-blocking-io":
                            options.BlockingIo = 0;
                            break;
                        case "-P":
                            options.DoProgress = true;
                            options.KeepPartial = true;
                            break;
                        case "--log-format":
                            options.LogFormat = args[++i];
                            break;
                        case "--bwlimit":
                            options.BwLimit = Convert.ToInt32(args[++i]);
                            break;
                        case "--backup-dir":
                            options.BackupDir = args[++i];
                            break;
                        case "--hard-links":
                        case "-H":
                            options.PreserveHardLinks = true;
                            break;
                        case "--read-batch":
                            options.BatchName = args[++i];
                            options.ReadBatch = true;
                            break;
                        case "--write-batch":
                            options.BatchName = args[++i];
                            options.WriteBatch = true;
                            break;
                        case "--files-from":
                            options.FilesFrom = args[++i];
                            break;
                        case "--from0":
                            options.EolNulls = true;
                            break;
                        case "--no-implied-dirs":
                            options.ImpliedDirs = true;
                            break;
                        case "--protocol":
                            options.ProtocolVersion = Convert.ToInt32(args[++i]);
                            break;
                        case "--checksum-seed":
                            options.ChecksumSeed = Convert.ToInt32(args[++i]);
                            break;
                        case "--daemon":
                            options.AmDaemon = true;
                            break;
                        case "--address":
                            options.BindAddress = args[++i];
                            break;
                        case "--port":
                            options.RsyncPort = Convert.ToInt32(args[++i]);
                            break;
                        case "--config":
                            options.ConfigFile = args[++i].Trim();
                            break;
                        default:
                            {
                                argsNotUsed += ParseMergeArgs(args[i], options);
                                break;
                            }
                    }
                    i++;
                }
                catch { return -1; }
            }
            if (options.AmSender && !options.AmServer)
            {
                WinRsync.Exit(String.Empty, null);
            }
            if (options.IoTimeout > 0 && options.IoTimeout < options.SelectTimeout)
            {
                options.SelectTimeout = options.IoTimeout;
            }
            return argsNotUsed;
        }

        private static int ParseMergeArgs(string mergeArgs, Options options)
        {
            if (mergeArgs != null && mergeArgs.StartsWith("-") && mergeArgs.Substring(1).IndexOf('-') == -1)
            {
                mergeArgs = mergeArgs.Substring(1);
                string[] args = new string[mergeArgs.Length];
                for (int i = 0; i < mergeArgs.Length; i++)
                {
                    args[i] = "-" + mergeArgs[i];
                }
                return ParseArguments(args, options);

            }
            return 1;
        }
    }



    public class Stats
    {
        public Int64 TotalSize = 0;
        public Int64 TotalTransferredSize = 0;
        public Int64 TotalWritten = 0;
        public Int64 TotalRead = 0;
        public Int64 LiteralData = 0;
        public Int64 MatchedData = 0;
        public int FileListSize = 0;
        public int NumFiles = 0;
        public int NumTransferredFiles = 0;
        public int CurrentFileIndex = 0;
    }

    public class Options
    {
        public Options()
        {
            ExcludeList.Add(new ExcludeStruct(String.Empty, 0, 0));
            LocalExcludeList.Add(new ExcludeStruct("per-dir .cvsignore ", 0, 0));
            ServerExcludeList.Add(new ExcludeStruct("server ", 0, 0));
        }

        public DateTime LastIo { get; } = DateTime.MinValue;
        //public static System.IO.StreamReader filesFromFD = null;
        /// <summary>
        /// Seems to be null all the time
        /// </summary>
        public static Stream FilesFromFd = null;
        public static Stats Stats = new Stats();
        /// <summary>
        /// "rsync://"
        /// </summary>
        public const string UrlPrefix = "rsync://";

        /// <summary>
        /// 873
        /// </summary>
        public int RsyncPort { get; set; } = 873;

        /// <summary>
        /// 1024
        /// </summary>
        public const int Maxpathlen = 1024;
        /// <summary>
        /// 700
        /// </summary>
        public const int MaxBlockSize = 700;
        /// <summary>
        /// 1000
        /// </summary>
        public const int MaxArgs = 1000;
        /// <summary>
        /// 20
        /// </summary>
        public const int MinProtocolVersion = 20;
        /// <summary>
        /// 25
        /// </summary>
        public const int OldProtocolVersion = 25;
        /// <summary>
        /// 40
        /// </summary>
        public const int MaxProtocolVersion = 40;
        /// <summary>
        /// (256 * 1024)
        /// </summary>
        public const int MaxMapSize = (256 * 1024);
        /// <summary>
        /// (1 &lt;&lt; 0)
        /// </summary>
        public const int FlagTopDir = (1 << 0);
        /// <summary>
        /// (1 &lt;&lt; 1)
        /// </summary>
        public const int FlagHlinkEol = (1 << 1);	/* generator only */
        /// <summary>
        /// (1 &lt;&lt; 2)
        /// </summary>
        public const int FlagMountPoint = (1 << 2);	/* sender only */
        /// <summary>
        /// 0
        /// </summary>
        public const int NoExcludes = 0;
        /// <summary>
        /// 1
        /// </summary>
        public const int ServerExcludes = 1;
        /// <summary>
        /// 2
        /// </summary>
        public const int AllExcludes = 2;
        /// <summary>
        /// 0
        /// </summary>
        public const int GidNone = 0;
        /// <summary>
        /// (1 &lt;&lt; 0)
        /// </summary>
        public const UInt32 XflgFatalErrors = (1 << 0);
        /// <summary>
        /// (1 &lt;&lt; 1)
        /// </summary>
        public const UInt32 XflgDefInclude = (1 << 1);
        /// <summary>
        /// (1 &lt;&lt; 2)
        /// </summary>
        public const UInt32 XflgWordsOnly = (1 << 2);
        /// <summary>
        /// (1 &lt;&lt; 3)
        /// </summary>
        public const UInt32 XflgWordSplit = (1 << 3);
        /// <summary>
        /// (1 &lt;&lt; 4)
        /// </summary>
        public const UInt32 XflgDirectory = (1 << 4);
        /// <summary>
        /// (1 &lt;&lt; 0)
        /// </summary>
        public const UInt32 MatchflgWild = (1 << 0); /* pattern has '*', '[', and/or '?' */
        /// <summary>
        /// 
        /// </summary>
        public const UInt32 MatchflgWild2 = (1 << 1); /* pattern has '**' */
        /// <summary>
        /// (1 &lt;&lt; 2)
        /// </summary>
        public const UInt32 MatchflgWild2Prefix = (1 << 2); /* pattern starts with '**' */
        /// <summary>
        /// (1 &lt;&lt; 3)
        /// </summary>
        public const UInt32 MatchflgAbsPath = (1 << 3); /* path-match on absolute path */
        /// <summary>
        /// (1 &lt;&lt; 4)
        /// </summary>
        public const UInt32 MatchflgInclude = (1 << 4); /* this is an include, not an exclude */
        /// <summary>
        /// (1 &lt;&lt; 5)
        /// </summary>
        public const UInt32 MatchflgDirectory = (1 << 5); /* this matches only directories */
        /// <summary>
        /// (1 &lt;&lt; 6)
        /// </summary>
        public const UInt32 MatchflgClearList = (1 << 6); /* this item is the "!" token */
        /// <summary>
        /// (1 &lt;&lt; 0)
        /// </summary>
        public const UInt32 XmitTopDir = (1 << 0);
        /// <summary>
        /// (1 &lt;&lt; 1)
        /// </summary>
        public const UInt32 XmitSameMode = (1 << 1);
        /// <summary>
        /// (1 &lt;&lt; 2)
        /// </summary>
        public const UInt32 XmitExtendedFlags = (1 << 2);
        /// <summary>
        /// XMIT_EXTENDED_FLAGS = (1 &lt;&lt; 2)
        /// </summary>
        public const UInt32 XmitSameRdevPre28 = XmitExtendedFlags; /* Only in protocols < 28 */
        /// <summary>
        /// (1 &lt;&lt; 3)
        /// </summary>
        public const UInt32 XmitSameUid = (1 << 3);
        /// <summary>
        /// (1 &lt;&lt; 4)
        /// </summary>
        public const UInt32 XmitSameGid = (1 << 4);
        /// <summary>
        /// (1 &lt;&lt; 5)
        /// </summary>
        public const UInt32 XmitSameName = (1 << 5);
        /// <summary>
        /// (1 &lt;&lt; 6)
        /// </summary>
        public const UInt32 XmitLongName = (1 << 6);
        /// <summary>
        /// (1 &lt;&lt; 7)
        /// </summary>
        public const UInt32 XmitSameTime = (1 << 7);
        /// <summary>
        /// (1 &lt;&lt; 8)
        /// </summary>
        public const UInt32 XmitSameRdevMajor = (1 << 8);
        /// <summary>
        /// (1 &lt;&lt; 9)
        /// </summary>
        public const UInt32 XmitHasIdevData = (1 << 9);
        /// <summary>
        /// (1 &lt;&lt; 10)
        /// </summary>
        public const UInt32 XmitSameDev = (1 << 10);
        /// <summary>
        /// (1 &lt;&lt; 11)
        /// </summary>
        public const UInt32 XmitRdevMinorIsSmall = (1 << 11);
        //
        public List<ExcludeStruct> ExcludeList = new List<ExcludeStruct>();
        public List<ExcludeStruct> LocalExcludeList = new List<ExcludeStruct>();
        public List<ExcludeStruct> ServerExcludeList = new List<ExcludeStruct>();
        public string ExcludePathPrefix = null;

        public DateTime StartTime = DateTime.Now;
        public string BackupSuffix = null;
        public string RsyncPath = null;
        public string PasswordFile = null;
        public bool IgnoreTimes = false;
        public bool SizeOnly = false;
        /// <summary>
        /// Allowed difference between two files modification time
        /// </summary>
        public int ModifyWindow = 0;
        public bool UsingModifyWindow = false;
        public bool OneFileSystem = false;
        public bool DeleteMode = false;
        public bool OnlyExisting = false;
        public bool OptIgnoreExisting = false;
        public bool DeleteAfter = false;
        public bool DeleteExcluded = false;
        public bool ForceDelete = false;
        public bool NumericIds = false;
        public bool SafeSymlinks = false;
        public bool MakeBackups = false;
        public bool DryRun = false;
        public bool SparseFiles = false;
        public bool CvsExclude = false;
        public bool UpdateOnly = false;
        public bool Inplace = false;
        public bool KeepDirLinks = false;
        public bool PreserveLinks = false;
        public bool CopyLinks = false;
        public int WholeFile = -1;
        public bool CopyUnsafeLinks = false;
        public bool PreservePerms = false;
        public bool PreserveUid = false;
        public bool PreserveGid = false;
        public bool PreserveDevices = false;
        public bool PreserveTimes = false;
        public bool AlwaysChecksum = false;
        public bool ArchiveMode = false;
        public bool AmServer = false;
        public bool Recurse = false;
        public int Verbose = 0;
        public int Quiet = 0;
        public bool AmSender = false;
        public bool RelativePaths = true; //changed to bool and set true as init value
        public string ShellCmd = null;
        public int BlockSize = 0;
        public int MaxDelete = 0;
        public int IoTimeout = 0;
        public string TmpDir = null;
        public string CompareDest = null;
        public int SelectTimeout = 0;
        public bool DoCompression = false;
        public bool DoStats = false;
        public bool DoProgress = false;
        public bool KeepPartial = false;
        public string PartialDir = null;
        public bool IgnoreErrors = false;
        public int BlockingIo = -1;
        public string LogFormat = null;
        public int BwLimit = 0;
        public string BackupDir = null;
        public bool PreserveHardLinks = false;
        public string BatchName = null;
        public string FilesFrom = null;
        public bool EolNulls = false;
        public bool ImpliedDirs = false;
        public int ProtocolVersion = 28;
        public int ChecksumSeed = 0;
        public bool ReadBatch = false;
        public bool WriteBatch = false;
        public bool ListOnly = false;
        public bool DelayUpdates = false;

        public string BindAddress = "127.0.0.1";
        public string ConfigFile = "rsyncd.conf";
        public bool DaemonOpt = false;
        public bool NoDetach = false;
        //public int defaultafHint = AIF_INET;
        public bool AmDaemon = false;
        public bool AmRoot = true;
        public bool AmGenerator = false;
        public string RemoteFilesFromFile = null;

        public bool ReadOnly = false;
        public bool SanitizePath = false;
        public Stream SockFIn = null;
        public Stream SockFOut = null;
        public int RemoteProtocol = 0;
        public string Dir = String.Empty;
        public FileStream LogFile = null;
        public int ModuleId = -1;
        public string RemoteAddr = null;
        public string RemoteHost = null;

        public string WhoAmI()
        {
            return AmSender ? "sender" : AmGenerator ? "generator" : "receiver";
        }

        public int ServerOptions(string[] args)
        {
            int argc = 0;
            args[argc++] = "--server";
            for (int i = 0; i < Verbose; i++)
            {
                args[argc++] = "-v";
            }


            args[argc++] = "-R";
            if (AlwaysChecksum)
            {
                args[argc++] = "-c";
            }
            if (Recurse)
            {
                args[argc++] = "-r";
            }

            if (AmSender)
            {
                if (DeleteExcluded)
                {
                    args[argc++] = "--delete-excluded";
                }
                else if (DeleteMode)
                {
                    args[argc++] = "--delete";
                }

                if (DeleteAfter)
                {
                    args[argc++] = "--delete-after";
                }

                if (ForceDelete)
                {
                    args[argc++] = "--force";
                }
            }

            if (!AmSender)
            {
                args[argc++] = "--sender";
            }

            return argc;
        }
    }
}
