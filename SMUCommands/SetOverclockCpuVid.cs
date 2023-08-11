namespace ZenStates.Core.SMUCommands
{
    internal class SetOverclockCpuVid : BaseSMUCommand
    {
        private readonly Mailbox mbox;
        public SetOverclockCpuVid(SMU smu, Mailbox mbox = null) : base(smu)
        {
            this.mbox = mbox ?? smu.Rsmu;
        }
        public CmdResult Execute(uint arg = 0U)
        {
            if (CanExecute())
            {
                uint cmd = ReferenceEquals(smu.Rsmu, mbox) ? smu.Rsmu.SMU_MSG_SetOverclockCpuVid : smu.Mp1Smu.SMU_MSG_SetOverclockCpuVid;
                if (cmd == 0 && ReferenceEquals(smu.Rsmu, mbox)) cmd = smu.Mp1Smu.SMU_MSG_SetOverclockCpuVid;
                if (cmd == 0 && ReferenceEquals(smu.Mp1Smu, mbox)) cmd = smu.Rsmu.SMU_MSG_SetOverclockCpuVid;
                if (cmd == 0) { result.status = SMU.Status.UNKNOWN_CMD; return result; }

                result.args[0] = arg;
                result.status = smu.SendSmuCommand(mbox, cmd, ref result.args);
            }
            return base.Execute();
        }
    }
}