namespace ZenStates.Core.SMUCommands
{
    internal class SetMp1HTCLimit : BaseSMUCommand
    {
        public SetMp1HTCLimit(SMU smu) : base(smu) { }
        public CmdResult Execute(uint cmd, uint arg = 0U)
        {
            if (CanExecute() && cmd != 0x0)
            {
                result.args[0] = arg;
                result.status = smu.SendMp1Command(cmd, ref result.args);
            }

            return base.Execute();
        }
    }
}
