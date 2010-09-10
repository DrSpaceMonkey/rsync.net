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
        MSG_DONE = 5,	/* current phase is done */
        MSG_REDO = 4,	/* reprocess indicated flist index */
        MSG_ERROR = 1, MSG_INFO = 2, MSG_LOG = 3, /* remote logging */
        MSG_DATA = 0	/* raw data on the multiplexed stream */
    };

    /// <summary>
    /// 
    /// </summary>
    public class IOStream
    {

        private Stream sockOut;
        private Stream sockIn;

        private bool IOMultiplexingIn = false;
        private bool IOMultiplexingOut = false;
        private const int MPLEX_BASE = 7;
        private const int IO_BUFFER_SIZE = 4096;
        private int IOBufInSize = 0;
        private int remaining = 0;
        private int IOBufInIndex = 0;
        private byte[] IOBufIn = null;
        private byte[] IOBufOut = null;
        private int IOBufOutCount = 0;
        private bool noFlush = false;
        //private ASCIIEncoding asen = new ASCIIEncoding();

        public Thread ClientThread = null;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="f"></param>
        public IOStream(Stream f)
        {
            IOSetSocketFds(f, f);
            IOBufInSize = 2 * IO_BUFFER_SIZE;
        }


        public void CalculationTotalWritten(int len)
        {
            Options.stats.totalWritten += len;
        }
        public void CalculationTotalRead(int len)
        {
            Options.stats.totalRead += len;
        }

        public UInt32 IVAL(byte[] b, int offset)
        {
            return (UInt32)(b[offset] + (b[offset + 1] << 8) + (b[offset + 2] << 16) + (b[offset + 3] << 24));
        }

        public void Write(byte[] buf, int offset, int len)
        {
            try
            {
                WriteFD(buf, offset, len);
            }
            catch (Exception e)
            {
                Log.Write(e.Message);
            }
            CalculationTotalWritten(len);
        }


        public void WriteFD(byte[] buf, int offset, int len)
        {

            if (IOBufOut == null)
            {
                sockOut.Write(buf, offset, len);
                return;
            }

            int off = 0;
            while (len > 0)
            {
                int n = Math.Min(len, IO_BUFFER_SIZE - IOBufOutCount);
                if (n > 0)
                {
                    Util.MemCpy(IOBufOut, IOBufOutCount, buf, offset + off, n);
                    off += n;
                    len -= n;
                    IOBufOutCount += n;
                }

                if (IOBufOutCount == IO_BUFFER_SIZE)
                {
                    Flush();
                }
            }
        }


        public void MplexWrite(MsgCode code, byte[] buf, int len)
        {
            byte[] buffer = new byte[4096];
            int n = len;

            CheckSum.SIVAL(ref buffer, 0, (UInt32)(((MPLEX_BASE + (int)code) << 24) + len));

            if (n > buffer.Length - 4)
            {
                n = buffer.Length - 4;
            }

            Util.MemCpy(buffer, 4, buf, 0, n);
            sockOut.Write(buffer, 0, n + 4);

            len -= n;
            if (len > 0)
            {
                sockOut.Write(buf, n, len);
            }
        }

        public void Flush()
        {
            if (IOBufOutCount == 0 || noFlush)
            {
                return;
            }

            if (IOMultiplexingOut)
            {
                MplexWrite(MsgCode.MSG_DATA, IOBufOut, IOBufOutCount);
            }
            else
            {
                sockOut.Write(IOBufOut, 0, IOBufOutCount);
            }
            IOBufOutCount = 0;
        }


        public void writeInt(int x)
        {
            byte[] arr = new byte[4];
            arr[0] = (byte)(x & 0xFF);
            arr[1] = (byte)((x >> 8) & 0xFF);
            arr[2] = (byte)((x >> 16) & 0xFF);
            arr[3] = (byte)((x >> 24));
            Write(arr, 0, 4);
        }

        public void writeUInt(UInt32 x)
        {
            byte[] arr = new byte[4];
            arr[0] = (byte)(x & 0xFF);
            arr[1] = (byte)((x >> 8) & 0xFF);
            arr[2] = (byte)((x >> 16) & 0xFF);
            arr[3] = (byte)((x >> 24));
            Write(arr, 0, 4);
        }

        public void writeByte(byte val)
        {
            byte[] data = new byte[1];
            data[0] = val;
            Write(data, 0, 1);
        }

        public void WriteLongInt(Int64 x)
        {
            byte[] b = new byte[8];

            if (x <= 0x7FFFFFFF)
            {
                writeInt((int)x);
                return;
            }

            writeUInt(0xFFFFFFFF);
            CheckSum.SIVAL(ref b, 0, (UInt32)(x & 0xFFFFFFFF));
            CheckSum.SIVAL(ref b, 4, (UInt32)((x >> 32) & 0xFFFFFFFF));

            Write(b, 0, 8);
        }

        public void IOPrintf(string message)
        {
            //byte[] data = usen.GetBytes(message);
            //byte[] data = asen.GetBytes(message);
            byte[] data = Encoding.ASCII.GetBytes(message);
            Write(data, 0, data.Length);
        }
        /*
        public void checkTimeOut()
        {
            if(Options.ioTimeout == 0)
                return;
            if(Options.lastIO == DateTime.MinValue)
            {
                Options.lastIO = DateTime.Now;
                return;
            }

            if(DateTime.Now.Ticks - Options.lastIO.Ticks >= Options.ioTimeout * 10000000)
            {
                if(!Options.amServer && !Options.amDaemon)
                    Log.WriteLine("io timeout after "+(DateTime.Now.Ticks - Options.lastIO.Ticks)/10000000+" seconds - exiting");
                Environment.Exit(0);
            }
        }
         */
        public string readFilesFromLine(Stream fd, Options options)
        {
            string fileName = String.Empty;
            bool readingRemotely = options.remoteFilesFromFile != null;
            bool nulls = options.eolNulls || readingRemotely;
            while (true)
            {
                while (true)
                {
                    int readByte = fd.ReadByte();
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
        public void IOSetSocketFds(Stream inSock, Stream outSock)
        {
            sockIn = inSock;
            sockOut = outSock;
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
                byte readAByte = readByte();
                //if (readAByte == -1)
                //{
                //    return data.ToString();
                //}
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

        public string ReadSBuf(int len)
        {
            byte[] data = new byte[len];
            data = ReadBuf(len);
            return ASCIIEncoding.ASCII.GetString(data);
        }

        public byte[] ReadBuf(int len)
        {
            byte[] data = new byte[len];
            int ret;
            int total = 0;

            while (total < len)
            {
                try
                {
                    ret = ReadfdUnbuffered(data, total, len - total);
                    total += ret;
                }
                catch (Exception)
                {
                    MainClass.Exit("Unable to read data from the transport connection", null);
                    if (ClientThread != null)
                    {
                        ClientThread.Abort();
                    }
                    return null;
                }
            }
            CalculationTotalRead(len);
            return data;
        }


        public void ReadLoop(byte[] data, int len)
        {
            int off = 0;
            while (len > 0)
            {
                Flush();
                int n = sockIn.Read(data, off, len);
                off += n;
                len -= n;
            }
        }

        public int ReadfdUnbuffered(byte[] data, int off, int len)
        {
            int tag, ret = 0;
            byte[] line = new byte[1024];

            if (IOBufIn == null)
            {
                return sockIn.Read(data, off, len);
            }

            if (!IOMultiplexingIn && remaining == 0)
            {
                Flush();
                remaining = sockIn.Read(IOBufIn, 0, IOBufInSize);
                IOBufInIndex = 0;
            }

            while (ret == 0)
            {
                if (remaining != 0)
                {
                    len = Math.Min(len, remaining);
                    Util.MemCpy(data, off, IOBufIn, IOBufInIndex, len);
                    IOBufInIndex += len;
                    remaining -= len;
                    ret = len;
                    break;
                }

                ReadLoop(line, 4);
                tag = (int)IVAL(line, 0);

                remaining = tag & 0xFFFFFF;
                tag = (tag >> 24) - MPLEX_BASE;

                switch ((MsgCode)tag)
                {
                    case MsgCode.MSG_DATA:
                        if (remaining > IOBufInSize)
                        {
                            MapFile.ReallocArray(ref IOBufIn, remaining);
                            IOBufInSize = remaining;
                        }
                        ReadLoop(IOBufIn, remaining);
                        IOBufInIndex = 0;
                        break;
                    case MsgCode.MSG_INFO:
                    case MsgCode.MSG_ERROR:
                        if (remaining >= line.Length)
                        {
                            throw new Exception("multiplexing overflow " + tag + ":" + remaining);
                        }
                        ReadLoop(line, remaining);
                        remaining = 0;
                        break;
                    default:
                        throw new Exception("Read unknown message from stream");
                }
            }

            if (remaining == 0)
            {
                Flush();
            }

            return ret;
        }



        public int readInt()
        {
            byte[] arr = new byte[4];
            arr = ReadBuf(4);
            return arr[0] + (arr[1] << 8) + (arr[2] << 16) + (arr[3] << 24);
        }

        public byte readByte()
        {
            byte data;
            byte[] tmp = ReadBuf(1);
            data = tmp[0];
            //if (data == -1) //because data is byte which can only be [0, 255]
            //{
            //    throw new Exception("Can't read from Stream");
            //}
            //else
            //{
            return Convert.ToByte(data);
            //}
        }

        public Int64 ReadLongInt()
        {
            Int64 ret;
            byte[] b = new byte[8];
            ret = readInt();

            if ((UInt32)ret != 0xffffffff)
            {
                return ret;
            }

            b = ReadBuf(8);
            ret = IVAL(b, 0) | (((Int64)IVAL(b, 4)) << 32);

            return ret;
        }


        public void IOStartMultiplexIn()
        {
            Flush();
            IOStartBufferingIn();
            IOMultiplexingIn = true;
        }

        public void IOCloseMultiplexIn()
        {
            IOMultiplexingIn = false;
        }

        public void IOStartMultiplexOut()
        {
            Flush();
            IOStartBufferingOut();
            IOMultiplexingOut = true;
        }

        public void IOCloseMultiplexOut()
        {
            IOMultiplexingOut = false;
        }

        public void IOStartBufferingIn()
        {
            if (IOBufIn != null)
            {
                return;
            }
            IOBufInSize = 2 * IO_BUFFER_SIZE;
            IOBufIn = new byte[IOBufInSize];
        }


        public void IOEndBuffering()
        {
            Flush();
            if (!IOMultiplexingOut)
            {
                IOBufIn = null;
                IOBufOut = null;
            }
        }


        public void IOStartBufferingOut()
        {
            if (IOBufOut != null)
            {
                return;
            }
            IOBufOut = new byte[IO_BUFFER_SIZE];
            IOBufOutCount = 0;
        }

        public void Close()
        {
            sockIn.Close();
            sockOut.Close();
        }
    }
}
