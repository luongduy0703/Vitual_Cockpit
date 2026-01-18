using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Uavlink_msg_velocity_control_t
{
    private static int LENGTH = 17; // 4 floats (4*4=16) + 1 byte = 17

    public float vx;        // velocity X (m/s)
    public float vy;        // velocity Y (m/s) 
    public float vz;        // velocity Z (m/s)
    public float yaw_rate;  // Yaw angle (radians)
    public byte frame;      // Frame (0: ENU local map, 1: body frame)

    public Uavlink_msg_velocity_control_t()
    {

    }

    public void Encode(out byte[] data)
    {
        data = new byte[LENGTH];
        int index = 0;

        BitConverter.GetBytes(vx).CopyTo(data, index);
        BitConverter.GetBytes(vy).CopyTo(data, index += 4);
        BitConverter.GetBytes(vz).CopyTo(data, index += 4);
        BitConverter.GetBytes(yaw_rate).CopyTo(data, index += 4);
        data[index += 4] = frame;
    }

    public void Decode(byte[] data)
    {
        int index = 0;

        vx = BitConverter.ToSingle(data, index);
        vy = BitConverter.ToSingle(data, index += 4);
        vz = BitConverter.ToSingle(data, index += 4);
        yaw_rate = BitConverter.ToSingle(data, index += 4);
        frame = data[index += 4];
    }
}