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
    public class Receiver
    {
        private Options _options;
        private CheckSum _checkSum;

        public Receiver(Options opt)
        {
            _options = opt;
            _checkSum = new CheckSum(_options);
        }

        private string LocalizePath(ClientInfo cInfo, string path)
        {
            var normalized = cInfo.Options.Dir.Replace('\\', '/').Replace(":", String.Empty).ToLower();
            var ret = String.Empty;
            if (path.ToLower().IndexOf(normalized) != -1)
            {
                ret = path.Substring(path.ToLower().IndexOf(normalized) + normalized.Length).Replace('/', Path.DirectorySeparatorChar);
            }

            if (ret == String.Empty)
            {
                return path.TrimEnd('\\');
            }
            if (ret[0] == Path.DirectorySeparatorChar)
            {
                ret = ret.Substring(1);
            }

            return ret;
        }

        public int ReceiveFiles(ClientInfo clientInfo, List<FileStruct> fileList, string localName)
        {
            var st = new FStat();
            FileStruct file;
            var ioStream = clientInfo.IoStream;

            string fileName;
            string fNameCmp = String.Empty, fNameTmp = String.Empty;
            var saveMakeBackups = _options.MakeBackups;
            int i, phase = 0;
            bool recvOk;

            if (_options.Verbose > 2)
            {
                Log.WriteLine("ReceiveFiles(" + fileList.Count + ") starting");
            }
            while (true)
            {
                i = ioStream.ReadInt();
                if (i == -1)
                {
                    if (phase != 0)
                    {
                        break;
                    }

                    phase = 1;
                    _checkSum.Length = CheckSum.SumLength;
                    if (_options.Verbose > 2)
                    {
                        Log.WriteLine("ReceiveFiles phase=" + phase);
                    }
                    ioStream.WriteInt(0); //send_msg DONE
                    if (_options.KeepPartial)
                    {
                        _options.MakeBackups = false;
                    }
                    continue;
                }

                if (i < 0 || i >= fileList.Count)
                {
                    WinRsync.Exit("Invalid file index " + i + " in receiveFiles (count=" + fileList.Count + ")", clientInfo);
                }

                file = (fileList[i]);

                Options.Stats.CurrentFileIndex = i;
                Options.Stats.NumTransferredFiles++;
                Options.Stats.TotalTransferredSize += file.Length;

                if (!localName.IsBlank())
                {
                    fileName = localName;
                }
                else
                {
                    fileName = Path.Combine(_options.Dir, LocalizePath(clientInfo, file.GetFullName().Replace(":", String.Empty)).Replace("\\", "/"));
                    //fileName = Path.Combine(options.dir, file.FNameTo().Replace(":",String.Empty)).Replace("\\", "/");
                    // TODO: path length
                    Directory.CreateDirectory(Path.Combine(_options.Dir, LocalizePath(clientInfo, file.DirName.Replace(":", String.Empty))).Replace("\\", "/"));
                    Log.WriteLine(Path.Combine(_options.Dir, file.DirName));
                    //FileSystem.Directory.CreateDirectory(Path.Combine(options.dir,file.dirName.Replace(":",String.Empty)).Replace("\\", "/"));
                }

                if (_options.DryRun)
                {
                    if (!_options.AmServer && _options.Verbose > 0)
                    {
                        Log.WriteLine(fileName);
                    }
                    continue;
                }

                if (_options.Verbose > 2)
                {
                    Log.WriteLine("receiveFiles(" + fileName + ")");
                }

                if (_options.PartialDir != null && _options.PartialDir.CompareTo(String.Empty) != 0)
                {
                }
                else
                {
                    fNameCmp = fileName;
                }

                FileStream fd1 = null;
                try
                {
                    fd1 = new FileStream(fNameCmp, FileMode.Open, FileAccess.Read);
                }
                catch (FileNotFoundException)
                {
                    fNameCmp = fileName;
                    try
                    {
                        fd1 = new FileStream(fNameCmp, FileMode.Open, FileAccess.Read);
                    }
                    catch (FileNotFoundException)
                    {
                    }
                }
                catch (Exception e)
                {
                    Log.Write(e.Message);
                }
                try
                {
                    var fi = new FileInfo(fNameCmp);
                    // TODO: path length
                    st.Size = fi.Length;
                }
                catch { }

                var tempFileName = GetTmpName(fileName);
                FileStream fd2 = null;
                fd2 = new FileStream(tempFileName, FileMode.OpenOrCreate, FileAccess.Write);

                if (!_options.AmServer && _options.Verbose > 0)
                {
                    Log.WriteLine(fileName);
                }

                /* recv file data */
                recvOk = ReceiveData(clientInfo, fNameCmp, fd1, st.Size,
                            fileName, fd2, file.Length);

                if (fd1 != null)
                {
                    fd1.Close();
                }
                if (fd2 != null)
                {
                    fd2.Close();
                }
                // TODO: path length
                File.Copy(tempFileName, fileName, true);
                // TODO: path length
                File.Delete(tempFileName);
                if (recvOk || _options.Inplace)
                {
                    FinishTransfer(fileName, fNameTmp, file, recvOk);
                }
            }
            _options.MakeBackups = saveMakeBackups;

            if (_options.DeleteAfter && _options.Recurse && localName == null && fileList.Count > 0)
            {
                DeleteFiles(fileList);
            }

            if (_options.Verbose > 2)
            {
                Log.WriteLine("ReceiveFiles finished");
            }

            return 0;
        }

        public bool ReceiveData(ClientInfo clientInfo, string fileNameR, Stream fdR, long sizeR, string fileName, Stream fd, int totalSize)
        {
            var f = clientInfo.IoStream;
            var fileSum1 = new byte[CheckSum.Md4SumLength];
            var fileSum2 = new byte[CheckSum.Md4SumLength];
            var data = new byte[Match.ChunkSize];
            var sumStruct = new SumStruct();
            MapFile mapBuf = null;
            var sender = new Sender(_options);
            sender.ReadSumHead(clientInfo, ref sumStruct);
            var offset = 0;
            UInt32 len;

            if (fdR != null && sizeR > 0)
            {
                var mapSize = (int)Math.Max(sumStruct.BLength * 2, 16 * 1024);
                mapBuf = new MapFile(fdR, (int)sizeR, mapSize, (int)sumStruct.BLength);
                if (_options.Verbose > 2)
                {
                    Log.WriteLine("recv mapped " + fileNameR + " of size " + sizeR);
                }
            }
            var sum = new Sum(_options);
            sum.Init(_options.ChecksumSeed);

            int i;
            var token = new Token(_options);
            while ((i = token.ReceiveToken(f, ref data, 0)) != 0)
            {
                if (_options.DoProgress)
                {
                    Progress.ShowProgress(offset, totalSize);
                }

                if (i > 0)
                {
                    if (_options.Verbose > 3)
                    {
                        Log.WriteLine("data recv " + i + " at " + offset);
                    }
                    Options.Stats.LiteralData += i;
                    sum.Update(data, 0, i);
                    if (fd != null && FileIo.WriteFile(fd, data, 0, i) != i)
                    {
                        goto report_write_error;
                    }
                    offset += i;
                    continue;
                }

                i = -(i + 1);
                var offset2 = (int)(i * sumStruct.BLength);
                len = sumStruct.BLength;
                if (i == sumStruct.Count - 1 && sumStruct.Remainder != 0)
                {
                    len = sumStruct.Remainder;
                }

                Options.Stats.MatchedData += len;

                if (_options.Verbose > 3)
                {
                    Log.WriteLine("chunk[" + i + "] of size " + len + " at " + offset2 + " offset=" + offset);
                }

                byte[] map = null;
                var off = 0;
                if (mapBuf != null)
                {
                    off = mapBuf.MapPtr(offset2, (int)len);
                    map = mapBuf.P;

                    token.SeeToken(map, offset, (int)len);
                    sum.Update(map, off, (int)len);
                }

                if (_options.Inplace)
                {
                    if (offset == offset2 && fd != null)
                    {
                        offset += (int)len;
                        if (fd.Seek(len, SeekOrigin.Current) != offset)
                        {
                            WinRsync.Exit("seek failed on " + Util.FullFileName(fileName), clientInfo);
                        }
                        continue;
                    }
                }
                if (fd != null && FileIo.WriteFile(fd, map, off, (int)len) != (int)len)
                {
                    goto report_write_error;
                }
                offset += (int)len;
            }

            if (_options.DoProgress)
            {
                Progress.EndProgress(totalSize);
            }
            if (fd != null && offset > 0 && FileIo.SparseEnd(fd) != 0)
            {
                WinRsync.Exit("write failed on " + Util.FullFileName(fileName), clientInfo);
            }

            fileSum1 = sum.End();

            if (mapBuf != null)
            {
                mapBuf = null;
            }

            fileSum2 = f.ReadBuffer(CheckSum.Md4SumLength);
            if (_options.Verbose > 2)
            {
                Log.WriteLine("got fileSum");
            }
            if (fd != null && Util.MemoryCompare(fileSum1, 0, fileSum2, 0, CheckSum.Md4SumLength) != 0)
            {
                return false;
            }
            return true;
        report_write_error:
            {
                WinRsync.Exit("write failed on " + Util.FullFileName(fileName), clientInfo);
            }
            return true;
        }

        public static void FinishTransfer(string fileName, string fileNameTmp, FileStruct file, bool okToSetTime)
        {
        }

        public void DeleteOne(string fileName, bool isDir)
        {
            var sc = new SysCall(_options);
            if (!isDir)
            {

                if (!sc.RobustUnlink(fileName))
                {
                    Log.WriteLine("Can't delete '" + fileName + "' file");
                }
                else
                {
                    if (_options.Verbose > 0)
                    {
                        Log.WriteLine("deleting file " + fileName);
                    }
                }
            }
            else
            {
                if (!sc.DoRmDir(fileName))
                {
                    Log.WriteLine("Can't delete '" + fileName + "' dir");
                }
                else
                {
                    if (_options.Verbose > 0)
                    {
                        Log.WriteLine("deleting directory " + fileName);
                    }
                }
            }
        }

        public bool IsBackupFile(string fileName)
        {
            return fileName.EndsWith(_options.BackupSuffix);
        }

        public string GetTmpName(string fileName)
        {
            return Path.GetTempFileName();
        }

        public void DeleteFiles(List<FileStruct> fileList)
        {
            var argv = new string[1];
            List<FileStruct> localFileList = null;
            if (_options.CvsExclude)
            {
                Exclude.AddCvsExcludes();
            }
            for (var j = 0; j < fileList.Count; j++)
            {
                if ((fileList[j].Mode & Options.FlagTopDir) == 0 || !Util.S_ISDIR(fileList[j].Mode))
                {
                    continue;
                }
                argv[0] = _options.Dir + fileList[j].GetFullName();
                var fList = new FileList(_options);
                if ((localFileList = fList.SendFileList(null, argv)) == null)
                {
                    continue;
                }
                for (var i = localFileList.Count - 1; i >= 0; i--)
                {
                    if (localFileList[i].BaseName == null)
                    {
                        continue;
                    }
                    localFileList[i].DirName = localFileList[i].DirName.Substring(_options.Dir.Length);
                    if (FileList.FileListFind(fileList, (localFileList[i])) < 0)
                    {
                        localFileList[i].DirName = _options.Dir + localFileList[i].DirName;
                        DeleteOne(localFileList[i].GetFullName(), Util.S_ISDIR(localFileList[i].Mode));
                    }
                }
            }
        }
    }



    public class SysCall
    {

        private Options _options;

        public SysCall(Options opt)
        {
            _options = opt;
        }

        public bool DoRmDir(string pathName)
        {
            if (_options.DryRun)
            {
                return true;
            }
            if (_options.ReadOnly || _options.ListOnly)
            {
                return false;
            }
            try
            {
                // TODO: path length
                Directory.Delete(pathName);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool RobustUnlink(string fileName)
        {
            return DoUnlink(fileName);
        }

        public bool DoUnlink(string fileName)
        {
            if (_options.DryRun)
            {
                return true;
            }
            if (_options.ReadOnly || _options.ListOnly)
            {
                return false;
            }
            try
            {
                // TODO: path length
                File.Delete(fileName);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
