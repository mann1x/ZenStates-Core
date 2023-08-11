namespace ZenStates.Core.SMUCommands
{
    internal class SetFrequencySingleCore : BaseSMUCommand
    {
        private readonly Mailbox mbox;
        public SetFrequencySingleCore(SMU smu, Mailbox mbox = null) : base(smu)
        {
            this.mbox = mbox ?? smu.Rsmu;
        }
        public CmdResult Execute(uint coreMask, uint frequency)
        {
            if (CanExecute())
            {
                uint cmd = ReferenceEquals(smu.Rsmu, mbox) ? smu.Rsmu.SMU_MSG_SetOverclockFrequencyPerCore : smu.Mp1Smu.SMU_MSG_SetOverclockFrequencyPerCore;
                if (cmd == 0 && ReferenceEquals(smu.Rsmu, mbox)) cmd = smu.Mp1Smu.SMU_MSG_SetOverclockFrequencyPerCore;
                if (cmd == 0 && ReferenceEquals(smu.Mp1Smu, mbox)) cmd = smu.Rsmu.SMU_MSG_SetOverclockFrequencyPerCore;
                if (cmd == 0) { result.status = SMU.Status.UNKNOWN_CMD; return result; }

                result.args[0] = coreMask | frequency & 0xfffff;
                // TODO: Add Manual OC mode
                // TODO: Add lo and hi frequency limits

                result.status = smu.SendSmuCommand(mbox, cmd, ref result.args);

            }

            return base.Execute();
        }
    }
}
