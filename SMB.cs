﻿using Pastel;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace Reecon
{
    class SMB //445
    {
        public static string GetInfo(string target, int port)
        {
            // if smb2
            // if 2008
            // if 2008 before r2 -- CVE-2009-3103
            string toReturn = "";
            toReturn += GetOSDetails(target);
            if (SMB_MS17_010.IsVulnerable(target, false))
            {
                toReturn += "----> VULNERABLE TO ETERNAL BLUE (MS10-017) <-----" + Environment.NewLine;
                toReturn += "-----> Metasploit: use windows/smb/ms17_010_psexec" + Environment.NewLine;
            }
            if (General.GetOS() == General.OS.Linux)
            {
                toReturn += SMB.TestAnonymousAccess_Linux(target);
            }
            else
            {
                toReturn += "- Reecon currently lacks advanced SMB Support on Windows (Ironic, I know)";
            }
            return toReturn.Trim(Environment.NewLine.ToCharArray());
        }

        // Taken from: https://github.com/TeskeVirtualSystem/MS17010Test
        private static string GetOSDetails(string target)
        {
            string osDetails = "";
            try
            {
                byte[] negotiateBytes = negotiateProtoRequest();
                byte[] sessionBytes = sessionSetupAndxRequest();
                List<byte[]> bytesToSend = new() { negotiateBytes, sessionBytes };
                byte[] byteResult = General.BannerGrabBytes(target, 445, bytesToSend);
                var sessionSetupAndxResponse = byteResult.Skip(36).ToArray();
                var nativeOsB = sessionSetupAndxResponse.Skip(9).ToArray();
                var osData = Encoding.ASCII.GetString(nativeOsB).Split('\x00');
                if (osData[0] != "et by peer" && !osData[0].EndsWith("closed by the remote host.")) // Invalid responses
                {
                    string osName = osData[0];
                    osDetails += "- OS Name: " + osName + Environment.NewLine;
                    if (osName == "Windows 5.1")
                    {
                        osDetails += "-- Windows 5.1 == Windows XP SP3" + Environment.NewLine;
                    }
                    if (osData.Count() >= 3)
                    {
                        osDetails += "- OS Build: " + osData[1] + Environment.NewLine;
                        osDetails += "- OS Workgroup: " + osData[2] + Environment.NewLine;
                    }
                }
                osDetails = "- Unable to get basic OS info :(" + Environment.NewLine;
                return osDetails;
            }
            catch (Exception ex)
            {
                return $"- Cannot find OS Details: {ex.Message} - Bug Reelix!" + Environment.NewLine;
            }
        }

        private static string TestAnonymousAccess_Linux(string target)
        {
            if (General.IsInstalledOnLinux("smbclient", "/usr/bin/smbclient"))
            {
                string smbClientItems = "";
                List<string> processResults = General.GetProcessOutput("smbclient", $" -L {target} --no-pass -g"); // null auth
                if (processResults.Count == 1 && processResults[0].Contains("NT_STATUS_ACCESS_DENIED"))
                {
                    return "- No Anonymous Access";
                }
                else if (processResults.Count == 1 && processResults[0].Contains("NT_STATUS_CONNECTION_DISCONNECTED"))
                {
                    return "- It connected, but instantly disconnected you";
                }
                else if (processResults.Count == 2 && processResults[0] == "Anonymous login successful" && processResults[1] == "SMB1 disabled -- no workgroup available")
                {
                    return "- Anonymous Access Allowed - But No Shares Found";
                }
                else if (processResults.Count >= 1 && processResults[0].Contains("NT_STATUS_IO_TIMEOUT"))
                {
                    return "- Timed out :(";
                }
                foreach (string item in processResults)
                {
                    // type|name|comment
                    if (item.Trim() != "SMB1 disabled -- no workgroup available" && item.Trim() != "Anonymous login successful")
                    {
                        try
                        {
                            string itemType = item.Split('|')[0];
                            string itemName = item.Split('|')[1];
                            string itemComment = item.Split('|')[2];
                            smbClientItems += "- " + itemType + ": " + itemName + " " + (itemComment == "" ? "" : "(" + itemComment.Trim() + ")") + Environment.NewLine;
                            List<string> subProcessResults = General.GetProcessOutput("smbclient", $"//{target}/{itemName} --no-pass -c \"ls\"");
                            if (subProcessResults.Count > 1 && !subProcessResults.Any(x => x.Contains("NT_STATUS_ACCESS_DENIED") || x.Contains("NT_STATUS_OBJECT_NAME_NOT_FOUND")))
                            {
                                smbClientItems += "-- " + $"{itemName} has ls perms - {subProcessResults.Count} items found! -> smbclient //{target}/{itemName} --no-pass".Pastel(Color.Orange) + Environment.NewLine;
                                smbClientItems += "--- To download the entire contents, add -c \"recurse; prompt; mget *\"" + Environment.NewLine;
                            }
                            if (itemType == "IPC" && itemName == "IPC$")
                            {
                                if (itemComment.Contains("Samba Server"))
                                {
                                    smbClientItems += "-- Samba Detected".Pastel(Color.Orange) + Environment.NewLine;
                                    smbClientItems += "-- If version Samba 3.5.0 < 4.4.14/4.5.10/4.6.4, https://www.exploit-db.com/exploits/42084 / msfconsole -x \"use /exploit/linux/samba/is_known_pipename\"" + Environment.NewLine;
                                }    
                            }
                        }
                        catch (Exception ex)
                        {
                            if (ex.Message.Contains("NT_STATUS_IO_TIMEOUT"))
                            {
                                smbClientItems = "-- Timeout - Try later :(" + Environment.NewLine;
                            }
                            else
                            {
                                Console.WriteLine($"TestAnonymousAccess_Linux - Error: {ex.Message} - Invalid item: {item} - Bug Reelix!");
                            }
                        }
                    }
                }
                return smbClientItems.Trim(Environment.NewLine.ToCharArray());
            }
            else
            {
                return "- Error: Cannot find /usr/bin/smbclient - Please install it".Pastel(Color.Red);
            }
        }

        public static void SMBBrute(string[] args)
        {
            // TODO: This still shows "Success" if:
            // - The username doesn't exist
            // - There is a space in the password
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Console.WriteLine("SMB Brute only currently works in Linux - Heh :p");
                return;
            }
            if (args.Length != 4)
            {
                Console.WriteLine("SMB Brute Usage: reecon -smb-brute IP Userfile Passfile");
                return;
            }
            string ip = args[1];
            string userFile = args[2];
            string passFile = args[3];
            if (!File.Exists(userFile))
            {
                Console.WriteLine("Unable to find UserFile: " + userFile);
                return;
            }
            if (!File.Exists(passFile))
            {
                Console.WriteLine("Unable to find Passfile: " + passFile);
                return;
            }
            List<string> userList = File.ReadAllLines(userFile).ToList();
            List<string> passList = File.ReadAllLines(passFile).ToList();
            foreach (string user in userList)
            {
                foreach (string pass in passList)
                {
                    List<string> outputResult = General.GetProcessOutput("smbclient", @"-L \\\\" + ip + " -U" + user + "%" + pass); // Bug if pass contains a space?
                    outputResult.RemoveAll(x => x.Equals("Unable to initialize messaging context"));
                    string resultItem = outputResult[0];
                    if (resultItem.Contains("NT_STATUS_HOST_UNREACHABLE"))
                    {
                        Console.WriteLine("Error - Unable to contact \\\\" + ip);
                        return;
                    }
                    else if (resultItem.Contains("NT_STATUS_LOGON_FAILURE"))
                    {
                        Console.WriteLine(user + ":" + pass + " - Failed");
                    }
                    else if (resultItem.Contains("NT_STATUS_UNSUCCESSFUL"))
                    {
                        Console.WriteLine("Fatal Error: " + resultItem);
                        return;
                    }
                    else
                    {
                        Console.WriteLine(user + ":" + pass + " - Success!");
                        return;
                    }
                }
            }
            // smbclient -L \\\\10.10.10.172 -USABatchJobs%SABatchJobs

        }

        // https://github.com/nixawk/labs/blob/master/MS17_010/smb_exploit.py
        private static byte[] negotiateProtoRequest()
        {
            byte[] netbios = new byte[]
            {
                0x00, // Message Type
                0x00, 0x00, 0x54 // Length
            };

            byte[] smbHeader = new byte[]
            {
                0xFF, 0x53, 0x4D, 0x42, // 'server_component': .SMB
                0x72,                   // 'smb_command': Negotiate Protocol
                0x00, 0x00, 0x00, 0x00, // 'nt_status'
                0x18,                   // 'flags'
                0x01, 0x28,             // 'flags2'
                0x00, 0x00,             // 'process_id_high'
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // 'signature'
                0x00, 0x00,             // 'reserved'
                0x00, 0x00,             // 'tree_id'
                0x2F, 0x4B,             // 'process_id'
                0x00, 0x00,             // 'user_id'
                0xC5, 0x5E              // 'multiplex_id'
            };

            byte[] negotiateProtoRequest = new byte[]
            {
                0x00, // 'word_count'
                0x31, 0x00, // 'byte_count'
                
                // Requested Dialects
                0x02, // 'dialet_buffer_format'
                0x4C, 0x41, 0x4E, 0x4D, 0x41, 0x4E, 0x31, 0x2E, 0x30, 0x00, // 'dialet_name': LANMAN1.0
                
                0x02, // 'dialet_buffer_format'
                0x4C, 0x4D, 0x31, 0x2E, 0x32, 0x58, 0x30, 0x30, 0x32, 0x00, // 'dialet_name': LM1.2X002

                0x02, // 'dialet_buffer_format'
                0x4E, 0x54, 0x20, 0x4C, 0x41, 0x4E, 0x4D, 0x41, 0x4E, 0x20, 0x31, 0x2E, 0x30, 0x00, // 'dialet_name3': NT LANMAN 1.0
                
                0x02, // 'dialet_buffer_format'
                0x4E, 0x54, 0x20, 0x4C, 0x4D, 0x20, 0x30, 0x2E, 0x31, 0x32, 0x00 // 'dialet_name4': NT LM 0.12
            };

            return netbios.Concat(smbHeader).Concat(negotiateProtoRequest).ToArray();
        }

        public static byte[] sessionSetupAndxRequest()
        {
            byte[] netbios = new byte[] { 0x00, 0x00, 0x00, 0x63 };
            byte[] smbHeader = new byte[]
            {
                0xFF, 0x53, 0x4D, 0x42,
                0x73,
                0x00, 0x00, 0x00, 0x00,
                0x18,
                0x01, 0x20,
                0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00,
                0x00, 0x00,
                0x2F, 0x4B,
                0x00, 0x00,
                0xC5, 0x5E
            };

            byte[] setupAndxRequest = new byte[]
            {
                0x0D,
                0xFF,
                0x00,
                0x00, 0x00,
                0xDF, 0xFF,
                0x02, 0x00,
                0x01, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00,
                0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x40, 0x00, 0x00, 0x00,
                0x26, 0x00,
                0x00,
                0x2e, 0x00,
                0x57, 0x69, 0x6e, 0x64, 0x6f, 0x77, 0x73, 0x20, 0x32, 0x30, 0x30, 0x30, 0x20, 0x32, 0x31, 0x39, 0x35, 0x00,
                0x57, 0x69, 0x6e, 0x64, 0x6f, 0x77, 0x73, 0x20, 0x32, 0x30, 0x30, 0x30, 0x20, 0x35, 0x2e, 0x30, 0x00,
            };

            return netbios.Concat(smbHeader).Concat(setupAndxRequest).ToArray();
        }
    }
}
