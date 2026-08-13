using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;


public class Waypoint 
{
    private int _waypointID;
    public int WaypointID
    {
        get { return _waypointID; }
    }

    private float _posX;
    public float PosX
    {
        get { return _posX; }
    }

    private float _posY;
    public float PosY
    {
        get { return _posY; }
    }

    private bool _isComplete;
    public bool IsComplete
    {
        get { return _isComplete; }
    }
}