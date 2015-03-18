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
using System.Text;
using System.Threading;

namespace NetSync
{
    public enum MsgCode
    {
        MsgDone = 5,	/* current phase is done */
        MsgRedo = 4,	/* reprocess indicated flist index */
        MsgError = 1, MsgInfo = 2, MsgLog = 3, /* remote logging */
        MsgData = 0	/* raw data on the multiplexed stream */
    };

    /// <summary>
    /// 
    /// </summary>
    public class IoStream
    {

        private Stream _socketOut;
        private Stream _socketIn;

        private bool _ioMultiplexingIn = false;
        private bool _ioMultiplexingOut = false;
        /// <summary>
        /// 7
        /// </summary>
        private const int MplexBase = 7;
        /// <summary>
        /// 4096
        /// </summary>
        private const int IoBufferSize = 4096;
        private int _ioBufInSize = 0;
        private int _remaining = 0;
        private int _ioBufInIndex = 0;
        private byte[] _ioBufIn = null;
        private byte[] _ioBufOut = null;
        private int _ioBufOutCount = 0;
        /// <summary>
        /// False
        /// </summary>
        private bool _noFlush = false;
        //private ASCIIEncoding asen = new ASCIIEncoding();

        public Thread ClientThread = null;

        /// <summary>
        /// Initializes new instance
        /// </summary>
        /// <param name="stream"></param>
        public IoStream(Stream stream)
        {
            IoSetSocketFields(stream, stream);
            _ioBufInSize = 2 * IoBufferSize;
        }

        /// <summary>
        /// Increments total written by given value
        /// </summary>
        /// <param name="length"></param>
        private void CalculateTotalWritten(int length) //@fixed change to private
        {
            Options.Stats.TotalWritten += length;
        }

        /// <summary>
        /// Increments total read by given value
        /// </summary>
        /// <param name="length"></param>
        private void CalculateTotalRead(int length) //@fixed change to private
        {
            Options.Stats.TotalRead += length;
        }

        /// <summary>
        /// Converts 4 bytes from 'buf' to UInt32 [in reverse order]
        /// </summary>
        /// <param name="buf"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        private UInt32 Ival(byte[] buf, int offset) //@fixed change to private
        {
            return (UInt32)(buf[offset] + (buf[offset + 1] << 8) + (buf[offset + 2] << 16) + (buf[offset + 3] << 24));
        }

        /// <summary>
        /// Write buffer to socket
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        public void Write(byte[] buffer, int offset, int length)
        {
            try
            {
                if (_ioBufOut == null)
                {
                    _socketOut.Write(buffer, offset, length);
                    return;
                }

                int localOffset = 0;
                while (length > 0)
                {
                    int n = Math.Min(length, IoBufferSize - _ioBufOutCount);
                    if (n > 0)
                    {
                        Util.MemoryCopy(_ioBufOut, _ioBufOutCount, buffer, offset + localOffset, n);
                        localOffset += n;
                        length -= n;
                        _ioBufOutCount += n;
                    }

                    if (_ioBufOutCount == IoBufferSize)
                    {
                        Flush();
                    }
                }
            }
            catch (Exception e)
            {
                Log.Write(e.Message);
            }
            CalculateTotalWritten(length);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="code"></param>
        /// <param name="buffer"></param>
        /// <param name="count"></param>
        public void MultiplexWrite(MsgCode code, byte[] buffer, int count)
        {
            byte[] localBuffer = new byte[4096];
            int n = count;

            CheckSum.Sival(ref localBuffer, 0, (UInt32)(((MplexBase + (int)code) << 24) + count));

            if (n > localBuffer.Length - 4)
            {
                n = localBuffer.Length - 4;
            }

            Util.MemoryCopy(localBuffer, 4, buffer, 0, n);
            _socketOut.Write(localBuffer, 0, n + 4);

            count -= n;
            if (count > 0)
            {
                _socketOut.Write(buffer, n, count);
            }
        }

        /// <summary>
        /// Writes all data from buffer
        /// </summary>
        public void Flush()
        {
            if (_ioBufOutCount == 0 || _noFlush)
            {
                return;
            }

            if (_ioMultiplexingOut)
            {
                MultiplexWrite(MsgCode.MsgData, _ioBufOut, _ioBufOutCount);
            }
            else
            {
                _socketOut.Write(_ioBufOut, 0, _ioBufOutCount);
            }
            _ioBufOutCount = 0;
        }

        /// <summary>
        /// Writes int value to out buffer
        /// </summary>
        /// <param name="x"></param>
        public void WriteInt(int x)
        {
            byte[] data = new byte[4];
            data[0] = (byte)(x & 0xFF);
            data[1] = (byte)((x >> 8) & 0xFF);
            data[2] = (byte)((x >> 16) & 0xFF);
            data[3] = (byte)((x >> 24));
            Write(data, 0, 4);
        }

        /// <summary>
        /// Writes uint value to out buffer
        /// </summary>
        /// <param name="x"></param>
        public void WriteUInt(UInt32 x)
        {
            byte[] data = new byte[4];
            data[0] = (byte)(x & 0xFF);
            data[1] = (byte)((x >> 8) & 0xFF);
            data[2] = (byte)((x >> 16) & 0xFF);
            data[3] = (byte)((x >> 24));
            Write(data, 0, 4);
        }

        /// <summary>
        /// Writes byte value to out buffer
        /// </summary>
        /// <param name="val"></param>
        public void WriteByte(byte val)
        {
            byte[] data = new byte[1];
            data[0] = val;
            Write(data, 0, 1);
        }

        /// <summary>
        /// Writes Int64 value to out buffer
        /// </summary>
        /// <param name="x"></param>
        public void WriteLongInt(Int64 x)
        {
            byte[] data = new byte[8];

            if (x <= 0x7FFFFFFF)
            {
                WriteInt((int)x);
                return;
            }

            WriteUInt(0xFFFFFFFF);
            CheckSum.Sival(ref data, 0, (UInt32)(x & 0xFFFFFFFF));
            CheckSum.Sival(ref data, 4, (UInt32)((x >> 32) & 0xFFFFFFFF));

            Write(data, 0, 8);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        public void IoPrintf(string message)
        {
            //byte[] data = usen.GetBytes(message);
            //byte[] data = asen.GetBytes(message);
            byte[] data = Encoding.ASCII.GetBytes(message); //@todo cyrillic
            Write(data, 0, data.Length);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        public string ReadFilesFromLine(Stream stream, Options options)
        {
            string fileName = String.Empty;
            bool readingRemotely = options.RemoteFilesFromFile != null;
            bool nulls = options.EolNulls || readingRemotely;
            while (true)
            {
                while (true)
                {
                    int readByte = stream.ReadByte();
                    if (readByte == -1)
                    {
                        break;
                    }
                    // ...select
                    if (nulls ? readByte == '\0' : (readByte == '\r' || readByte == '\n'))
                    {
                        if (!readingRemotely && fileName == String.Empty)
                        {
                            continue;
                        }
                        break;
                    }
                    fileName += readByte;
                }
                if (fileName == String.Empty || !(fileName[0] == '#' || fileName[0] == ';'))
                {
                    break;
                }
                continue;
            }
            return fileName;
        }

        /// <summary>
        /// Set corresponding fields to given Streams
        /// </summary>
        /// <param name="inSock"></param>
        /// <param name="outSock"></param>
        public void IoSetSocketFields(Stream inSock, Stream outSock)
        {
            _socketIn = inSock;
            _socketOut = outSock;
        }

        /// <summary>
        /// Reads a line from stream
        /// </summary>
        /// <returns></returns>
        public string ReadLine()
        {
            StringBuilder data = new StringBuilder();
            while (true)
            {
                byte readAByte = ReadByte();
                char read = Convert.ToChar(readAByte);
                if (read != '\r')
                {
                    data.Append(read);
                }
                if (read == '\n')
                {
                    break;
                }
            }
            return data.ToString();
        }

        /// <summary>
        /// Read buffer as string
        /// </summary>
        /// <param name="count"></param>
        /// <returns></returns>
        public string ReadStringFromBuffer(int count)
        {
            byte[] data = new byte[count];
            data = ReadBuffer(count);
            return Encoding.ASCII.GetString(data); //@todo cyrillic
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="count"></param>
        /// <returns></returns>
        public byte[] ReadBuffer(int count)
        {
            byte[] buffer = new byte[count];
            int bytesRead;
            int total = 0;

            while (total < count)
            {
                try
                {
                    bytesRead = ReadSocketUnbuffered(buffer, total, count - total);
                    total += bytesRead;
                }
                catch (Exception)
                {
                    WinRsync.Exit("Unable to read data from the transport connection", null);
                    if (ClientThread != null)
                    {
                        ClientThread.Abort();
                    }
                    return null;
                }
            }
            CalculateTotalRead(count);
            return buffer;
        }

        /// <summary>
        /// Reads socket into buffer directly
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="count"></param>
        public void ReadLoop(byte[] buffer, int count)
        {
            int offset = 0;
            while (count > 0)
            {
                Flush();
                int n = _socketIn.Read(buffer, offset, count);
                offset += n;
                count -= n;
            }
        }

        /// <summary>
        /// Reads socket into buffer
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public int ReadSocketUnbuffered(byte[] buffer, int offset, int count)
        {
            int tag, result = 0;
            byte[] line = new byte[1024];

            if (_ioBufIn == null)
            {
                return _socketIn.Read(buffer, offset, count);
            }

            if (!_ioMultiplexingIn && _remaining == 0)
            {
                Flush();
                _remaining = _socketIn.Read(_ioBufIn, 0, _ioBufInSize);
                _ioBufInIndex = 0;
            }

            while (result == 0)
            {
                if (_remaining != 0)
                {
                    count = Math.Min(count, _remaining);
                    Util.MemoryCopy(buffer, offset, _ioBufIn, _ioBufInIndex, count);
                    _ioBufInIndex += count;
                    _remaining -= count;
                    result = count;
                    break;
                }

                ReadLoop(line, 4);
                tag = (int)Ival(line, 0);

                _remaining = tag & 0xFFFFFF;
                tag = (tag >> 24) - MplexBase;

                switch ((MsgCode)tag)
                {
                    case MsgCode.MsgData:
                        if (_remaining > _ioBufInSize)
                        {
                            MapFile.ExtendArray(ref _ioBufIn, _remaining);
                            _ioBufInSize = _remaining;
                        }
                        ReadLoop(_ioBufIn, _remaining);
                        _ioBufInIndex = 0;
                        break;
                    case MsgCode.MsgInfo:
                    case MsgCode.MsgError:
                        if (_remaining >= line.Length)
                        {
                            throw new Exception("Multiplexing overflow " + tag + ":" + _remaining);
                        }
                        ReadLoop(line, _remaining);
                        _remaining = 0;
                        break;
                    default:
                        throw new Exception("Read unknown message from stream");
                }
            }

            if (_remaining == 0)
            {
                Flush();
            }

            return result;
        }

        /// <summary>
        /// Reads int from buffer
        /// </summary>
        /// <returns></returns>
        public int ReadInt()
        {
            byte[] arr = new byte[4];
            arr = ReadBuffer(4);
            return arr[0] + (arr[1] << 8) + (arr[2] << 16) + (arr[3] << 24);
        }

        /// <summary>
        /// Reads byte from buffer
        /// </summary>
        /// <returns></returns>
        public byte ReadByte()
        {
            return ReadBuffer(1)[0];
        }

        /// <summary>
        /// Reads Int64 from buffer
        /// </summary>
        /// <returns></returns>
        public Int64 ReadLongInt()
        {
            Int64 result;
            byte[] b = new byte[8];
            result = ReadInt();

            if ((UInt32)result != 0xffffffff)
            {
                return result;
            }

            b = ReadBuffer(8);
            result = Ival(b, 0) | (((Int64)Ival(b, 4)) << 32);

            return result;
        }

        /// <summary>
        /// Starts multiplex in
        /// </summary>
        public void IoStartMultiplexIn()
        {
            Flush();
            IoStartBufferingIn();
            _ioMultiplexingIn = true;
        }

        /// <summary>
        /// Stops multiplex in
        /// </summary>
        public void IoCloseMultiplexIn()
        {
            _ioMultiplexingIn = false;
        }

        /// <summary>
        /// Starts multiplex out
        /// </summary>
        public void IoStartMultiplexOut()
        {
            Flush();
            IoStartBufferingOut();
            _ioMultiplexingOut = true;
        }

        /// <summary>
        /// Stops multiplex out
        /// </summary>
        public void IoCloseMultiplexOut()
        {
            _ioMultiplexingOut = false;
        }

        /// <summary>
        /// Inits IOBufIn if needed
        /// </summary>
        public void IoStartBufferingIn()
        {
            if (_ioBufIn != null)
            {
                return;
            }
            _ioBufInSize = 2 * IoBufferSize;
            _ioBufIn = new byte[_ioBufInSize];
        }

        /// <summary>
        /// Stops buffering safely
        /// </summary>
        public void IoEndBuffering()
        {
            Flush();
            if (!_ioMultiplexingOut)
            {
                _ioBufIn = null;
                _ioBufOut = null;
            }
        }

        /// <summary>
        /// Inits new IOBufOut is needed
        /// </summary>
        public void IoStartBufferingOut()
        {
            if (_ioBufOut != null)
            {
                return;
            }
            _ioBufOut = new byte[IoBufferSize];
            _ioBufOutCount = 0;
        }

        /// <summary>
        /// Close sockets
        /// </summary>
        public void Close()
        {
            _socketIn.Close();
            _socketOut.Close();
        }
    }
}
