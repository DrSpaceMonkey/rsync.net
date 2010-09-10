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

namespace NetSync
{
    class Authentication
    {
        public static string password_file = String.Empty;

        /// <summary>
        /// Encodes message, treated as ASCII, into base64 string
        /// </summary>
        /// <param name="message">ASCII string</param>
        /// <returns></returns>
        public static string base64_encode(string message)
        {
            Encoding asciiEncoding = Encoding.ASCII;
            byte[] byteArray = new byte[asciiEncoding.GetByteCount(message)];
            byteArray = asciiEncoding.GetBytes(message);
            return Convert.ToBase64String(byteArray);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="addr"></param>
        /// <param name="opt"></param>
        /// <returns></returns>
        public static string gen_challenge(string addr, Options opt)
        {
            string challenge = String.Empty;
            byte[] input = new byte[32];
            DateTime tv = DateTime.Now;

            for (int i = 0; i < addr.Length; i++)
            {
                input[i] = Convert.ToByte(addr[i]);
            }

            CheckSum.SIVAL(ref input, 16, (UInt32)tv.Second);
            CheckSum.SIVAL(ref input, 20, (UInt32)tv.Hour);
            CheckSum.SIVAL(ref input, 24, (UInt32)tv.Day);

            Sum sum = new Sum(opt);
            sum.Init(0);
            sum.Update(input, 0, input.Length);
            challenge = Encoding.ASCII.GetString(sum.End());
            return challenge;
        }

        /// <summary>
        /// Generate hash for passwords
        /// </summary>
        /// <param name="indata"></param>
        /// <param name="challenge"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        public static string generate_hash(string indata, string challenge, Options options)
        {
            Sum sum = new Sum(options);

            sum.Init(0);
            sum.Update(Encoding.ASCII.GetBytes(indata), 0, indata.Length);
            sum.Update(Encoding.ASCII.GetBytes(challenge), 0, challenge.Length);
            byte[] buf = sum.End();
            string hash = Convert.ToBase64String(buf);
            return hash.Substring(0, (buf.Length * 8 + 5) / 6);
        }

        /// <summary>
        /// Client-side authorization request maker
        /// </summary>
        /// <param name="user"></param>
        /// <param name="pass"></param>
        /// <param name="challenge"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        public static string auth_client(string user, string pass, string challenge, Options options)
        {
            if (String.Compare(user, String.Empty) == 0)
            {
                user = "nobody";
            }
            if (pass == null || String.Compare(pass, String.Empty) == 0)
            {
                pass = getpassf(password_file);
            }
            if (String.Compare(pass, String.Empty) == 0)
            {
                pass = System.Environment.GetEnvironmentVariable("RSYNC_PASSWORD");
            }
            if (pass == null || String.Compare(pass, String.Empty) == 0)
            {
                pass = getpass();
            }
            string pass2 = generate_hash(pass, challenge, options);
            Log.WriteLine(user + " " + pass2);

            return pass2;
        }

        /// <summary>
        /// Request a password from user by Console
        /// </summary>
        /// <returns></returns>
        public static string getpass()
        {
            Console.Write("Password: ");
            return Console.ReadLine();
        }

        /// <summary>
        /// Return String.Empty in any case
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public static string getpassf(string filename)
        {
            return String.Empty; //@todo what is the goal of this method?
        }

        /// <summary>
        /// Server-side authorization check
        /// </summary>
        /// <param name="cInfo"></param>
        /// <param name="moduleNumber"></param>
        /// <param name="addr"></param>
        /// <param name="leader"></param>
        /// <returns></returns>
        public static bool AuthServer(ClientInfo cInfo, int moduleNumber, string addr, string leader)
        {
            string users = Daemon.config.GetAuthUsers(moduleNumber).Trim();
            //string challenge;
            string b64_challenge;
            IOStream f = cInfo.IoStream;
            string line;

            string user = String.Empty;
            string secret = String.Empty;
            string pass = String.Empty;
            string pass2 = String.Empty;
            string[] listUsers;
            string tok = String.Empty;


            /* if no auth list then allow anyone in! */
            if (users == null || users.CompareTo(String.Empty) == 0)
            {
                return true;
            }

            //challenge = gen_challenge(addr, cInfo.Options); //@todo if challenge is used only once why not remove it?

            b64_challenge = base64_encode(gen_challenge(addr, cInfo.Options));

            f.IOPrintf(leader + b64_challenge + "\n");

            line = f.ReadLine();

            if (line.IndexOf(' ') > 0)
            {
                user = line.Substring(0, line.IndexOf(' '));
                pass = line.Substring(line.IndexOf(' ')).Trim('\n').Trim();
            }
            else
            {
                return false;
            }
            listUsers = users.Split(',');

            for (int i = 0; i < listUsers.Length; i++)
            {
                tok = listUsers[i];
                if (user.CompareTo(tok) == 0)
                {
                    break;
                }
                tok = null;
            }

            if (tok == null || tok.CompareTo(String.Empty) == 0)
            {
                return false;
            }

            if ((secret = GetSecret(moduleNumber, user)) == null)
            {
                return false;
            }

            pass2 = generate_hash(secret, b64_challenge, cInfo.Options);

            if (pass.CompareTo(pass2) == 0)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets secret for given Module(by module number) and user
        /// </summary>
        /// <param name="moduleNumber"></param>
        /// <param name="user"></param>
        /// <returns></returns>
        static string GetSecret(int moduleNumber, string user)
        {
            //if (fname == null || fname.CompareTo(String.Empty) == 0) //@todo Why check if next there is a try/catch and also Path.combine won't return null or emprty string
            //{
            //    return null;
            //}
            try
            {
                string fname = Path.Combine(Environment.SystemDirectory, Daemon.config.GetSecretsFile(moduleNumber));
                string secret = null;
                using (var fd = new System.IO.StreamReader(fname))
                {
                    while (true)
                    {
                        string line = fd.ReadLine();
                        if (line == null)
                        {
                            break;
                        }
                        line.Trim();
                        if (line.CompareTo(String.Empty) != 0 && (line[0] != ';' && line[0] != '#'))
                        {
                            string[] userp = line.Split(':');
                            if (userp[0].Trim().CompareTo(user) == 0)
                            {
                                secret = userp[1].Trim();
                                break;
                            }
                        }
                    }
                    fd.Close();
                    return secret;
                }
            }
            catch (Exception)
            {
                return null;
            }

        }
    }

}
