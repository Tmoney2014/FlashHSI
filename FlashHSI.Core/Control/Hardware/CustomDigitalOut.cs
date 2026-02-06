using System;
using System.Linq;
using EtherCAT.NET.Infrastructure;

namespace FlashHSI.Core.Control.Hardware
{
    public class CustomDigitalOut : DigitalIn
    {
        private readonly object _lockObject = new();

        public CustomDigitalOut(SlaveInfo slave) : base(slave)
        {
        }

        public unsafe bool GetChannelBit(int channel)
        {
            var (bitOffset, memptr) = GetChannelInfo(channel);
            var memptrInt = (int*)memptr.ToPointer();
            return (memptrInt[0] & (1 << bitOffset)) != 0;
        }

        public unsafe void SetChannel(int channel, bool value)
        {
            lock (_lockObject)
            {
                var (bitOffset, memptr) = GetChannelInfo(channel);
                var memptrInt = (int*)memptr.ToPointer();

                if (value)
                    memptrInt[0] |= 1 << bitOffset;
                else
                    memptrInt[0] &= ~(1 << bitOffset);
            }
        }

        private unsafe (int bitOffset, IntPtr memptr) GetChannelInfo(int channel)
        {
            var totalVariables = _slavePdos.Sum(pdo => pdo.Variables.Count);
            if (channel < 1 || channel > totalVariables)
                throw new ArgumentOutOfRangeException(nameof(channel), "Channel is out of range.");

            var remainingChannels = channel - 1;
            var pdoIndex = 0;

            while (remainingChannels >= _slavePdos[pdoIndex].Variables.Count)
            {
                remainingChannels -= _slavePdos[pdoIndex].Variables.Count;
                pdoIndex++;
            }

            var pdo = _slavePdos[pdoIndex];
            var variable = pdo.Variables[remainingChannels];
            
            return (variable.BitOffset, new IntPtr((void*)variable.DataPtr));
        }
    }
}
