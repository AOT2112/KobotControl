using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace KOBOT_Control
{
    internal class KobotContol
    {
        public enum KobotState
        {
            NotConnected,
            Initializing,
            NeedPrepare,
            Preparing,
            Ready,
            Proccessing
        }
        public event EventHandler<KobotState> StateChanged;
        public KobotState GetState { get { return currentState; } }
        public int GetCurrentCommandIndex { get { return commander.GetCurrentCommandIndex; } }

        private readonly CultureInfo _ci = new CultureInfo("en");
        readonly object lockDrives = new object();
        const string INVALID_STATE_MESSAGE = "Manipulator State must be \"{0}\"!";
        const byte INTERVAL_PROC = 5;
        private volatile bool isConnected = false;
        volatile int isSending = 0;
        volatile int isProcRunning = 1;
        UdpClient listener;
        readonly UdpClient client;
        Thread threadSender;
        readonly Thread threadProc;
        readonly Dictionary<string, Drive> masterDrives;
        KobotState currentState = KobotState.NotConnected;
        KobotState targetState = KobotState.NotConnected;
        readonly Stopwatch stopwatch;
        readonly List<DriveFeedback> feedback;
        readonly Commander commander;
        readonly ConfigManager.RestApiParameters restApi;
        readonly ConfigManager.GripParameters grip;

        public KobotContol()
        {
            client = new UdpClient();
            feedback = new List<DriveFeedback>();
            masterDrives = new Dictionary<string, Drive>();
            ConfigManager cfgMgr = new ConfigManager();
            ConfigManager.Config cfg = cfgMgr.LoadConfig();
            if (cfg.Commands == null || cfg.Commands.Length == 0)
            {
                cfg.Commands = cfg.GetDefaultCommands();
            }
            cfg.Commands = cfg.Commands.Where(x => { return x.CommandName != null; }).ToArray();
            commander = new Commander(cfg, masterDrives);
            restApi = cfg.RestAPI;
            grip = cfg.Grip;
            stopwatch = new Stopwatch();
            threadProc = new Thread(new ThreadStart(Proc));
            threadProc.Start();
        }
        public KobotState State 
        { 
            get 
            {
                return currentState;
            } 
        }
        public bool Connect 
        { 
            get
            {
                return isConnected;
            } 
            set 
            {
                if (value)
                {
                    if (isConnected) 
                        throw new InvalidOperationException("Already connected!");
                    else
                    {
                        if (currentState == KobotState.NotConnected)
                        {
                            _connect();
                            isConnected = true;
                        }
                        else
                            throw new InvalidOperationException(string.Format(INVALID_STATE_MESSAGE, KobotState.NotConnected.ToString()));
                    }
                }
                else
                {
                    if (isConnected)
                    {
                        _disconnect();
                        isConnected = false;
                    }
                    else
                        throw new InvalidOperationException("Already disconnected!");
                }
            } 
        }

        public void Prepare()
        {
            if (currentState == KobotState.NeedPrepare)
                targetState = KobotState.Preparing;
            else
                throw new InvalidOperationException(string.Format(INVALID_STATE_MESSAGE, KobotState.NeedPrepare.ToString()));
        }

        public void Start()
        {
            if (currentState == KobotState.Ready)
                targetState = KobotState.Proccessing;
            else
                throw new InvalidOperationException(string.Format(INVALID_STATE_MESSAGE, KobotState.Ready.ToString()));
        }

        public void Stop()
        {
            if (currentState == KobotState.Proccessing)
                targetState = KobotState.Ready;
            else
                throw new InvalidOperationException(string.Format(INVALID_STATE_MESSAGE, KobotState.Proccessing.ToString()));
        }

        public void EmergencyStop()
        {
            if (currentState > KobotState.Initializing)
                targetState = KobotState.NeedPrepare;
        }

        public List<DriveFeedback> GetFeedback()
        {
            feedback.Clear();
            lock (lockDrives)
            {
                for (int i = 0; i < masterDrives.Count; i++)
                {
                    var d = masterDrives.ElementAt(i);
                    feedback.Add(new DriveFeedback()
                    {
                        CurrentPosition = d.Value.Position,
                        DriveName = d.Key,
                        EndPosition = d.Value.EndPosition,
                        StartPosition = d.Value.StartPosition,
                        TargetPosition = d.Value.PositionTarget,
                        DesiredPosition = d.Value.DesiredPosition
                    });
                }
            }
            return feedback;
        }
        public void Close()
        {
            try { _disconnect(); } catch { }
            Interlocked.Exchange(ref isProcRunning, 0);
            if (threadProc != null)
            {
                if (!threadProc.Join(INTERVAL_PROC))
                    threadProc.Abort();
            }
        }

        private void _connect()
        {
            commander.Connect();
            try { listener?.Close(); } catch { }
            listener = new UdpClient(restApi.ClientPort);
            listener.BeginReceive(ReceivedUDP, listener);
            Interlocked.Exchange(ref isSending, 0);
            if (threadSender != null)
            {
                if (!threadSender.Join(restApi.SendingInterval))
                    threadSender.Abort();
            }
            Interlocked.Exchange(ref isSending, 1);
            threadSender = new Thread(new ThreadStart(SendUDP));
            threadSender.Start();
            targetState = KobotState.Initializing;
        }
        private void _disconnect()
        {
            commander.Disconnect();
            listener?.Close();
            Interlocked.Exchange(ref isSending, 0);
            if (threadSender != null)
            {
                if (!threadSender.Join(restApi.SendingInterval))
                    threadSender.Abort();
            }
            targetState = KobotState.NotConnected;
        }

        private void Proc()
        {
            while (1 == isProcRunning)
            {
                Thread.Sleep(INTERVAL_PROC);
                if (currentState != targetState)
                {
                    switch (targetState)
                    {
                        case KobotState.NeedPrepare:
                            lock (lockDrives)
                            {
                                for (int i = 0; i < masterDrives.Count; i++)
                                {
                                    var d = masterDrives.ElementAt(i).Value;
                                    d.ModeTarget = DriveMode.stop;
                                }
                            }
                            break;
                        case KobotState.Ready:
                            commander.Stop();
                            break;
                        case KobotState.Preparing:
                            lock (lockDrives)
                            {
                                for (int i = 0; i < masterDrives.Count; i++)
                                {
                                    var d = masterDrives.ElementAt(i).Value;
                                    d.PositionTarget = d.Position;
                                    d.ModeTarget = DriveMode.position;
                                }
                            }

                            stopwatch.Restart();
                            break;
                    }
                    currentState = targetState;
                    StateChanged?.Invoke(this, currentState);
                }

                switch (currentState)
                {
                    case KobotState.Preparing:
                        stopwatch.Stop();
                        long elapsed = stopwatch.ElapsedMilliseconds;
						
                        if (elapsed < grip.InitTimeMS)
                        {
                            stopwatch.Start();
                        }
                        else
                        {
                            stopwatch.Reset();
                            targetState = KobotState.Ready;
                        }
                        break;

                    case KobotState.Proccessing:
                        lock (lockDrives)
                        {
                            if (commander.ExecuteCommand())
                                targetState = KobotState.Ready;
                        }
                        break;
                }
                //}
            }
        }

        #region ControlAPI
        private void ReceivedUDP(IAsyncResult ar)
        {
            if (listener == null || ar == null)
                return;
			
            var local_listener = (UdpClient)ar.AsyncState;
            if (local_listener == null)
                return;
			
            if (local_listener.Client == null)
                return;
			
            var addr = new IPEndPoint(0, 0);
            byte[] data;
            try
            {
                data = local_listener.EndReceive(ar, ref addr);
            }
            catch
            {
                goto RestartListener;
            }
            if (data == null || data?.Length < 2)
            {
                goto RestartListener;
            }

            try
            {
                string pack = Encoding.ASCII.GetString(data);
                var json = JsonConvert.DeserializeObject<InputPacket>(pack);
                lock (lockDrives)
                {
                    Drive d;
                    int len = json.slaves.Length;
                    for (int i = 0; i < len; i++)
                    {
                        d = json.slaves[i];
                        if (d == null)
							continue;
                        
						if (masterDrives.TryGetValue(d.Name, out Drive drive))
                        {
                            Drive.Copy(d, drive);
                        }
                        else
                        {
                            masterDrives.Add(d.Name, d);
                        }
                    }
                    if (currentState == KobotState.Initializing && len > 0)
                        targetState = KobotState.NeedPrepare;
                }
            }
            catch
            {
                goto RestartListener;
            }

            Thread.Sleep(1);
        RestartListener:
            if (isConnected)
                try { local_listener.BeginReceive(new AsyncCallback(ReceivedUDP), local_listener); } catch { }
        }
        private void SendUDP() 
        {
            while (1 == isSending) 
            {
                try
                {
                    var json = "{\"slaves\":[";
                    var jsonDrives = "";
                    lock (lockDrives)
                    {
                        for (int i = 0; i < masterDrives.Count; i++)
                        {
                            var d = masterDrives.ElementAt(i).Value;
                            if (d.Use)
                            {

                                var target = "";
                                var mode = d.ModeTarget;

                                switch (mode)
                                {
                                    case DriveMode.position:
                                        target = string.Format(",\"target\":{0}", d.PositionTarget.ToString(_ci));
                                        break;
                                    case DriveMode.velocity:
                                        target = string.Format(",\"target\":{0}", d.VelocityTarget.ToString(_ci));
                                        break;
                                    case DriveMode.current:
                                        target = string.Format(",\"target\":{0}", d.CurrentTarget.ToString(_ci));
                                        break;
                                }

                                var DO = "";
                                if (d.DigitalOutTarget >= 0)
                                    DO = string.Format(",\"DO\":{0}", d.DigitalOutTarget);

                                var acc = "";
                                if (d.AccelerationTarget >= 0)
                                    acc = string.Format(",\"acceleration\":{0}", d.AccelerationTarget.ToString(_ci));

                                //var slave = string.Format("{{\"class\":\"{0}\",\"name\":\"{1}\",\"mode\":\"{2}\"{3}{4}{5}}}{6}",
                                var slave = string.Format("{{\"name\":\"{0}\",\"mode\":\"{1}\"{2}{3}{4}}}{5}",
                                        //d.Class,
                                        d.Name,
                                        //d.Use,
                                        mode.ToString(),
                                        target,
                                        DO,
                                        acc,
                                        i < masterDrives.Count - 1 ? "," : ""
                                    );
                                jsonDrives += slave;
                            }
                        }
                    }

                    json += jsonDrives + "]}";
                    byte[] data = Encoding.ASCII.GetBytes(json);
                    client.Send(data, data.Length, restApi.ServerIP, restApi.ServerPort);
                }
                catch (Exception e)
                {
                    if (e is ThreadAbortException)
                    {
                        isSending = 0;
                    }
                }
                Thread.Sleep(restApi.SendingInterval);
            }
        }



        public enum DriveMode { unknown, stop, position, velocity, current }
        public class Drive
        {
            /// <summary>
            /// 
            /// </summary>
            internal string Class { get { return _jClass; } }

            /// <summary>
            /// 
            /// </summary>
            public string Name { get { return _jName; } }

            /// <summary>
            /// 
            /// </summary>
            public DriveMode Mode { get { return _jMode; } }

            /// <summary>
            /// 
            /// </summary>
            public float Position { get { return _jPosition; } }

            /// <summary>
            /// 
            /// </summary>
            public float Velocity { get { return _jVelocity; } }

            /// <summary>
            /// 
            /// </summary>
            public float Current { get { return _jCurrent; } }

            /// <summary>
            /// 
            /// </summary>
            public float PositionTarget { get; set; }

            /// <summary>
            /// 
            /// </summary>
            public float VelocityTarget { get; set; }

            /// <summary>
            /// 
            /// </summary>
            public int DigitalOutTarget { get; set; }

            /// <summary>
            /// Sets acceleration in precent of the maximum in the range from 0 to 1
            /// </summary>
            public float AccelerationTarget { get; set; }

            /// <summary>
            /// 
            /// </summary>
            public bool Use { get; set; }

            /// <summary>
            /// 
            /// </summary>

            public bool Invert { get; set; }

            /// <summary>
            /// 
            /// </summary>
            public float CurrentTarget { get; set; }

            public DriveMode ModeTarget { get; set; }

            public float StartPosition { get; set; }
            public float DesiredPosition { get; set; }
            public float EndPosition { get; set; }

            public Drive(string classAlias, string driveName, float endPosition)
            {
                _jClass = classAlias;
                _jName = driveName;
                EndPosition = endPosition;
                Use = true;
                ModeTarget = DriveMode.stop;
                DigitalOutTarget = -1;
                AccelerationTarget = -1;
            }

            public static void Copy(Drive src, Drive dst)
            {
                //dst._jClass = src._jClass;
                dst._jName = src._jName;
                dst._jMode = src._jMode;
                if (!dst.Invert) dst._jPosition = src._jPosition;
                else dst._jPosition = -src._jPosition;
                dst._jVelocity = src._jVelocity;
                dst._jCurrent = src._jCurrent;
                dst._jAnalogInput = src._jAnalogInput;
            }

            [JsonProperty(PropertyName = "class")]
            private string _jClass;
            [JsonProperty(PropertyName = "name")]
            private string _jName;
            [JsonProperty(PropertyName = "mode")]
            private DriveMode _jMode;
            [JsonProperty(PropertyName = "position")]
            private float _jPosition;
            [JsonProperty(PropertyName = "velocity")]
            private float _jVelocity;
            [JsonProperty(PropertyName = "current")]
            private float _jCurrent;
            [JsonProperty(PropertyName = "ai")]
            private float _jAnalogInput;
        }
        private class InputPacket
        {
            public Drive[] slaves;
        }
        private class OutputPacket
        {
            /// <summary>
            /// Packet number
            /// </summary>
            public ulong id;

            /// <summary>
            /// Ethercat datagram number
            /// </summary>
            public ulong dtg;

            /// <summary>
            /// Array of drivers
            /// </summary>
            public Drive[] slaves;
        }
        #endregion
        public class DriveFeedback
        {
            public string DriveName;
            public float CurrentPosition;
            public float StartPosition;
            public float TargetPosition;
            public float DesiredPosition;
            public float EndPosition;
        }
    }
}
