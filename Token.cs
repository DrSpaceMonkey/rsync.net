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

namespace NetSync
{
    public class Token
    {
        public static int Residue;
        private Options _options;

        public Token(Options opt)
        {
            _options = opt;
        }
        public void SetCompression(string fname)
        {
            if (!_options.DoCompression)
            {
                return;
            }
        }

        public void SendToken(IoStream f, int token, MapFile buf, int offset, int n, int toklen)
        {
            if (!_options.DoCompression)
            {
                SimpleSendToken(f, token, buf, offset, n);
            }
            else
            {
                SendDeflatedToken(f, token, buf, offset, n, toklen);
            }
        }

        public int ReceiveToken(IoStream ioStream, ref byte[] data, int offset)
        {
            int token;
            if (!_options.DoCompression)
            {
                token = SimpleReceiveToken(ioStream, ref data, offset);
            }
            else
            {
                token = ReceiveDeflatedToken(ioStream, data, offset);
            }
            return token;
        }

        public int SimpleReceiveToken(IoStream ioStream, ref byte[] data, int offset)
        {
            int n;
            if (Residue == 0)
            {
                var i = ioStream.ReadInt();
                if (i <= 0)
                {
                    return i;
                }
                Residue = i;
            }

            n = Math.Min(Match.ChunkSize, Residue);
            Residue -= n;
            data = ioStream.ReadBuffer(n);
            return n;
        }

        public int ReceiveDeflatedToken(IoStream f, byte[] data, int offset)
        {
            return 0;
        }

        public void SendDeflatedToken(IoStream f, int token, MapFile buf, int offset, int nb, int toklen)
        {
        }

        public void SeeToken(byte[] data, int offset, int tokLen)
        {
            if (_options.DoCompression)
            {
                SeeDeflateToken(data, offset, tokLen);
            }
        }

        public void SeeDeflateToken(byte[] data, int offset, int tokLen)
        {
        }

        public void SimpleSendToken(IoStream f, int token, MapFile buf, int offset, int n)
        {
            if (n > 0)
            {
                var l = 0;
                while (l < n)
                {
                    var n1 = Math.Min(Match.ChunkSize, n - l);
                    f.WriteInt(n1);
                    var off = buf.MapPtr(offset + l, n1);
                    f.Write(buf.P, off, n1);
                    l += n1;
                }
            }
            if (token != -2)
            {
                f.WriteInt(-(token + 1));
            }
        }
    }
}
