using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Control_AG95
{
    public class ThresholdReachedEventArgs : EventArgs
    {
        public Int16 data { get; set; }
    }

    internal class COM_Port
    {
        public event EventHandler<ThresholdReachedEventArgs> ThresholdReached;
        ThresholdReachedEventArgs args = new ThresholdReachedEventArgs();

        public SerialPort sPort1;
        Thread readThread;
        private bool _continue = false;
        public bool COM_Open1(string COM_name, int speed)
        {
            try
            {
                sPort1 = new SerialPort
                {
                    PortName = COM_name,
                    BaudRate = speed
                };                         

                sPort1.Open();
                if (sPort1.IsOpen)
                {
                    _continue = true;
                    readThread = new Thread(Read);
                    readThread.Priority = ThreadPriority.BelowNormal;
                    readThread.Start();

                    return true;
                }
                else 
                    return false;
            }
            catch
            {
                return false;
            }
        }

        public void COM_Close1()
        {
           
            _continue = false;
            try
            {
                if (sPort1 != null)
                {
                    if (sPort1.IsOpen)
                        sPort1.Close();
                }
                if (readThread != null)
                {
                    if (readThread.IsAlive) 
                        readThread.Abort();
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
            }
        }
        
        public void Send(short msg)
        {
            byte[] data = BitConverter.GetBytes(msg);
            if (sPort1.IsOpen)
                sPort1.Write(data, 0, data.Length);
        }
        public void Read()
        {
            while (_continue)
            {
                try
                {
                    if (sPort1.BytesToRead > 0)
                    {
                        byte[] readBt = new byte[sPort1.BytesToRead];
                        sPort1.Read(readBt, 0, readBt.Length);
                        args.data = BitConverter.ToInt16(readBt,0);// Int16.Parse(readBt,0);
                        ThresholdReached?.Invoke(this, args);
                        sPort1.BaseStream.Flush();
                    }
                }
                catch { }
                Thread.Sleep(2);
            }
        }
    }
}
