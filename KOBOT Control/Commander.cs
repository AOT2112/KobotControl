using Control_AG95;
using KOBOT_Control;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KOBOT_Control
{
    internal class Commander
    {
        volatile int cmdIndex;
        Stopwatch stopwatch;
        Command[] commands;
        Dictionary<string, KobotContol.Drive> masterDrives;
        ConfigManager.Config config;
        COM_Port comPort;
        KobotClient kclient;
        internal int GetCurrentCommandIndex { get { return cmdIndex; } }
        internal Commander(ConfigManager.Config config, Dictionary<string, KobotContol.Drive> drives)
        {
            comPort = new COM_Port();
            kclient = new KobotClient();
            cmdIndex = 0;
            masterDrives = drives;
            this.config = config;
            stopwatch = new Stopwatch();
            List<Command> cmds = new List<Command>();
            for (int i = 0; i < config.Commands.Length; i++)
            {
                var cmd = config.Commands[i];
                if (commandTypes.TryGetValue(cmd.CommandName, out Type type))
                {
                    if (type == cmd.GetType())
                        cmds.Add(cmd);
                }
            }
            if (cmds.Count > 0)
                commands = cmds.ToArray();
            else
                throw new Exception("Commands are absent!");
        }
        private class Constants
        {
            public const string GSTR_MOTION = "MOTION";
            public const string GSTR_PAUSE = "PAUSE";
            public const string GSTR_START_ANOTHER = "ANOTHER";
            public const string GSTR_GOTO = "GOTO";
            public const string GSTR_GRIP = "GRIP";
        }
        private readonly Dictionary<string, Type> commandTypes = new Dictionary<string, Type>()
        {
            { Constants.GSTR_MOTION, typeof(Motion) },
            { Constants.GSTR_PAUSE, typeof(Pause) },
            { Constants.GSTR_START_ANOTHER, typeof(Another) },
            { Constants.GSTR_GOTO, typeof(Goto) },
            { Constants.GSTR_GRIP, typeof(Grip) }
        };
        public abstract class Command
        {
            [JsonProperty(Order = -1)]
            public string CommandName;
        }
        public class Motion : Command
        {
            [JsonProperty(Order = 1)]
            public Dictionary<string, Spline> Splines;
            [JsonProperty(Order = 2)]
            public uint TimeoutMS;

            public Motion(Dictionary<string, Spline> splines, uint timeout)
            {
                CommandName = Constants.GSTR_MOTION;
                Splines = splines;
                TimeoutMS = timeout;
            }
        }
        public class Spline
        {
            public float Position;
            public uint DelayMS;

            public Spline(float position, uint delay)
            {
                Position = position;
                DelayMS = delay;
            }
        }
        public class Pause : Command
        {
            [JsonProperty(Order = 1)]
            public uint TimeMS;

            public Pause(uint time)
            {
                CommandName = Constants.GSTR_PAUSE;
                TimeMS = time;
            }
        }
        public class Goto : Command
        {
            [JsonProperty(Order = 1)]
            public uint Index;

            public Goto(uint index)
            {
                CommandName = Constants.GSTR_GOTO;
                Index = index;
            }
        }
        public class Another : Command
        {
            [JsonProperty(Order = 1)]
            public uint LineNumber;

            public Another(uint line)
            {
                CommandName = Constants.GSTR_START_ANOTHER;
                LineNumber = line;
            }
        }
        public class Grip : Command
        {
            [JsonProperty(Order = 0)]
            public byte Position;
            [JsonProperty(Order = 1)]
            public byte Force;
            [JsonProperty(Order = 2)]
            public uint TimeMS;

            public Grip(byte position, byte force, uint time)
            {
                CommandName = Constants.GSTR_GRIP;
                Position = position;
                Force = force;
                TimeMS = time;
            }
        }
        public bool ExecuteCommand()
        {
            bool result = false;
            var cmd = commands[cmdIndex];
            long elapsed;
            switch (cmd.CommandName)
            {
                case Constants.GSTR_MOTION:
                    if (stopwatch.IsRunning)
                    {
                        Motion motion = (Motion)cmd;
                        stopwatch.Stop();
                        elapsed = stopwatch.ElapsedMilliseconds;
                        if (elapsed < motion.TimeoutMS)
                        {
                            int completeCounts = 0;
                            int splineCounts = motion.Splines.Count;
                            for (int i = 0; i < splineCounts; i++)
                            {
                                var splineKVP = motion.Splines.ElementAt(i);
                                Spline spline = splineKVP.Value;
                                if (masterDrives.TryGetValue(splineKVP.Key, out KobotContol.Drive drive))
                                {
                                    if (elapsed < spline.DelayMS)
                                        drive.PositionTarget = drive.Position;
                                    else
                                    {
                                        float pos = spline.Position;
                                        drive.PositionTarget = pos;
                                        if (drive.Position <= pos + config.UpperPositionDeviation && drive.Position >= pos - config.LowerPositionDeviation)
                                            completeCounts++;
                                    }
                                    drive.ModeTarget = KobotContol.DriveMode.position;
                                }
                            }
                            if (completeCounts == splineCounts)
                                result = Next();
                            else
                                stopwatch.Start();
                        }
                        else
                            result = Next();
                    }
                    else
                        stopwatch.Start();
                    break;
                case Constants.GSTR_PAUSE:
                    if (stopwatch.IsRunning)
                    {
                        Pause pause = (Pause)cmd;
                        stopwatch.Stop();
                        elapsed = stopwatch.ElapsedMilliseconds;
                        if (elapsed < pause.TimeMS)
                            stopwatch.Start();
                        else
                            result = Next();
                    }
                    else
                        stopwatch.Start();
                    break;
                case Constants.GSTR_START_ANOTHER:
                    if (!kclient.IsStarted)
                        kclient.Start();
                    else if (kclient.IsReady)
                    {
                        kclient.Stop();
                        result = Next();
                    }
                    break;
                case Constants.GSTR_GOTO:
                    Goto gcmd = (Goto)cmd;
                    uint idx = gcmd.Index;
                    if (idx < commands.Length)
                        cmdIndex = Convert.ToInt32(idx);
                    else
                        result = Next();
                    break;
                case Constants.GSTR_GRIP:
                    if (stopwatch.IsRunning)
                    {
                        Grip grip = (Grip)cmd;
                        stopwatch.Stop();
                        elapsed = stopwatch.ElapsedMilliseconds;
                        if (elapsed < grip.TimeMS)
                        {
                            byte position = grip.Position;
                            if (position == 0)
                                comPort.Send(87);
                            else
                                comPort.Send(99);
                            Thread.Sleep(10);
                            stopwatch.Start();
                        }
                        else
                            result = Next();
                    }
                    else
                        stopwatch.Start();
                    break;
            }
            return result;
        }
        public void Connect()
        {
            if (comPort.COM_Open1(config.Grip.COMPortName, 9600))
                kclient.Connect = true;
            else
                MessageBox.Show("COM Port is not open!", "Error!", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        public void Disconnect()
        {
            comPort.COM_Close1();
            kclient.Connect = false;
        }
        public void Stop()
        {
            stopwatch.Reset();
            cmdIndex = 0;
        }
        private bool Next()
        {
            stopwatch.Reset();
            cmdIndex++;
            if (cmdIndex >= commands.Length)
            {
                cmdIndex = 0;
                return true;
            }
            return false;
        }
    }
}
