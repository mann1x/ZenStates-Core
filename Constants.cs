namespace ZenStates.Core
{
    internal static class Constants
    {
        internal const string VENDOR_AMD = "AuthenticAMD";
        internal const string VENDOR_HYGON = "HygonGenuine";
        internal const uint F17H_M01H_SVI = 0x0005A000;
        internal const uint F17H_M60H_SVI = 0x0006F000; // Renoir only?
        internal const uint F17H_M01H_SVI_TEL_PLANE0 = (F17H_M01H_SVI + 0xC);
        internal const uint F17H_M01H_SVI_TEL_PLANE1 = (F17H_M01H_SVI + 0x10);
        internal const uint F17H_M30H_SVI_TEL_PLANE0 = (F17H_M01H_SVI + 0x14);
        internal const uint F17H_M30H_SVI_TEL_PLANE1 = (F17H_M01H_SVI + 0x10);
        internal const uint F17H_M60H_SVI_TEL_PLANE0 = (F17H_M60H_SVI + 0x38);
        internal const uint F17H_M60H_SVI_TEL_PLANE1 = (F17H_M60H_SVI + 0x3C);
        internal const uint F17H_M70H_SVI_TEL_PLANE0 = (F17H_M01H_SVI + 0x10);
        internal const uint F17H_M70H_SVI_TEL_PLANE1 = (F17H_M01H_SVI + 0xC);
        internal const uint F19H_M01H_SVI_TEL_PLANE0 = (F17H_M01H_SVI + 0x10);
        internal const uint F19H_M01H_SVI_TEL_PLANE1 = (F17H_M01H_SVI + 0x14);
        internal const uint F19H_M21H_SVI_TEL_PLANE0 = (F17H_M01H_SVI + 0x10);
        internal const uint F19H_M21H_SVI_TEL_PLANE1 = (F17H_M01H_SVI + 0xC);
        internal const uint F17H_M70H_CCD_TEMP = 0x00059954;
        internal const uint THM_CUR_TEMP = 0x00059800;
        internal const uint THM_CUR_TEMP_RANGE_SEL_MASK = 0x80000;
        internal const uint F17H_PCI_CONTROL_REGISTER = 0x60;
        internal const uint MSR_CPUID_EXTFEATURES = 0xC0011005;
        internal const uint MSR_PSTATE_0 = 0xC0010064;
        internal const uint MSR_PSTATE_CTL = 0xC0010062;
        internal const uint MSR_HWCR = 0xC0010015;
        internal const uint MSR_MPERF = 0x000000E7;
        internal const uint MSR_APERF = 0x000000E8;
        internal const uint MSR_APIC_BAR = 0x0000001B;

    }
}