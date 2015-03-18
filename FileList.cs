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
using System.IO;

namespace NetSync
{
    public class FileStruct
    {
        public int Length;
        public string BaseName;
        public string DirName;
        public string BaseDir;
        public DateTime ModTime;
        public uint Mode;
        public int Uid;
        public int Gid;
        public uint Flags;
        public bool IsTopDir;
        public byte[] Sum;

        public string GetFullName()
        {
            string fullName = String.Empty;

            if (string.IsNullOrEmpty(BaseName))
            {
                BaseName = null;
            }
            if (string.IsNullOrEmpty(DirName))
            {
                DirName = null;
            }

            if (DirName != null && BaseName != null)
            {
                fullName = DirName + "/" + BaseName;
            }
            else if (BaseName != null)
            {
                fullName = BaseName;
            }
            else if (DirName != null)
            {
                fullName = DirName;
            }
            return fullName;

        }
    }

    public class FileStructComparer : IComparer<FileStruct>, IComparer
    {
        int IComparer.Compare(Object x, Object y)
        {
            return FileList.FileCompare((FileStruct)x, (FileStruct)y);
        }

        public int Compare(FileStruct x, FileStruct y)
        {
            return FileList.FileCompare(x, y);
        }
    }

    public class FileList
    {
        private string _lastDir = String.Empty;
        private string _fileListDir = String.Empty;
        private string _lastName = String.Empty;
        private UInt32 _mode = 0;
        private DateTime _modTime = DateTime.Now;
        private Options _options;
        private CheckSum _checkSum;

        public FileList(Options opt)
        {
            _options = opt;
            _checkSum = new CheckSum(_options);
        }

        public List<FileStruct> SendFileList(ClientInfo clientInfo, string[] argv)
        {
            IoStream ioStream = null;
            if (clientInfo != null)
            {
                ioStream = clientInfo.IoStream;
            }

            string dir, oldDir;
            string lastPath = String.Empty; //@todo_long seems to be Empty all the time
            string fileName = String.Empty;            
            bool useFffd = false; //@todo_long seems to be false all the time
            if (ShowFileListProgress() && ioStream != null)
            {
                StartFileListProgress("building file list");
            }
            Int64 startWrite = Options.Stats.TotalWritten;
            List<FileStruct> fileList = new List<FileStruct>();
            if (ioStream != null)
            {
                ioStream.IoStartBufferingOut();
                if (Options.FilesFromFd != null) //@todo_long seems to be unused because filesFromFD seems to be null all the time
                {
                    if (!string.IsNullOrEmpty(argv[0]) && !Util.PushDir(argv[0]))
                    {
                        WinRsync.Exit("pushDir " + Util.FullFileName(argv[0]) + " failed", clientInfo);
                    }
                    useFffd = true;
                }
            }
            while (true)
            {
                if (useFffd) //@todo_long seems to be unused because useFFFD seems to be false all the time
                {
                    if ((fileName = ioStream.ReadFilesFromLine(Options.FilesFromFd, _options)).Length == 0)
                    {
                        break;
                    }
                }
                else
                {
                    if (argv.Length == 0)
                    {
                        break;
                    }
                    fileName = argv[0];
                    argv = Util.DeleteFirstElement(argv);
                    if (fileName != null && fileName.Equals("."))
                    {
                        continue;
                    }
                    if (fileName != null)
                    {
                        fileName = fileName.Replace(@"\", "/");
                    }
                }
                // TODO: path length
                if (Directory.Exists(fileName) && !_options.Recurse && _options.FilesFrom == null)
                {
                    Log.WriteLine("skipping directory " + fileName);
                    continue;
                }

                dir = null;
                oldDir = String.Empty;

                if (!_options.RelativePaths)
                {
                    int index = fileName.LastIndexOf('/');
                    if (index != -1)
                    {
                        if (index == 0)
                        {
                            dir = "/";
                        }
                        else
                        {
                            dir = fileName.Substring(0, index);
                        }
                        fileName = fileName.Substring(index + 1);
                    }
                }
                else
                {
                    if (ioStream != null && _options.ImpliedDirs && fileName.LastIndexOf('/') > 0)
                    {
                        string fileDir = fileName.Substring(0, fileName.LastIndexOf('/'));
                        string slash = fileName;
                        int i = 0; //@todo_long seems to be 0 all the time
                        while (i < fileDir.Length && i < lastPath.Length && fileDir[i] == lastPath[i]) //@todo_long seems that it is never executed because lastPath is allways Empty
                        {
                            if (fileDir[i] == '/')
                            {
                                slash = fileName.Substring(i);
                            }
                            i++;
                        }
                        if (i != fileName.LastIndexOf('/') || (i < lastPath.Length && lastPath[i] != '/'))//@todo_long seems to be executed unconditionally because i=0 and fileName.LastIndexOf('/') > 0
                        {
                            bool copyLinksSaved = _options.CopyLinks;
                            bool recurseSaved = _options.Recurse;
                            _options.CopyLinks = _options.CopyUnsafeLinks;
                            _options.Recurse = true;
                            int j;
                            while ((j = slash.IndexOf('/')) != -1)
                            {
                                SendFileName(ioStream, fileList, fileName.Substring(0, j), false, 0);
                                slash = slash.Substring(0, j) + ' ' + slash.Substring(j + 1);

                            }
                            _options.CopyLinks = copyLinksSaved;
                            _options.Recurse = recurseSaved;
                            lastPath = fileName.Substring(0, i);
                        }
                    }
                }
                if (!string.IsNullOrEmpty(dir))
                {
                    oldDir = Util.CurrDir;
                    if (!Util.PushDir(dir))
                    {
                        Log.WriteLine("pushDir " + Util.FullFileName(dir) + " failed");
                        continue;
                    }
                    if (_lastDir != null && _lastDir.Equals(dir))
                    {
                        _fileListDir = _lastDir;
                    }
                    else
                    {
                        _fileListDir = _lastDir = dir;
                    }
                }
                SendFileName(ioStream, fileList, fileName, _options.Recurse, Options.XmitTopDir);
                if (!string.IsNullOrEmpty(oldDir))
                {
                    _fileListDir = null;
                    if (Util.PopDir(oldDir))
                    {
                        WinRsync.Exit("pop_dir " + Util.FullFileName(dir) + " failed", clientInfo);
                    }
                }
            }
            if (ioStream != null)
            {
                SendFileEntry(null, ioStream, 0);
                if (ShowFileListProgress())
                {
                    FinishFileListProgress(fileList);
                }
            }
            CleanFileList(fileList, false, false);
            if (ioStream != null)
            {
                ioStream.WriteInt(0);
                Options.Stats.FileListSize = (int)(Options.Stats.TotalWritten - startWrite);
                Options.Stats.NumFiles = fileList.Count;
            }

            if (_options.Verbose > 3)
            {
                OutputFileList(fileList);
            }
            if (_options.Verbose > 2)
            {
                Log.WriteLine("sendFileList done");
            }
            return fileList;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="clientInfo"></param>
        /// <returns></returns>
        public List<FileStruct> ReceiveFileList(ClientInfo clientInfo)
        {
            IoStream ioStream = clientInfo.IoStream;
            List<FileStruct> fileList = new List<FileStruct>();

            if (ShowFileListProgress())
            {
                StartFileListProgress("receiving file list");
            }

            Int64 startRead = Options.Stats.TotalRead;

            UInt32 flags;
            while ((flags = ioStream.ReadByte()) != 0)
            {
                if (_options.ProtocolVersion >= 28 && (flags & Options.XmitExtendedFlags) != 0)
                {
                    flags |= (UInt32)(ioStream.ReadByte() << 8);
                }
                FileStruct file = ReceiveFileEntry(flags, clientInfo);
                if (file == null)
                {
                    continue;
                }
                fileList.Add(file);                
                Options.Stats.TotalSize += (fileList[fileList.Count - 1]).Length;
                EmitFileListProgress(fileList);
                if (_options.Verbose > 2)
                {
                    Log.WriteLine("receiveFileName(" + (fileList[fileList.Count - 1]).GetFullName() + ")");
                }
            }
            ReceiveFileEntry(0, null);

            if (_options.Verbose > 2)
            {
                Log.WriteLine("received " + fileList.Count + " names");
            }

            if (ShowFileListProgress())
            {
                FinishFileListProgress(fileList);
            }

            CleanFileList(fileList, _options.RelativePaths, true);

            if (ioStream != null)
            {
                ioStream.ReadInt();
            }

            if (_options.Verbose > 3)
            {
                OutputFileList(fileList);
            }
            if (_options.ListOnly)
            {
                LogFileList(fileList);
            }
            if (_options.Verbose > 2)
            {
                Log.WriteLine("receiveFileList done");
            }

            Options.Stats.FileListSize = (int)(Options.Stats.TotalRead - startRead);
            Options.Stats.NumFiles = fileList.Count;
            return fileList;
        }

        public static int FileCompare(FileStruct file1, FileStruct file2)
        {
            return UStringCompare(file1.GetFullName(), file2.GetFullName());
        }

        public static int UStringCompare(string s1, string s2)
        {
            int i = 0;
            while (s1.Length > i && s2.Length > i && s1[i] == s2[i])
            {
                i++;
            }

            if ((s2.Length == s1.Length) && (s1.Length == i) && (s2.Length == i))
            {
                return 0;
            }

            if (s1.Length == i)
            {
                return -(int)s2[i];
            }
            if (s2.Length == i)
            {
                return (int)s1[i];
            }
            return (int)s1[i] - (int)s2[i];
        }

        public static int FileListFind(List<FileStruct> fileList, FileStruct file)
        {
            int low = 0, high = fileList.Count - 1;
            while (high >= 0 && (fileList[high]).BaseName == null)
            {
                high--;
            }
            if (high < 0)
            {
                return -1;
            }
            while (low != high)
            {
                int mid = (low + high) / 2;
                int ret = FileCompare(fileList[FileListUp(fileList, mid)], file);
                if (ret == 0)
                {
                    return FileListUp(fileList, mid);
                }
                if (ret > 0)
                {
                    high = mid;
                }
                else
                {
                    low = mid + 1;
                }
            }

            if (FileCompare(fileList[FileListUp(fileList, low)], file) == 0)
            {
                return FileListUp(fileList, low);
            }
            return -1;
        }

        static int FileListUp(List<FileStruct> fileList, int i)
        {
            while ((fileList[i]).BaseName == null)
            {
                i++;
            }
            return i;
        }

        public void OutputFileList(List<FileStruct> fileList)
        {
            string uid = String.Empty, gid = String.Empty;
            for (int i = 0; i < fileList.Count; i++)
            {
                FileStruct file = fileList[i];
                if ((_options.AmRoot || _options.AmSender) && _options.PreserveUid)
                {
                    uid = " uid=" + file.Uid;
                }
                if (_options.PreserveGid && file.Gid != Options.GidNone)
                {
                    gid = " gid=" + file.Gid;
                }
                Log.WriteLine("[" + _options.WhoAmI() + "] i=" + i + " " + Util.Ns(file.BaseDir) + " " +
                    Util.Ns(file.DirName) + " " + Util.Ns(file.BaseName) + " mode=0" + Convert.ToString(file.Mode, 8) +
                    " len=" + file.Length + uid + gid);
            }
        }

        public void SendFileName(IoStream ioStream, List<FileStruct> fileList, string fileName, bool recursive, UInt32 baseFlags)
        {
            FileStruct file = MakeFile(fileName, fileList, ioStream == null && _options.DeleteExcluded ? Options.ServerExcludes : Options.AllExcludes);
            if (file == null)
            {
                return;
            }
            EmitFileListProgress(fileList);
            if (!string.IsNullOrEmpty(file.BaseName))
            {
                fileList.Add(file);
                SendFileEntry(file, ioStream, baseFlags);

                if (recursive && Util.S_ISDIR(file.Mode) && (file.Flags & Options.FlagMountPoint) == 0)
                {
                    _options.LocalExcludeList.Clear();
                    SendDirectory(ioStream, fileList, file.GetFullName());
                }
            }
        }

        /// <summary>
        /// Generates a FileStruct filled with all info
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="fileList"></param>
        /// <param name="excludeLevel"></param>
        /// <returns></returns>
        public FileStruct MakeFile(string fileName, List<FileStruct> fileList, int excludeLevel)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return null;
            }
            string thisName = Util.CleanFileName(fileName, false);
            if (_options.SanitizePath) //@todo_long It is useless for this moment
            {
                thisName = Util.SanitizePath(thisName, String.Empty, 0);
            }
            FileStruct fileStruct = new FileStruct();
            // TODO: path length
            if (Directory.Exists(thisName))
            {
                if (thisName.LastIndexOf('/') != -1)
                {
                    thisName = thisName.TrimEnd('/');
                    fileStruct.DirName = thisName.Substring(0, thisName.LastIndexOf('/')).Replace(@"\", "/");
                    fileStruct.BaseName = thisName.Substring(thisName.LastIndexOf('/') + 1);
                    fileStruct.Gid = 0;
                    fileStruct.Uid = 0;
                    fileStruct.Mode = 0x4000 | 0x16B;
                    // TODO: path length
                    DirectoryInfo di = new DirectoryInfo(thisName);
                    if ((di.Attributes & FileAttributes.ReadOnly) != FileAttributes.ReadOnly)
                    {
                        fileStruct.Mode |= 0x92;
                    }
                }

            }
            // TODO: path length
            if (File.Exists(thisName))
            {

                if (excludeLevel != Options.NoExcludes && CheckExcludeFile(thisName, 0, excludeLevel))
                {
                    return null;
                }
                fileStruct.BaseName = Path.GetFileName(thisName);
                fileStruct.DirName = Path.GetDirectoryName(thisName).Replace(@"\", "/").TrimEnd('/');
                FileInfo fi = new FileInfo(thisName);

                // TODO: path length
                fileStruct.Length = (int)fi.Length;
                // TODO: path length
                fileStruct.ModTime = fi.LastWriteTime;
                fileStruct.Mode = 0x8000 | 0x1A4;
                // TODO: path length
                if ((File.GetAttributes(thisName) & FileAttributes.ReadOnly) != FileAttributes.ReadOnly)
                {
                    fileStruct.Mode |= 0x92;
                }
                fileStruct.Gid = 0;
                fileStruct.Uid = 0;

                int sumLen = _options.AlwaysChecksum ? CheckSum.Md4SumLength : 0;
                if (sumLen != 0)
                    if (!_checkSum.FileCheckSum(thisName, ref fileStruct.Sum, fileStruct.Length))
                    {
                        Log.Write("Skipping file " + thisName);
                        return null;
                    }

                Options.Stats.TotalSize += fileStruct.Length;

            }
            fileStruct.BaseDir = _fileListDir;
            return fileStruct;
        }

        public FileStruct ReceiveFileEntry(UInt32 flags, ClientInfo clientInfo)
        {
            if (clientInfo == null)
            {
                _lastName = String.Empty;
                return null;
            }
            IoStream f = clientInfo.IoStream;

            int l1 = 0, l2 = 0;

            if ((flags & Options.XmitSameName) != 0)
            {
                l1 = f.ReadByte();
            }

            if ((flags & Options.XmitLongName) != 0)
            {
                l2 = f.ReadInt();
            }
            else
            {
                l2 = f.ReadByte();
            }
            if (l2 >= Options.Maxpathlen - l1)
            {
                WinRsync.Exit("overflow: lastname=" + _lastName, clientInfo);
            }

            string thisName = _lastName.Substring(0, l1);
            thisName += f.ReadStringFromBuffer(l2);
            _lastName = thisName;

            thisName = Util.CleanFileName(thisName, false);
            if (_options.SanitizePath)
            {
                thisName = Util.SanitizePath(thisName, String.Empty, 0);
            }

            string baseName = String.Empty;
            string dirName = String.Empty;
            if (thisName.LastIndexOf("/") != -1)
            {
                baseName = Path.GetFileName(thisName);
                dirName = Path.GetDirectoryName(thisName);
            }
            else
            {
                baseName = thisName;
                dirName = null;
            }

            Int64 fileLength = f.ReadLongInt();

            if ((flags & Options.XmitSameTime) == 0)
            {
                _modTime = DateTime.FromFileTime(f.ReadInt());
            }
            if ((flags & Options.XmitSameMode) == 0)
            {
                _mode = (UInt32)f.ReadInt();
            }

            if (_options.PreserveUid && (flags & Options.XmitSameUid) == 0)
            {
                int uid = f.ReadInt();
            }
            if (_options.PreserveGid && (flags & Options.XmitSameGid) == 0)
            {
                int gid = f.ReadInt();
            }

            byte[] sum = new byte[0];
            if (_options.AlwaysChecksum && !Util.S_ISDIR(_mode))
            {
                sum = new byte[CheckSum.Md4SumLength];
                sum = f.ReadBuffer(_options.ProtocolVersion < 21 ? 2 : CheckSum.Md4SumLength);
            }

            FileStruct fs = new FileStruct();
            fs.Length = (int)fileLength;
            fs.BaseName = baseName;
            fs.DirName = dirName;
            fs.Sum = sum;
            fs.Mode = _mode;
            fs.ModTime = _modTime;
            fs.Flags = flags;
            return fs;
        }

        public void SendFileEntry(FileStruct file, IoStream ioStream, UInt32 baseflags)
        {
            UInt32 flags = baseflags;
            int l1 = 0, l2 = 0;

            if (ioStream == null)
            {
                return;
            }
            if (file == null)
            {
                ioStream.WriteByte(0);
                _lastName = String.Empty;
                return;
            }
            string fileName = file.GetFullName().Replace(":", String.Empty);
            for (l1 = 0;
                _lastName.Length > l1 && (fileName[l1] == _lastName[l1]) && (l1 < 255);
                l1++)
            {

            }
            l2 = fileName.Substring(l1).Length;

            flags |= Options.XmitSameName;

            if (l2 > 255)
            {
                flags |= Options.XmitLongName;
            }
            if (_options.ProtocolVersion >= 28)
            {
                if (flags == 0 && !Util.S_ISDIR(file.Mode))
                {
                    flags |= Options.XmitTopDir;
                }
                /*if ((flags & 0xFF00) > 0 || flags == 0) 
                {
                    flags |= Options.XMIT_EXTENDED_FLAGS;
                    f.writeByte((byte)flags);
                    f.writeByte((byte)(flags >> 8));
                } 
                else					*/
                ioStream.WriteByte((byte)flags);
            }
            else
            {
                if ((flags & 0xFF) == 0 && !Util.S_ISDIR(file.Mode))
                {
                    flags |= Options.XmitTopDir;
                }
                if ((flags & 0xFF) == 0)
                {
                    flags |= Options.XmitLongName;
                }
                ioStream.WriteByte((byte)flags);
            }
            if ((flags & Options.XmitSameName) != 0)
            {
                ioStream.WriteByte((byte)l1);
            }
            if ((flags & Options.XmitLongName) != 0)
            {
                ioStream.WriteInt(l2);
            }
            else
            {
                ioStream.WriteByte((byte)l2);
            }


            byte[] b = System.Text.Encoding.ASCII.GetBytes(fileName);

            ioStream.Write(b, l1, l2);
            ioStream.WriteLongInt(file.Length);


            if ((flags & Options.XmitSameTime) == 0)
            {
                ioStream.WriteInt(file.ModTime.Second);
            }
            if ((flags & Options.XmitSameMode) == 0)
            {
                ioStream.WriteInt((int)file.Mode);
            }
            if (_options.PreserveUid && (flags & Options.XmitSameUid) == 0)
            {
                ioStream.WriteInt(file.Uid);
            }
            if (_options.PreserveGid && (flags & Options.XmitSameGid) == 0)
            {
                ioStream.WriteInt(file.Gid);
            }
            if (_options.AlwaysChecksum)
            {
                byte[] sum;
                if (!Util.S_ISDIR(file.Mode))
                {
                    sum = file.Sum;
                }
                else if (_options.ProtocolVersion < 28)
                {
                    sum = new byte[16];
                }
                else
                {
                    sum = null;
                }

                if (sum != null)
                {
                    ioStream.Write(sum, 0, _options.ProtocolVersion < 21 ? 2 : CheckSum.Md4SumLength);
                }

            }

            _lastName = fileName;
        }

        public void SendDirectory(IoStream ioStream, List<FileStruct> fileList, string dir)
        {
            DirectoryInfo directoryInfo = new DirectoryInfo(dir);
            if (directoryInfo.Exists)
            {
                if (_options.CvsExclude)
                {
                    Exclude excl = new Exclude(_options);
                    excl.AddExcludeFile(ref _options.LocalExcludeList, dir, (int)(Options.XflgWordSplit & Options.XflgWordsOnly)); //@todo (int)(Options.XFLG_WORD_SPLIT & Options.XFLG_WORDS_ONLY) evaluates to 0 unconditionally. May be change & with | ?
                }
                FileInfo[] files = directoryInfo.GetFiles();
                for (int i = 0; i < files.Length; i++)
                {
                    // TODO: path length
                    SendFileName(ioStream, fileList, files[i].FullName.Replace(@"\", "/"), _options.Recurse, 0);
                }
                DirectoryInfo[] dirs = directoryInfo.GetDirectories();
                for (int i = 0; i < dirs.Length; i++)
                {
                    // TODO: path length
                    SendFileName(ioStream, fileList, dirs[i].FullName.Replace(@"\", "/"), _options.Recurse, 0);
                }
            }
            else
            {
                Log.WriteLine("Can't find directory '" + Util.FullFileName(dir) + "'");
                return;
            }
        }

        public void CleanFileList(List<FileStruct> fileList, bool stripRoot, bool noDups)
        {
            if (fileList == null || fileList.Count == 0)
            {
                return;
            }
            fileList.Sort(new FileStructComparer());
            for (int i = 0; i < fileList.Count; i++)
            {
                if (fileList[i] == null)
                {
                    fileList.RemoveAt(i);
                }
            }
            if (stripRoot)
            {
                for (int i = 0; i < fileList.Count; i++)
                {
                    if ((fileList[i]).DirName != null && (fileList[i]).DirName[0] == '/')
                    {
                        (fileList[i]).DirName = (fileList[i]).DirName.Substring(1);
                    }
                    if ((fileList[i]).DirName != null && (fileList[i]).DirName.CompareTo(String.Empty) == 0)
                    {
                        (fileList[i]).DirName = null;
                    }
                }

            }
            return;
        }

        private bool ShowFileListProgress()
        {
            return (_options.Verbose != 0) && (_options.Recurse || _options.FilesFrom != null) && !_options.AmServer;
        }

        private void StartFileListProgress(string kind)
        {
            Log.Write(kind + " ...");
            if (_options.Verbose > 1 || _options.DoProgress)
            {
                Log.WriteLine(String.Empty);
            }
        }

        private void FinishFileListProgress(List<FileStruct> fileList)
        {
            if (_options.DoProgress)
            {
                Log.WriteLine(fileList.Count.ToString() + " file" + (fileList.Count == 1 ? " " : "s ") + "to consider");
            }
            else
            {
                Log.WriteLine("Done.");
            }
        }

        private void EmitFileListProgress(List<FileStruct> fileList)
        {
            if (_options.DoProgress && ShowFileListProgress() && (fileList.Count % 100) == 0)
            {
                //EmitFileListProgress(fileList);
                Log.WriteLine(" " + fileList.Count + " files...");
            }
        }

        //private void EmitFileListProgress(List<FileStruct> fileList) //removed
        //{
        //    Log.WriteLine(" " + fileList.Count + " files...");
        //}

        //private void listFileEntry(FileStruct fileEntry)
        //{
        //    if (fileEntry.baseName == null || fileEntry.baseName.CompareTo(String.Empty) == 0)
        //    {
        //        return;
        //    }
        //    string perms = String.Empty;
        //    Log.WriteLine(perms + " " + fileEntry.length + " " + fileEntry.modTime.ToString() + " " + fileEntry.FNameTo());
        //}

        /// <summary>
        /// Write short info about files to log
        /// </summary>
        /// <param name="fileList"></param>
        private void LogFileList(List<FileStruct> fileList)
        {
            for (int i = 0; i < fileList.Count; i++)
            {
                FileStruct file = fileList[i];
                if (string.IsNullOrEmpty(file.BaseName))
                {
                    continue;
                }
                Log.WriteLine(" " + file.Length + " " + file.ModTime.ToString() + " " + file.GetFullName());
            }            
        }

        /*
         * This function is used to check if a file should be included/excluded
         * from the list of files based on its name and type etc.  The value of
         * exclude_level is set to either SERVER_EXCLUDES or ALL_EXCLUDES.
         */
        private bool CheckExcludeFile(string fileName, int isDir, int excludeLevel)
        {
            int rc;

            if (excludeLevel == Options.NoExcludes)
            {
                return false;
            }
            if (fileName.CompareTo(String.Empty) != 0)
            {
                /* never exclude '.', even if somebody does --exclude '*' */
                if (fileName[0] == '.' && fileName.Length == 1)
                {
                    return false;
                }
                /* Handle the -R version of the '.' dir. */
                if (fileName[0] == '/')
                {
                    int len = fileName.Length;
                    if (fileName[len - 1] == '.' && fileName[len - 2] == '/')
                    {
                        return true;
                    }
                }
            }
            if (excludeLevel != Options.AllExcludes)
            {
                return false;
            }
            Exclude excl = new Exclude(_options);
            if (_options.ExcludeList.Count > 0
                && (rc = excl.CheckExclude(_options.ExcludeList, fileName, isDir)) != 0)
            {
                return (rc < 0) ? true : false;
            }
            return false;
        }
    }
}
