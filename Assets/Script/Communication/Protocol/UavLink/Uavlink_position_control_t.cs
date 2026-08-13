using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Uavlink_position_control_t
{
    private static int LENGTH = 17; // 4 floats (4*4=16) + 1 byte = 17

    public float x;        // Position X (m)
    public float y;        // Position Y (m) 
    public float z;        // Position Z (m)
    public float yaw;      // Yaw angle (radians)
    public byte frame;     // Coordinate frame (0: local, 1: global)

    public Uavlink_position_control_t()
    {

    }

    public void Encode(out byte[] data)
    {
        data = new byte[LENGTH];
        int index = 0;

        BitConverter.GetBytes(x).CopyTo(data, index);
        BitConverter.GetBytes(y).CopyTo(data, index += 4);
        BitConverter.GetBytes(z).CopyTo(data, index += 4);
        BitConverter.GetBytes(yaw).CopyTo(data, index += 4);
        data[index += 4] = frame;
    }

    public void Decode(byte[] data)
    {
        int index = 0;

        x = BitConverter.ToSingle(data, index);
        y = BitConverter.ToSingle(data, index += 4);
        z = BitConverter.ToSingle(data, index += 4);
        yaw = BitConverter.ToSingle(data, index += 4);
        frame = data[index += 4];
    }
}
