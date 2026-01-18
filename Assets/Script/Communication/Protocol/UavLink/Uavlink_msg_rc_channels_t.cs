using System;

public class Uavlink_msg_rc_channels_t
{
    public ushort chan1;
    public ushort chan2;
    public ushort chan3;
    public ushort chan4;
    public ushort chan5;
    public ushort chan6;
    public ushort chan7;
    public ushort chan8;

    public void Encode(out byte[] data)
    {
        data = new byte[16];
        Buffer.BlockCopy(BitConverter.GetBytes(chan1), 0, data, 0, 2);
        Buffer.BlockCopy(BitConverter.GetBytes(chan2), 0, data, 2, 2);
        Buffer.BlockCopy(BitConverter.GetBytes(chan3), 0, data, 4, 2);
        Buffer.BlockCopy(BitConverter.GetBytes(chan4), 0, data, 6, 2);
        Buffer.BlockCopy(BitConverter.GetBytes(chan5), 0, data, 8, 2);
        Buffer.BlockCopy(BitConverter.GetBytes(chan6), 0, data, 10, 2);
        Buffer.BlockCopy(BitConverter.GetBytes(chan7), 0, data, 12, 2);
        Buffer.BlockCopy(BitConverter.GetBytes(chan8), 0, data, 14, 2);
    }
}
