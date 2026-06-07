using KOBOT_Control;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net;

namespace KOBOT_Control
{
    internal class ConfigManager
    {
        const string CONFIG_FILENAME = "cfg.json";
        JsonSerializerSettings serializerSettings;
        internal ConfigManager()
        {
            serializerSettings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All };
        }
        internal Config LoadConfig()
        {
            Config cfg;
            try
            {

                using (StreamReader sr = new StreamReader(CONFIG_FILENAME))
                {
                    string str = sr.ReadToEnd();
                    cfg = JsonConvert.DeserializeObject<Config>(str, serializerSettings) ?? new Config();
                }
            }
            catch (Exception exc)
            {
                MessageBox.Show(exc.Message);
                cfg = new Config();
                using (StreamWriter sw = new StreamWriter(CONFIG_FILENAME))
                {
                    string str = JsonConvert.SerializeObject(cfg, Formatting.Indented, serializerSettings);
                    try
                    {
                        sw.Write(str);
                        sw.Flush();
                    }
                    catch { }
                }
            }

            if (!IPAddress.TryParse(cfg.RestAPI.ServerIP, out _))
                cfg.RestAPI.ServerIP = RestApiParameters.SERVER_IP_ADDRESS;

            return cfg;
        }

        internal class Config
        {
            public RestApiParameters RestAPI;
            public GripParameters Grip;
            public float UpperPositionDeviation;
            public float LowerPositionDeviation;
            public Commander.Command[] Commands;

            public Config()
            {
                RestAPI = new RestApiParameters();
                Grip = new GripParameters();
                UpperPositionDeviation = 1.0f;
                LowerPositionDeviation = 1.0f;
                Commands = GetDefaultCommands();
            }

            public Commander.Command[] GetDefaultCommands()
            {
                Dictionary<string, Commander.Spline> splines = new Dictionary<string, Commander.Spline>()
                {
                    { "Unit5R", new Commander.Spline(10.0f, 0) }
                };
                var cmds = new Commander.Command[]
                {
                    new Commander.Another(0),
                    new Commander.Motion(splines, 3000),
                    new Commander.Grip(0, 20, 2000),
                    new Commander.Pause(1500),
                    new Commander.Goto(0)
                };
                return cmds;
            }
        }
        public class RestApiParameters
        {
            const byte INTERVAL_SENDING_UDP = 50;
            const ushort CLIENT_PORT = 11002;
            const ushort SERVER_PORT = 11001;
            internal const string SERVER_IP_ADDRESS = "127.0.0.1";

            public ushort SendingInterval;
            public ushort ClientPort;
            public ushort ServerPort;
            public string ServerIP;

            public RestApiParameters()
            {
                SendingInterval = INTERVAL_SENDING_UDP;
                ClientPort = CLIENT_PORT;
                ServerPort = SERVER_PORT;
                ServerIP = SERVER_IP_ADDRESS;
            }
        }
        public class GripParameters
        {
            const string COM_PORT_NAME = "COM3";
            const ushort INIT_TIME = 5000;

            public string COMPortName;
            public ushort InitTimeMS;

            public GripParameters()
            {
                COMPortName = COM_PORT_NAME;
                InitTimeMS = INIT_TIME;
            }
        }
    }
}
