using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UavlinkDroneStatus
{
    public float Altitude { get; set; }
    public sbyte Battery { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
    public float VelocityZ { get; set; }
    public float Roll { get; set; }
    public float Pitch { get; set; }
    public float Yaw { get; set; }
    public float ShortDistance { get; set; }
    public float LongDistance { get; set; }

    public void Decode(byte[] data)
    {
        int offset = 0;
        Altitude = BitConverter.ToSingle(data, offset); offset += 4;
        Battery = (sbyte)data[offset]; offset += 1;
        Latitude = BitConverter.ToDouble(data, offset); offset += 8;
        Longitude = BitConverter.ToDouble(data, offset); offset += 8;
        PosX = BitConverter.ToSingle(data, offset); offset += 4;
        PosY = BitConverter.ToSingle(data, offset); offset += 4;
        PosZ = BitConverter.ToSingle(data, offset); offset += 4;
        VelocityX = BitConverter.ToSingle(data, offset); offset += 4;
        VelocityY = BitConverter.ToSingle(data, offset); offset += 4;
        VelocityZ = BitConverter.ToSingle(data, offset); offset += 4;
        Roll = BitConverter.ToSingle(data, offset); offset += 4;
        Pitch = BitConverter.ToSingle(data, offset); offset += 4;
        Yaw = BitConverter.ToSingle(data, offset); offset += 4;
        ShortDistance = BitConverter.ToSingle(data, offset); offset += 4;
        LongDistance = BitConverter.ToSingle(data, offset); offset += 4;

    }
}
