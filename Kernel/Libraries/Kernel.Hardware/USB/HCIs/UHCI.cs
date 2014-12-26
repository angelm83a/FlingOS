﻿#region Copyright Notice
// ------------------------------------------------------------------------------ //
//                                                                                //
//               All contents copyright � Edward Nutting 2014                     //
//                                                                                //
//        You may not share, reuse, redistribute or otherwise use the             //
//        contents this file outside of the Fling OS project without              //
//        the express permission of Edward Nutting or other copyright             //
//        holder. Any changes (including but not limited to additions,            //
//        edits or subtractions) made to or from this document are not            //
//        your copyright. They are the copyright of the main copyright            //
//        holder for all Fling OS files. At the time of writing, this             //
//        owner was Edward Nutting. To be clear, owner(s) do not include          //
//        developers, contributors or other project members.                      //
//                                                                                //
// ------------------------------------------------------------------------------ //
#endregion

using System;
using Kernel.FOS_System.Collections;
using Kernel.Hardware.USB.Devices;
using Utils = Kernel.Utilities.ConstantsUtils;
using Kernel.Utilities;

namespace Kernel.Hardware.USB.HCIs
{
    public class UHCI_Consts
    {
        public static byte PORTMAX = 8;

        public static byte USBCMD = 0x00;
        public static byte USBSTS = 0x02;
        public static byte USBINTR = 0x04;
        public static byte FRNUM = 0x06;
        public static byte FRBASEADD = 0x08;
        public static byte SOFMOD = 0x0C;
        public static byte PORTSC1 = 0x10;
        public static byte PORTSC2 = 0x12;

        public static ushort CMD_MAXP = (ushort)Utils.BIT(7);
        public static ushort CMD_CF = (ushort)Utils.BIT(6);
        public static ushort CMD_SWDBG = (ushort)Utils.BIT(5);
        public static ushort CMD_FGR = (ushort)Utils.BIT(4);
        public static ushort CMD_EGSM = (ushort)Utils.BIT(3);
        public static ushort CMD_GRESET = (ushort)Utils.BIT(2);
        public static ushort CMD_HCRESET = (ushort)Utils.BIT(1);
        public static ushort CMD_RS = (ushort)Utils.BIT(0);

        public static ushort STS_HCHALTED = (ushort)Utils.BIT(5);
        public static ushort STS_HC_PROCESS_ERROR = (ushort)Utils.BIT(4);
        public static ushort STS_HOST_SYSTEM_ERROR = (ushort)Utils.BIT(3);
        public static ushort STS_RESUME_DETECT = (ushort)Utils.BIT(2);
        public static ushort STS_USB_ERROR = (ushort)Utils.BIT(1);
        public static ushort STS_USBINT = (ushort)Utils.BIT(0);
        public static ushort STS_MASK = 0x3F;

        public static ushort INT_SHORT_PACKET_ENABLE = (ushort)Utils.BIT(3);
        public static ushort INT_IOC_ENABLE = (ushort)Utils.BIT(2);
        public static ushort INT_RESUME_ENABLE = (ushort)Utils.BIT(1);
        public static ushort INT_TIMEOUT_ENABLE = (ushort)Utils.BIT(0);
        public static ushort INT_MASK = 0xF;

        public static ushort SUSPEND = (ushort)Utils.BIT(12);
        public static ushort PORT_RESET = (ushort)Utils.BIT(9);
        public static ushort PORT_LOWSPEED_DEVICE = (ushort)Utils.BIT(8);
        public static ushort PORT_VALID = (ushort)Utils.BIT(7); // reserved an readonly; always read as 1
        public static ushort PORT_RESUME_DETECT = (ushort)Utils.BIT(6);
        public static ushort PORT_ENABLE_CHANGE = (ushort)Utils.BIT(3);
        public static ushort PORT_ENABLE = (ushort)Utils.BIT(2);
        public static ushort PORT_CS_CHANGE = (ushort)Utils.BIT(1);
        public static ushort PORT_CS = (ushort)Utils.BIT(0);

        /*
        Packet Identification (PID). This field contains the Packet ID to be used for this transaction. Only
        the IN (69h), OUT (E1h), and SETUP (2Dh) tokens are allowed. Any other value in this field causes
        a consistency check failure resulting in an immediate halt of the Host Controller. Bits [3:0] are
        complements of bits [7:4].
        */
        public static byte TD_SETUP = 0x2D; // 00101101
        public static byte TD_IN = 0x69; // 11100001
        public static byte TD_OUT = 0xE1; // 11100001


        // Register in PCI (uint16_t)
        public static byte PCI_LEGACY_SUPPORT = 0xC0;
        // Interrupt carried out as a PCI interrupt
        public static ushort PCI_LEGACY_SUPPORT_PIRQ = 0x2000;
        // RO Bits
        public static ushort PCI_LEGACY_SUPPORT_NO_CHG = 0x5040;
        // Status bits that are cleared by setting to 1
        public static ushort PCI_LEGACY_SUPPORT_STATUS = 0x8F00;

        public static uint BIT_T = Utils.BIT(0);
        public static uint BIT_QH = Utils.BIT(1);
        public static uint BIT_Vf = Utils.BIT(2);

        public static int NUMBER_OF_UHCI_RETRIES = 3;
    }

    //TODO: Something is wrong with this UHCI driver.
    //  - Interrupts never occur
    //  Literally no ideas why!

    public unsafe class UHCI : HCI
    {
        protected byte* usbBaseAddress;
        protected bool EnabledPorts = false;
        protected bool run = false;

        protected IO.IOPort USBCMD;
        protected IO.IOPort USBINTR;
        protected IO.IOPort USBSTS;
        protected IO.IOPort SOFMOD;
        protected IO.IOPort FRBASEADD;
        protected IO.IOPort FRNUM;
        protected IO.IOPort PORTSC1;
        protected IO.IOPort PORTSC2;

        protected uint* FrameList;

        protected uint TransactionsCompleted = 0;

        protected UHCI_QueueHead_Struct* qhPointer;

        public UHCI(PCI.PCIDeviceNormal aPCIDevice)
            : base(aPCIDevice)
        {
            BasicConsole.WriteLine("UHCI: Constructor");
            BasicConsole.DelayOutput(5);

            usbBaseAddress = pciDevice.BaseAddresses[4].BaseAddress();
            Processes.ProcessManager.CurrentProcess.TheMemoryLayout.AddDataPage(
                (uint)usbBaseAddress & 0xFFFFF000,
                (uint)usbBaseAddress & 0xFFFFF000);
            VirtMemManager.Map((uint)usbBaseAddress & 0xFFFFF000, (uint)usbBaseAddress & 0xFFFFF000, 4096,
                VirtMem.VirtMemImpl.PageFlags.KernelOnly);

            RootPortCount = UHCI_Consts.PORTMAX;
            EnabledPorts = false;

            USBCMD = new IO.IOPort(MapPort(UHCI_Consts.USBCMD));
            USBINTR = new IO.IOPort(MapPort(UHCI_Consts.USBINTR));
            USBSTS = new IO.IOPort(MapPort(UHCI_Consts.USBSTS));
            SOFMOD = new IO.IOPort(MapPort(UHCI_Consts.SOFMOD));
            FRBASEADD = new IO.IOPort(MapPort(UHCI_Consts.FRBASEADD));
            FRNUM = new IO.IOPort(MapPort(UHCI_Consts.FRNUM));
            PORTSC1 = new IO.IOPort(MapPort(UHCI_Consts.PORTSC1));
            PORTSC2 = new IO.IOPort(MapPort(UHCI_Consts.PORTSC2));

            FrameList = (uint*)VirtMemManager.MapFreePage(VirtMem.VirtMemImpl.PageFlags.KernelOnly);
            Processes.ProcessManager.CurrentProcess.TheMemoryLayout.AddDataPage(
                (uint)FrameList & 0xFFFFF000,
                (uint)FrameList & 0xFFFFF000);

            Start();
        }

        protected ushort MapPort(uint portOffset)
        {
            uint portAddr = (uint)(usbBaseAddress + portOffset);

            if ((portAddr & 0xFFFFF000) !=
                ((uint)usbBaseAddress & 0xFFFFF000))
            {
                Processes.ProcessManager.CurrentProcess.TheMemoryLayout.AddDataPage(
                    portAddr & 0xFFFFF000,
                    portAddr & 0xFFFFF000);
                VirtMemManager.Map(portAddr & 0xFFFFF000, portAddr & 0xFFFFF000, 4096,
                    VirtMem.VirtMemImpl.PageFlags.KernelOnly);
            }

            return (ushort)portAddr;
        }

        protected void Start()
        {
            BasicConsole.WriteLine("UHCI: Start");
            BasicConsole.DelayOutput(5);

            InitHC();
        }
        protected void InitHC()
        {
            BasicConsole.WriteLine("UHCI: InitHC");
            BasicConsole.DelayOutput(5);

            BasicConsole.WriteLine(((FOS_System.String)"IRQ: ") + pciDevice.InterruptLine);
            BasicConsole.WriteLine(((FOS_System.String)"USBCMD Port: ") + USBCMD.Port);
            BasicConsole.DelayOutput(5);

            // prepare PCI command register
            // bit 9: Fast Back-to-Back Enable // not necessary
            // bit 2: Bus Master               // cf. http://forum.osdev.org/viewtopic.php?f=1&t=20255&start=0
            pciDevice.Command = pciDevice.Command | PCI.PCIDevice.PCICommand.IO | PCI.PCIDevice.PCICommand.Master;

            Interrupts.Interrupts.AddIRQHandler(pciDevice.InterruptLine, UHCI.InterruptHandler, this, false);

            ResetHC();
        }
        protected void ResetHC()
        {
            BasicConsole.WriteLine("UHCI: ResetHC");
            BasicConsole.DelayOutput(5);

            //Processes.Scheduler.Disable();

            // http://www.lowlevel.eu/wiki/Universal_Host_Controller_Interface#Informationen_vom_PCI-Treiber_holen

            ushort legacySupport = pciDevice.ReadRegister16(UHCI_Consts.PCI_LEGACY_SUPPORT);
            pciDevice.WriteRegister16(UHCI_Consts.PCI_LEGACY_SUPPORT, UHCI_Consts.PCI_LEGACY_SUPPORT_STATUS); // resets support status bits in Legacy support register
                
            USBCMD.Write_UInt16(UHCI_Consts.CMD_GRESET);
            Hardware.Devices.Timer.Default.Wait(50);
            USBCMD.Write_UInt16(0);
            
            RootPortCount = (byte)(pciDevice.BaseAddresses[4].Size() / 2);
            BasicConsole.WriteLine(((FOS_System.String)"UHCI: RootPortCount=") + RootPortCount);
            for (byte i = 2; i < RootPortCount; i++)
            {
                if ((PORTSC1.Read_UInt16((ushort)(i * 2)) & UHCI_Consts.PORT_VALID) == 0 ||
                   (PORTSC1.Read_UInt16((ushort)(i * 2)) == 0xFFFF))
                {
                    RootPortCount = i;
                    break;
                }
            }
            BasicConsole.WriteLine(((FOS_System.String)"UHCI: RootPortCount=") + RootPortCount);

            if (RootPortCount > UHCI_Consts.PORTMAX)
            {
                RootPortCount = UHCI_Consts.PORTMAX;
            }
            BasicConsole.WriteLine(((FOS_System.String)"UHCI: RootPortCount=") + RootPortCount);
            BasicConsole.DelayOutput(1);

            RootPorts.Empty();
            for (byte i = 0; i < RootPortCount; i++)
            {
                RootPorts.Add(new HCPort()
                {
                    portNum = i
                });
            }

            BasicConsole.WriteLine("UHCI: Checking HC state: Get USBCMD...");
            BasicConsole.DelayOutput(1);

            ushort usbcmd = USBCMD.Read_UInt16();
            BasicConsole.WriteLine("UHCI: Checking HC state: Check...");
            BasicConsole.DelayOutput(1);
            if ((legacySupport & ~(UHCI_Consts.PCI_LEGACY_SUPPORT_STATUS | UHCI_Consts.PCI_LEGACY_SUPPORT_NO_CHG | UHCI_Consts.PCI_LEGACY_SUPPORT_PIRQ)) != 0 ||
                (usbcmd & UHCI_Consts.CMD_RS) != 0 ||
                (usbcmd & UHCI_Consts.CMD_CF) != 0 ||
                (usbcmd & UHCI_Consts.CMD_EGSM) == 0 ||
                (USBINTR.Read_UInt16() & UHCI_Consts.INT_MASK) != 0)
            {
                BasicConsole.WriteLine("UHCI: Checking HC state: Do reset...");
                BasicConsole.DelayOutput(1);

                USBSTS.Write_UInt16(UHCI_Consts.STS_MASK);
                Hardware.Devices.Timer.Default.Wait(1);
                USBCMD.Write_UInt16(UHCI_Consts.CMD_HCRESET);

                byte timeout = 50;
                while ((USBCMD.Read_UInt16() & UHCI_Consts.CMD_HCRESET) != 0)
                {
                    if (timeout == 0)
                    {
                        BasicConsole.WriteLine("UHCI: HC Reset timed out!");
                        BasicConsole.DelayOutput(1);
                        break;
                    }
                    Hardware.Devices.Timer.Default.Wait(10);
                    timeout--;
                }

                BasicConsole.WriteLine("UHCI: Checking HC state: Turning off interrupts and HC...");
                BasicConsole.DelayOutput(1);

                USBINTR.Write_UInt16(0); // switch off all interrupts
                USBCMD.Write_UInt16(0); // switch off the host controller

                BasicConsole.WriteLine("UHCI: Checking HC state: Disabling ports...");
                BasicConsole.DelayOutput(1);

                for (byte i = 0; i < RootPortCount; i++) // switch off the valid root ports
                {
                    PORTSC1.Write_UInt16(0, (ushort)(i * 2));
                }
            }

            // TODO: mutex for frame list

            BasicConsole.WriteLine("UHCI: Creating queue head...");
            BasicConsole.DelayOutput(1);

            UHCI_QueueHead_Struct* qh = (UHCI_QueueHead_Struct*)FOS_System.Heap.Alloc((uint)sizeof(UHCI_QueueHead_Struct), 16);
            qh->next = (UHCI_QueueHead_Struct*)UHCI_Consts.BIT_T;
            qh->transfer = (UHCI_qTD_Struct*)UHCI_Consts.BIT_T;
            qh->q_first = null;
            qh->q_last = null;
            qhPointer = qh;

            BasicConsole.WriteLine("UHCI: Setting up frame list entries...");
            BasicConsole.DelayOutput(1);

            for (ushort i = 0; i < 1024; i++)
            {
                FrameList[i] = UHCI_Consts.BIT_T;
            }

            BasicConsole.WriteLine("UHCI: Setting SOFMOD...");
            BasicConsole.DelayOutput(1);

            // define each millisecond one frame, provide physical address of frame list, and start at frame 0
            SOFMOD.Write_Byte(0x40); // SOF cycle time: 12000. For a 12 MHz SOF counter clock input, this produces a 1 ms Frame period.

            BasicConsole.WriteLine("UHCI: Setting frame base addr and frame num...");
            BasicConsole.DelayOutput(1);

            FRBASEADD.Write_UInt32((uint)VirtMemManager.GetPhysicalAddress(FrameList));
            FRNUM.Write_UInt16(0);

            BasicConsole.WriteLine("UHCI: Setting PCI PIRQ...");
            BasicConsole.DelayOutput(1);

            // set PIRQ
            pciDevice.WriteRegister16(UHCI_Consts.PCI_LEGACY_SUPPORT, UHCI_Consts.PCI_LEGACY_SUPPORT_PIRQ);

            BasicConsole.WriteLine("UHCI: Starting HC...");
            BasicConsole.DelayOutput(1);

            // start host controller and mark it configured with a 64-byte max packet
            USBSTS.Write_UInt16(UHCI_Consts.STS_MASK);
            USBINTR.Write_UInt16(UHCI_Consts.INT_MASK); // switch on all interrupts
            USBCMD.Write_UInt16((ushort)(UHCI_Consts.CMD_RS | UHCI_Consts.CMD_CF | UHCI_Consts.CMD_MAXP));
            
            BasicConsole.WriteLine("UHCI: Reset CSC ports...");
            BasicConsole.DelayOutput(1);

            for (byte i = 0; i < RootPortCount; i++) // reset the CSC of the valid root ports
            {
                PORTSC1.Write_UInt16(UHCI_Consts.PORT_CS_CHANGE, (ushort)(i * 2));
            }

            BasicConsole.WriteLine("UHCI: Forcing global resume...");
            BasicConsole.DelayOutput(1);

            USBSTS.Write_UInt16(UHCI_Consts.STS_MASK);
            BasicConsole.WriteLine("     - STS MASK set");
            USBCMD.Write_UInt16((ushort)(UHCI_Consts.CMD_RS | UHCI_Consts.CMD_CF | UHCI_Consts.CMD_MAXP | UHCI_Consts.CMD_FGR));
            BasicConsole.WriteLine("     - FGR issued");
            Hardware.Devices.Timer.Default.Wait(20);
            USBCMD.Write_UInt16((ushort)(UHCI_Consts.CMD_RS | UHCI_Consts.CMD_CF | UHCI_Consts.CMD_MAXP));
            BasicConsole.WriteLine("     - FGR cleared");
            
            BasicConsole.DelayOutput(1);

            //Hardware.Devices.Timer.Default.Wait(100);

            BasicConsole.WriteLine("UHCI: Getting run state...");
            BasicConsole.DelayOutput(1);

            run = (USBCMD.Read_UInt16() & UHCI_Consts.CMD_RS) != 0;

            //Processes.Scheduler.Enable();

            if (!run)
            {
                BasicConsole.SetTextColour(BasicConsole.error_colour);
                BasicConsole.WriteLine("UHCI: Run/Stop not set!");
                BasicConsole.SetTextColour(BasicConsole.default_colour);
                BasicConsole.DelayOutput(5);
            }
            else
            {
                if ((USBSTS.Read_UInt16() & UHCI_Consts.STS_HCHALTED) == 0)
                {
                    EnablePorts(); // attaches the ports
                }
                else
                {
                    BasicConsole.SetTextColour(BasicConsole.error_colour);
                    BasicConsole.WriteLine("UHCI: HC Halted!");
                    BasicConsole.SetTextColour(BasicConsole.default_colour);
                    BasicConsole.DelayOutput(5);
                }
            }
        }        
        protected void EnablePorts()
        {
            BasicConsole.WriteLine("UHCI: Enable ports");
            BasicConsole.DelayOutput(5);

            EnabledPorts = true;

            for (byte j = 0; j < RootPortCount; j++)
            {
                ResetPort(j);

                ushort val = PORTSC1.Read_UInt16((ushort)(2 * j));
                BasicConsole.WriteLine(((FOS_System.String)"UHCI: Port ") + j + " : " + val);
                AnalysePortStatus(j, val);
            }

            //TODO: Shift this EnabledPorts = true; to this location
        }
        protected void ResetPort(byte port)
        {
            BasicConsole.WriteLine(((FOS_System.String)"UHCI: Reset port : ") + port);
            BasicConsole.DelayOutput(5);

            //Processes.Scheduler.Disable();

            ushort portOffset = (ushort)(port * 2);

            PORTSC1.Write_UInt16((ushort)(UHCI_Consts.PORT_CS_CHANGE | UHCI_Consts.PORT_ENABLE_CHANGE), portOffset);

            PORTSC1.Write_UInt16(UHCI_Consts.PORT_RESET, portOffset);
            Hardware.Devices.Timer.Default.Wait(60); // do not delete this wait
            PORTSC1.Write_UInt16((ushort)(PORTSC1.Read_UInt16(portOffset) & ~UHCI_Consts.PORT_RESET), portOffset); // clear reset bit
            Hardware.Devices.Timer.Default.Wait(20);

            for (int i = 0; i < 10; i++)
            {
                ushort val = PORTSC1.Read_UInt16(portOffset);

                if ((val & UHCI_Consts.PORT_CS) == 0)
                {
                    BasicConsole.WriteLine("UHCI: Nothing attached so not enabling.");
                    BasicConsole.DelayOutput(1);

                    //Nothing attached so don't enable
                    return;
                }

                if ((val & (UHCI_Consts.PORT_ENABLE_CHANGE | UHCI_Consts.PORT_CS_CHANGE)) != 0)
                {
                    PORTSC1.Write_UInt16((ushort)(UHCI_Consts.PORT_ENABLE_CHANGE | UHCI_Consts.PORT_CS_CHANGE), portOffset);
                }

                if ((val & UHCI_Consts.PORT_ENABLE) != 0)
                {
                    BasicConsole.WriteLine("UHCI: Nothing attached so not enabling.");
                    BasicConsole.DelayOutput(1);

                    break;
                }
            }

            BasicConsole.WriteLine("UHCI: Enabling...");
            BasicConsole.DelayOutput(1);

            // Enable
            PORTSC1.Write_UInt16(0xF, portOffset);
            Hardware.Devices.Timer.Default.Wait(10);

            //Processes.Scheduler.Enable();

            BasicConsole.WriteLine(((FOS_System.String)"UHCI: Port ") + port + " reset and enabled.");
            BasicConsole.DelayOutput(5);
        }

        protected static void InterruptHandler(FOS_System.Object data)
        {
            ((UHCI)data).InterruptHandler();
        }
        protected void InterruptHandler()
        {
            BasicConsole.SetTextColour(BasicConsole.warning_colour);
            BasicConsole.WriteLine("UHCI: Interrupt handler");
            BasicConsole.SetTextColour(BasicConsole.default_colour);
            BasicConsole.DelayOutput(20);

            ushort val = USBSTS.Read_UInt16();

            if (val == 0) // Interrupt came from another UHCI device
            {
                return;
            }

            //if ((val & UHCI_Consts.STS_USBINT) == 0)
            //{
            //    //printf("\nUSB UHCI %u: ", u->num);
            //}

            //textColor(IMPORTANT);

            BasicConsole.SetTextColour(BasicConsole.warning_colour);

            if ((val & UHCI_Consts.STS_USBINT) != 0)
            {
                BasicConsole.WriteLine(((FOS_System.String)"UHCI Frame: ") + FRNUM.Read_UInt16() + " - USB transaction completed");
                USBSTS.Write_UInt16(UHCI_Consts.STS_USBINT); // reset interrupt
                TransactionsCompleted++;
            }

            if ((val & UHCI_Consts.STS_RESUME_DETECT) != 0)
            {
                BasicConsole.WriteLine("UHCI: Resume Detect");
                USBSTS.Write_UInt16(UHCI_Consts.STS_RESUME_DETECT); // reset interrupt
            }

            BasicConsole.SetTextColour(BasicConsole.error_colour);

            if ((val & UHCI_Consts.STS_HCHALTED) != 0)
            {
                BasicConsole.WriteLine("UHCI: Host Controller Halted");
                USBSTS.Write_UInt16(UHCI_Consts.STS_HCHALTED); // reset interrupt
            }

            if ((val & UHCI_Consts.STS_HC_PROCESS_ERROR) != 0)
            {
                BasicConsole.WriteLine("UHCI: Host Controller Process Error");
                USBSTS.Write_UInt16(UHCI_Consts.STS_HC_PROCESS_ERROR); // reset interrupt
            }

            if ((val & UHCI_Consts.STS_USB_ERROR) != 0)
            {
                BasicConsole.WriteLine("UHCI: USB Error");
                USBSTS.Write_UInt16(UHCI_Consts.STS_USB_ERROR); // reset interrupt
            }

            if ((val & UHCI_Consts.STS_HOST_SYSTEM_ERROR) != 0)
            {
                BasicConsole.WriteLine("UHCI: Host System Error");
                USBSTS.Write_UInt16(UHCI_Consts.STS_HOST_SYSTEM_ERROR); // reset interrupt
            }

            BasicConsole.SetTextColour(BasicConsole.default_colour);
        }
        
        protected void AnalysePortStatus(byte j, ushort val)
        {
            BasicConsole.WriteLine("UHCI: Anaylse port status");
            BasicConsole.DelayOutput(5);

            HCPort port = GetPort(j);
            if ((val & UHCI_Consts.PORT_LOWSPEED_DEVICE) != 0)
            {
                BasicConsole.Write("UHCI: Lowspeed device");
                port.speed = USBPortSpeed.Low; // Save lowspeed/fullspeed information in data
            }
            else
            {
                BasicConsole.Write("UHCI: Fullspeed device");
                port.speed = USBPortSpeed.Full; // Save lowspeed/fullspeed information in data
            }

            if (((val & UHCI_Consts.PORT_CS) != 0) && !port.connected)
            {
                BasicConsole.WriteLine(" attached.");
                port.connected = true;
                ResetPort(j);      // reset on attached
                
                SetupUSBDevice(j);
            }
            else if (port.connected)
            {
                BasicConsole.WriteLine(" removed.");
                port.connected = false;

                if (port.deviceInfo != null)
                {
                    port.deviceInfo.FreePort();
                }
            }
        }
               
        //////////////////////
        //// analysis tools //
        //////////////////////

        //void uhci_showStatusbyteTD(uhciTD_t* TD)
        //{
        //    textColor(ERROR);
        //    if (TD->bitstuffError)     printf("\nBitstuff Error");          // receive data stream contained a sequence of more than 6 ones in a row
        //    if (TD->crc_timeoutError)  printf("\nNo Response from Device"); // no response from the device (CRC or timeout)
        //    if (TD->nakReceived)       printf("\nNAK received");            // NAK handshake
        //    if (TD->babbleDetected)    printf("\nBabble detected");         // Babble (fatal error), e.g. more data from the device than MAXP
        //    if (TD->dataBufferError)   printf("\nData Buffer Error");       // HC cannot keep up with the data  (overrun) or cannot supply data fast enough (underrun)
        //    if (TD->stall)             printf("\nStalled");                 // can be caused by babble, error counter (0) or STALL from device

        //    textColor(GRAY);
        //    if (TD->active)            printf("\nactive");                  // 1: HC will execute   0: set by HC after excution (HC will not excute next time)

        //  #ifdef _UHCI_DIAGNOSIS_
        //    textColor(IMPORTANT);
        //    if (TD->intOnComplete)     printf("\ninterrupt on complete");   // 1: HC issues interrupt on completion of the frame in which the TD is executed
        //    if (TD->isochrSelect)      printf("\nisochronous TD");          // 1: Isochronous TD
        //    if (TD->lowSpeedDevice)    printf("\nLowspeed Device");         // 1: LS   0: FS
        //    if (TD->shortPacketDetect) printf("\nShortPacketDetect");       // 1: enable   0: disable
        //  #endif

        //    textColor(TEXT);
        //}

        protected static void ShowPortState(ushort val)
        {
            if ((val & UHCI_Consts.PORT_RESET) != 0) { BasicConsole.WriteLine(" RESET"); }

            if ((val & UHCI_Consts.SUSPEND) != 0) { BasicConsole.WriteLine(" SUSPEND"); }
            if ((val & UHCI_Consts.PORT_RESUME_DETECT) != 0) { BasicConsole.WriteLine(" RESUME DETECT"); }

            if ((val & UHCI_Consts.PORT_LOWSPEED_DEVICE) != 0) { BasicConsole.WriteLine(" LOWSPEED DEVICE"); }
            else { BasicConsole.WriteLine(" FULLSPEED DEVICE"); }
            if ((val & Utils.BIT(5)) != 0) { BasicConsole.WriteLine(" Line State: D-"); }
            if ((val & Utils.BIT(4)) != 0) { BasicConsole.WriteLine(" Line State: D+"); }

            if ((val & UHCI_Consts.PORT_ENABLE_CHANGE) != 0) { BasicConsole.WriteLine(" ENABLE CHANGE"); }
            if ((val & UHCI_Consts.PORT_ENABLE) != 0) { BasicConsole.WriteLine(" ENABLED"); }

            if ((val & UHCI_Consts.PORT_CS_CHANGE) != 0) { BasicConsole.WriteLine(" DEVICE CHANGE"); }
            if ((val & UHCI_Consts.PORT_CS) != 0) { BasicConsole.WriteLine(" DEVICE ATTACHED"); }
            else { BasicConsole.WriteLine(" NO DEVICE ATTACHED"); }
        }


        protected bool isTransactionSuccessful(UHCITransaction uT)
        {
            BasicConsole.WriteLine("UHCI: Is Transaction Successful");
            BasicConsole.DelayOutput(5);

            //Zero bits:
            //  17, 18, 19, 20, 21, 22, 23
            return (uT.qTD->u1 & 0x00FE0000) == 0;
        }

        protected override void _SETUPTransaction(USBTransfer transfer, USBTransaction uTransaction, bool toggle, ushort tokenBytes, byte type, byte req, byte hiVal, byte loVal, ushort index, ushort length)
        {
            BasicConsole.WriteLine("UHCI: SETUP Transaction");
            BasicConsole.DelayOutput(5);

            UHCITransaction uT = new UHCITransaction();
            uTransaction.underlyingTz = uT;
            uT.inBuffer = null;
            uT.inLength = 0;

            uT.qTD = CreateQTD_SETUP((UHCI_QueueHead_Struct*)transfer.underlyingTransferData, (uint*)1, toggle, tokenBytes, type, req, hiVal, loVal, index, length, transfer.device.address, transfer.endpoint, transfer.packetSize);
            uT.qTDBuffer = uT.qTD->virtBuffer;

            if (transfer.transactions.Count > 0)
            {
                UHCITransaction uLastTransaction = (UHCITransaction)((USBTransaction)(transfer.transactions[transfer.transactions.Count - 1])).underlyingTz;
                uLastTransaction.qTD->next = (((uint)VirtMemManager.GetPhysicalAddress(uT.qTD) & 0xFFFFFFF0) | UHCI_Consts.BIT_Vf); // build TD queue
                uLastTransaction.qTD->q_next = uT.qTD;
            }
        }
        protected override void _INTransaction(USBTransfer transfer, USBTransaction uTransaction, bool toggle, void* buffer, ushort length)
        {
            BasicConsole.WriteLine("UHCI: IN Transaction");
            BasicConsole.DelayOutput(5);

            UHCITransaction uT = new UHCITransaction();
            uTransaction.underlyingTz = uT;
            uT.inBuffer = buffer;
            uT.inLength = length;

            uT.qTD = CreateQTD_IO((UHCI_QueueHead_Struct*)transfer.underlyingTransferData, (uint*)1, UHCI_Consts.TD_IN, toggle, length, transfer.device.address, transfer.endpoint, transfer.packetSize);
            uT.qTDBuffer = uT.qTD->virtBuffer;

            if (transfer.transactions.Count > 0)
            {
                UHCITransaction uLastTransaction = (UHCITransaction)((USBTransaction)(transfer.transactions[transfer.transactions.Count - 1])).underlyingTz;
                uLastTransaction.qTD->next = (((uint)VirtMemManager.GetPhysicalAddress(uT.qTD) & 0xFFFFFFF0) | UHCI_Consts.BIT_Vf); // build TD queue
                uLastTransaction.qTD->q_next = uT.qTD;
            }
        }
        protected override void _OUTTransaction(USBTransfer transfer, USBTransaction uTransaction, bool toggle, void* buffer, ushort length)
        {
            BasicConsole.WriteLine("UHCI: OUT Transaction");
            BasicConsole.DelayOutput(5);

            UHCITransaction uT = new UHCITransaction();
            uTransaction.underlyingTz = uT;
            uT.inBuffer = null;
            uT.inLength = 0;

            uT.qTD = CreateQTD_IO((UHCI_QueueHead_Struct*)transfer.underlyingTransferData, (uint*)1, UHCI_Consts.TD_OUT, toggle, length, transfer.device.address, transfer.endpoint, transfer.packetSize);
            uT.qTDBuffer = uT.qTD->virtBuffer;

            if (buffer != null && length != 0)
            {
                MemoryUtils.MemCpy_32((byte*)uT.qTDBuffer, (byte*)buffer, length);
            }

            if (transfer.transactions.Count > 0)
            {
                UHCITransaction uLastTransaction = (UHCITransaction)((USBTransaction)(transfer.transactions[transfer.transactions.Count - 1])).underlyingTz;
                uLastTransaction.qTD->next = (((uint)VirtMemManager.GetPhysicalAddress(uT.qTD) & 0xFFFFFFF0) | UHCI_Consts.BIT_Vf); // build TD queue
                uLastTransaction.qTD->q_next = uT.qTD;
            }
        }
        protected override void _SetupTransfer(USBTransfer transfer)
        {
            BasicConsole.WriteLine("UHCI: Setup Transfer");
            BasicConsole.DelayOutput(5);

            transfer.underlyingTransferData = qhPointer; // QH
        }
        protected override void _IssueTransfer(USBTransfer transfer)
        {
            BasicConsole.WriteLine("UHCI: Issue Transfer");
            BasicConsole.DelayOutput(5);

            UHCITransaction firstTransaction = (UHCITransaction)((USBTransaction)transfer.transactions[0]).underlyingTz;
            CreateQH((UHCI_QueueHead_Struct*)transfer.underlyingTransferData, (uint)transfer.underlyingTransferData, firstTransaction.qTD);

            BasicConsole.WriteLine("    Queue head data:");
            BasicConsole.DumpMemory(transfer.underlyingTransferData, sizeof(UHCI_QueueHead_Struct));
            BasicConsole.WriteLine("    Transactions data:");
            for (int i = 0; i < transfer.transactions.Count; i++)
            {
                BasicConsole.Write(" ");
                BasicConsole.Write(i);
                BasicConsole.WriteLine(" : ");
                BasicConsole.WriteLine("  - qTD:");
                BasicConsole.DumpMemory(
                    ((UHCITransaction)((USBTransaction)transfer.transactions[i]).underlyingTz).qTD, sizeof(UHCI_qTD_Struct));
                BasicConsole.WriteLine("  - qTDBuffer:");
                BasicConsole.DumpMemory(
                    ((UHCITransaction)((USBTransaction)transfer.transactions[i]).underlyingTz).qTDBuffer, 16);
            }
            BasicConsole.DelayOutput(60);

            BasicConsole.WriteLine("UHCI: Issuing transfer...");

            for (byte i = 0; i < UHCI_Consts.NUMBER_OF_UHCI_RETRIES && !transfer.success; i++)
            {
                TransactionsCompleted = 0;

                // stop scheduler
                USBSTS.Write_UInt16(UHCI_Consts.STS_MASK);
                USBCMD.Write_UInt16((ushort)(USBCMD.Read_UInt16() & ~UHCI_Consts.CMD_RS));
                while ((USBSTS.Read_UInt16() & UHCI_Consts.STS_HCHALTED) == 0)
                {
                    Hardware.Devices.Timer.Default.Wait(10);
                }

                // update scheduler
                uint qhPhysAddr = ((uint)VirtMemManager.GetPhysicalAddress(transfer.underlyingTransferData) | UHCI_Consts.BIT_QH);
                FrameList[0] = qhPhysAddr;
                FRBASEADD.Write_UInt32((uint)VirtMemManager.GetPhysicalAddress(FrameList));
                FRNUM.Write_UInt16(0);
                // start scheduler
                USBSTS.Write_UInt16(UHCI_Consts.STS_MASK);
                USBCMD.Write_UInt16((ushort)(USBCMD.Read_UInt16() | UHCI_Consts.CMD_RS));
                while ((USBSTS.Read_UInt16() & UHCI_Consts.STS_HCHALTED) != 0)
                {
                    Hardware.Devices.Timer.Default.Wait(10);
                }

                BasicConsole.WriteLine(((FOS_System.String)"USBINT val: ") + USBINTR.Read_UInt16());

                // run transactions
                int timeout = 20;
                while (TransactionsCompleted < transfer.transactions.Count &&
                      timeout > 0)
                {
                    BasicConsole.WriteLine("UHCI: Waiting on transfer complete...");
                    //BasicConsole.WriteLine(((FOS_System.String)"FRNUM val: ") + FRNUM.Read_UInt16());

                    Hardware.Devices.Timer.Default.Wait(500);

                    timeout--;
                }

                BasicConsole.WriteLine("Finished waiting.");

                if (timeout == 0 ||
                    TransactionsCompleted != transfer.transactions.Count)
                {
                    BasicConsole.SetTextColour(BasicConsole.error_colour);
                    BasicConsole.WriteLine("UHCI: Error! Transactions wait timed out or wrong number of transactions completed.");
                    BasicConsole.SetTextColour(BasicConsole.default_colour);

                    BasicConsole.WriteLine(((FOS_System.String)"Transactions completed: ") + TransactionsCompleted);

                    transfer.success = false;

                    bool completeDespiteNoInterrupt = true;
                    for (int j = 0; j < transfer.transactions.Count; j++)
                    {
                        USBTransaction elem = (USBTransaction)transfer.transactions[j];
                        UHCITransaction uT = (UHCITransaction)(elem.underlyingTz);

                        BasicConsole.WriteLine(((FOS_System.String)"u1=") + uT.qTD->u1 + ", u2=" + uT.qTD->u2);
                        BasicConsole.WriteLine(((FOS_System.String)"Status=") + (byte)(uT.qTD->u1 >> 16));
                        completeDespiteNoInterrupt = completeDespiteNoInterrupt && ((byte)(uT.qTD->u1 >> 16) == 0);
                    }

                    transfer.success = completeDespiteNoInterrupt;

                    BasicConsole.WriteLine(((FOS_System.String)"Complete despite no interrupts: ") + completeDespiteNoInterrupt);
                    BasicConsole.DelayOutput(5);
                }
                else
                {
                    transfer.success = true;
                }

                if (transfer.success)
                {
                    // check conditions and save data
                    for (int j = 0; j < transfer.transactions.Count; j++)
                    {
                        USBTransaction elem = (USBTransaction)transfer.transactions[j];
                        UHCITransaction uT = (UHCITransaction)(elem.underlyingTz);
                        transfer.success = transfer.success && isTransactionSuccessful(uT); // executed w/o error

                        if (uT.inBuffer != null && uT.inLength != 0)
                        {
                            MemoryUtils.MemCpy_32((byte*)uT.inBuffer, (byte*)uT.qTDBuffer, uT.inLength);
                        }
                    }
                }

                if (!transfer.success)
                {
                    BasicConsole.SetTextColour(BasicConsole.error_colour);
                    BasicConsole.WriteLine("Transfer failed.");
                    BasicConsole.SetTextColour(BasicConsole.default_colour);
                }
            }
        }

        protected static UHCI_qTD_Struct* AllocQTD(uint* next)
        {
            BasicConsole.WriteLine("UHCI: Alloc qTD");
            BasicConsole.DelayOutput(5);

            UHCI_qTD_Struct* td = (UHCI_qTD_Struct*)FOS_System.Heap.Alloc((uint)sizeof(UHCI_qTD_Struct), 16);

            if ((uint)next != Utils.BIT(0))
            {
                td->next = ((uint)VirtMemManager.GetPhysicalAddress(next) & 0xFFFFFFF0) | UHCI_Consts.BIT_Vf;
                td->q_next = (UHCI_qTD_Struct*)next;
            }
            else
            {
                td->next = UHCI_Consts.BIT_T;
            }

            UHCI_qTD.SetActive(td, true); // to be executed
            UHCI_qTD.SetIntOnComplete(td, true);  // We want an interrupt after complete transfer
            UHCI_qTD.SetPacketID(td, UHCI_Consts.TD_SETUP);
            UHCI_qTD.SetMaxLength(td, 0x3F); // 64 byte // uhci, rev. 1.1, page 24

            return td;
        }
        protected static void* AllocQTDbuffer(UHCI_qTD_Struct* td)
        {
            BasicConsole.WriteLine("UHCI: Alloc qTD Buffer");
            BasicConsole.DelayOutput(5);

            td->virtBuffer = FOS_System.Heap.Alloc(1024);
            MemoryUtils.ZeroMem(td->virtBuffer, 1024);
            td->buffer = (uint*)VirtMemManager.GetPhysicalAddress(td->virtBuffer);

            return td->virtBuffer;
        }
        protected UHCI_qTD_Struct* CreateQTD_SETUP(UHCI_QueueHead_Struct* uQH, uint* next, bool toggle, ushort tokenBytes, byte type, byte req, byte hiVal, byte loVal, ushort i, ushort length, byte device, byte endpoint, uint packetSize)
        {
            BasicConsole.WriteLine("UHCI: Create qTD SETUP");
            BasicConsole.DelayOutput(5);

            UHCI_qTD_Struct* td = AllocQTD(next);

            UHCI_qTD.SetPacketID(td, UHCI_Consts.TD_SETUP);
            UHCI_qTD.SetDataToggle(td, toggle);
            UHCI_qTD.SetDeviceAddress(td, device);
            UHCI_qTD.SetEndpoint(td, endpoint);
            UHCI_qTD.SetMaxLength(td, (ushort)(tokenBytes - 1));
            UHCI_qTD.SetC_ERR(td, 0x3);

            //TODO: *buffer = 
            USBRequest* request = (USBRequest*)(AllocQTDbuffer(td));
            request->type = type;
            request->request = req;
            request->valueHi = hiVal;
            request->valueLo = loVal;
            request->index = i;
            request->length = length;

            uQH->q_last = td;
            return td;
        }
        protected UHCI_qTD_Struct* CreateQTD_IO(UHCI_QueueHead_Struct* uQH, uint* next, byte direction, bool toggle, ushort tokenBytes, byte device, byte endpoint, uint packetSize)
        {
            BasicConsole.WriteLine("UHCI: Create qTD IO");
            BasicConsole.DelayOutput(5);

            UHCI_qTD_Struct* td = AllocQTD(next);

            UHCI_qTD.SetPacketID(td, direction);

            if (tokenBytes != 0)
            {
                UHCI_qTD.SetMaxLength(td, (ushort)((tokenBytes - 1u) & 0x7FFu));
            }
            else
            {
                UHCI_qTD.SetMaxLength(td, 0x7FF);
            }

            UHCI_qTD.SetDataToggle(td, toggle);
            UHCI_qTD.SetC_ERR(td, 0x3);
            UHCI_qTD.SetDeviceAddress(td, device);
            UHCI_qTD.SetEndpoint(td, endpoint);

            AllocQTDbuffer(td);

            uQH->q_last = td;
            return td;
        }
        protected void CreateQH(UHCI_QueueHead_Struct* head, uint horizPtr, UHCI_qTD_Struct* firstTD)
        {
            BasicConsole.WriteLine("UHCI: Create QH");
            BasicConsole.DelayOutput(5);

            head->next = (UHCI_QueueHead_Struct*)UHCI_Consts.BIT_T; // (paging_getPhysAddr((void*)horizPtr) & 0xFFFFFFF0) | BIT_QH;

            if (firstTD == null)
            {
                head->transfer = (UHCI_qTD_Struct*)UHCI_Consts.BIT_T;
            }
            else
            {
                head->transfer = (UHCI_qTD_Struct*)((uint)VirtMemManager.GetPhysicalAddress(firstTD) & 0xFFFFFFF0);
                head->q_first = firstTD;
            }
        }

        public override void Update()
        {
            BasicConsole.WriteLine("UHCI: Update");
            BasicConsole.DelayOutput(5);

            for (byte j = 0; j < RootPortCount; j++)
            {
                ushort val = PORTSC1.Read_UInt16((ushort)(2 * j));

                if ((val & UHCI_Consts.PORT_CS_CHANGE) != 0)
                {
                    //printf("\nUHCI %u: Port %u changed: ", u->num, j + 1);
                    PORTSC1.Write_UInt16(UHCI_Consts.PORT_CS_CHANGE, (ushort)(2 * j));
                    AnalysePortStatus(j, val);
                }
            }
        }
    }
    
    /// <summary>
    /// Represents a transaction made through a UHCI.
    /// </summary>
    public unsafe class UHCITransaction : HCTransaction
    {
        /// <summary>
        /// A pointer to the actual qTD of the transaction.
        /// </summary>
        public UHCI_qTD_Struct* qTD;
        
        public void* qTDBuffer;

        /// <summary>
        /// A pointer to the input buffer.
        /// </summary>
        public void* inBuffer;
        /// <summary>
        /// The length of the input buffer.
        /// </summary>
        public uint inLength;
    }

    // Transfer Descriptors (TD) are always aligned on 16-byte boundaries.
    // All transfer descriptors have the same basic, 32-byte structure.
    // The last 4 DWORDs are for software use.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
    public unsafe struct UHCI_qTD_Struct
    {
        // pointer to another TD or QH
        // inclusive control bits  (DWORD 0)
        public UInt32 next;

        // TD CONTROL AND STATUS  (DWORD 1)
        //public UInt32 actualLength      : 11; //  0-10
        //public UInt32 reserved1         :  5; // 11-15
        //public UInt32 reserved2         :  1; //    16
        //public UInt32 bitstuffError     :  1; //    17
        //public UInt32 crc_timeoutError  :  1; //    18
        //public UInt32 nakReceived       :  1; //    19
        //public UInt32 babbleDetected    :  1; //    20
        //public UInt32 dataBufferError   :  1; //    21
        //public UInt32 stall             :  1; //    22
        //public UInt32 active            :  1; //    23
        //public UInt32 intOnComplete     :  1; //    24
        //public UInt32 isochrSelect      :  1; //    25
        //public UInt32 lowSpeedDevice    :  1; //    26
        //public UInt32 errorCounter      :  2; // 27-28
        //public UInt32 shortPacketDetect :  1; //    29
        //public UInt32 reserved3         :  2; // 30-31
        public UInt32 u1;

        // TD TOKEN  (DWORD 2)
        //public UInt32 PacketID          :  8; //  0-7
        //public UInt32 deviceAddress     :  7; //  8-14
        //public UInt32 endpoint          :  4; // 15-18
        //public UInt32 dataToggle        :  1; //    19
        //public UInt32 reserved4         :  1; //    20
        //public UInt32 maxLength         : 11; // 21-31
        public UInt32 u2;

        // TD BUFFER POINTER (DWORD 3)
        public UInt32* buffer;

        // RESERVED FOR SOFTWARE (DWORDS 4-7)
        public UHCI_qTD_Struct* q_next;
        public void* virtBuffer;
        public UInt32 dWord6; // ?
        public UInt32 dWord7; // ?
    }
    public static unsafe class UHCI_qTD
    {
        //u1
        public static bool GetActive(UHCI_qTD_Struct* qTD)
        {
            return (qTD->u1 & Utils.BIT(23)) != 0;
        }
        public static void SetActive(UHCI_qTD_Struct* qTD, bool val)
        {
            if (val)
            {
                qTD->u1 |= Utils.BIT(23);
            }
            else
            {
                qTD->u1 &= ~Utils.BIT(23);
            }
        }
        public static bool GetIntOnComplete(UHCI_qTD_Struct* qTD)
        {
            return (qTD->u1 & Utils.BIT(24)) != 0;
        }
        public static void SetIntOnComplete(UHCI_qTD_Struct* qTD, bool val)
        {
            if (val)
            {
                qTD->u1 |= Utils.BIT(24);
            }
            else
            {
                qTD->u1 &= ~Utils.BIT(24);
            }
        }
        public static byte GetC_ERR(UHCI_qTD_Struct* qTD)
        {
            return (byte)((qTD->u1 & 0x18000000) >> 27);
        }
        public static void SetC_ERR(UHCI_qTD_Struct* qTD, byte val)
        {
            qTD->u1 = (qTD->u1 & 0xE7FFFFFF) | ((uint)(val & 0x03) << 27);
        }
        
        //u2
        public static byte GetPacketID(UHCI_qTD_Struct* qTD)
        {
            return (byte)(qTD->u2);
        }
        public static void SetPacketID(UHCI_qTD_Struct* qTD, byte val)
        {
            qTD->u2 = (qTD->u2 & 0xFFFFFF00) | val;
        }
        public static byte GetDeviceAddress(UHCI_qTD_Struct* qTD)
        {
            return (byte)((qTD->u2 & 0x00007F00) >> 8);
        }
        public static void SetDeviceAddress(UHCI_qTD_Struct* qTD, byte val)
        {
            qTD->u2 = (qTD->u2 & 0xFFFF80FF) | ((uint)(val & 0x7F) << 8);
        }
        public static byte GetEndpoint(UHCI_qTD_Struct* qTD)
        {
            return (byte)((qTD->u2 & 0x00078000) >> 15);
        }
        public static void SetEndpoint(UHCI_qTD_Struct* qTD, byte val)
        {
            qTD->u2 = (qTD->u2 & 0xFFF87FFF) | ((uint)(val & 0x0F) << 15);
        }
        public static bool GetDataToggle(UHCI_qTD_Struct* qTD)
        {
            return (qTD->u2 & Utils.BIT(19)) != 0;
        }
        public static void SetDataToggle(UHCI_qTD_Struct* qTD, bool val)
        {
            if (val)
            {
                qTD->u2 |= Utils.BIT(19);
            }
            else
            {
                qTD->u2 &= ~Utils.BIT(19);
            }
        }
        public static ushort GetMaxLength(UHCI_qTD_Struct* qTD)
        {
            return (ushort)((qTD->u2 & 0xFFE00000) >> 21);
        }
        public static void SetMaxLength(UHCI_qTD_Struct* qTD, ushort val)
        {
            qTD->u2 = (qTD->u2 & 0x001FFFFF) | ((uint)val << 21);
        }

    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
    public unsafe struct UHCI_QueueHead_Struct
    {
        // QUEUE HEAD LINK POINTER
        // inclusive control bits (DWORD 0)
        public UHCI_QueueHead_Struct* next;

        // QUEUE ELEMENT LINK POINTER
        // inclusive control bits (DWORD 1)
        public UHCI_qTD_Struct* transfer;

        // TDs
        public UHCI_qTD_Struct* q_first;
        public UHCI_qTD_Struct* q_last;
    }
}