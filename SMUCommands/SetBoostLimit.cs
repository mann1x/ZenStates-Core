namespace ZenStates.Core.SMUCommands
{
    internal class SetBoostLimit : BaseSMUCommand
    {
        public SetBoostLimit(SMU smu) : base(smu) { }
        public CmdResult Execute(uint cmd, uint arg = 0U)
        {
            if (CanExecute() && cmd != 0x0)
            {
                result.args[0] = arg;
                result.status = smu.SendHsmpCommand(cmd, ref result.args);
            }

            return base.Execute();
        }
    }
}
