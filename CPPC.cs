using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using static ZenStates.Core.ACPI;

namespace ZenStates.Core
{
    public class CPPC
    {
        internal readonly IOModule io;
        internal readonly ACPI acpi;
        public CPPCTable AcpiTable;

        public bool CPPC_Enable;
        public bool CPPC_MSR;
        public bool CPPC_ACPI;
        public int CPPC_ACPI_Version;

        private static string GetByKey(Dictionary<int, string> dict, int key)
        {
            return dict.TryGetValue(key, out string output) ? output : "N/A";
        }


        [StructLayout(LayoutKind.Explicit)]
        public struct CPPCData
        {
            [FieldOffsetAttribute(0)]
            public uint NumEntries;
            [FieldOffsetAttribute(0)]
            public uint Revision;
            [FieldOffsetAttribute(0)]
            public uint HighestPerformance;
            [FieldOffsetAttribute(0)]
            public uint NominalPerformance;
            [FieldOffsetAttribute(0)]
            public uint LowestNonlinearPerformance;
            [FieldOffsetAttribute(0)]
            public uint LowestPerformance;
            [FieldOffsetAttribute(0)]
            public uint GuaranteedPerformanceRegister;
            [FieldOffsetAttribute(0)]
            public uint DesiredPerformanceRegister;
            [FieldOffsetAttribute(0)]
            public uint MinimumPerformanceRegister;
            [FieldOffsetAttribute(0)]
            public uint MaximumPerformanceRegister;
            [FieldOffsetAttribute(0)]
            public uint PerformanceReductionToleranceRegister;
            [FieldOffsetAttribute(0)]
            public uint TimeWindowRegister;
            [FieldOffsetAttribute(0)]
            public ulong CounterWraparoundTime;
            [FieldOffsetAttribute(0)]
            public ulong ReferencePerformanceCounterRegister;
            [FieldOffsetAttribute(0)]
            public ulong DeliveredPerformanceCounterRegister;
            [FieldOffsetAttribute(0)]
            public uint PerformanceLimitedRegister;
            [FieldOffsetAttribute(0)]
            public uint CPPCEnableRegister;
            [FieldOffsetAttribute(0)]
            public uint AutonomousSelectionEnable;
            [FieldOffsetAttribute(0)]
            public uint AutonomousActivityWindowRegister;
            [FieldOffsetAttribute(0)]
            public uint EnergyPerformancePreferenceRegister;
            [FieldOffsetAttribute(0)]
            public uint ReferencePerformance;

        }

        [Serializable]
        public class CPPCTable
        {
            public readonly uint Signature;
            public ulong OemTableId;
            // public readonly byte[] RegionSignature;
            public uint BaseAddress;
            public int Length;
            public ACPITable? acpiTable;
            public CPPCData Data;
            public byte[] rawCPPCTable;

            public CPPCTable()
            {
                this.Signature = Signature(TableSignature.SSDT);
                this.OemTableId = SignatureUL(TableSignature.CPUSSDT);
                //this.RegionSignature = ByteSignature(TableSignature.AODE);
            }
        }

        public CPPC(IOModule io)
        {
            this.io = io;
            this.acpi = new ACPI(io);
            this.AcpiTable = new CPPCTable();
            CPPC_Enable = false;
            CPPC_MSR = false;
            CPPC_ACPI = false;

            this.Init();
        }

        private ACPITable? GetCpuSsdtTable()
        {
            try
            {
                RSDT rsdt = acpi.GetRSDT();

                foreach (uint addr in rsdt.Data)
                {
                    if (addr != 0)
                    {
                        try
                        {
                            SDTHeader hdr = acpi.GetHeader<SDTHeader>(addr);
                            if (hdr.Signature == Signature(TableSignature.SSDT) && hdr.OEMTableID == SignatureUL("CPUSSDT"))
                            {
                                this.AcpiTable.BaseAddress = addr;
                                return ParseSdtTable(io.ReadMemory(new IntPtr(addr), (int)hdr.Length));
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return null;
        }

        private void Init()
        {


            //this.Table.acpiTable = GetCpuSsdtTable();

            /*
            if (this.Table.acpiTable != null)
            {
                int regionIndex = Utils.FindSequence(this.Table.acpiTable?.Data, 0, ByteSignature(TableSignature.CPUSSDT));
                if (regionIndex == -1)
                    return;
                byte[] region = new byte[16];
                Buffer.BlockCopy(this.Table.acpiTable?.Data, regionIndex, region, 0, 16);
                // OperationRegion(AODE, SystemMemory, Offset, Length)
                OperationRegion opRegion = Utils.ByteArrayToStructure<OperationRegion>(region);
                this.Table.BaseAddress = opRegion.Offset;
                this.Table.Length = opRegion.Length[1] << 8 | opRegion.Length[0];
            }
            */
            //this.Table.BaseAddress = 0;
            //this.Table.Length = (int)this.Table.acpiTable.Value.Data.Length;
            //this.Refresh();
        }

        public bool RefreshAcpiTable()
        {
            try
            {
                this.AcpiTable.rawCPPCTable = this.io.ReadMemory(new IntPtr(this.AcpiTable.BaseAddress), this.AcpiTable.Length);
                // this.Table.Data = Utils.ByteArrayToStructure<CPPCData>(this.Table.rawCPPCTable);
                // int test = Utils.FindSequence(rawTable, 0, BitConverter.GetBytes(0x3ae));
                return true;
            }
            catch { }

            return false;
        }
    }
}