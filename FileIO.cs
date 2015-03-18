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
using System.IO;

namespace NetSync
{

    public class FileIo
    {
        static byte _lastByte;
        static int _lastSparse;

        public static int SparseEnd(Stream f)
        {
            if (_lastSparse != 0)
            {
                f.Seek(-1, SeekOrigin.Current);
                f.WriteByte(_lastByte);
                return 0;
            }
            _lastSparse = 0;
            return 0;
        }


        public static int WriteSparse(Stream f, byte[] buf, int len)
        {
            int l1 = 0, l2 = 0;
            int ret;

            for (l1 = 0; l1 < len && buf[l1] == 0; l1++) { }
            for (l2 = 0; l2 < len - l1 && buf[len - (l2 + 1)] == 0; l2++) { }

            _lastByte = buf[len - 1];

            if (l1 == len || l2 > 0)
            {
                _lastSparse = 1;
            }

            if (l1 > 0)
            {
                f.Seek(l1, SeekOrigin.Current);
            }


            if (l1 == len)
            {
                return len;
            }

            f.Write(buf, l1, len - (l1 + l2));
            ret = len - (l1 + l2);
            if (ret == -1 || ret == 0)
            {
                return ret;
            }
            else if (ret != (len - (l1 + l2)))
            {
                return (l1 + ret);
            }

            if (l2 > 0)
            {
                f.Seek(l2, SeekOrigin.Current);
            }

            return len;
        }
        public static int WriteFile(Stream f, byte[] buf, int off, int len)
        {
            f.Write(buf, off, len);
            return len;
        }

    }

    public class MapFile
    {
        /// <summary>
        /// Window pointer
        /// </summary>
        public byte[] P = null;
        /// <summary>
        /// File Descriptor
        /// </summary>
        Stream _fileDescriptor;
        /// <summary>
        /// Largest window size we allocated
        /// </summary>
        int _pSize;
        /// <summary>
        /// Latest (rounded) window size
        /// </summary>
        int _pLength;
        /// <summary>
        /// Default window size
        /// </summary>
        int _defaultWindowSize;
        /// <summary>
        /// First errno from read errors (Seems to be false all the time)
        /// </summary>
        public bool Status = false;
        /// <summary>
        /// File size (from stat)
        /// </summary>
        public int FileSize;
        /// <summary>
        /// Window start
        /// </summary>
        int _pOffset;
        /// <summary>
        /// Offset of cursor in file descriptor ala lseek
        /// </summary>
        int _pFileDescriptorOffset;

        /// <summary>
        /// Initialyze new instance
        /// </summary>
        /// <param name="fileDescriptor"></param>
        /// <param name="length"></param>
        /// <param name="mapSize"></param>
        /// <param name="blockSize"></param>
        public MapFile(Stream fileDescriptor, int length, int mapSize, int blockSize)
        {
            if (blockSize != 0 && (mapSize % blockSize) != 0)
            {
                mapSize += blockSize - (mapSize % blockSize);
            }
            this._fileDescriptor = fileDescriptor;
            this.FileSize = length;
            this._defaultWindowSize = mapSize;

        }

        /// <summary>
        /// Returns offset in p array
        /// </summary>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public int MapPtr(int offset, int length)
        {
            int numberOfReadBytes;
            int windowStart, readStart;
            int windowSize, readSize, readOffset;

            if (length == 0)
            {
                return -1;
            }

            if (length > (this.FileSize - offset))
            {
                length = this.FileSize - offset;
            }

            if (offset >= this._pOffset && offset + length <= this._pOffset + this._pLength)
            {
                return offset - this._pOffset;
            }

            windowStart = offset;
            windowSize = this._defaultWindowSize;
            if (windowStart + windowSize > this.FileSize)
            {
                windowSize = this.FileSize - windowStart;
            }
            if (offset + length > windowStart + windowSize)
            {
                windowSize = (offset + length) - windowStart;
            }

            if (windowSize > this._pSize)
            {
                ExtendArray(ref P, windowSize);
                this._pSize = windowSize;
            }

            if (windowStart >= this._pOffset &&
                windowStart < this._pOffset + this._pLength &&
                windowStart + windowSize >= this._pOffset + this._pLength)
            {
                readStart = this._pOffset + this._pLength;
                readOffset = readStart - windowStart;
                readSize = windowSize - readOffset;
                MemoryMove(ref this.P, this.P, (this._pLength - readOffset), readOffset);
            }
            else
            {
                readStart = windowStart;
                readSize = windowSize;
                readOffset = 0;
            }
            if (readSize <= 0)
            {
                Log.WriteLine("Warning: unexpected read size of " + readSize + " in MapPtr");
            }
            else
            {
                if (this._pFileDescriptorOffset != readStart)
                {
                    if (this._fileDescriptor.Seek(readStart, SeekOrigin.Begin) != readStart)
                    {
                        WinRsync.Exit("Seek failed in MapPtr", null);
                    }
                    this._pFileDescriptorOffset = readStart;
                }

                if ((numberOfReadBytes = _fileDescriptor.Read(this.P, readOffset, readSize)) != readSize)
                {
                    //if (numberOfReadBytes < 0) //@fixed Read never returns <0 so status is false all the time
                    //{
                    //    numberOfReadBytes = 0;
                    //    status = true;
                    //}
                    FillMemory(ref this.P, readOffset + numberOfReadBytes, 0, readSize - numberOfReadBytes);
                }
                this._pFileDescriptorOffset += numberOfReadBytes;
            }

            this._pOffset = windowStart;
            this._pLength = windowSize;
            return offset - this._pOffset;
        }

        /// <summary>
        /// Returns status
        /// </summary>
        /// <returns></returns>
        public bool UnMapFile()
        {
            return this.Status;
        }

        /// <summary>
        /// Fills 'data' with given 'value'
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="value"></param>
        /// <param name="length"></param>
        private void FillMemory(ref byte[] data, int offset, byte value, int length)
        {
            for (int i = 0; i < length; i++)
            {
                data[offset + i] = value;
            }
        }

        /// <summary>
        /// Moves 'length' bytes in array. So pass the same array as source and destination
        /// </summary>
        /// <param name="destination">Destination array</param>
        /// <param name="source">Source array</param>
        /// <param name="sourceOffset">Start taking bytes from this offset</param>
        /// <param name="length">Number of bytes to move</param>
        private void MemoryMove(ref byte[] destination, byte[] source, int sourceOffset, int length) //it seems that ref is't needed
        {
            byte[] sourceCopy = (byte[])source.Clone();
            for (int i = 0; i < length; i++)
            {
                destination[i] = sourceCopy[sourceOffset + i];
            }
        }

        /// <summary>
        /// Extends array to new [bigger] 'size'
        /// </summary>
        /// <param name="array"></param>
        /// <param name="size"></param>
        public static void ExtendArray(ref byte[] array, int size)
        {
            if (array == null)
            {
                array = new byte[size];
            }
            else
            {
                byte[] tempArray = new byte[array.Length];
                array.CopyTo(tempArray, 0);
                array = new byte[size];
                tempArray.CopyTo(array, 0);
            }
        }

        /// <summary>
        /// Extends array to new [bigger] 'size'
        /// </summary>
        /// <param name="array"></param>
        /// <param name="size"></param>
        public static void ExtendArray(ref string[] array, int size)
        {
            if (array == null)
            {
                array = new string[size];
            }
            else
            {
                string[] tempArray = new string[array.Length];
                array.CopyTo(tempArray, 0);
                array = new string[size];
                tempArray.CopyTo(array, 0);
            }
        }
    }
}
