using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class Uavlink_cmd_setmode_t
{
    private static int LENGTH = 6;  // 2 bytes command ID + 4 bytes param1 (float)

    public float Mode { get; set; }

    public void Encode(out byte[] _data)
    {
        byte[] data = new byte[LENGTH];
        int index = 0;

        // Command ID (2 bytes)
        BitConverter.GetBytes((UInt16)CommandId.UAVLINK_CMD_SETMODE).CopyTo(data, index);
        index += 2;

        // param1 (4 bytes float)
        BitConverter.GetBytes(Mode).CopyTo(data, index);
        index += 4;

        _data = data;
    }
}