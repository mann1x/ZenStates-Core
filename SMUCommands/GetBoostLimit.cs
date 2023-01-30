using System;

namespace ZenStates.Core.SMUCommands
{
    internal class GetBoostLimit : BaseSMUCommand
    {
        public int BoostLimit { get; protected set; }
        public GetBoostLimit(SMU smu) : base(smu) 
        {
            BoostLimit = 0;
        }
        public override CmdResult Execute()
        {
            if (CanExecute() && smu.Hsmp.ReadBoostLimit != 0x0)
            {
                result.status = smu.SendHsmpCommand(smu.Hsmp.ReadBoostLimit, ref result.args);
                if (result.Success)
                {
                    byte[] bytes = BitConverter.GetBytes(result.args[0]);
                    BoostLimit = BitConverter.ToInt32(bytes, 0);
                }
            }

            return base.Execute();
        }
    }
}
