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
        public CmdResult Execute(uint apicId)
        {
            uint cmd = smu.Hsmp.ReadBoostLimit;
            if (CanExecute() && cmd != 0x0)
            {
                result.args[0] = apicId;
                result.status = smu.SendHsmpCommand(cmd, ref result.args);
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
