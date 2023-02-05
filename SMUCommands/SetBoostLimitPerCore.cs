namespace ZenStates.Core.SMUCommands
{
    internal class SetBoostLimitPerCore : BaseSMUCommand
    {
        public SetBoostLimitPerCore(SMU smu) : base(smu) { }
        public CmdResult Execute(uint apicId, uint frequency)
        {
            uint cmd = smu.Hsmp.WriteBoostLimit;
            if (CanExecute() && cmd != 0x0)
            {
                    result.args[0] = frequency << 0 | apicId << 16;
                    if (cmd != 0)
                        result.status = smu.SendHsmpCommand(cmd, ref result.args);
                    else
                        result.status = SMU.Status.UNKNOWN_CMD;
            }

            return base.Execute();
        }
    }
}
