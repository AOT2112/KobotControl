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

namespace KOBOT_Control
{
    public class KobotClient : IDisposable
    {
        #region ConfigFileData
        private static class Config
        {
            internal const ushort PortTx = 5900;
            internal const string IPAddressTx = "192.168.10.2";

            internal const string RunSecondRobotMessage = "DOSING";
            internal const string SecondRobotReadyMessage = "DOSING_OK";
        }
        #endregion
        
        #region Public fields
        public bool Connect
        {
            get
            {
                if (disposedValue)
                    throw new ObjectDisposedException(new StackFrame(1).GetMethod().DeclaringType.Name);

                return isConnected;
            }
            set
            {
                if (disposedValue)
                    throw new ObjectDisposedException(new StackFrame(1).GetMethod().DeclaringType.Name);

                if (value)
                {
                    if (!isConnected)
                    {
                        ConnectProc();
                        isConnected = true;
                    }
                }
                else
                {
                    if (isConnected)
                    {
                        DisconnectProc();
                        isConnected = false;
                    }
                }
            }
        }
        public bool IsStarted { get { return isStarted; } }
        public bool IsReady { get { return isReady; } }
        #endregion
        #region Local fields
        readonly object udpLocker;
        private bool disposedValue;
        int stopSending;
        bool isConnected = false;
        bool isStarted = false;
        bool isReady = false;
        volatile bool runKobot;
        UdpClient udpTx;
        Thread threadTx;
        #endregion
        public KobotClient() 
        {
            udpLocker = new object();
            udpTx = new UdpClient();
            udpTx.Client.Bind(new IPEndPoint(IPAddress.Any, Config.PortTx));
        }
        #region Public methods
        public void Start()
        {
            isReady = false;
            if (isConnected)
            {
                string msg = Config.RunSecondRobotMessage;
                try
                {
                    var data = Encoding.ASCII.GetBytes(msg);
                    udpTx.Send(data, data.Length, Config.IPAddressTx, Config.PortTx);
                    isStarted = true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                    Thread.Sleep(10);
                }
                runKobot = true;
            }
        }
        public void Stop()
        {
            isStarted = false;
            runKobot = false;
        }
        #endregion
        #region Tx & Rx methods
        private void ReceiveProc(IAsyncResult ar)
        {

            byte[] data;
            UdpClient listener;
            lock (udpLocker)
            {
                listener = ar.AsyncState as UdpClient;
                if (listener == null || listener.Client == null)
                    return;

                var addr = new IPEndPoint(0, 0);
                data = listener.EndReceive(ar, ref addr);
                if (data == null || data.Length < 2)
                    return;

                var msg = Encoding.ASCII.GetString(data);
                if (msg.Equals(Config.SecondRobotReadyMessage))
                {
                    if (runKobot)
                    {
                        isReady = true;
                        runKobot = false;
                    }
                }
            }
            listener.BeginReceive(new AsyncCallback(ReceiveProc), listener);
        }
        #endregion
        #region Local methods

        private void ConnectProc()
        {
            try
            {
                lock (udpLocker)
                {
                    udpTx.BeginReceive(new AsyncCallback(ReceiveProc), udpTx);
                }
            }
            catch (Exception e)
            {
                // @TODO: Error logging
                Debug.WriteLine("ConnectProc error: " + e.Message);
            }

            KillTxThread();
            stopSending = 0;
        }
        private void DisconnectProc()
        {
            KillTxThread();
            isStarted = false;
        }
        private void KillTxThread()
        {
            Interlocked.Exchange(ref stopSending, 0);
            if (threadTx != null && threadTx.IsAlive)
            {
                try
                {
                    if (!threadTx.Join(300))
                    {
                        // @TODO: Error logging
                        Debug.WriteLine("Killing Tx Thread error: Join timeout");
                    }
                }
                catch (Exception e)
                {
                    // @TODO: Error logging
                    Debug.WriteLine("Killing Tx Thread error: " + e.Message);
                }
            }
        }
        #endregion
        #region Disposer
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    DisconnectProc();
                    threadTx = null;
                    //udpRx = null;
                    udpTx = null;
                    // TODO: освободить управляемое состояние (управляемые объекты)
                }

                // TODO: освободить неуправляемые ресурсы (неуправляемые объекты) и переопределить метод завершения
                // TODO: установить значение NULL для больших полей
                disposedValue = true;
            }
        }

        // // TODO: переопределить метод завершения, только если "Dispose(bool disposing)" содержит код для освобождения неуправляемых ресурсов
        // ~KobotClient()
        // {
        //     // Не изменяйте этот код. Разместите код очистки в методе "Dispose(bool disposing)".
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Не изменяйте этот код. Разместите код очистки в методе "Dispose(bool disposing)".
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
