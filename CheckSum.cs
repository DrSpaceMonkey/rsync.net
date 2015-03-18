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

namespace NetSync
{
    /// <summary>
    /// 
    /// </summary>
    class CheckSum
    {
        private Options _options;

        public CheckSum(Options opt)
        {
            _options = opt;
        }

        /// <summary>
        /// Writes bytes of 'x' to 'buf' [in reverse order] starting from 'offset'
        /// </summary>
        /// <param name="buf"></param>
        /// <param name="offset"></param>
        /// <param name="x"></param>
        public static void Sival(ref byte[] buf, int offset, UInt32 x)
        {
            buf[offset + 0] = (byte)(x & 0xFF);
            buf[offset + 1] = (byte)((x >> 8) & 0xFF);
            buf[offset + 2] = (byte)((x >> 16) & 0xFF);
            buf[offset + 3] = (byte)((x >> 24));
        }

        /// <summary>
        /// Converts b to int. If b >=0x80 then returns b-256, else returns b
        /// </summary>
        /// <param name="b"></param>
        /// <returns></returns>
        public static int ToInt(byte b)
        {
            return ((b & 0x80) == 0x80) ? (b - 256) : b;
        }
        /// <summary>
        /// Current length of checksum
        /// </summary>
        public int Length = 2;
        /// <summary>
        /// 16
        /// </summary>
        public const int SumLength = 16;
        /// <summary>
        /// 0
        /// </summary>
        public const int CharOffset = 0;
        /// <summary>
        /// 64
        /// </summary>
        public const int CsumChunk = 64;
        /// <summary>
        /// 16
        /// </summary>
        public const int Md4SumLength = 16;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="buf"></param>
        /// <param name="pos"></param>
        /// <param name="len"></param>
        /// <returns></returns>
        public static UInt32 GetChecksum1(byte[] buf, int pos, int len)
        {
            Int32 i;
            UInt32 s1, s2;

            int b1 = 0, b2 = 0, b3 = 0, b4 = 0;

            s1 = s2 = 0;
            for (i = 0; i < (len - 4); i += 4)
            {
                b1 = ToInt(buf[i + 0 + pos]);
                b2 = ToInt(buf[i + 1 + pos]);
                b3 = ToInt(buf[i + 2 + pos]);
                b4 = ToInt(buf[i + 3 + pos]);

                s2 += (UInt32)(4 * (s1 + b1) + 3 * b2 + 2 * b3 + b4 + 10 * CharOffset);
                s1 += (UInt32)(b1 + b2 + b3 + b4 + 4 * CharOffset);
            }
            for (; i < len; i++)
            {
                s1 += (UInt32)(ToInt(buf[i + pos]) + CharOffset);
                s2 += s1;
            }
            UInt32 sum = ((s1 & 0xffff) + (s2 << 16));
            return sum;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="buf"></param>
        /// <param name="pos"></param>
        /// <param name="len"></param>
        /// <returns></returns>
        public byte[] GetChecksum2(byte[] buf, int pos, int len)
        {
            byte[] buf1 = new byte[len + 4];
            for (int j = 0; j < len; j++)
            {
                buf1[j] = buf[pos + j];
            }
            MdFour mdFour = new MdFour(_options);
            mdFour.Begin();
            if (_options.ChecksumSeed != 0)
            {
                Sival(ref buf1, len, (UInt32)_options.ChecksumSeed);
                len += 4;
            }
            int i;
            for (i = 0; i + CsumChunk <= len; i += CsumChunk)
            {
                mdFour.Update(buf1, i, CsumChunk);
            }
            if (len - i > 0 || _options.ProtocolVersion >= 27)
            {
                mdFour.Update(buf1, i, (UInt32)(len - i));
            }
            return mdFour.Result();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="sum"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        public bool FileCheckSum(string fileName, ref byte[] sum, int size)
        {
            int i;
            MdFour mdFour = new MdFour(_options);
            sum = new byte[Md4SumLength];
            try
            {
                using (var fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
                {

                    MapFile buf = new MapFile(fileStream, size, Options.MaxMapSize, CsumChunk);
                    mdFour.Begin();

                    for (i = 0; i + CsumChunk <= size; i += CsumChunk)
                    {
                        int offset = buf.MapPtr(i, CsumChunk);
                        mdFour.Update(buf.P, offset, CsumChunk);
                    }

                    if (size - i > 0 || _options.ProtocolVersion >= 27)
                    {
                        int offset = buf.MapPtr(i, size - i);
                        mdFour.Update(buf.P, offset, (UInt32)(size - i));
                    }

                    sum = mdFour.Result();

                    fileStream.Close();
                    //buf.UnMapFile(); //@fixed useless string
                    return true;
                }
            }
            catch (Exception)
            {
            }
            return false;

        }
    }

    /// <summary>
    /// Calculates MD4
    /// </summary>
    public class MdFour
    {
        public const UInt32 Mask32 = 0xFFFFFFFF;

        public UInt32 A, B, C, D;
        public UInt32 TotalN;
        public UInt32 TotalN2;
        private Options _options;

        public MdFour(Options opt)
        {
            _options = opt;
        }


        public UInt32 F(UInt32 x, UInt32 y, UInt32 z)
        {
            return ((((x) & (y)) | ((~(x)) & (z))));
        }

        public UInt32 G(UInt32 x, UInt32 y, UInt32 z)
        {
            return ((((x) & (y)) | ((x) & (z)) | ((y) & (z))));
        }

        public UInt32 H(UInt32 x, UInt32 y, UInt32 z)
        {
            return (((x) ^ (y) ^ (z)));
        }

        public UInt32 Lshift(UInt32 x, int s)
        {
            return (((((x) << (s)) & Mask32) | (((x) >> (32 - (s))) & Mask32)));
        }

        public UInt32 Round1(UInt32 a, UInt32 b, UInt32 c, UInt32 d, UInt32[] m, int k, int s)
        {
            return Lshift((a + F(b, c, d) + m[k]) & Mask32, s);
        }

        public UInt32 Round2(UInt32 a, UInt32 b, UInt32 c, UInt32 d, UInt32[] m, int k, int s)
        {
            return Lshift((a + G(b, c, d) + m[k] + 0x5A827999) & Mask32, s);
        }

        public UInt32 Round3(UInt32 a, UInt32 b, UInt32 c, UInt32 d, UInt32[] m, int k, int s)
        {
            return Lshift((a + H(b, c, d) + m[k] + 0x6ED9EBA1) & Mask32, s);
        }

        public void MdFour64(UInt32[] m)
        {
            UInt32 aa, bb, cc, dd;
            aa = A; bb = B; cc = C; dd = D;

            A = Round1(A, B, C, D, m, 0, 3);
            D = Round1(D, A, B, C, m, 1, 7);
            C = Round1(C, D, A, B, m, 2, 11);
            B = Round1(B, C, D, A, m, 3, 19);
            A = Round1(A, B, C, D, m, 4, 3);
            D = Round1(D, A, B, C, m, 5, 7);
            C = Round1(C, D, A, B, m, 6, 11);
            B = Round1(B, C, D, A, m, 7, 19);
            A = Round1(A, B, C, D, m, 8, 3);
            D = Round1(D, A, B, C, m, 9, 7);
            C = Round1(C, D, A, B, m, 10, 11);
            B = Round1(B, C, D, A, m, 11, 19);
            A = Round1(A, B, C, D, m, 12, 3);
            D = Round1(D, A, B, C, m, 13, 7);
            C = Round1(C, D, A, B, m, 14, 11);
            B = Round1(B, C, D, A, m, 15, 19);

            A = Round2(A, B, C, D, m, 0, 3);
            D = Round2(D, A, B, C, m, 4, 5);
            C = Round2(C, D, A, B, m, 8, 9);
            B = Round2(B, C, D, A, m, 12, 13);
            A = Round2(A, B, C, D, m, 1, 3);
            D = Round2(D, A, B, C, m, 5, 5);
            C = Round2(C, D, A, B, m, 9, 9);
            B = Round2(B, C, D, A, m, 13, 13);
            A = Round2(A, B, C, D, m, 2, 3);
            D = Round2(D, A, B, C, m, 6, 5);
            C = Round2(C, D, A, B, m, 10, 9);
            B = Round2(B, C, D, A, m, 14, 13);
            A = Round2(A, B, C, D, m, 3, 3);
            D = Round2(D, A, B, C, m, 7, 5);
            C = Round2(C, D, A, B, m, 11, 9);
            B = Round2(B, C, D, A, m, 15, 13);

            A = Round3(A, B, C, D, m, 0, 3);
            D = Round3(D, A, B, C, m, 8, 9);
            C = Round3(C, D, A, B, m, 4, 11);
            B = Round3(B, C, D, A, m, 12, 15);
            A = Round3(A, B, C, D, m, 2, 3);
            D = Round3(D, A, B, C, m, 10, 9);
            C = Round3(C, D, A, B, m, 6, 11);
            B = Round3(B, C, D, A, m, 14, 15);
            A = Round3(A, B, C, D, m, 1, 3);
            D = Round3(D, A, B, C, m, 9, 9);
            C = Round3(C, D, A, B, m, 5, 11);
            B = Round3(B, C, D, A, m, 13, 15);
            A = Round3(A, B, C, D, m, 3, 3);
            D = Round3(D, A, B, C, m, 11, 9);
            C = Round3(C, D, A, B, m, 7, 11);
            B = Round3(B, C, D, A, m, 15, 15);

            A += aa; B += bb;
            C += cc; D += dd;

            A &= Mask32; B &= Mask32;
            C &= Mask32; D &= Mask32;

            //this.A = A; this.B = B; this.C = C; this.D = D;
        }

        public void Begin()
        {
            A = 0x67452301;
            B = 0xefcdab89;
            C = 0x98badcfe;
            D = 0x10325476;
            TotalN = 0;
            TotalN2 = 0;
        }

        public byte[] Result()
        {
            byte[] ret = new byte[16];
            Copy4(ref ret, 0, A);
            Copy4(ref ret, 4, B);
            Copy4(ref ret, 8, C);
            Copy4(ref ret, 12, D);
            return ret;
        }

        private void Copy4(ref byte[] outData, int ind, UInt32 x)
        {
            outData[ind] = (byte)x;
            outData[ind + 1] = (byte)(x >> 8);
            outData[ind + 2] = (byte)(x >> 16);
            outData[ind + 3] = (byte)(x >> 24);
        }

        private void Copy64(ref UInt32[] m, int ind, byte[] inData, int ind2)
        {
            for (int i = 0; i < 16; i++)
            {
                m[i + ind] = (UInt32)((inData[i * 4 + 3 + ind2] << 24) | (inData[i * 4 + 2 + ind2] << 16) | (inData[i * 4 + 1 + ind2] << 8) | (inData[i * 4 + 0 + ind2] << 0));
            }
        }

        public void Tail(byte[] inData, int ind, UInt32 n)
        {
            UInt32[] m = new UInt32[16];
            TotalN += n << 3;
            if (TotalN < (n << 3))
            {
                TotalN2++;
            }
            TotalN2 += n >> 29;
            byte[] buf = new byte[128];
            for (int i = 0; i < n; i++)
            {
                buf[i] = inData[ind + i];
            }
            buf[n] = 0x80;
            if (n <= 55)
            {
                Copy4(ref buf, 56, TotalN);
                if (_options.ProtocolVersion >= 27)
                {
                    Copy4(ref buf, 60, TotalN2);
                }
                Copy64(ref m, 0, buf, 0);
                MdFour64(m);
            }
            else
            {
                Copy4(ref buf, 120, TotalN);
                if (_options.ProtocolVersion >= 27)
                {
                    Copy4(ref buf, 124, TotalN2);
                }
                Copy64(ref m, 0, buf, 0);
                MdFour64(m);
                Copy64(ref m, 0, buf, 64);
                MdFour64(m);
            }
        }

        public void Update(byte[] inData, int ind, UInt32 n)
        {
            UInt32[] m = new UInt32[16];

            if (n == 0)
            {
                Tail(inData, ind, 0);
            }

            int i = 0;
            while (n >= 64)
            {
                Copy64(ref m, 0, inData, ind + i);
                MdFour64(m);
                i += 64;
                n -= 64;
                TotalN += 64 << 3;
                if (TotalN < 64 << 3)
                {
                    TotalN2++;
                }
            }

            if (n != 0)
            {
                Tail(inData, ind + i, n);
            }
        }
    }

    /// <summary>
    /// 
    /// </summary>
    public class Sum
    {
        public int SumResidue;
        public byte[] Sumrbuf = new byte[CheckSum.CsumChunk];
        public MdFour Md;
        private Options _options;

        public Sum(Options opt)
        {
            _options = opt;
            Md = new MdFour(opt);
        }

        public void Init(int seed)
        {
            byte[] s = new byte[4];
            Md.Begin();
            SumResidue = 0;
            CheckSum.Sival(ref s, 0, (UInt32)seed);
            Update(s, 0, 4);
        }

        public void Update(byte[] p, int ind, int len)
        {
            int pPos = 0;
            if (len + SumResidue < CheckSum.CsumChunk)
            {
                for (int j = 0; j < len; j++)
                {
                    Sumrbuf[SumResidue + j] = p[j + ind];
                }
                SumResidue += len;
                return;
            }

            if (SumResidue != 0)
            {
                int min = Math.Min(CheckSum.CsumChunk - SumResidue, len);
                for (int j = 0; j < min; j++)
                {
                    Sumrbuf[SumResidue + j] = p[j + ind];
                }
                Md.Update(Sumrbuf, 0, (UInt32)(min + SumResidue));
                len -= min;
                pPos += min;
            }

            int i;
            for (i = 0; i + CheckSum.CsumChunk <= len; i += CheckSum.CsumChunk)
            {
                for (int j = 0; j < CheckSum.CsumChunk; j++)
                {
                    Sumrbuf[j] = p[pPos + i + j + ind];
                }
                Md.Update(Sumrbuf, 0, CheckSum.CsumChunk);
            }

            if (len - i > 0)
            {
                SumResidue = len - i;
                for (int j = 0; j < SumResidue; j++)
                {
                    Sumrbuf[j] = p[pPos + i + j + ind];
                }
            }
            else
            {
                SumResidue = 0;
            }
        }

        public byte[] End()
        {
            if (SumResidue != 0 || _options.ProtocolVersion >= 27)
            {
                Md.Update(Sumrbuf, 0, (UInt32)SumResidue);
            }
            return Md.Result();
        }

    }
}
