// * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
// * Produced at the Lawrence Livermore National Laboratory.
// * All rights reserved.
// */

using gov.llnl.wintap.etl.models;

///*
internal class ProcessStartData : SensorData
{
    private readonly string _parentPidHash;
    private readonly int _parentPid;
    private readonly int _pid;
    private readonly string _pidHash;
    private readonly string _processName;
    private readonly long _startTime;
    private readonly long _eventTime;
    private readonly string _processPath;
    private readonly string _userName;
    private readonly string _userSid;
    private readonly string _fileMd5;
    private readonly string _fileSha2;
    private readonly string _processArgs;
    private readonly string _uniqueProcessKey;

    public string ParentPidHash
    {
        get { return _parentPidHash; }
    }

    public int ParentPid
    {
        get { return _parentPid; }
    }

    public int PID
    {
        get { return _pid; }
    }

    public string PidHash
    {
        get { return _pidHash; }
    }

    public string ProcessName
    {
        get { return _processName; }
    }

    public string ProcessPath
    {
        get { return _processPath; }
    }

    public long StartTime
    {
        get { return _startTime; }
    }

    public string FileMd5
    {
        get { return _fileMd5; }
    }

    public string FileSha2
    {
        get { return _fileSha2; }
    }

    public string UserName
    {
        get { return _userName; }
    }


    public string ProcessArgs
    {
        get { return _processArgs; }
    }

    public long EventTime
    {
        get { return _eventTime; }
    }


    public string MessageType
    {
        get { return "PROCESS"; }
    }

    public string ActivityType { get; set; }
    public string CommandLine { get; set; }
    public string Hostname { get; set; }

    public string UniqueProcessKey
    {
        get { return _uniqueProcessKey; }
    }

    public ProcessStartData(string parentPidHash, int parentPid, int pid, string pidHash, string processName, long startTime, string processPath, string userName, string userSid, string fileMd5, string fileSha2, string args, string commandLine, string uniqueProcessKey)
    {
        _parentPidHash = parentPidHash;
        _parentPid = parentPid;
        _pid = pid;
        _pidHash = pidHash;
        _processName = processName;
        _startTime = startTime;
        _processPath = processPath;
        _userName = userName;
        _userSid = userSid;
        _fileMd5 = fileMd5;
        _fileSha2 = fileSha2;
        _processArgs = args;
        _eventTime = startTime;
        _uniqueProcessKey = uniqueProcessKey;
        if(_uniqueProcessKey == null)
        {
            _uniqueProcessKey = "";
        }
    }
}
