﻿#region LICENSE
// ---------------------------------- LICENSE ---------------------------------- //
//
//    Fling OS - The educational operating system
//    Copyright (C) 2015 Edward Nutting
//
//    This program is free software: you can redistribute it and/or modify
//    it under the terms of the GNU General Public License as published by
//    the Free Software Foundation, either version 2 of the License, or
//    (at your option) any later version.
//
//    This program is distributed in the hope that it will be useful,
//    but WITHOUT ANY WARRANTY; without even the implied warranty of
//    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//    GNU General Public License for more details.
//
//    You should have received a copy of the GNU General Public License
//    along with this program.  If not, see <http://www.gnu.org/licenses/>.
//
//  Project owner: 
//		Email: edwardnutting@outlook.com
//		For paper mail address, please contact via email for details.
//
// ------------------------------------------------------------------------------ //
#endregion
    
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Drivers.Debugger
{
    public delegate void NotificationHandler(NotificationEventArgs e, object sender);

    public sealed class Debugger : IDisposable
    {
        private Serial MsgSerial;
        private Serial NotifSerial;
        private DebugDataReader DebugData;

        private bool terminating;
        public bool Terminating
        {
            get
            {
                return terminating;
            }
            set
            {
                terminating = true;
                NotifSerial.AbortRead = true;
            }
        }

        public bool Ready
        {
            get;
            private set;
        }

        private bool NotificationReceived = false;
        private bool WaitingForNotification = false;
        public event NotificationHandler NotificationEvent;

        public Debugger()
        {
            MsgSerial = new Serial();
        }
        public void Dispose()
        {
            Terminating = true;
            
            MsgSerial.Dispose();
            MsgSerial = null;
            NotifSerial.Dispose();
            NotifSerial = null;
        }

        public bool Init(string PipeName, string BinFolderPath, string AssemblyName)
        {
            DebugData = new DebugDataReader();
            DebugData.ReadDataFiles(BinFolderPath, AssemblyName);

            MsgSerial = new Serial();
            NotifSerial = new Serial();
            MsgSerial.OnConnected += MsgSerial_OnConnected;
            return MsgSerial.Init(PipeName + "_Msg") && NotifSerial.Init(PipeName + "_Notif");
        }

        private void MsgSerial_OnConnected()
        {
            string str;
            while((str = MsgSerial.ReadLine()) != "Debug thread :D")
            {
                System.Threading.Thread.Sleep(100);
            }
            Ready = true;

            Task.Run((Action)ProcessNotifications);
        }

        private void ProcessNotifications()
        {
            while (!Terminating)
            {
                try
                {
                    byte NotifByte = NotifSerial.ReadBytes(1)[0];
                    if (NotificationEvent != null)
                    {
                        NotificationReceived = true;
                        if (!WaitingForNotification)
                        {
                            NotificationEvent.Invoke(new NotificationEventArgs()
                            {
                                NotificationByte = NotifByte
                            }, this);
                        }
                    }
                }
                catch
                {
                }
            }
        }

        public string[] ExecuteCommand(string cmd)
        {
            MsgSerial.WriteLine(cmd);

            // First line should be command echo
            {
                string line = MsgSerial.ReadLine();
                if (line.Trim().ToLower() != cmd.Trim().ToLower())
                {
                    while ((line = MsgSerial.ReadLine()) != "END OF COMMAND")
                    {
                    }

                    return null;
                }
            }

            return ReadToEndOfCommand();
        }
        public void AbortCommand()
        {
            MsgSerial.AbortRead = true;
        }

        public bool GetPing()
        {
            try
            {
                string[] Lines = ExecuteCommand("ping");
                if (Lines[0] == "pong")
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }
        public Dictionary<uint, Process> GetThreads()
        {
            try
            {
                string[] Lines = ExecuteCommand("threads");

                Dictionary<uint, Process> Processes = new Dictionary<uint, Process>();
                Process CurrentProcess = null;

                foreach (string Line in Lines)
                {
                    string[] LineParts = Line.Split(':').Select(x => x.Trim()).ToArray();
                    if (LineParts[0] == "- Process")
                    {
                        uint Id = uint.Parse(LineParts[1].Substring(2), System.Globalization.NumberStyles.HexNumber);
                        CurrentProcess = new Process()
                        {
                            Id = Id,
                            Name = LineParts[2]
                        };
                        Processes.Add(Id, CurrentProcess);
                    }
                    else if (LineParts[0] == "- Thread")
                    {
                        uint Id = uint.Parse(LineParts[1].Substring(2), System.Globalization.NumberStyles.HexNumber);
                        CurrentProcess.Threads.Add(Id, new Thread()
                        {
                            Id = Id,
                            Name = LineParts[3],
                            State = (Thread.States)Enum.Parse(typeof(Thread.States), LineParts[2])
                        });
                    }
                }

                return Processes;
            }
            catch
            {
                return new Dictionary<uint, Process>();
            }
        }
        public Dictionary<string, uint> GetRegisters(uint ProcessId, uint ThreadId)
        {
            Dictionary<string, uint> Result = new Dictionary<string, uint>();

            try
            {
                string[] Lines = ExecuteCommand("regs " + ProcessId.ToString() + " " + ThreadId.ToString());

                for (int i = 1; i < Lines.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(Lines[i]))
                    {
                        string[] LineParts = Lines[i].Split(':');
                        string Reg = LineParts[0].Trim().Substring(2);
                        string ValStr = LineParts[1].Trim();
                        uint Val = uint.Parse(ValStr.Substring(2), System.Globalization.NumberStyles.HexNumber);
                        Result.Add(Reg, Val);
                    }
                }
            }
            catch
            {
                Result.Clear();
            }

            return Result;
        }
        public bool SuspendThread(uint ProcessId, int ThreadId)
        {
            try
            {
                string[] Lines = ExecuteCommand("suspend " + ProcessId.ToString() + " " + ThreadId.ToString());
                return true;
            }
            catch
            {
                return false;
            }
        }
        public bool ResumeThread(uint ProcessId, int ThreadId)
        {
            try
            {
                string[] Lines = ExecuteCommand("resume " + ProcessId.ToString() + " " + ThreadId.ToString());
                return true;
            }
            catch
            {
                return false;
            }
        }
        public bool StepThread(uint ProcessId, int ThreadId)
        {
            try
            {
                BeginWaitForNotification();
                string[] Lines = ExecuteCommand("step " + ProcessId.ToString() + " " + ThreadId.ToString());
                EndWaitForNotification();
                return true;
            }
            catch
            {
                return false;
            }
        }
        public bool SingleStepThread(uint ProcessId, int ThreadId)
        {
            try
            {
                BeginWaitForNotification();
                string[] Lines = ExecuteCommand("ss " + ProcessId.ToString() + " " + ThreadId.ToString());
                EndWaitForNotification();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public Tuple<uint, string> GetNearestLabel(uint Address)
        {
            while (!DebugData.AddressMappings.ContainsKey(Address) && Address > 0)
            {
                Address--;
            }

            if (DebugData.AddressMappings.ContainsKey(Address))
            {
                return new Tuple<uint, string>(Address, DebugData.AddressMappings[Address].OrderBy(x => x.Length).First());
            }
            else
            {
                return null;
            }
        }
        public string GetMethodLabel(string FullLabel)
        {
            return FullLabel.Split('.')[0];
        }

        private string[] ReadToEndOfCommand()
        {
            List<string> Result = new List<string>();
            string str;
            while ((str = MsgSerial.ReadLine()) != "END OF COMMAND")
            {
                Result.Add(str);
            }
            return Result.ToArray();
        }
        private void BeginWaitForNotification()
        {
            WaitingForNotification = true;
            NotificationReceived = false;
        }
        private void EndWaitForNotification()
        {
            while (!NotificationReceived && !Terminating)
            {
                System.Threading.Thread.Sleep(50);
            }

            WaitingForNotification = false;
        }
    }

    public class NotificationEventArgs : EventArgs
    {
        public byte NotificationByte;
    }
}
