using System;

namespace ZenStates.Core.SMUCommands
{
    internal class GetMaxPPTLimit : BaseSMUCommand
    {
        public int PPT { get; protected set; }
        public GetMaxPPTLimit(SMU smu) : base(smu)
        {
            PPT = 0;
        }

        public override CmdResult Execute()
        {
            if (CanExecute())
            {
                result.status = smu.SendHsmpCommand(smu.Hsmp.ReadMaxSocketPowerLimit, ref result.args);
                if (result.Success)
                {
                    byte[] bytes = BitConverter.GetBytes(result.args[0]);
                    PPT = BitConverter.ToInt32(bytes, 0)/1000;
                }
            }

            return base.Execute();
        }
    }
}
