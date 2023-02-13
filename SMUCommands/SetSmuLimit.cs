namespace ZenStates.Core.SMUCommands
{
    internal class SetSmuLimit : BaseSMUCommand
    {
        private readonly Mailbox mbox;
        public SetSmuLimit(SMU smu, Mailbox mbox = null) : base(smu)
        {
            this.mbox = mbox ?? smu.Rsmu;
        }
        public CmdResult Execute(uint cmd, uint arg = 0U)
        {
            if (CanExecute() && cmd != 0x0)
            {
                result.args[0] = arg * 1000;
                result.status = smu.SendSmuCommand(mbox, cmd, ref result.args);
            }

            return base.Execute();
        }
    }
}
