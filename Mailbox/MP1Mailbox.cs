﻿namespace ZenStates.Core
{
    public sealed class MP1Mailbox : Mailbox
    {
        // Configurable commands
        public uint SMU_MSG_SetToolsDramAddress { get; set; } = 0x0;
        public uint SMU_MSG_EnableOcMode { get; set; } = 0x0;
        public uint SMU_MSG_DisableOcMode { get; set; } = 0x0;
        public uint SMU_MSG_SetOverclockFrequencyAllCores { get; set; } = 0x0;
        public uint SMU_MSG_SetOverclockFrequencyPerCore { get; set; } = 0x0;
        public uint SMU_MSG_SetBoostLimitFrequencyAllCores { get; set; } = 0x0;
        public uint SMU_MSG_SetBoostLimitFrequency { get; set; } = 0x0;
        public uint SMU_MSG_SetOverclockCpuVid { get; set; } = 0x0;
        public uint SMU_MSG_SetDldoPsmMargin { get; set; } = 0x0;
        public uint SMU_MSG_SetAllDldoPsmMargin { get; set; } = 0x0;
        public uint SMU_MSG_GetDldoPsmMargin { get; set; } = 0x0;
        public uint SMU_MSG_SetPBOScalar { get; set; } = 0x0;
        public uint SMU_MSG_SetTDCVDDLimit { get; set; } = 0x0;
        public uint SMU_MSG_SetTDCSOCLimit { get; set; } = 0x0;
        public uint SMU_MSG_SetEDCVDDLimit { get; set; } = 0x0;
        public uint SMU_MSG_SetEDCSOCLimit { get; set; } = 0x0;
        public uint SMU_MSG_SetPPTLimit { get; set; } = 0x0;
        public uint SMU_MSG_SetPPTSlowTime { get; set; } = 0x0;
        public uint SMU_MSG_SetPPTFastLimit { get; set; } = 0x0;
        public uint SMU_MSG_SetHTCLimit { get; set; } = 0x0;
        public uint SMU_MSG_SetSTAPMLimit { get; set; } = 0x0;
        public uint SMU_MSG_SetSTAPMTime { get; set; } = 0x0;
        public uint SMU_MSG_SetPPTAPULimit { get; set; } = 0x0;
        public uint SMU_MSG_GetSome1Clock { get; set; } = 0x0;
    }
}