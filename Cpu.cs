using OpenHardwareMonitor.Hardware;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using ZenStates.Core.SMUCommands;

namespace ZenStates.Core
{
    public class Cpu : IDisposable
    {
        private bool disposedValue;
        private const string InitializationExceptionText = "CPU module initialization failed.";

        private static ushort group0 = 0x0;
        private static ulong bitmask0 = (1 << 0);
        private GroupAffinity cpu0Affinity = new GroupAffinity(group0, bitmask0);

        public float? ccd1Temp { get; private set; }
        public float? ccd2Temp { get; private set; }
        public float? cpuTemp { get; private set; }
        public float? cpuVcore { get; private set; }
        public float? cpuVsoc { get; private set; }
        public float cpuBusClock { get; private set; }

        public string teststring { get; private set; }
        public string teststring1 { get; private set; }
        public string teststring2 { get; private set; }
        public enum Family
        {
            UNSUPPORTED = 0x0,
            FAMILY_15H = 0x15,
            FAMILY_17H = 0x17,
            FAMILY_18H = 0x18,
            FAMILY_19H = 0x19,
        };

        public enum CodeName
        {
            Unsupported = 0,
            DEBUG,
            BristolRidge,
            SummitRidge,
            Whitehaven,
            Naples,
            RavenRidge,
            RavenRidge2,
            PinnacleRidge,
            Colfax,
            Picasso,
            FireFlight,
            Matisse,
            CastlePeak,
            Rome,
            Dali,
            Renoir,
            VanGogh,
            Vermeer,
            Chagall,
            Milan,
            Cezanne,
            Rembrandt,
            Lucienne,
            Raphael,
            Phoenix,
            Mendocino,
        };


        // CPUID_Fn80000001_EBX [BrandId Identifier] (BrandId)
        // [31:28] PkgType: package type.
        // Socket FP5/FP6 = 0
        // Socket AM4 = 2
        // Socket SP3 = 4
        // Socket TR4/TRX4 (SP3r2/SP3r3) = 7
        public enum PackageType
        {
            FPX = 0,
            AM4 = 2,
            SP3 = 4,
            TRX = 7,
        }

        public struct SVI2
        {
            public uint coreAddress;
            public uint socAddress;
        }
        public struct CpuTopology
        {
            public uint ccds;
            public uint ccxs;
            public uint coresPerCcx;
            public uint ccxPerCcd;
            public uint cores;
            public uint logicalCores;
            public uint physicalCores;
            public uint threadsPerCore;
            public uint enabledCores;
            public uint cpuNodes;
            public uint coreDisableMap;
            public uint coreEnabledMap;
            public uint coreLayout;
            public uint coreLayoutInit;
            public uint[] coreMap;
            public uint[] performanceOfCore;
            public uint[] logical2apicIds;
            public uint[] coreIds;
            public uint[] cores2apicId;
            // Index = CorePhysicalIndex = { CoreId, CCX}
            public uint[,] coreCcxMap;
            // Index = CoreId = { CorePhysicalIndex, CCX, CCD}
            public uint[,] coreFullMap;
        }
        public struct CPUInfo
        {
            public uint cpuid;
            public Family family;
            public CodeName codeName;
            public string cpuName;
            public string vendor;
            public PackageType packageType;
            public uint baseModel;
            public uint extModel;
            public uint model;
            public uint patchLevel;
            public CpuTopology topology;
            public SVI2 svi2;
            public AOD aod;
            public CPPC cppc;
            public bool cppcMSR;
            public bool PPTSupported;
            public bool TDCSupported;
            public bool EDCSupported;
            public bool THMSupported;

        }

        public readonly IOModule io = new IOModule();
        private readonly ACPI_MMIO mmio;
        public readonly CPUInfo info;
        public readonly SystemInfo systemInfo;
        public readonly SMU smu;
        public readonly PowerTable powerTable;

        public IOModule.LibStatus Status { get; }
        public Exception LastError { get; }

        private CpuTopology GetCpuTopology(Family family, CodeName codeName, uint model)
        {
            CpuTopology topology = new CpuTopology();

            if (Opcode.Cpuid(0x00000001, 0, out uint eax, out uint ebx, out uint ecx, out uint edx))
                topology.logicalCores = Utils.GetBits(ebx, 16, 8);
            else
                throw new ApplicationException($"{InitializationExceptionText} Phase 1");

            if (Opcode.Cpuid(0x8000001E, 0, out eax, out ebx, out ecx, out edx))
            {
                topology.threadsPerCore = Utils.GetBits(ebx, 8, 4) + 1;
                topology.cpuNodes = ((ecx >> 8) & 0x7) + 1;

                if (topology.threadsPerCore == 0)
                    topology.cores = topology.logicalCores;
                else
                    topology.cores = topology.logicalCores / topology.threadsPerCore;

                if (Opcode.Cpuid(0x80000008, 0, out eax, out ebx, out ecx, out edx))
                {
                    topology.enabledCores = (Utils.GetBits(ecx, 0, 8) + 1) / topology.threadsPerCore;
                }
            }
            else
            {
                throw new ApplicationException($"{InitializationExceptionText} Phase 2");
            }

            try
            {
                topology.performanceOfCore = new uint[topology.cores];

                for (int i = 0; i < topology.logicalCores; i += (int)topology.threadsPerCore)
                {
                    if (Ring0.RdmsrTx(0xC00102B3, out eax, out edx, GroupAffinity.Single(0, i)))
                        topology.performanceOfCore[i / topology.threadsPerCore] = eax & 0xff;
                    else
                        topology.performanceOfCore[i / topology.threadsPerCore] = 0;
                }

            }
            catch { }

            uint ccdsPresent = 0, ccdsDown = 0, coreFuse = 0;
            uint fuse1 = 0x5D218;
            uint fuse2 = 0x5D21C;
            uint offset = 0x238;
            uint ccxPerCcd = 2;

            // Get CCD and CCX configuration
            // https://gitlab.com/leogx9r/ryzen_smu/-/blob/master/userspace/monitor_cpu.c
            if (family == Family.FAMILY_19H)
            {
                offset = 0x598;
                ccxPerCcd = 1;
                if (codeName == CodeName.Raphael)
                {
                    offset = 0x4D0;
                    fuse1 += 0x1A4;
                    fuse2 += 0x1A4;
                }
            }
            else if (family == Family.FAMILY_17H && model != 0x71 && model != 0x31)
            {
                fuse1 += 0x40; // 0x5D258
                fuse2 += 0x40; // 0x5D25C
            }

            bool fuseRead = false;

            uint ccdEnableMap = 0;
            uint ccdDisableMap = 0;
            uint coreDisableMapAddress = 0;
            uint enabledCcd = 0;

            if (ReadDwordEx(fuse1, ref ccdsPresent) && ReadDwordEx(fuse2, ref ccdsDown))
            {
                ccdEnableMap = Utils.GetBits(ccdsPresent, 22, 8);
                ccdDisableMap = Utils.GetBits(ccdsPresent, 30, 2) | (Utils.GetBits(ccdsDown, 0, 6) << 2);
                coreDisableMapAddress = 0x30081800 + offset;
                enabledCcd = Utils.CountSetBits(ccdEnableMap);

                topology.ccds = enabledCcd > 0 ? enabledCcd : 1;
                topology.ccxs = topology.ccds * ccxPerCcd;
                topology.physicalCores = topology.ccxs * 8 / ccxPerCcd;

                fuseRead = true;
            }

            #region Phase3

            topology.ccxPerCcd = ccxPerCcd;
            topology.coreEnabledMap = 0x00000000;
            topology.coreLayoutInit = 0x00000000;
            topology.coreFullMap = new uint[topology.cores, 3];

            try
            {
                topology.coreIds = new uint[topology.cores];
                topology.logical2apicIds = new uint[topology.logicalCores];
                topology.cores2apicId = new uint[topology.cores];

                Process Proc = Process.GetCurrentProcess();
                IntPtr _systemAffinity = Proc.ProcessorAffinity;

                uint apicIdSharedId = uint.MaxValue, prevApicIdSharedId = uint.MaxValue;
                uint numSharingCache = 0, numSharingCacheLog2 = 0, ccxSharing = 1, numSharingCacheId = uint.MaxValue, prevNumSharingCacheId = uint.MaxValue;
                uint coreId = 0, _logicalCoreId = 0, _logicalCoreIdLog2 = 0, logical2apicId = 0;

                topology.coreCcxMap = new uint[topology.enabledCores, 8];

                for (int i = 0; i < topology.logicalCores; i++)
                {
                    ulong bitmask = (ulong)1 << i;
                    Proc.ProcessorAffinity = (IntPtr)bitmask;

                    foreach (ProcessThread thrd in Proc.Threads)
                    {
                        thrd.ProcessorAffinity = (IntPtr)bitmask;
                    }

                    // LocalApicId
                    if (Opcode.Cpuid(0x00000001, 0, out eax, out ebx, out ecx, out edx))
                        logical2apicId = Utils.GetBits(ebx, 24, 8);

                    // numSharingCache
                    if (Opcode.Cpuid(0x8000001D, 3, out eax, out ebx, out ecx, out edx))
                        numSharingCache = Utils.GetBits(eax, 14, 12);

                    numSharingCacheLog2 = (8 / ccxPerCcd) * topology.threadsPerCore;

                    // CoreId
                    if (Opcode.Cpuid(0x8000001E, 0, out eax, out ebx, out ecx, out edx))
                        _logicalCoreId = Utils.GetBits(eax, 0, 8);

                    topology.logical2apicIds[i] = logical2apicId;

                    apicIdSharedId = logical2apicId >> (int)Math.Log(topology.threadsPerCore, 2);
                    
                    numSharingCacheId = _logicalCoreId >> (int)Math.Log(numSharingCacheLog2, 2);

                    prevNumSharingCacheId = prevNumSharingCacheId == uint.MaxValue ? numSharingCacheId : prevNumSharingCacheId;

                    if (numSharingCacheId != prevNumSharingCacheId)
                    {
                        ccxSharing++;
                    }

                    if (apicIdSharedId != prevApicIdSharedId)
                    {
                        topology.coreIds[coreId] = _logicalCoreId;
                        topology.cores2apicId[coreId] = logical2apicId;

                        topology.coreCcxMap[coreId, 0] = 1;
                        topology.coreCcxMap[coreId, 1] = ccxSharing;
                        topology.coreCcxMap[coreId, 2] = (uint)_logicalCoreId;
                        topology.coreCcxMap[coreId, 3] = numSharingCache;
                        topology.coreCcxMap[coreId, 4] = numSharingCacheId;
                        topology.coreCcxMap[coreId, 5] = prevNumSharingCacheId;
                        topology.coreCcxMap[coreId, 6] = _logicalCoreIdLog2;
                        topology.coreCcxMap[coreId, 7] = numSharingCacheLog2;
                        coreId++;

                        prevNumSharingCacheId = numSharingCacheId;
                    }

                    prevApicIdSharedId = apicIdSharedId;
                }

                Proc.ProcessorAffinity = _systemAffinity;

                foreach (ProcessThread thrd in Proc.Threads)
                {
                    thrd.ProcessorAffinity = _systemAffinity;
                }

            }
            catch (Exception ex)
            {
                throw new ApplicationException($"{InitializationExceptionText} Phase 3 - Exception: {ex}");
            }

            #endregion

            if (fuseRead)
            {
                try { 
                    if (ReadDwordEx(coreDisableMapAddress, ref coreFuse))
                        topology.coresPerCcx = (8 - Utils.CountSetBits(coreFuse & 0xff)) / ccxPerCcd;
                    else
                        Console.WriteLine("Could not read core fuse!");

                    uint ccdOffset = 0;
                    uint xbit, ccxSharing, ccxSharingPrev = 1;
                    int p = 0, enabledOffset = 0;
                    int ccd = 1;
                    bool psmCheck;

                    for (int i = 0; i < topology.coreCcxMap.GetLength(0); ++i)
                    {
                        xbit = topology.coreCcxMap[i, 0];
                        ccxSharing = topology.coreCcxMap[i, 1];

                        if ((ccxPerCcd == 1 && ccxSharing != ccxSharingPrev) || (ccxPerCcd == 2 && (ccxSharing != ccxSharingPrev && ccxSharing != ccxSharingPrev + 1)))
                        {
                            enabledOffset = ((8 * ccd) - i) / ccd;
                            for (int f = 0; f < enabledOffset; f++)
                            {
                                topology.coreEnabledMap |= (uint)0 << p;
                                p++;
                            };
                            ccd = (ccxPerCcd == 1) || (ccxSharing & (ccxSharing - 1)) == 0 ? ccd + 1 : ccd;
                            ccxSharingPrev = ccxSharing;
                        }
                        if (xbit == 0) continue;
                        topology.coreEnabledMap |= (uint)1 << p;
                        p++;
                    }

                    int _CoreIndex = 0;

                    for (int i = 0; i < topology.ccds; i++)
                    {
                        if (Utils.GetBits(ccdEnableMap, i, 1) == 1)
                        {
                            if (ReadDwordEx(coreDisableMapAddress | ccdOffset, ref coreFuse))
                            {
                                topology.coreDisableMap |= (coreFuse & 0xff) << i * 8;
                                uint _coreDisableMap = (topology.coreDisableMap >> i * 8) & 0xff;
                                uint _coreEnabledMap = (topology.coreEnabledMap >> i * 8) & 0xff;
                                p = i * 8;
                                for (int b = 0; b < 8; ++b, ++p)
                                {
                                    uint ebit = Utils.GetBits(_coreEnabledMap, b, 1);
                                    uint dbit = Utils.GetBits(_coreDisableMap, b, 1);
                                    if (dbit == 1) { topology.coreLayoutInit |= (uint)0 << p; p++; }
                                    topology.coreLayoutInit |= (uint)ebit << p;
                                    if (ebit == 1)
                                    {
                                        topology.coreFullMap[_CoreIndex, 0] = (uint)p;
                                        topology.coreFullMap[_CoreIndex, 1] = (uint)(topology.coreCcxMap[_CoreIndex, 1] - (i * ccxPerCcd));
                                        topology.coreFullMap[_CoreIndex, 2] = (uint)i+1;
                                        _CoreIndex++;
                                    }
                                }
                            }
                            else
                                Console.WriteLine($"Could not read core fuse for CCD{i}!");
                        }
                        ccdOffset += 0x2000000;
                    }
                }
                catch (Exception ex)
                {
                    throw new ApplicationException($"{InitializationExceptionText} Phase 4 - Exception: {ex}");
                }

                try
                {
                    topology.coreMap = new uint[Utils.CountSetBits(~topology.coreLayoutInit)];
                    uint _coreMap = topology.coreLayoutInit;

                    for (uint i = 0, k = 0; i < topology.physicalCores; _coreMap = _coreMap >> 1)
                    { if ((_coreMap & 1) == 1) topology.coreMap[k++] = i; i++; }

                }
                catch (Exception ex)
                {
                    throw new ApplicationException($"{InitializationExceptionText} Phase 5 - Exception: {ex}");
                }

            }
            else
            {
                Console.WriteLine("Could not read CCD fuse!");
            }

            return topology;
        }
        public Cpu()
        {
            Ring0.Open();

            if (!Ring0.IsOpen)
            {
                string errorReport = Ring0.GetReport();
                using (var sw = new StreamWriter("WinRing0.txt", true))
                {
                    sw.Write(errorReport);
                }

                Console.WriteLine("Error opening WinRing kernel driver");
            }

            Opcode.Open();
            mmio = new ACPI_MMIO(io);

            info.vendor = GetVendor();
            if (info.vendor != Constants.VENDOR_AMD && info.vendor != Constants.VENDOR_HYGON)
                throw new Exception("Not an AMD CPU");

            if (Opcode.Cpuid(0x00000001, 0, out uint eax, out uint ebx, out uint ecx, out uint edx))
            {
                info.cpuid = eax;
                info.family = (Family)(((eax & 0xf00) >> 8) + ((eax & 0xff00000) >> 20));
                info.baseModel = (eax & 0xf0) >> 4;
                info.extModel = (eax & 0xf0000) >> 12;
                info.model = info.baseModel + info.extModel;
                // info.logicalCores = Utils.GetBits(ebx, 16, 8);
            }
            else
            {
                throw new ApplicationException(InitializationExceptionText);
            }

            // Package type
            if (Opcode.Cpuid(0x80000001, 0, out eax, out ebx, out ecx, out edx))
            {
                info.packageType = (PackageType)(ebx >> 28);
                info.codeName = GetCodeName(info);
                smu = GetMaintainedSettings.GetByType(info.codeName);
                smu.Version = GetSmuVersion();
                smu.TableVersion = GetTableVersion();
                smu.Hsmp.Init(this);
            }
            else
            {
                throw new ApplicationException(InitializationExceptionText);
            }

            info.cpuName = GetCpuName();

            // Non-critical block
            try
            {
                info.topology = GetCpuTopology(info.family, info.codeName, info.model);
                info.topology.coreLayout = CheckCoreLayout(info.topology.coreLayoutInit);
            }
            catch (Exception ex)
            {
                LastError = ex;
                Status = IOModule.LibStatus.PARTIALLY_OK;
            }

            try
            {
                info.patchLevel = GetPatchLevel();
                info.svi2 = GetSVI2Info(info.codeName);
                systemInfo = new SystemInfo(info, smu);
                powerTable = new PowerTable(smu, io, mmio);

                info.aod = new AOD(io);
                info.cppc = new CPPC(io);
                info.PPTSupported = powerTable.tableDef.offsetPPT >= 0 ? true : false;
                info.TDCSupported = powerTable.tableDef.offsetTDC >= 0 ? true : false;
                info.EDCSupported = powerTable.tableDef.offsetEDC >= 0 ? true : false;
                info.THMSupported = powerTable.tableDef.offsetTHM >= 0 ? true : false;

                if (!SendTestMessage())
                    LastError = new ApplicationException("SMU is not responding to test message!");

                Status = IOModule.LibStatus.OK;
            }
            catch (Exception ex)
            {
                LastError = ex;
                Status = IOModule.LibStatus.PARTIALLY_OK;
            }

            ccd1Temp = null;
            ccd2Temp = null;
            cpuTemp = null;
            cpuVcore = null;
            cpuVsoc = null;
            cpuBusClock = 0;
            info.cppcMSR = false;


            RefreshSensors();
        }

        // [31-28] ccd index
        // [27-24] ccx index (always 0 for Zen3 where each ccd has just one ccx)
        // [23-20] core index
        public uint MakeCoreMask(uint core = 0, uint ccd = 0, uint ccx = 0)
        {
            uint ccxInCcd = info.family == Family.FAMILY_19H ? 1U : 2U;
            uint coresInCcx = 8 / ccxInCcd;

            return ((ccd << 4 | ccx % ccxInCcd & 0xF) << 4 | core % coresInCcx & 0xF) << 20;
        }
        
        public uint GetCoreMask(int coreIndex)
        {
            uint ccxInCcd = info.family == Cpu.Family.FAMILY_19H ? 1U : 2U;
            uint coresInCcx = 8 / ccxInCcd;

            uint ccd = Convert.ToUInt32(coreIndex / 8);
            uint ccx = Convert.ToUInt32(coreIndex / coresInCcx - ccxInCcd * ccd);
            uint core = Convert.ToUInt32(coreIndex % coresInCcx);
            return MakeCoreMask(core, ccd, ccx);
        }

        public uint GetCoreIdMask(uint coreIndex)
        {
            return GetCoreMask((int)GetPhysicalCore(coreIndex));
        }
        public uint GetPhysicalCore(uint coreIndex)
        {
            if (coreIndex > info.topology.physicalCores || coreIndex < 0) return 0;
            return info.topology.coreMap[coreIndex];
        }

        public uint GetCcd(uint coreIndex)
        {
            uint ccxInCcd = info.family == Cpu.Family.FAMILY_19H ? 1U : 2U;
            uint coresInCcx = 8 / ccxInCcd;

            return Convert.ToUInt32(GetPhysicalCore(coreIndex) / 8);
        }

        public uint GetCcx(uint coreIndex)
        {
            uint ccxInCcd = info.family == Cpu.Family.FAMILY_19H ? 1U : 2U;
            uint coresInCcx = 8 / ccxInCcd;
            uint ccd = Convert.ToUInt32(coreIndex / 8);

            return Convert.ToUInt32(GetPhysicalCore(coreIndex) / coresInCcx - ccxInCcd * ccd);
        }

        public bool ReadDwordEx(uint addr, ref uint data)
        {
            bool res = false;
            if (Ring0.WaitPciBusMutex(10))
            {
                if (Ring0.WritePciConfig(smu.SMU_PCI_ADDR, smu.SMU_OFFSET_ADDR, addr))
                    res = Ring0.ReadPciConfig(smu.SMU_PCI_ADDR, smu.SMU_OFFSET_DATA, out data);
                Ring0.ReleasePciBusMutex();
            }
            return res;
        }

        public uint ReadDword(uint addr)
        {
            uint data = 0;

            if (Ring0.WaitPciBusMutex(10))
            {
                Ring0.WritePciConfig(smu.SMU_PCI_ADDR, (byte)smu.SMU_OFFSET_ADDR, addr);
                Ring0.ReadPciConfig(smu.SMU_PCI_ADDR, (byte)smu.SMU_OFFSET_DATA, out data);
                Ring0.ReleasePciBusMutex();
            }

            return data;
        }

        public bool IsValidPsmCheck(int coreIndex)
        {
            SMUCommands.CmdResult result;
            bool psmCheck = false;

            int highlimit = 30;
            int lowlimit = info.codeName == CodeName.Raphael ? -16959 : -30;

            for (int r = 0; r < 10; r++)
            {
                if (r > 0) Thread.Sleep(10);
                result = new SMUCommands.GetPsmMarginSingleCore(smu).Execute(GetCoreMask(coreIndex));
                if (result.status == SMU.Status.FAILED) { psmCheck = false; r = 10; }
                if (result.status == SMU.Status.OK)
                {
                    psmCheck = (int)result.args[0] > highlimit || (int)result.args[0] < lowlimit ? false : true;
                    r = 10;
                }
            }

            return psmCheck;
        }
        public uint CheckCoreLayout(uint oldCoreLayout)
        {
            try 
            {
                if (info.codeName != CodeName.Raphael) return oldCoreLayout;

                uint newCoreLayout = 0;
                uint setbit;

                for (int ccd = 0; ccd < info.topology.ccds; ccd++)
                {
                    int p = 0;
                    bool psmCheck = false;

                    uint _oldCoreLayout = (oldCoreLayout >> ccd * 8) & 0xff;

                    for (int i = 0; i < 8; _oldCoreLayout >>= 1, ++i)
                    {
                        setbit = (_oldCoreLayout & 1) == 1 ? (uint)1 : 0;

                        if (setbit == 1)
                        {
                            while (true)
                            {
                                psmCheck = IsValidPsmCheck(p + (ccd * 8));
                                if (psmCheck) break;
                                p++;
                            }
                        }
                        newCoreLayout |= (uint)setbit << p + (ccd * 8);
                        p++;
                        i = p < 8 ? i : 8;
                    }

                }
                return newCoreLayout;
            }
            catch
            {
                return 0;
            }
        }
        public bool WriteDwordEx(uint addr, uint data)
        {
            bool res = false;
            if (Ring0.WaitPciBusMutex(10))
            {
                if (Ring0.WritePciConfig(smu.SMU_PCI_ADDR, (byte)smu.SMU_OFFSET_ADDR, addr))
                    res = Ring0.WritePciConfig(smu.SMU_PCI_ADDR, (byte)smu.SMU_OFFSET_DATA, data);
                Ring0.ReleasePciBusMutex();
            }

            return res;
        }

        public double GetCoreMulti(int index = 0)
        {
            if (!Ring0.RdmsrTx(0xC0010293, out uint eax, out uint edx, GroupAffinity.Single(0, index)))
                return 0;

            double multi = 25 * (eax & 0xFF) / (12.5 * (eax >> 8 & 0x3F));
            return Math.Round(multi * 4, MidpointRounding.ToEven) / 4;
        }

        public bool Cpuid(uint index, ref uint eax, ref uint ebx, ref uint ecx, ref uint edx)
        {
            return Opcode.Cpuid(index, 0, out eax, out ebx, out ecx, out edx);
        }

        public bool ReadMsr(uint index, ref uint eax, ref uint edx)
        {
            return Ring0.Rdmsr(index, out eax, out edx);
        }

        public bool ReadMsrTx(uint index, ref uint eax, ref uint edx, int i)
        {
            GroupAffinity affinity = GroupAffinity.Single(0, i);

            return Ring0.RdmsrTx(index, out eax, out edx, affinity);
        }

        public bool WriteMsr(uint msr, uint eax, uint edx)
        {
            bool res = true;

            for (var i = 0; i < info.topology.logicalCores; i++)
            {
                res = Ring0.WrmsrTx(msr, eax, edx, GroupAffinity.Single(0, i));
            }

            return res;
        }

        public void WriteIoPort(uint port, byte value) => Ring0.WriteIoPort(port, value);
        public byte ReadIoPort(uint port) => Ring0.ReadIoPort(port);
        public bool ReadPciConfig(uint pciAddress, uint regAddress, ref uint value) => Ring0.ReadPciConfig(pciAddress, regAddress, out value);
        public uint GetPciAddress(byte bus, byte device, byte function) => Ring0.GetPciAddress(bus, device, function);

        // https://en.wikichip.org/wiki/amd/cpuid
        public CodeName GetCodeName(CPUInfo cpuInfo)
        {
            CodeName codeName = CodeName.Unsupported;

            if (cpuInfo.family == Family.FAMILY_15H)
            {
                switch (cpuInfo.model)
                {
                    case 0x65:
                        codeName = CodeName.BristolRidge;
                        break;
                }
            }
            else if (cpuInfo.family == Family.FAMILY_17H)
            {
                switch (cpuInfo.model)
                {
                    // Zen
                    case 0x1:
                        if (cpuInfo.packageType == PackageType.SP3)
                            codeName = CodeName.Naples;
                        else if (cpuInfo.packageType == PackageType.TRX)
                            codeName = CodeName.Whitehaven;
                        else
                            codeName = CodeName.SummitRidge;
                        break;
                    case 0x11:
                        codeName = CodeName.RavenRidge;
                        break;
                    case 0x20:
                        codeName = CodeName.Dali;
                        break;
                    // Zen+
                    case 0x8:
                        if (cpuInfo.packageType == PackageType.SP3 || cpuInfo.packageType == PackageType.TRX)
                            codeName = CodeName.Colfax;
                        else
                            codeName = CodeName.PinnacleRidge;
                        break;
                    case 0x18:
                        if (cpuInfo.packageType == PackageType.AM4)
                            codeName = CodeName.RavenRidge2;
                        else
                            codeName = CodeName.Picasso;
                        break;
                    case 0x50: // Subor Z+, CPUID 0x00850F00
                        codeName = CodeName.FireFlight;
                        break;
                    // Zen2
                    case 0x31:
                        if (cpuInfo.packageType == PackageType.TRX)
                            codeName = CodeName.CastlePeak;
                        else
                            codeName = CodeName.Rome;
                        break;
                    case 0x60:
                        codeName = CodeName.Renoir;
                        break;
                    case 0x68:
                        codeName = CodeName.Lucienne;
                        break;
                    case 0x71:
                        codeName = CodeName.Matisse;
                        break;
                    case 0x90:
                        codeName = CodeName.VanGogh;
                        break;

                    default:
                        codeName = CodeName.Unsupported;
                        break;
                }
            }
            else if (cpuInfo.family == Family.FAMILY_19H)
            {
                switch (cpuInfo.model)
                {
                    // Does Chagall (Zen3 TR) has different model number than Milan?
                    case 0x1:
                        if (cpuInfo.packageType == PackageType.TRX)
                            codeName = CodeName.Chagall;
                        else
                            codeName = CodeName.Milan;
                        break;
                    case 0x21:
                        codeName = CodeName.Vermeer;
                        break;
                    case 0x44:
                        codeName = CodeName.Rembrandt;
                        break;
                    case 0x50:
                        codeName = CodeName.Cezanne;
                        break;
                    case 0x61:
                        codeName = CodeName.Raphael;
                        break;
                    case 0x74:
                    case 0x78:
                        codeName = CodeName.Phoenix;
                        break;
                    case 0xa0:
                        codeName = CodeName.Mendocino;
                        break;
                    default:
                        codeName = CodeName.Unsupported;
                        break;
                }
            }

            return codeName;
        }

        // SVI2 interface
        public SVI2 GetSVI2Info(CodeName codeName)
        {
            var svi = new SVI2();

            switch (codeName)
            {
                case CodeName.BristolRidge:
                    break;

                //Zen, Zen+
                case CodeName.SummitRidge:
                case CodeName.PinnacleRidge:
                case CodeName.RavenRidge:
                case CodeName.RavenRidge2:
                case CodeName.FireFlight:
                case CodeName.Dali:
                    svi.coreAddress = Constants.F17H_M01H_SVI_TEL_PLANE0;
                    svi.socAddress = Constants.F17H_M01H_SVI_TEL_PLANE1;
                    break;

                // Zen Threadripper/EPYC
                case CodeName.Whitehaven:
                case CodeName.Naples:
                case CodeName.Colfax:
                    svi.coreAddress = Constants.F17H_M01H_SVI_TEL_PLANE1;
                    svi.socAddress = Constants.F17H_M01H_SVI_TEL_PLANE0;
                    break;

                // Zen2 Threadripper/EPYC
                case CodeName.CastlePeak:
                case CodeName.Rome:
                    svi.coreAddress = Constants.F17H_M30H_SVI_TEL_PLANE0;
                    svi.socAddress = Constants.F17H_M30H_SVI_TEL_PLANE1;
                    break;

                // Picasso
                case CodeName.Picasso:
                    if ((smu.Version & 0xFF000000) > 0)
                    {
                        svi.coreAddress = Constants.F17H_M01H_SVI_TEL_PLANE0;
                        svi.socAddress = Constants.F17H_M01H_SVI_TEL_PLANE1;
                    }
                    else
                    {
                        svi.coreAddress = Constants.F17H_M01H_SVI_TEL_PLANE1;
                        svi.socAddress = Constants.F17H_M01H_SVI_TEL_PLANE0;
                    }
                    break;

                // Zen2
                case CodeName.Matisse:
                    svi.coreAddress = Constants.F17H_M70H_SVI_TEL_PLANE0;
                    svi.socAddress = Constants.F17H_M70H_SVI_TEL_PLANE1;
                    break;

                // Zen2 APU, Zen3 APU ?
                case CodeName.Renoir:
                case CodeName.Lucienne:
                case CodeName.Mendocino:
                case CodeName.Cezanne:
                case CodeName.VanGogh:
                case CodeName.Rembrandt:
                case CodeName.Phoenix:
                    svi.coreAddress = Constants.F17H_M60H_SVI_TEL_PLANE0;
                    svi.socAddress = Constants.F17H_M60H_SVI_TEL_PLANE1;
                    break;

                // Zen3, Zen3 Threadripper/EPYC ?
                case CodeName.Vermeer:
                case CodeName.Raphael: // Unknown
                    svi.coreAddress = Constants.F19H_M21H_SVI_TEL_PLANE0;
                    svi.socAddress = Constants.F19H_M21H_SVI_TEL_PLANE1;
                    break;
                case CodeName.Chagall:
                case CodeName.Milan:
                    svi.coreAddress = Constants.F19H_M01H_SVI_TEL_PLANE1;
                    svi.socAddress = Constants.F19H_M01H_SVI_TEL_PLANE0;
                    break;

                default:
                    svi.coreAddress = Constants.F17H_M01H_SVI_TEL_PLANE0;
                    svi.socAddress = Constants.F17H_M01H_SVI_TEL_PLANE1;
                    break;
            }

            return svi;
        }
        public string GetVendor()
        {
            if (Opcode.Cpuid(0, 0, out uint _eax, out uint ebx, out uint ecx, out uint edx))
                return Utils.IntToStr(ebx) + Utils.IntToStr(edx) + Utils.IntToStr(ecx);
            return "";
        }

        public string GetCpuName()
        {
            string model = "";

            if (Opcode.Cpuid(0x80000002, 0, out uint eax, out uint ebx, out uint ecx, out uint edx))
                model = model + Utils.IntToStr(eax) + Utils.IntToStr(ebx) + Utils.IntToStr(ecx) + Utils.IntToStr(edx);

            if (Opcode.Cpuid(0x80000003, 0, out eax, out ebx, out ecx, out edx))
                model = model + Utils.IntToStr(eax) + Utils.IntToStr(ebx) + Utils.IntToStr(ecx) + Utils.IntToStr(edx);

            if (Opcode.Cpuid(0x80000004, 0, out eax, out ebx, out ecx, out edx))
                model = model + Utils.IntToStr(eax) + Utils.IntToStr(ebx) + Utils.IntToStr(ecx) + Utils.IntToStr(edx);

            return model.Trim();
        }

   
        public uint GetPatchLevel()
        {
            if (Ring0.Rdmsr(0x8b, out uint eax, out uint edx))
                return eax;
            return 0;
        }
        public uint GetApicId(uint coreId)
        {
            if (info.topology.cores2apicId == null) return 0;
            if (info.topology.cores2apicId.Length < coreId) return 0;

            return info.topology.cores2apicId[coreId];
        }

        public bool GetOcMode()
        {
            if (info.codeName == CodeName.SummitRidge)
            {
                if (Ring0.Rdmsr(0xC0010063, out uint eax, out uint edx))
                {
                    // Summit Ridge, Raven Ridge
                    return Convert.ToBoolean((eax >> 1) & 1);
                }
                return false;
            }

            if (info.family == Family.FAMILY_15H)
            {
                return false;
            }

            return Equals(GetPBOScalar(), 0.0f);
        }

        public int GetPBOScalar()
        {
            var cmd = new SMUCommands.GetPBOScalar(smu);
            cmd.Execute();

            return (int)cmd.Scalar;
        }

        public int GetBoostLimit(uint coreId)
        {
            var cmd = new SMUCommands.GetBoostLimit(smu);
            cmd.Execute(GetApicId(coreId));

            return cmd.BoostLimit;
        }

        public int GetMaxPPTLimit()
        {
            int ret_ppt = 0;

            if (smu.Hsmp.ReadMaxSocketPowerLimit != 0x0 && smu.Hsmp.IsSupported)
            {
                var cmd = new SMUCommands.GetMaxPPTLimit(smu);
                cmd.Execute();

                if (cmd.PPT > 0)
                {
                    ret_ppt = cmd.PPT;
                }
                else
                {
                    ret_ppt = -5;
                }
            }

            if (!info.PPTSupported && ret_ppt < 1) return ret_ppt;

            if (smu.Rsmu.SMU_MSG_SetPPTLimit == 0x0 && smu.Mp1Smu.SMU_MSG_SetPPTLimit == 0x0 && ret_ppt < 1) return -1;

            if (ret_ppt < 1)
            {
                SMU.Status status = RefreshPowerTable();
                Thread.Sleep(25);

                if (status == SMU.Status.OK && powerTable.PPT > 0)
                {
                    uint _ppt = (uint)powerTable.PPT;

                    SMU.Status statusrsmu = SetPPTLimit(2000);
                    Thread.Sleep(25);
                    status = RefreshPowerTable();

                    if (statusrsmu == SMU.Status.OK && status == SMU.Status.OK && powerTable.PPT > 0)
                    {
                        ret_ppt = powerTable.PPT;
                        Thread.Sleep(25);
                        SetPPTLimit(_ppt);
                    }
                    else ret_ppt = -2;

                    if (statusrsmu != SMU.Status.OK || ret_ppt < 0)
                    {
                        SMU.Status statusmp1 = SetPPTLimitMP1(2000);
                        Thread.Sleep(25);
                        status = RefreshPowerTable();

                        if (statusmp1 == SMU.Status.OK && status == SMU.Status.OK && powerTable.PPT > 0)
                        {
                            ret_ppt = powerTable.PPT;
                            Thread.Sleep(25);
                            SetPPTLimitMP1(_ppt);
                        }
                        else ret_ppt = -3;
                    }

                }
                else ret_ppt = -4;

            }

            return ret_ppt;
        }

        public int GetPPTLimit()
        {
            if (smu.Hsmp.ReadMaxSocketPowerLimit != 0x0 && smu.Hsmp.IsSupported) 
            { 
                var cmd = new SMUCommands.GetPPTLimit(smu);
                cmd.Execute();
                if (cmd.PPT > 0) return cmd.PPT;
            }

            if (info.PPTSupported && powerTable.PPT > 0) return powerTable.PPT;
            return 0;
        }

        public int GetMaxTDCLimit()
        {
            int ret_tdc;

            if (!info.TDCSupported) return 0;

            if (smu.Rsmu.SMU_MSG_SetTDCVDDLimit == 0x0 && smu.Mp1Smu.SMU_MSG_SetTDCVDDLimit == 0x0) return -1;

            SMU.Status status = RefreshPowerTable();
            Thread.Sleep(25);

            if (status == SMU.Status.OK && powerTable.TDC > 0)
            {
                uint _tdc = (uint)powerTable.TDC;

                SMU.Status statusrsmu = SetTDCVDDLimit(2000);
                Thread.Sleep(25);
                status = RefreshPowerTable();

                if (statusrsmu == SMU.Status.OK && status == SMU.Status.OK && powerTable.TDC > 0)
                {
                    ret_tdc = powerTable.TDC;
                    Thread.Sleep(25);
                    SetTDCVDDLimit(_tdc);
                }
                else ret_tdc = -2;

                if (statusrsmu != SMU.Status.OK || ret_tdc < 0)
                {
                    SMU.Status statusmp1 = SetTDCVDDLimitMP1(2000);
                    Thread.Sleep(25);
                    status = RefreshPowerTable();

                    if (statusmp1 == SMU.Status.OK && status == SMU.Status.OK && powerTable.TDC > 0)
                    {
                        ret_tdc = powerTable.TDC;
                        Thread.Sleep(25);
                        SetTDCVDDLimitMP1(_tdc);
                    }
                    else ret_tdc = -3;
                }

            }
            else ret_tdc = -4;

            return ret_tdc;

        }

        public int GetMaxEDCLimit()
        {
            int ret_edc;

            if (!info.EDCSupported) return 0;

            if (smu.Rsmu.SMU_MSG_SetEDCVDDLimit == 0x0 && smu.Mp1Smu.SMU_MSG_SetEDCVDDLimit == 0x0) return -1;

            SMU.Status status = RefreshPowerTable();
            Thread.Sleep(25);

            if (status == SMU.Status.OK && powerTable.EDC > 0)
            {
                uint _edc = (uint)powerTable.EDC;

                SMU.Status statusrsmu = SetEDCVDDLimit(2000);
                Thread.Sleep(25);
                status = RefreshPowerTable();

                if (statusrsmu == SMU.Status.OK && status == SMU.Status.OK && powerTable.EDC > 0)
                {
                    ret_edc = powerTable.EDC;
                    Thread.Sleep(25);
                    SetEDCVDDLimit(_edc);
                }
                else ret_edc = -2;

                if (statusrsmu != SMU.Status.OK || ret_edc < 0)
                {
                    SMU.Status statusmp1 = SetEDCVDDLimitMP1(2000);
                    Thread.Sleep(25);
                    status = RefreshPowerTable();

                    if (statusmp1 == SMU.Status.OK && status == SMU.Status.OK && powerTable.EDC > 0)
                    {
                        ret_edc = powerTable.EDC;
                        Thread.Sleep(25);
                        SetEDCVDDLimitMP1(_edc);
                    }
                    else ret_edc = -3;
                }

            }
            else ret_edc = -4;

            return ret_edc;

        }

        public int GetMaxTHMLimit()
        {
            int ret_thm;

            if (!info.THMSupported) return 0;

            if (smu.Rsmu.SMU_MSG_SetHTCLimit == 0x0 && smu.Mp1Smu.SMU_MSG_SetHTCLimit == 0x0) return -1;

            SMU.Status status = RefreshPowerTable();
            Thread.Sleep(25);

            if (status == SMU.Status.OK && powerTable.THM > 0)
            {
                uint _thm = (uint)powerTable.THM;

                SMU.Status statusrsmu = SetHTCLimit(200);
                Thread.Sleep(25);
                status = RefreshPowerTable();

                if (statusrsmu == SMU.Status.OK && status == SMU.Status.OK && powerTable.THM > 0)
                {
                    ret_thm = powerTable.THM;
                    Thread.Sleep(25);
                    SetHTCLimit(_thm);
                }
                else ret_thm = -2;

                if (statusrsmu != SMU.Status.OK || ret_thm < 0)
                {
                    SMU.Status statusmp1 = SetHTCLimit(200);
                    Thread.Sleep(25);
                    status = RefreshPowerTable();

                    if (statusmp1 == SMU.Status.OK && status == SMU.Status.OK && powerTable.THM > 0)
                    {
                        ret_thm = powerTable.THM;
                        Thread.Sleep(25);
                        SetHTCLimitMP1(_thm);
                    }
                    else ret_thm = -3;
                }

            }
            else ret_thm = - 4;

            return ret_thm;
        }

        public int GetMaxBoostLimit()
        {
            uint _boost = (uint)GetBoostLimit(0);

            if (_boost > 0)
            {
                SMU.Status status = SetBoostLimitAllCore(9999);

                if (status == SMU.Status.OK)
                {
                    uint maxboost = (uint)GetBoostLimit(0);
                    SetBoostLimitAllCore(_boost);
                    return (int)maxboost;
                }
                else return 0;
            }
            else return 0;
        }

        public bool SetPsmSingleCoreId(uint core, int margin)
        {
            return SetPsmMarginSingleCore(GetCoreIdMask(core), margin);
        }

        public int? GetPsmSingleCoreId(uint core)
        {
            return GetPsmMarginSingleCore(GetCoreIdMask(core));
        }

        public bool SendTestMessage(uint arg = 1, Mailbox mbox = null)
        {
            var cmd = new SMUCommands.SendTestMessage(smu, mbox);
            SMUCommands.CmdResult result = cmd.Execute(arg);
            return result.Success && cmd.IsSumCorrect;
        }
        public uint GetSmuVersion() => new SMUCommands.GetSmuVersion(smu).Execute().args[0];
        public uint GetHsmpVersion() => new SMUCommands.GetHsmpVersion(smu).Execute().args[0];
        public double? GetBclk() => mmio.GetBclk();
        public bool SetBclk(double blck) => mmio.SetBclk(blck);
        public SMU.Status TransferTableToDram() => new SMUCommands.TransferTableToDram(smu).Execute().status;
        public uint GetTableVersion() => new SMUCommands.GetTableVersion(smu).Execute().args[0];
        public uint GetDramBaseAddress() => new SMUCommands.GetDramAddress(smu).Execute().args[0];
        public long GetDramBaseAddress64()
        {
            SMUCommands.CmdResult result = new SMUCommands.GetDramAddress(smu).Execute();
            return (long)result.args[1] << 32 | result.args[0];
        }
        public bool GetLN2Mode() => new SMUCommands.GetLN2Mode(smu).Execute().args[0] == 1;
        public SMU.Status SetPPTLimit(uint arg = 0U) => new SMUCommands.SetSmuLimit(smu).Execute(smu.Rsmu.SMU_MSG_SetPPTLimit, arg).status;
        public SMU.Status SetPPTLimitMP1(uint arg = 0U) => new SMUCommands.SetSmuLimit(smu, smu.Mp1Smu).Execute(smu.Mp1Smu.SMU_MSG_SetPPTLimit, arg).status;
        public SMU.Status SetEDCVDDLimit(uint arg = 0U) => new SMUCommands.SetSmuLimit(smu).Execute(smu.Rsmu.SMU_MSG_SetEDCVDDLimit, arg).status;
        public SMU.Status SetEDCVDDLimitMP1(uint arg = 0U) => new SMUCommands.SetSmuLimit(smu, smu.Mp1Smu).Execute(smu.Mp1Smu.SMU_MSG_SetEDCVDDLimit, arg).status;
        public SMU.Status SetEDCSOCLimit(uint arg = 0U) => new SMUCommands.SetSmuLimit(smu).Execute(smu.Rsmu.SMU_MSG_SetEDCSOCLimit, arg).status;
        public SMU.Status SetEDCSOCLimitMP1(uint arg = 0U) => new SMUCommands.SetSmuLimit(smu, smu.Mp1Smu).Execute(smu.Mp1Smu.SMU_MSG_SetEDCSOCLimit, arg).status;
        public SMU.Status SetTDCVDDLimit(uint arg = 0U) => new SMUCommands.SetSmuLimit(smu).Execute(smu.Rsmu.SMU_MSG_SetTDCVDDLimit, arg).status;
        public SMU.Status SetTDCVDDLimitMP1(uint arg = 0U) => new SMUCommands.SetSmuLimit(smu, smu.Mp1Smu).Execute(smu.Mp1Smu.SMU_MSG_SetTDCVDDLimit, arg).status;
        public SMU.Status SetTDCSOCLimit(uint arg = 0U) => new SMUCommands.SetSmuLimit(smu).Execute(smu.Rsmu.SMU_MSG_SetTDCSOCLimit, arg).status;
        public SMU.Status SetTDCSOCLimitMP1(uint arg = 0U) => new SMUCommands.SetSmuLimit(smu, smu.Mp1Smu).Execute(smu.Mp1Smu.SMU_MSG_SetTDCSOCLimit, arg).status;
        public SMU.Status SetOverclockCpuVid(byte arg) => new SMUCommands.SetOverclockCpuVid(smu).Execute(arg).status;
        public SMU.Status SetHTCLimit(uint arg = 0U) => new SMUCommands.SetHTCLimit(smu).Execute(smu.Rsmu.SMU_MSG_SetHTCLimit, arg).status; 
        public SMU.Status SetHTCLimitMP1(uint arg = 0U) => new SMUCommands.SetHTCLimit(smu, smu.Mp1Smu).Execute(smu.Mp1Smu.SMU_MSG_SetHTCLimit, arg).status;
        public SMU.Status SetBoostLimit(uint arg = 0U) => new SMUCommands.SetBoostLimit(smu).Execute(smu.Hsmp.WriteBoostLimit, arg).status;
        public SMU.Status SetBoostLimitAllCore(uint arg = 0U) => new SMUCommands.SetBoostLimit(smu).Execute(smu.Hsmp.WriteBoostLimitAllCores, arg).status;
        public SMU.Status EnableOcMode() => new SMUCommands.SetOcMode(smu).Execute(true, info.codeName).status;
        public SMU.Status DisableOcMode() => new SMUCommands.SetOcMode(smu).Execute(false, info.codeName).status;
        public SMU.Status SetPBOScalar(uint scalar) => new SMUCommands.SetPBOScalar(smu).Execute(scalar).status;
        public SMU.Status RefreshPowerTable() => powerTable != null ? powerTable.Refresh() : SMU.Status.FAILED;
        public int? GetPsmMarginSingleCore(uint coreMask)
        {
            SMUCommands.CmdResult result = new SMUCommands.GetPsmMarginSingleCore(smu).Execute(coreMask);
            return result.Success ? (int)result.args[0] : (int?)null;
        }
        public int? GetPsmMarginSingleCore(uint core, uint ccd, uint ccx) => GetPsmMarginSingleCore(MakeCoreMask(core, ccd, ccx));
        public bool SetPsmMarginAllCores(int margin) => new SMUCommands.SetPsmMarginAllCores(smu).Execute(margin).Success;
        public bool SetPsmMarginSingleCore(uint coreMask, int margin) => new SMUCommands.SetPsmMarginSingleCore(smu).Execute(coreMask, margin).Success;
        public bool SetPsmMarginSingleCore(uint core, uint ccd, uint ccx, int margin) => SetPsmMarginSingleCore(MakeCoreMask(core, ccd, ccx), margin);
        public bool SetBoostLimitSingleCore(uint coreId, uint frequency) => new SMUCommands.SetBoostLimitPerCore(smu).Execute(GetApicId(coreId), frequency).Success;
        public bool SetFrequencyAllCore(uint frequency) => new SMUCommands.SetFrequencyAllCore(smu).Execute(frequency).Success;
        public bool SetFrequencySingleCore(uint coreMask, uint frequency) => new SMUCommands.SetFrequencySingleCore(smu).Execute(coreMask, frequency).Success;
        public bool SetFrequencySingleCore(uint core, uint ccd, uint ccx, uint frequency) => SetFrequencySingleCore(MakeCoreMask(core, ccd, ccx), frequency);
        private bool SetFrequencyMultipleCores(uint mask, uint frequency, int count)
        {
            // ((i.CCD << 4 | i.CCX % 2 & 0xF) << 4 | i.CORE % 4 & 0xF) << 20;
            for (uint i = 0; i < count; i++)
            {
                mask = Utils.SetBits(mask, 20, 2, i);
                if (!SetFrequencySingleCore(mask, frequency))
                    return false;
            }
            return true;
        }
        public bool SetFrequencyCCX(uint mask, uint frequency) => SetFrequencyMultipleCores(mask, frequency, 8/*SI.NumCoresInCCX*/);
        public bool SetFrequencyCCD(uint mask, uint frequency)
        {
            bool ret = true;
            for (uint i = 0; i < systemInfo.CCXCount / systemInfo.CCDCount; i++)
            {
                mask = Utils.SetBits(mask, 24, 1, i);
                ret = SetFrequencyCCX(mask, frequency);
            }

            return ret;
        }
        public bool IsProchotEnabled()
        {
            uint data = ReadDword(0x59804);
            return (data & 1) == 1;
        }

        public float? GetCpuTemperature()
        {
            uint thmData = 0;

            if (ReadDwordEx(Constants.THM_CUR_TEMP, ref thmData))
            {
                float offset = 0.0f;

                // Get tctl temperature offset
                // Offset table: https://github.com/torvalds/linux/blob/master/drivers/hwmon/k10temp.c#L78
                if (info.cpuName.Contains("2700X"))
                    offset = -10.0f;
                else if (info.cpuName.Contains("1600X") || info.cpuName.Contains("1700X") || info.cpuName.Contains("1800X"))
                    offset = -20.0f;
                else if (info.cpuName.Contains("Threadripper 19") || info.cpuName.Contains("Threadripper 29"))
                    offset = -27.0f;

                // THMx000[31:21] = CUR_TEMP, THMx000[19] = CUR_TEMP_RANGE_SEL
                // Range sel = 0 to 255C (Temp = Tctl - offset)
                float temperature = (thmData >> 21) * 0.125f + offset;

                // Range sel = -49 to 206C (Temp = Tctl - offset - 49)
                if ((thmData & Constants.THM_CUR_TEMP_RANGE_SEL_MASK) != 0)
                    temperature -= 49.0f;

                return temperature;
            }

            return null;
        }

        public bool RefreshSensors()
        {

            if (info.family != Family.FAMILY_17H && info.family != Family.FAMILY_19H) return false;

            if (Ring0.WaitPciBusMutex(25))
            {
                GroupAffinity previousAffinity = ThreadAffinity.Set(cpu0Affinity);

                Ring0.WritePciConfig(0x00, Constants.F17H_PCI_CONTROL_REGISTER, Constants.THM_CUR_TEMP);
                Ring0.ReadPciConfig(0x00, Constants.F17H_PCI_CONTROL_REGISTER + 4, out uint temperature);

                uint smuSvi0Tfn = 0;
                uint smuSvi0TelPlane0 = 0;
                uint smuSvi0TelPlane1 = 0;

                Ring0.WritePciConfig(0x00, Constants.F17H_PCI_CONTROL_REGISTER, Constants.F17H_M01H_SVI + 0x8);
                Ring0.ReadPciConfig(0x00, Constants.F17H_PCI_CONTROL_REGISTER + 4, out smuSvi0Tfn);

                Ring0.WritePciConfig(0x00, Constants.F17H_PCI_CONTROL_REGISTER, info.svi2.coreAddress);
                Ring0.ReadPciConfig(0x00, Constants.F17H_PCI_CONTROL_REGISTER + 4, out smuSvi0TelPlane0);

                Ring0.WritePciConfig(0x00, Constants.F17H_PCI_CONTROL_REGISTER, info.svi2.socAddress);
                Ring0.ReadPciConfig(0x00, Constants.F17H_PCI_CONTROL_REGISTER + 4, out smuSvi0TelPlane1);

                for (int ccd = 0; ccd < 2; ccd++)
                {
                    Ring0.WritePciConfig(0x00, Constants.F17H_PCI_CONTROL_REGISTER, Constants.F17H_M70H_CCD_TEMP + ((uint)ccd * 0x4));
                    Ring0.ReadPciConfig(0x00, Constants.F17H_PCI_CONTROL_REGISTER + 4, out uint ccdRawTemp);

                    ccdRawTemp &= 0xFFF;
                    float ccdTemp = ((ccdRawTemp * 125) - 305000) * 0.001f;
                    if (ccd == 0)
                    {
                        if (ccdRawTemp > 0 && ccdTemp < 125) // Zen 2 reports 95 degrees C max, but it might exceed that.
                        {
                            ccd1Temp = ccdTemp;
                        }
                        else
                        {
                            ccd1Temp = null;
                        }
                    }
                    if (ccd == 1)
                    {
                        if (ccdRawTemp > 0 && ccdTemp < 125) // Zen 2 reports 95 degrees C max, but it might exceed that.
                        {
                            ccd2Temp = ccdTemp;
                        }
                        else
                        {
                            ccd2Temp = null;
                        }
                    }
                }

                Ring0.ReleasePciBusMutex();

                ThreadAffinity.Set(previousAffinity);

                double? bclk = mmio.GetBclk();
                if (bclk != null)
                {
                    cpuBusClock = (float)bclk;
                }
                else
                {
                    cpuBusClock = 100;
                }

                // current temp Bit [31:21]
                // If bit 19 of the Temperature Control register is set, there is an additional offset of 49 degrees C.
                bool tempOffsetFlag = (temperature & Constants.THM_CUR_TEMP_RANGE_SEL_MASK) != 0;
                temperature = (temperature >> 21) * 125;

                float offset = 0.0f;

                // Offset table: https://github.com/torvalds/linux/blob/master/drivers/hwmon/k10temp.c#L78

                if (info.cpuName.Contains("2700X"))
                    offset = -10.0f;
                else if (info.cpuName.Contains("1600X") || info.cpuName.Contains("1700X") || info.cpuName.Contains("1800X"))
                    offset = -20.0f;
                else if (info.cpuName.Contains("Threadripper 19") || info.cpuName.Contains("Threadripper 29"))
                    offset = -27.0f;

                if (temperature > 0)
                {
                    float t = temperature * 0.001f;
                    if (tempOffsetFlag)
                        t += -49.0f;

                    cpuTemp = offset < 0 ? t + offset : t;
                }


                const double vidStep = 0.00625;
                double vcc;
                uint svi0PlaneXVddCor;

                // Core (0x01)
                if ((smuSvi0Tfn & 0x01) == 0)
                {
                    svi0PlaneXVddCor = (smuSvi0TelPlane0 >> 16) & 0xff;
                    vcc = 1.550 - vidStep * svi0PlaneXVddCor;
                    cpuVcore = (float)vcc;
                }
                else
                {
                    cpuVcore = null;
                }

                // SoC (0x02)
                if (info.model == 0x11 || info.model == 0x21 || info.model == 0x71 || info.model == 0x31 || (smuSvi0Tfn & 0x02) == 0)
                {
                    svi0PlaneXVddCor = (smuSvi0TelPlane1 >> 16) & 0xff;
                    vcc = 1.550 - vidStep * svi0PlaneXVddCor;
                    cpuVsoc = (float)vcc;
                }
                else
                {
                    cpuVsoc = null;
                }

            }
            return true;
        }
 
        private double GetTimeStampCounterMultiplier()
        {
            if (info.family == Family.FAMILY_17H)
            {
                uint ctlEax, ctlEdx;
                Ring0.Rdmsr(Constants.MSR_PSTATE_CTL, out ctlEax, out ctlEdx);
                ctlEax = Utils.SetBits(ctlEax, 0, 3, 1);
                Ring0.Wrmsr(Constants.MSR_PSTATE_CTL, ctlEax , ctlEdx);
            }

            Ring0.Rdmsr(Constants.MSR_PSTATE_0, out uint eax, out _);

            uint cpuDfsId = (eax >> 8) & 0x3f;
            uint cpuFid = eax & 0xff;

            return 2.0 * cpuFid / cpuDfsId;
        }

		public float? GetSingleCcdTemperature(uint ccd)
        {
            uint thmData = 0;

            if (ReadDwordEx(Constants.F17H_M70H_CCD_TEMP + (ccd * 0x4), ref thmData))
            {
                float ccdTemp = (thmData & 0xfff) * 0.125f - 305.0f;
                if (ccdTemp > 0 && ccdTemp < 125) // Zen 2 reports 95 degrees C max, but it might exceed that.
                    return ccdTemp;
                return 0;
            }

            return null;
        }

        public void EnableTopologyExtensions()
        {
            uint ctlEax, ctlEdx;
            Ring0.Rdmsr(Constants.MSR_CPUID_EXTFEATURES, out ctlEax, out ctlEdx);
            ctlEax = Utils.SetBits(ctlEax, 54, 1, 1);
            Ring0.Wrmsr(Constants.MSR_CPUID_EXTFEATURES, ctlEax, ctlEdx);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    io.Dispose();
                    Ring0.Close();
                    Opcode.Close();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
