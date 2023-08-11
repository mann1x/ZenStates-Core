namespace ZenStates.Core.SMUCommands
{
    // Set DLDO Psm margin for all cores
    // CO margin range seems to be from -30 to 30
    // Margin arg seems to be 16 bits (lowest 16 bits of the command arg)
    // [15-0] CO margin
    internal class SetPsmMarginAllCores : BaseSMUCommand
    {
        private readonly Mailbox mbox; 
        public SetPsmMarginAllCores(SMU smu, Mailbox mbox = null) : base(smu)
        {
            this.mbox = mbox ?? smu.Rsmu;
        }
        public override bool CanExecute()
        {
            return smu.Mp1Smu.SMU_MSG_SetAllDldoPsmMargin > 0 || smu.Rsmu.SMU_MSG_SetAllDldoPsmMargin > 0;
        }

        public CmdResult Execute(int margin)
        {
            if (CanExecute())
            {
                uint cmd = ReferenceEquals(smu.Rsmu, mbox) ? smu.Rsmu.SMU_MSG_SetAllDldoPsmMargin : smu.Mp1Smu.SMU_MSG_SetAllDldoPsmMargin;
                if (cmd == 0 && ReferenceEquals(smu.Rsmu, mbox)) cmd = smu.Mp1Smu.SMU_MSG_SetAllDldoPsmMargin;
                if (cmd == 0 && ReferenceEquals(smu.Mp1Smu, mbox)) cmd = smu.Rsmu.SMU_MSG_SetAllDldoPsmMargin;
                if (cmd == 0) { result.status = SMU.Status.UNKNOWN_CMD; return result; }

                result.args[0] = Utils.MakePsmMarginArg(margin);
                result.status = smu.SendSmuCommand(mbox, cmd, ref result.args);
            }

            return base.Execute();
        }
    }
}
