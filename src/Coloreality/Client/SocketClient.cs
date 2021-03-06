﻿using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;

namespace Coloreality.Client
{
    public delegate void ConnectEventHandler(object sender, EventArgs e);
    public delegate void DisconnectEventHandler(object sender, EventArgs e);

    public class SocketClient
    {
        public int bufferSize = Globals.DEFAULT_BUFFER_SIZE;

        public event ConnectEventHandler OnConnected;
        public event ReceiveEventHandler OnReceived;
        public event DisconnectEventHandler OnDisconnected;
        public event ErrorEventHandler OnError;

        public Dictionary<int, ReceiveEventHandler> OnReceiveDataCollection = new Dictionary<int, ReceiveEventHandler>();

        public int connectTimeout = 5000;

        private bool _isConnected = false;
        public bool IsConnected
        {
            get
            {
                return _isConnected;
            }
            private set
            {
                _isConnected = value;
            }
        }

        private int _port = Globals.SERVER_DEFAULT_PORT;
        public int Port
        {
            get
            {
                return _port;
            }
            set
            {
                if (value >= NetworkUtil.PORT_MIN && value <= NetworkUtil.PORT_MAX)
                {
                    _port = value;
                }
                else
                {
                    if (OnError != null) OnError.Invoke(this, new ErrorEventArgs("The port is out of range(" + NetworkUtil.PORT_MIN.ToString() + " to " + NetworkUtil.PORT_MAX.ToString() + ")."));
                }
            }
        }

        private string _ip = Globals.SERVER_DEFAULT_IP;
        IPAddress _ipAddress = null;
        public string Ip
        {
            get
            {
                return _ip;
            }
            set
            {
                IPAddress tempIp;
                if (IPAddress.TryParse(value, out tempIp))
                {
                    _ip = value;
                    _ipAddress = tempIp;
                }
                else
                {
                    if (OnError != null) OnError.Invoke(this, new ErrorEventArgs("The IP is wrong. Please check again."));
                }
            }
        }

        /// <summary>
        /// Will disconnect when received {DataType.Close} as a Close command.
        /// </summary>
        public bool closeForCommand = true;

        private Thread receiveThread = null;
        private Socket socket;

        private bool doReceive = true;


        byte[] fullDataTemp = new byte[0];
        int newLength = -1;
        int newDataIndex = -1;
        int recievedLength = 0;

        public SocketClient(int port = Globals.SERVER_DEFAULT_PORT)
        {
            this.Port = port;
        }

        public SocketClient(string ip, int port, bool connectNow = false)
        {
            this.Ip = ip;
            this.Port = port;
            if (connectNow)
            {
                Connect();
            }
        }

        public void Connect(bool reconnectIfConnected = false)
        {
            if (!IsConnected)
            {
                try
                {
                    if (_ipAddress == null)
                    {
                        if (OnError != null) OnError.Invoke(this, new ErrorEventArgs("Server IP is not set."));
                        return;
                    }

                    socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    IPEndPoint endPoint = new IPEndPoint(_ipAddress, Port);

                    IAsyncResult result = socket.BeginConnect(endPoint, null, null);
                    bool success = result.AsyncWaitHandle.WaitOne(connectTimeout, true);

                    if (!success && !socket.Connected)
                    {
                        socket.Close();
                        if (OnError != null)
                        {
                            OnError.Invoke(this, new ErrorEventArgs("Failed to connect within the last " + (connectTimeout / 1000f).ToString("0.##") + " seconds."));
                        }
                        return;
                    }

                    socket.EndConnect(result);

                    IsConnected = true;
                    if (OnConnected != null) OnConnected.Invoke(this, EventArgs.Empty);

                    doReceive = true;
                    Globals.CloseThreadIfExists(ref receiveThread);
                    receiveThread = new Thread(new ThreadStart(ReceiveThread))
                    {
                        IsBackground = true
                    };
                    receiveThread.Start();

                }
                catch (Exception ex)
                {
                    //if (OnError != null) OnError.Invoke(this, new ErrorEventArgs(ex.Message));
                    if (OnError != null) OnError.Invoke(this, new ErrorEventArgs(ex.GetType().ToString() + ": " + ex.Message + ex.StackTrace));
                }
            }
            else if (reconnectIfConnected)
            {
                Close();
                Connect();
            }
            else if (OnError != null)
            {
                OnError.Invoke(this, new ErrorEventArgs("It's connected already."));
            }
        }

        private void ReceiveThread()
        {
            while (doReceive)
            {
                try
                {
                    if (socket.Available <= 0) continue;

                    byte[] buffer = new byte[bufferSize];
                    int length = socket.Receive(buffer);

                    if (length == 0) continue;

                    byte[] curBytes = new byte[length];
                    Buffer.BlockCopy(buffer, 0, curBytes, 0, length);

                    ProcessReceiving(curBytes);
                }
                catch (System.IO.EndOfStreamException endOfStreamEx)
                {
                    if (OnError != null) OnError.Invoke(this, new ErrorEventArgs(endOfStreamEx.GetType().ToString() + endOfStreamEx.Message + endOfStreamEx.StackTrace + "\r\nConsider increasing buffer size."));
                }
                catch (Exception ex)
                {
                    if (OnError != null) OnError.Invoke(this, new ErrorEventArgs(ex.GetType().ToString() + ": " + ex.Message + ex.StackTrace));
                    if (!socket.Connected)
                    {
                        Close();
                    }
                }

            }
        }

        private void ProcessReceiving(byte[] receivedBytes)
        {
            if (newLength == -1)
            {
                ReceiveEventArgs receiveMessage = new ReceiveEventArgs(receivedBytes);
                if (OnReceived != null) OnReceived.Invoke(this, receiveMessage);

                if (receiveMessage.MessageType == DataType.PreSerialization)
                {
                    object serializedObject = receiveMessage.SerializedObject;
                    if (serializedObject == null) return;
                    PreSerialization nextSerialization = (PreSerialization)serializedObject;
                    newDataIndex = nextSerialization.dataIndex;
                    newLength = nextSerialization.dataLength;
                    fullDataTemp = new byte[newLength];

                    recievedLength = 0;
                }
                else if (receiveMessage.MessageType == DataType.Close && closeForCommand)
                {
                    Close();
                }
            }
            else if (recievedLength > 0 || receivedBytes[0] == (byte)DataType.Serialization)
            {

                int restLength = newLength - recievedLength;
                int addCount = Math.Min(restLength, receivedBytes.Length);

                Buffer.BlockCopy(receivedBytes, 0, fullDataTemp, recievedLength, addCount);

                if (restLength > addCount)
                {
                    recievedLength += addCount;
                }
                else
                {
                    // Done this data.
                    ReceivedFullData(newDataIndex, fullDataTemp);
                    newLength = -1;
                }
            }
        }

        private void ReceivedFullData(int dataIndex, byte[] data)
        {
            if (OnReceiveDataCollection.ContainsKey(dataIndex) && OnReceiveDataCollection[dataIndex] != null)
            {
                OnReceiveDataCollection[dataIndex].Invoke(this, new ReceiveEventArgs(data));
            }
        }

        public void SendRawBytes(byte[] value)
        {
            if (socket != null && socket.Connected)
                socket.Send(value);
        }

        public void SendBytesWithDefinedStart(byte first, byte[] value)
        {
            SendRawBytes(ByteUtil.GetStartDefinedBytes(first, value));
        }

        public void SendBytesWithDefinedStart(DataType type, byte[] value)
        {
            SendBytesWithDefinedStart((byte)type, value);
        }

        public void SendBytes(byte[] value)
        {
            SendBytesWithDefinedStart(DataType.Byte, value);
        }

        public void SendString(string value)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
            SendBytesWithDefinedStart(DataType.String, bytes);
        }

        public void SendType(DataType type)
        {
            SendRawBytes(new byte[] { (byte)type });
        }

        /// <summary>
        /// Close connection.
        /// </summary>
        /// <param name="isInitiative">
        /// If true, will send a Close command to the server side. Like saying "Hey, I'm closing the connection! (Do something if you need.)"
        /// If false, just close without telling the server. Can be used when has been told to close(server sent the Close command to here) and no need to notify back.
        /// </param>
        public void Close(bool isInitiative = false)
        {
            doReceive = false;
            if (socket != null)
            {
                IsConnected = false;
                if (socket.Connected)
                {
                    if (isInitiative) SendType(DataType.Close);
                    socket.Shutdown(SocketShutdown.Both);
                }
                socket.Close();
                socket = null;

                if (OnDisconnected != null)
                    OnDisconnected.Invoke(this, EventArgs.Empty);
            }
        }

    }
}