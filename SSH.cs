﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;

namespace Reecon
{
    class SSH
    {
        public static string GetInfo(string ip, int port)
        {
            string returnInfo = "";
            string sshVersion = "- SSH Version: " + SSH.GetVersion(ip, port);
            string authMethods = "- Authentication Methods: " + SSH.GetAuthMethods(ip, port);
            returnInfo = sshVersion + Environment.NewLine + authMethods;
            return returnInfo;
        }
        // Get version
        public static string GetVersion(string ip, int port)
        {
            try
            {
                Byte[] buffer = new Byte[512];
                using (Socket sshSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    sshSocket.Connect(ip, port);
                    int bytes = sshSocket.Receive(buffer, buffer.Length, 0);
                    string versionMessage = Encoding.ASCII.GetString(buffer, 0, bytes);
                    versionMessage = versionMessage.Trim().Replace(Environment.NewLine, "");
                    return versionMessage;
                }
            }
            catch (SocketException se)
            {
                if (se.Message.StartsWith("No connection could be made because the target machine actively refused it"))
                {
                    return "Port is closed";
                }
                return "SSG.GetVersion - Fatal Woof!";
            }
        }

        // Get Auth Methods
        public static string GetAuthMethods(string ip, int port)
        {
            string returnString = "";
            if (string.IsNullOrEmpty(ip))
            {
                Console.WriteLine("Error in ssh.GetAuthMethods - Missing IP");
                return "";
            }
            List<string> outputLines = General.GetProcessOutput("ssh", $"-oPreferredAuthentications=none -oStrictHostKeyChecking=no {ip} -p {port}");
            // kex_exchange_identification: read: Connection reset by peer
            if (outputLines.Count == 1 && outputLines[0].EndsWith("Connection refused"))
            {
                return "- Port is closed";
            }
            if (outputLines.Contains("kex_exchange_identification: read: Connection reset by peer"))
            {
                returnString = "- SSH Exists, but connection reset - Doesn't like you :(";
                return returnString;
            }
            if (!outputLines.Any(x => x.Contains("Permission denied")))
            {
                Console.WriteLine("Error in ssh.GetAuthMethods - No Permission denied found");
                foreach (string line in outputLines)
                {
                    Console.WriteLine($"Debug: --> {line}");
                }
                return "";
            }
            returnString = outputLines.First(x => x.Contains("Permission denied"));
            returnString = returnString.Remove(0, returnString.IndexOf("("));
            returnString = returnString.Replace("(", "").Replace(")", "");
            // ssh - oPreferredAuthentications = none - oStrictHostKeyChecking = no 10.10.10.147

            // reelix@10.10.10.147: Permission denied(publickey, password).
            // reelix@10.10.10.110: Permission denied (publickey,keyboard-interactive).
            return returnString;

        }
    }
}
